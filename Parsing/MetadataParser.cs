using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public abstract class MetadataParser(ILogger logger, ChannelContext context) : IMetadataParser
{
    protected const string METADATA_PATTERN = "metadata*.json";

    protected ILogger Logger { get; } = logger;

    protected int _daysParsed = 0;
    protected int _totalCacheWrites = 0;

    public async Task ParseMetadata(CancellationToken ct)
    {
        Logger.Information("Starting to parse metadata...");

        IReadOnlyCollection<CommentChain> comments = await ParseComments(ct);
        await ParseChannel(comments, ct);
        await context.SaveChangesAsync();

        Logger.Information("Successfully parsed metadata, days parsed: {days}, total cache writes: {writes}", _daysParsed, _totalCacheWrites);
    }

    protected abstract Task ParseChannel(IReadOnlyCollection<CommentChain> comments, CancellationToken ct);
    protected abstract Task<IReadOnlyCollection<CommentChain>> ParseComments(CancellationToken ct);

    protected async Task ParseDay<T>(IItemParser itemParser, string dayDir, CancellationToken ct, IProgress<int> cacheWritesProgress = null) where T : Item, new()
    {
        ct.ThrowIfCancellationRequested();

        Logger.Debug("Starting to parse day {name}", Path.GetFileName(dayDir));

        var itemDirs = Directory.EnumerateDirectories(dayDir)
            .ToHashSet();

        var items = await context.Set<T>()
            .Where(i => itemDirs.Contains(i.DirPath))
            .ToDictionaryAsync(i => i.DirPath);

        foreach (var itemDir in itemDirs)
        {
            Logger.Debug("Starting to parse item {name}", Path.GetFileName(itemDir));

            var isNewItem = !items.ContainsKey(itemDir);

            var item = isNewItem
                ? new T() { DirPath = itemDir }
                : items[itemDir];

            var metadatas = Directory.EnumerateFiles(itemDir, METADATA_PATTERN)
                .ToHashSet();

            if (metadatas.Count == 0)
            {
                Logger.Error("Directory {itemDir} does not contain any metadatas", itemDir);
                continue;
            }

            var otherFiles = Directory.EnumerateFiles(itemDir)
                .Except(metadatas)
                .ToDictionary(f => long.Parse(Path.GetFileNameWithoutExtension(f)));

            var cached = await context.Cache
                .AsNoTracking()
                .Where(c => metadatas.Contains(c.Path))
                .ToDictionaryAsync(c => c.Path);

            foreach (var metadata in metadatas)
            {
                bool mismatch = false;
                var newCache = BuildCache(metadata, out var fi);

                if (newCache is null)
                {
                    Logger.Error("Failed to build cache for metadata {path}, skipping", metadata);
                    continue;
                }

                if (cached.TryGetValue(metadata, out var cache))
                {
                    if (cache.Equals(newCache))
                        continue;

                    mismatch = true;
                }

                await ParseMetadata(fi, item, otherFiles, itemParser);

                if (mismatch)
                {
                    await context.Cache
                        .Where(c => c.Path == metadata)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(c => c.LastWriteDT, newCache.LastWriteDT)
                            .SetProperty(c => c.Size, newCache.Size));

                    Logger.Warning("Cache mismatch at {file}. Updated cache", metadata);
                }
                else
                {
                    context.Cache.Add(newCache);
                }

                cacheWritesProgress?.Report(1);
            }

            if (isNewItem)
                context.Set<T>().Add(item);

            Logger.Debug("Successfully parsed item {name}", Path.GetFileName(itemDir));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Logger.Debug("Successfully parsed day {name}", Path.GetFileName(dayDir));
        IncreaseDaysCount();
    }

    private async Task ParseMetadata(FileInfo metadata, Item item, IReadOnlyDictionary<long, string> otherFiles, IItemParser itemParser)
    {
        JsonDocument jDoc;

        using (var metadataFile = metadata.OpenRead())
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
            item.DT = dt.UtcDateTime;

        if (!string.IsNullOrEmpty(text))
            item.Text = text;

        var media = GetMedia(metadata, rootEl, otherFiles);

        if (media is { } m)
        {
            var mediaModel = new Media()
            {
                TelegramId = id,
                DT = dt.UtcDateTime,
                FilePath = m.File,
                Type = m.Type,
            };

            item.Media.Add(mediaModel);
        }

        itemParser.ParseItem(item, rootEl);
    }

    private (MediaType Type, string File)? GetMedia(FileInfo metadata, JsonElement rootEl, IReadOnlyDictionary<long, string> files)
    {
        var media = rootEl.GetProperty("media");
        if (media.ValueKind != JsonValueKind.Null)
        {
            if (media.TryGetProperty("photo", out var photo))
            {
                var fileId = photo.GetProperty("id").GetInt64();
                if (files.TryGetValue(fileId, out var file))
                    return (MediaType.Photo, file);

                Logger.Error("Metadata {metadata} references media {id}, but there's no file with that name", metadata, fileId);
                return null;
            }
            else if (media.TryGetProperty("document", out var document))
            {
                var fileId = document.GetProperty("id").GetInt64();
                if (files.TryGetValue(fileId, out var file))
                    return (MediaType.Document, file);

                Logger.Error("Metadata {metadata} references media {id}, but there's no file with that name", metadata, fileId);
                return null;
            }
            else if (media.TryGetProperty("webpage", out _))
            {
                // Ignore for now
            }
            else if (media.TryGetProperty("poll", out _))
            {
                // Ignore for now
            }
            else if (media.TryGetProperty("extended_media", out _))
            {
                // Ignore for now
            }
            else
            {
                Logger.Warning("Metadata {metadata}: property \"media\" is not null, but does not contain neither \"photo\" nor \"document\"", metadata.FullName);
                return null;
            }
        }

        return null;
    }

    private MetadataCache BuildCache(string file, out FileInfo fileInfo)
    {
        fileInfo = null;

        try
        {
            fileInfo = new FileInfo(file);
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            Logger.Error("Failed to read file {file}, skipping", file);
            return null;
        }

        DateTimeOffset lastWriteDt;
        long size;

        try
        {
            lastWriteDt = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
            size = fileInfo.Length;
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            Logger.Error("Failed to read file {file} metadata, skipping", file);
            return null;
        }

        return new MetadataCache
        {
            Path = file,
            LastWriteDT = lastWriteDt,
            Size = size
        };
    }

    protected virtual void IncreaseDaysCount()
    {
        ++_daysParsed;
    }
}

