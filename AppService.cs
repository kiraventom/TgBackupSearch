using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TgChannelRecognize.Parsing;
using TgChannelRecognize.Recognition;

namespace TgChannelRecognize;

public class AppService(ILogger logger, IServiceScopeFactory spf, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = spf.CreateScope();
        var parser = scope.ServiceProvider.GetRequiredService<IMetadataParser>();
        await parser.Init();

        var medias = parser.GetUnrecognizedMedia(ct);
        var recognizer = scope.ServiceProvider.GetRequiredService<Recognizer>();
        await recognizer.Recognize(medias, ct);

        logger.Information("Database is up to date, closing");
        lifetime.StopApplication();
    }
}

