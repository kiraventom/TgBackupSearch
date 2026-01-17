using System.Text.Json;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch.Parsing;

public class BackupParser(ILogger logger, Config config)
{
    public async Task FillDb(MainContext context)
    {
        var channelDir = config.ChannelDir;

        if (string.IsNullOrEmpty(channelDir) || !Directory.Exists(channelDir))
            throw new NotSupportedException($"{nameof(Config)}.{nameof(Config.ChannelDir)} = '{config.ChannelDir}' is invalid path");

        // Parse comments
        var commentParser = new CommentParser(logger);
        await ParseDirectory<Comment>(context, channelDir, commentParser);

        var comments = commentParser.BuildComments();

        // Parse posts
        var postParser = new PostParser(logger, comments);
        await ParseDirectory<Post>(context, channelDir, postParser);

        await context.SaveChangesAsync();
    }

    private async Task ParseDirectory<T>(MainContext context, string channelDir, IItemParser itemParser) where T : Item, new()
    {
        foreach (var dayDir in Directory.EnumerateDirectories(channelDir))
        {
            foreach (var itemDir in Directory.EnumerateDirectories(dayDir))
            {
                var item = new T()
                {
                    DirPath = itemDir
                };

                var metadatas = Directory.GetFiles(itemDir, "metadata*.json");
                var otherFiles = Directory.EnumerateFiles(itemDir)
                    .Except(metadatas)
                    .ToDictionary(f => long.Parse(Path.GetFileNameWithoutExtension(f)));

                if (metadatas.Length == 0)
                    throw new NotSupportedException($"Directory {itemDir} does not contain any metadatas");

                foreach (var metadata in metadatas)
                {
                    await ParseMetadata(metadata, item, otherFiles, itemParser);
                }

                context.Set<T>().Add(item);
            }
        }
    }

    private async Task ParseMetadata(string metadata, Item item, IReadOnlyDictionary<long, string> otherFiles, IItemParser itemParser)
    {
        JsonDocument jDoc;

        using (var metadataFile = File.OpenRead(metadata))
        {
            jDoc = await JsonDocument.ParseAsync(metadataFile);
        }

        var rootEl = jDoc.RootElement;

        var id = rootEl.GetProperty("id").GetInt32();
        var dt = rootEl.GetProperty("date").GetDateTimeOffset();
        var text = rootEl.GetProperty("message").GetString();
        var groupId = rootEl.GetProperty("grouped_id").GetInt64();

        // All props are either the only or the first
        if (item.TelegramId == default || item.TelegramId > id)
            item.TelegramId = id;

        if (item.DT == default || item.DT > dt)
            item.DT = dt;

        if (!string.IsNullOrEmpty(text))
            item.Text = text;

        var media = GetMedia(metadata, rootEl, otherFiles);

        if (media is { } m)
        {
            var mediaModel = new Media()
            {
                TelegramId = id,
                DT = dt,
                FilePath = m.File,
                Type = m.Type,
            };

            item.Media.Add(mediaModel);
        }

        itemParser.ParseItem(item, rootEl);
    }

    private (MediaType Type, string File)? GetMedia(string metadata, JsonElement rootEl, IReadOnlyDictionary<long, string> files)
    {
        var media = rootEl.GetProperty("media");
        if (media.ValueKind != JsonValueKind.Null)
        {
            if (media.TryGetProperty("photo", out var photo))
            {
                var fileId = photo.GetProperty("id").GetInt64();
                return (MediaType.Photo, files[fileId]);
            }
            else if (media.TryGetProperty("document", out var document))
            {
                var fileId = photo.GetProperty("id").GetInt64();
                return (MediaType.Document, files[fileId]);
            }
            else
            {
                logger.Warning("Metadata {metadata}: property \"media\" is not null, but does not contain neither \"photo\" nor \"document\"", metadata);
                return null;
            }
        }

        return null;
    }
}

