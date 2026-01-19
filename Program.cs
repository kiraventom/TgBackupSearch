using System.CommandLine;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using TgBackupSearch.IO;
using TgBackupSearch.Model;
using TgBackupSearch.Parsing;
using TgBackupSearch.Recognition;
using TgBackupSearch.Utils;
using TgBackupSearch.Video;

namespace TgBackupSearch;

internal class Program
{
    private const string PROJECT_NAME = "TgBackupSearch";

    private static async Task Main(string[] args)
    {
        var appDataDir = CreateAppDataDir();
        var appConfigDir = CreateAppConfigDir();

        var logger = BuildLogger(appDataDir);

        if (!TryGetRunOptions(args, out var channelDir, out var discussionDir, out var mode))
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

        var paths = new Paths(appDataDir, appConfigDir, tesseractDir, channelDir, discussionDir);
        CheckPaths(paths);

        var runOptions = new RunOptions(mode, !string.IsNullOrEmpty(discussionDir));

        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSerilog(logger)
            .AddSingleton(config)
            .AddSingleton(paths)
            .AddSingleton(runOptions)
            .AddSingleton<ChannelInfo>()
            .AddKeyedSingleton<IIOInterface, ConsoleIO>(IOMode.Console)
            .AddKeyedSingleton<IIOInterface, WebIO>(IOMode.Web)
            .AddKeyedSingleton<IIOInterface, TelegramIO>(IOMode.Telegram)
            .AddSingleton<IIOInterface>(static sp =>
            {
                var runOptions = sp.GetRequiredService<RunOptions>();
                return sp.GetRequiredKeyedService<IIOInterface>(runOptions.Mode);
            })
            .AddDbContext<ChannelContext>(static (sp, options) =>
            {
                var paths = sp.GetRequiredService<Paths>();
                var channelInfo = sp.GetRequiredService<ChannelInfo>();
                Program.SetContextOptions(options, paths, channelInfo);
            })
            .AddTransient<BackupParser>()
            .AddTransient<Recognizer>()
            .AddTransient<FrameExtractor>()
            .AddScoped<SearchService>()
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

    public static void SetContextOptions(DbContextOptionsBuilder options, Paths paths, ChannelInfo channelInfo)
    {
        var dbPath = Path.Combine(paths.AppDataDir, $"{channelInfo.ChannelId}.db");
        options.UseSqlite($"Data Source={dbPath};");
    }

    private static void CheckPaths(Paths paths)
    {
        ThrowIfDirNotValid(paths.AppConfigDir);
        ThrowIfDirNotValid(paths.AppDataDir);
        ThrowIfDirNotValid(paths.TesseractDir);
        ThrowIfDirNotValid(paths.ChannelDir);

        if (paths.DiscussionGroupDir is not null)
            ThrowIfDirNotValid(paths.DiscussionGroupDir);
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

    public static bool TryGetRunOptions(string[] args, out string channelDir, out string discussionDir, out IOMode mode)
    {
        channelDir = null;
        discussionDir = null;
        mode = IOMode.Console;

        var modeNames = Enum.GetNames<IOMode>().Select(n => n.ToLower()).ToArray();
        var modeNamesStr = string.Join(", ", modeNames);

        var channelDirOpt = new Option<string>("--channelDir", "-c")
        {
            HelpName = "Channel directory",
            Required = true,
            Description = "Path to the directory of channel backup"
        }.AcceptLegalFilePathsOnly();

        var discussionDirOpt = new Option<string>("--discussionDir", "-d")
        {
            HelpName = "Discussion group directory",
            Required = false,
            Description = "Path to the directory of discussion group backup. If not specified, discussion group will not be included in search"
        }.AcceptLegalFilePathsOnly();

        var modeOpt = new Option<string>("--mode", "-m")
        {
            HelpName = "Launch mode",
            Required = false,
            Description = $"Launch mode. Valid values: {modeNamesStr}."
        }.AcceptOnlyFromAmong(modeNames);

        var rootCommand = new RootCommand()
        {
            channelDirOpt, discussionDirOpt, modeOpt
        };

        var parseResult = rootCommand.Parse(args);
        foreach (var error in parseResult.Errors)
            Log.Error(error.Message);

        if (parseResult.Errors.Any())
            return false;

        channelDir = parseResult.GetRequiredValue(channelDirOpt);
        discussionDir = parseResult.GetValue(discussionDirOpt);
        var modeStr = parseResult.GetValue(modeOpt);

        if (!string.IsNullOrEmpty(modeStr))
        {
            if (!Enum.TryParse<IOMode>(modeStr, out var newMode))
                Log.Warning("Failed to parse mode \"{mode}\", fallback to \"{default}\"", modeStr, Enum.GetName<IOMode>(mode).ToLower());
            else
                mode = newMode;
        }

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
