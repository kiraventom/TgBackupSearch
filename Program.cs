using System.CommandLine;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using TgChannelRecognize.Parsing;
using TgChannelRecognize.Recognition;
using TgChannelRecognize.Utils;
using TgChannelRecognize.Video;
using TgChannelLib.Model;

namespace TgChannelRecognize;

internal class Program
{
    private const string PROJECT_NAME = "TgChannelRecognize";

    private static async Task Main(string[] args)
    {
        var appDataDir = CreateAppDataDir();
        var appConfigDir = CreateAppConfigDir();

        var logger = BuildLogger(appDataDir);

        if (!TryGetRunOptions(args, out var runOptions))
        {
            logger.Fatal("Failed to parse arguments, closing");
            return;
        }

        var configFile = Path.Combine(appConfigDir, "config.json");
        if (!Config.TryLoad(configFile, out var config))
        {
            logger.Fatal("Failed to load config, closing");
            return;
        }

        if (!CheckFfmpeg())
        {
            logger.Fatal("Failed to locate ffmpeg, closing");
            return;
        }

        if (!TryGetTesseractDir(config, out var tesseractDir))
        {
            logger.Fatal("Failed to locate tesseract dir, closing");
            return;
        }

        var paths = new AppPaths(appDataDir, appConfigDir, tesseractDir);
        CheckPaths(paths);

        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSerilog(logger)
            .AddSingleton(config)
            .AddSingleton(paths)
            .AddSingleton(runOptions)
            .AddSingleton<ChannelInfo>()
            .AddDbContext<ChannelContext>(static (sp, options) =>
            {
                var paths = sp.GetRequiredService<AppPaths>();
                var channelInfo = sp.GetRequiredService<ChannelInfo>();
                Program.SetContextOptions(options, paths, channelInfo);
            })
            .AddKeyedTransient<IMetadataParser, NetworkParser>(RunMode.Network)
            .AddKeyedTransient<IMetadataParser, OfflineParser>(RunMode.Offline)
            .AddTransient<IMetadataParser>(sp => 
            {
                var runOptions = sp.GetRequiredService<RunOptions>();
                return sp.GetRequiredKeyedService<IMetadataParser>(runOptions.RunMode);
            })
            .AddTransient<Recognizer>()
            .AddTransient<FrameExtractor>()
            .AddHostedService<AppService>();

        var host = builder.Build();

        await CreateDb(host.Services);

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex.ToString());
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task CreateDb(IServiceProvider sp)
    {
        using var context = sp.GetRequiredService<ChannelContext>();
        await context.Database.MigrateAsync();
    }

    public static void SetContextOptions(DbContextOptionsBuilder options, AppPaths paths, ChannelInfo channelInfo)
    {
        var dbPath = BuildDbPath(paths, channelInfo);
        options.UseSqlite($"Data Source={dbPath};");
    }

    public static string BuildDbPath(AppPaths paths, ChannelInfo channelInfo)
    {
        return Path.Combine(paths.AppDataDir, $"{channelInfo.ChannelId}.db");
    }

    private static void CheckPaths(AppPaths paths)
    {
        ThrowIfDirNotValid(paths.AppConfigDir);
        ThrowIfDirNotValid(paths.AppDataDir);
        ThrowIfDirNotValid(paths.TesseractDir);
    }

    private static void ThrowIfDirNotValid(string path)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(path);
        if (Directory.Exists(path) == false)
            throw new DirectoryNotFoundException($"{path} does not exist");
    }

    private static bool CheckFfmpeg()
    {
        using var ffmpegProc = ProcessHelper.RunSilent("ffmpeg", "-hide_banner -version");
        ffmpegProc.WaitForExit();

        if (ffmpegProc.ExitCode != 0)
        {
            if (OperatingSystem.IsLinux())
            {
                Log.Logger.Error("ffmpeg not found. You can install it with package manager (e.g., sudo apt install ffmpeg)");
            }
            else
            {
                Log.Logger.Error("ffmpeg not found");
            }

            return false;
        }

        using var ffprobeProc = ProcessHelper.RunSilent("ffprobe", "-hide_banner -version");
        ffprobeProc.WaitForExit();

        if (ffprobeProc.ExitCode != 0)
        {
            Log.Logger.Error("ffprobe not found");
            return false;
        }

        return true;
    }

    private static bool CheckTesseract()
    {
        using var process = ProcessHelper.RunSilent("tesseract", "--version");
        process.WaitForExit();

        var result = process.ExitCode == 0;
        if (!result)
        {
            if (OperatingSystem.IsLinux())
            {
                Log.Logger.Error("Tesseract not found. You can install it with package manager (e.g., sudo apt install tesseract-ocr)");
            }
            else
            {
                Log.Logger.Error("Tesseract not found");
            }
        }

        return result;
    }

    private static bool TryGetTesseractDir(Config config, out string tessdataDir)
    {
        tessdataDir = null;

        if (!CheckTesseract())
            return false;

        if (!CheckTesseractLangs(config))
            return false;

        if (CheckDir(Environment.GetEnvironmentVariable("$TESSDATA_PREFIX"), ref tessdataDir))
            return true;

        if (CheckDir(config.TesseractDir, ref tessdataDir))
            return true;

        if (CheckDir(Path.Combine(AppContext.BaseDirectory, "tessdata"), ref tessdataDir))
            return true;

        return false;

        static bool CheckDir(string path, ref string dir)
        {
            var result = !string.IsNullOrEmpty(path) && Directory.Exists(path);
            if (result)
            {
                var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                if (folderName == "tessdata")
                    path = Path.GetDirectoryName(path);

                dir = path;
            }

            return result;
        }
    }

    private static bool CheckTesseractLangs(Config config)
    {
        if (config.Languages.Count == 0)
            return true;

        var psi = new ProcessStartInfo()
        {
            FileName = "tesseract",
            Arguments = "--list-langs",
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        process.WaitForExit();

        var output = process.StandardOutput.ReadToEnd();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.TrimEntries);

        bool result = true;

        foreach (var lang in config.Languages)
        {
            if (!lines.Contains(lang))
            {
                result = false;

                if (OperatingSystem.IsLinux())
                {
                    Log.Logger.Error("Tesseract language \"{lang}\" not found. You can install it with package manager (e.g., sudo apt install tesseract-ocr-{lang})", lang);
                }
                else
                {
                    Log.Logger.Error("Tesseract language \"{lang}\" not found", lang);
                }
            }
        }

        return result;
    }

    public static bool TryGetRunOptions(string[] args, out RunOptions runOptions)
    {
        runOptions = null;

        var channelOption = new Option<string>("--channel", "-c")
        {
            HelpName = "Channel (ID or directory)",
            Required = true,
            Description = "Either ID of channel or path to the directory of channel backup"
        }.AcceptLegalFilePathsOnly();

        var discussionOption = new Option<string>("--discussion", "-d")
        {
            HelpName = "Discussion group (ID or directory)",
            Required = false,
            Description = "Either ID of discussion group or path to the directory of discussion group backup"
        }.AcceptLegalFilePathsOnly();

        var rootCommand = new RootCommand()
        {
            channelOption, discussionOption
        };

        var parseResult = rootCommand.Parse(args);
        foreach (var error in parseResult.Errors)
            Log.Error(error.Message);

        if (parseResult.Errors.Any())
            return false;

        var channelStr = parseResult.GetRequiredValue<string>(channelOption);

        var hasDiscussion = parseResult.GetResult(discussionOption) is not null;
        var discussionStr = parseResult.GetValue<string>(discussionOption);

        if (long.TryParse(channelStr, out var channelId))
        {
            if (!hasDiscussion)
            {
                runOptions = new RunOptions(channelId);
                return true;
            }

            if (long.TryParse(discussionStr, out var discussionGroupId))
            {
                runOptions = new RunOptions(channelId, discussionGroupId);
                return true;
            }

            Log.Error("{channelOpt} is set to channel ID {channelID}, but {groupOpt} is set to {value} which is not parseable as ID", channelOption.Name, channelId, discussionOption.Name, discussionStr);
            return false;
        }

        if (!Directory.Exists(channelStr))
        {
            Log.Error("Directory {path} does not exist", channelStr);
            return false;
        }

        if (!hasDiscussion)
        {
            runOptions = new RunOptions(channelStr);
            return true;
        }

        if (!Directory.Exists(discussionStr))
        {
            Log.Error("Directory {path} does not exist", discussionStr);
            return false;
        }

        runOptions = new RunOptions(channelStr, discussionStr);
        return true;
    }

    private static string CreateAppConfigDir()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    PROJECT_NAME,
                    "config");
        }
        else
        {
            path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    PROJECT_NAME);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateAppDataDir()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    PROJECT_NAME,
                    "data");
        }
        else
        {
            path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share",
                    PROJECT_NAME);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static ILogger BuildLogger(string appDataDir)
    {
        var logDir = Path.Combine(appDataDir, "logs");
        Directory.CreateDirectory(logDir);

        var logFile = Path.Combine(logDir, $"{PROJECT_NAME}-.log");
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Logger = logger;

        return logger;
    }
}
