using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TgChannelRecognize.Parsing;
using TgChannelRecognize.Recognition;

namespace TgChannelRecognize;

public class AppService(ILogger logger, IServiceScopeFactory spf, IApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await ParseMetadata(ct);
        await RecognizeMedia(ct);

        logger.Information("Database is up to date, closing");
        lifetime.StopApplication();
    }

    private async Task ParseMetadata(CancellationToken ct)
    {
        using var scope = spf.CreateScope();
        var parser = scope.ServiceProvider.GetRequiredService<IMetadataParser>();

        await parser.ParseMetadata(ct);
    }

    private async Task RecognizeMedia(CancellationToken ct)
    {
        using var scope = spf.CreateScope();
        var recognizer = scope.ServiceProvider.GetRequiredService<Recognizer>();

        logger.Information("Starting to recognize media");
        await recognizer.Recognize(ct);
        logger.Information("Media recognized");
    }
}

