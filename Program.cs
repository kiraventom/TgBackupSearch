using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using TgBackupSearch.Model;
using TgBackupSearch.Parsing;

namespace TgBackupSearch;

public class SearchService(ILogger logger, IServiceScopeFactory spf) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fill database
        // TODO: Do not parse each time
        using (var scope = spf.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();
            var backupParser = scope.ServiceProvider.GetRequiredService<BackupParser>();
            await backupParser.FillDb(context);
        }

        // Recognition
        // TODO:
        // Run OCR on files, write result to Media.Recognition
        // If picture: run OCR
        // If video: pick frames with ffmpeg, run OCR

        // Search
        // TODO
    }
}

public record Paths(string AppDataDir, string AppConfigDir, string ChannelDir, string DiscussionGroupDir = null);

internal class Program
{
    private const string PROJECT_NAME = "TgBackupSearch";

    private static async Task Main(string[] args)
    {
        var appDataDir = CreateAppDataDir();
        var appConfigDir = CreateAppConfigDir();

        var logger = BuildLogger(appDataDir);

        if (!TryGetRunOptions(args, out var channelDir, out var discussionDir))
        {
            logger.Fatal("Failed to parse arguments, closing");
            return;
        }

        var paths = new Paths(appDataDir, appConfigDir, channelDir, discussionDir);

        // var configFile = Path.Combine(appConfigDir, "config.json");
        // if (!Config.TryLoad(configFile, out var config))
        // {
        //     logger.Fatal("Failed to load config, closing");
        //     return;
        // }

        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSerilog(logger)
            // .AddSingleton(config)
            .AddSingleton(paths)
            .AddDbContext<MainContext>(static (sp, options) =>
            {
                var paths = sp.GetRequiredService<Paths>();
                var dbPath = Path.Combine(paths.AppDataDir, "main.db");
                options.UseSqlite($"Data Source={dbPath};");
            })
            .AddTransient<BackupParser>()
            .AddHostedService<SearchService>();

        var host = builder.Build();

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

    private static bool TryGetRunOptions(string[] args, out string channelDir, out string discussionDir)
    {
        channelDir = null;
        discussionDir = null;

        var channelDirOpt = new Option<string>("--channelDir", "-c")
        {
            Required = true,
            Description = "Path to the directory of channel backup"
        };

        var discussionDirOpt = new Option<string>("--discussionDir", "-d")
        {
            Required = false,
            Description = "Path to the directory of discussion group backup. If not specified, discussion group will not be included in search"
        };

        var rootCommand = new RootCommand()
        {
            channelDirOpt, discussionDirOpt
        };

        var parseResult = rootCommand.Parse(args);
        foreach (var error in parseResult.Errors)
            Log.Fatal(error.Message);

        if (parseResult.Errors.Any())
            return false;

        channelDir = parseResult.GetRequiredValue(channelDirOpt);
        discussionDir = parseResult.GetValue(discussionDirOpt);

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

    private static string CreateAppDataDir()
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
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Logger = logger;

        return logger;
    }
}
