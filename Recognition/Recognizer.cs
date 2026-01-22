using Microsoft.EntityFrameworkCore;
using Serilog;
using TgChannelLib.Model;
using TgChannelRecognize.Video;
using System.Diagnostics;
using System.Globalization;

namespace TgChannelRecognize.Recognition;

public class Recognizer(ILogger logger, RunOptions runOptions, Config config, TgChannelLib.Model.ChannelContext context, FrameExtractor frameExtractor)
{
    private const string TESSERACT_FILENAME = "tesseract";
    private const string TSV_TYPE = "tsv";
    private const string TSV_EXTENSION = $".{TSV_TYPE}";
    private const float CONFIDENCE_THRESHOLD = 0.75f;

    private const int OFFLINE_CHUNK_SIZE = 50;
    private const int NETWORK_CHUNK_SIZE = 1;

    private int _totalRecognizedCount = 0;

    private async Task RecognizeMedia(Media media, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        switch (media.Type)
        {
            case MediaType.Photo:
                await RecognizeImage(media.FilePath, media, ct);
                break;

            case MediaType.Document:
                var frames = await frameExtractor.ExtractFrames(media);

                foreach (var frame in frames)
                {
                    ct.ThrowIfCancellationRequested();
                    await RecognizeImage(frame, media, ct);

                    File.Delete(frame);
                }
                break;

            default:
                logger.Warning("Media {id} has type {type}", media.MediaId, media.Type.ToString());
                break;
        }
    }

    public async Task Recognize(IAsyncEnumerable<Media> medias, CancellationToken ct)
    {
        logger.Information("Starting to recognize media");

        var chunkSize = runOptions.RunMode == RunMode.Offline
            ? OFFLINE_CHUNK_SIZE
            : NETWORK_CHUNK_SIZE;

        var chunks = medias.Chunk(chunkSize);

        await foreach (var chunk in chunks)
        {
            foreach (var media in chunk)
            {
                await RecognizeMedia(media, ct);
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        logger.Information("Media recognized");
    }

    private async Task RecognizeImage(string filepath, Media media, CancellationToken ct)
    {
        var token = await RecognizeText(filepath, ct);
        media.Recognitions.Add(new TgChannelLib.Model.Recognition() { Text = token.Text, Confidence = token.Confidence });
        IncreaseRecognizedCount();
    }

    private void IncreaseRecognizedCount()
    {
        ++_totalRecognizedCount;

        if (_totalRecognizedCount % OFFLINE_CHUNK_SIZE == 0)
            logger.Information("Recognized {count} files, still going...", _totalRecognizedCount);
    }

    private async Task<OcrToken> RecognizeText(string filepath, CancellationToken ct)
    {
        var tokens = await RunTesseract(filepath, ct);
        if (tokens.Count == 0)
            return new OcrToken(string.Empty, 100);

        var text = string.Join(' ', tokens.Select(t => t.Text));
        var meanConfidence = tokens.Average(t => t.Confidence);

        return new OcrToken(text, meanConfidence);
    }

    private async Task<IReadOnlyCollection<OcrToken>> RunTesseract(string filepath, CancellationToken ct)
    {
        if (!File.Exists(filepath))
        {
            logger.Error("Can't run Tesseract, file '{path}' does not exist", filepath);
            return [];
        }

        var tsvBaseName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", string.Empty));
        var tsvFile = Path.ChangeExtension(tsvBaseName, TSV_EXTENSION);

        var languageArg = string.Join('+', config.Languages);
        var args = $"\"{filepath}\" \"{tsvBaseName}\" -l {languageArg} --dpi 300 {TSV_TYPE}";

        var psi = new ProcessStartInfo
        {
            FileName = TESSERACT_FILENAME,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            logger.Error("Failed to start tesseract process");
            return [];
        }

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            logger.Error("Tesseract failed with exit code {code}: {error}", process.ExitCode, error);
            return [];
        }

        if (!File.Exists(tsvFile))
        {
            logger.Error("Tesseract output file not found: {file}", tsvFile);
            return [];
        }

        using var stream = File.OpenText(tsvFile);
        var token = await ParseTsv(stream, ct);

        File.Delete(tsvFile);
        return token;
    }

    private async Task<IReadOnlyCollection<OcrToken>> ParseTsv(StreamReader stream, CancellationToken ct)
    {
        Dictionary<string, OcrToken> tokens = [];

        int lineNumber = -1;
        string line;

        while ((line = await stream.ReadLineAsync(ct)) != null)
        {
            ++lineNumber;

            if (lineNumber == 0)
                continue;

            var parts = line.Split('\t');
            if (parts.Length < 12)
                continue;

            var level = parts[0];
            if (level != "5")
                continue;

            if (!int.TryParse(parts[6], out var x) ||
                !int.TryParse(parts[7], out var y) ||
                !int.TryParse(parts[8], out var width) ||
                !int.TryParse(parts[9], out var height) ||
                !float.TryParse(parts[10], CultureInfo.InvariantCulture, out var confidence))
            {
                continue;
            }

            if (confidence < CONFIDENCE_THRESHOLD)
                continue;

            var text = parts[11];
            if (string.IsNullOrWhiteSpace(text))
                continue;

            text = text.Trim().ToLowerInvariant();
            tokens[text] = new OcrToken(text, confidence / 100f);
        }

        return tokens.Values;
    }
}
