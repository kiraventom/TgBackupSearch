using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TgBackupSearch.Parsing;
using TgBackupSearch.Recognition;

namespace TgBackupSearch;

public class SearchService(ILogger logger, IServiceScopeFactory spf) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = spf.CreateScope();
        var backupParser = scope.ServiceProvider.GetRequiredService<BackupParser>();
        var recognizer = scope.ServiceProvider.GetRequiredService<Recognizer>();
        //
        // Fill database
        // TODO: Do not parse each time
        logger.Information("Starting to fill database");
        await backupParser.ParseMetadata();
        logger.Information("Database filled");

        // Recognition
        logger.Information("Starting to recognize media");
        await recognizer.Recognize();
        logger.Information("Media recognized");

        // Search
        // TODO
    }
}
