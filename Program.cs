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

internal class Program
{
    private const string PROJECT_NAME = "TgBackupSearch";

    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();

        var appDataDir = CreateAppDataDir();
        var appConfigDir = CreateAppConfigDir();

        var logger = BuildLogger(appDataDir);

        var configFile = Path.Combine(appConfigDir, "config.json");
        if (!Config.TryLoad(configFile, out var config))
        {
            logger.Fatal("Failed to load config, closing");
            return;
        }

        builder.Services
            .AddSerilog(logger)
            .AddSingleton(config)
            .AddDbContext<MainContext>(static (sp, options) =>
            {
                var config = sp.GetRequiredService<Config>();
                options.UseSqlite($"Data Source={config.DatabasePath};");
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
