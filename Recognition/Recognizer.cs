using Microsoft.EntityFrameworkCore;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using TgBackupSearch.Model;
using TgBackupSearch.Video;

namespace TgBackupSearch.Recognition;

public class Recognizer(ILogger logger, Config config, Paths paths, MainContext context, FrameExtractor frameExtractor)
{
    public async Task Recognize()
    {
        var medias = context.Media.Include(p => p.Recognitions);

        var engines = config.Languages
            .Select(l => new TesseractEngine(paths.TesseractDir, l))
            .ToList();

        foreach (var media in medias.Where(m => m.Recognitions.Count == 0))
        {
            if (media.Type == MediaType.Photo)
            {
                var text = await RecognizeText(engines, media.FilePath);
                media.Recognitions.Add(new Model.Recognition() { Text = text });
            }
            else if (media.Type == MediaType.Document)
            {
                var frames = await frameExtractor.ExtractFrames(media);
                var tasks = frames.Select(f => RecognizeText(engines, media.FilePath));
                var texts = await Task.WhenAll(tasks);

                foreach (var text in texts)
                    media.Recognitions.Add(new Model.Recognition() { Text = text });
            }
            else
            {
                logger.Warning("Media {id} has type {type}", media.MediaId, media.Type.ToString());
            }
        }

        foreach (var engine in engines)
            engine.Dispose();

        await context.SaveChangesAsync();
    }

    private async Task<string> RecognizeText(IReadOnlyCollection<TesseractEngine> engines, string filepath)
    {
        const float threshold = 0.75f;

        using var pix = Pix.LoadFromFile(filepath);

        var seedEngine = engines.FirstOrDefault();
        if (seedEngine is null)
            return null;

        var otherEngines = engines.Skip(1).ToList();

        var tokens = new List<OcrToken>();

        using var seedPage = await Task.Run(() => seedEngine.Process(pix));
        using (var iter = seedPage.GetIterator())
        {
            iter.Begin();
            do
            {
                var word = iter.GetText(PageIteratorLevel.Word).Trim();
                if (string.IsNullOrEmpty(word))
                    continue;

                if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                    continue;

                var confidence = iter.GetConfidence(PageIteratorLevel.Word);
                var seedToken = new OcrToken(word, confidence);

                if (confidence >= threshold || engines.Count == 1)
                {
                    tokens.Add(seedToken);
                    continue;
                }

                using var crop = await Crop(filepath, bounds);
                var token = await RecognizeCrop(otherEngines, crop);

                if (token is not null && token.Confidence > seedToken.Confidence)
                    tokens.Add(token);
                else
                    tokens.Add(seedToken);
            }
            while (iter.Next(PageIteratorLevel.Word));
        }

        var text = string.Join(' ', tokens.Select(t => t.Text));
        return text;
    }

    private async Task<OcrToken> RecognizeCrop(IReadOnlyCollection<TesseractEngine> engines, Pix pix)
    {
        List<OcrToken> tokens = [];

        foreach (var engine in engines)
        {
            var prevMode = engine.DefaultPageSegMode;
            engine.DefaultPageSegMode = PageSegMode.SingleWord;
            using var page = await Task.Run(() => engine.Process(pix));
            engine.DefaultPageSegMode = prevMode;

            var word = page.GetText().Trim();
            if (string.IsNullOrEmpty(word))
                continue;

            var confidence = page.GetMeanConfidence();
            var token = new OcrToken(word, confidence);
            tokens.Add(token);
        }

        if (tokens.Count == 0)
            return null;

        return tokens.MaxBy(t => t.Confidence);
    }

    private async Task<Pix> Crop(string file, Rect bbox)
    {
        using var img = await Image.LoadAsync(file);
        img.Mutate(i => i.Crop(new Rectangle(bbox.X1, bbox.Y1, bbox.Width, bbox.Height)));
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        var bytes = ms.ToArray();
        return Pix.LoadFromMemory(bytes);
    }
}
