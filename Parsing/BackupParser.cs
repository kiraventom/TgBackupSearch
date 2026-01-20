using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch.Parsing;

public class BackupParser(ILogger logger, RunOptions runOptions, Paths paths, ChannelContext context)
{
    private const string METADATA_PATTERN = "metadata*.json";
    private const int DAYS_CHUNK_SIZE = 250;

    private int _totalCacheWrites = 0;
    private int _daysParsed = 0;

    public async Task ParseMetadata(CancellationToken ct)
    {
        logger.Information("Starting to parse metadata...");

        IReadOnlyCollection<CommentChain> comments = [];
        if (paths.DiscussionGroupDir is not null)
        {
            // Parse comments
            var commentParser = new CommentParser(logger);
            await ParseDirectory<Comment>(paths.DiscussionGroupDir, commentParser, ct);

            comments = commentParser.BuildComments();
        }

        // Parse posts
        var postParser = new PostParser(logger, runOptions, comments);
        await ParseDirectory<Post>(paths.ChannelDir, postParser, ct);

        await context.SaveChangesAsync();

        logger.Information("Successfully parsed metadata, days parsed: {days}, total cache writes: {writes}", _daysParsed, _totalCacheWrites);
    }

    private void IncreaseDaysCount()
    {
        ++_daysParsed;
        if (_daysParsed % DAYS_CHUNK_SIZE == 0)
            logger.Information("Parsed {count} days, still going...", _daysParsed);
    }

    private async Task ParseDirectory<T>(string dir, IItemParser itemParser, CancellationToken ct) where T : Item, new()
    {
        foreach (var dayDir in Directory.EnumerateDirectories(dir))
        {
            ct.ThrowIfCancellationRequested();

            logger.Debug("Starting to parse day {name}", Path.GetFileName(dayDir));

            var itemDirs = Directory.EnumerateDirectories(dayDir)
                .ToHashSet();

            var items = await context.Set<T>()
                .Where(i => itemDirs.Contains(i.DirPath))
                .ToDictionaryAsync(i => i.DirPath);

            int cacheWrites = 0;

            foreach (var itemDir in itemDirs)
            {
                logger.Debug("Starting to parse item {name}", Path.GetFileName(itemDir));

                var isNewItem = !items.ContainsKey(itemDir);

                var item = isNewItem
                    ? new T() { DirPath = itemDir }
                    : items[itemDir];

                var metadatas = Directory.EnumerateFiles(itemDir, METADATA_PATTERN)
                    .ToHashSet();

                if (metadatas.Count == 0)
                {
                    logger.Error("Directory {itemDir} does not contain any metadatas", itemDir);
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
                        logger.Error("Failed to build cache for metadata {path}, skipping", metadata);
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

                        logger.Warning("Cache mismatch at {file}. Updated cache", metadata);
                    }
                    else
                    {
                        context.Cache.Add(newCache);
                    }

                    ++cacheWrites;
                }

                if (isNewItem)
                    context.Set<T>().Add(item);

                logger.Debug("Successfully parsed item {name}", Path.GetFileName(itemDir));
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            logger.Debug("Successfully parsed day {name}, total cache writes: {writes}", Path.GetFileName(dayDir), cacheWrites);
            _totalCacheWrites += cacheWrites;

            IncreaseDaysCount();
        }
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

                logger.Error("Metadata {metadata} references media {id}, but there's no file with that name", metadata, fileId);
                return null;
            }
            else if (media.TryGetProperty("document", out var document))
            {
                var fileId = document.GetProperty("id").GetInt64();
                if (files.TryGetValue(fileId, out var file))
                    return (MediaType.Document, file);

                logger.Error("Metadata {metadata} references media {id}, but there's no file with that name", metadata, fileId);
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
                logger.Warning("Metadata {metadata}: property \"media\" is not null, but does not contain neither \"photo\" nor \"document\"", metadata.FullName);
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
            logger.Error(e.Message);
            logger.Error("Failed to read file {file}, skipping", file);
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
            logger.Error(e.Message);
            logger.Error("Failed to read file {file} metadata, skipping", file);
            return null;
        }

        return new MetadataCache
        {
            Path = file,
            LastWriteDT = lastWriteDt,
            Size = size
        };
    }
}
