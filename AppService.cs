using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TgBackupSearch.IO;
using TgBackupSearch.Parsing;
using TgBackupSearch.Recognition;

namespace TgBackupSearch;

public class AppService(ILogger logger, IIOInterface io, IServiceScopeFactory spf) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await ParseMetadata(ct);
        await RecognizeMedia(ct);

        while (true)
        {
            using var scope = spf.CreateScope();
            var prompt = await io.GetInput();

            var searchService = scope.ServiceProvider.GetRequiredService<SearchService>();
            var results = searchService.Search(prompt);

            await io.SetOutput(results);
        }
    }

    private async Task ParseMetadata(CancellationToken ct)
    {
        using var scope = spf.CreateScope();
        var backupParser = scope.ServiceProvider.GetRequiredService<BackupParser>();

        // Fill database
        await backupParser.ParseMetadata(ct);
    }

    private async Task RecognizeMedia(CancellationToken ct)
    {
        using var scope = spf.CreateScope();
        var recognizer = scope.ServiceProvider.GetRequiredService<Recognizer>();

        // Recognize stuff
        logger.Information("Starting to recognize media");
        await recognizer.Recognize(ct);
        logger.Information("Media recognized");
    }
}

