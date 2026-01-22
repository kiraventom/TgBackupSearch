using System.Runtime.CompilerServices;
using Serilog;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public class OfflineParser(ILogger logger, RunOptions runOptions, ChannelContext context) : MetadataParser(logger, context)
{
    private const int DAYS_CHUNK_SIZE = 250;

    protected override bool IgnoreMismatch => false;

    protected override async Task<IReadOnlyCollection<CommentChain>> ParseComments(CancellationToken ct)
    {
        if (runOptions.DiscussionGroupDir is null)
            return [];

        var commentParser = new CommentParser(Logger);
        // TODO Temp
        _ = await ParseDirectory<Comment>(runOptions.DiscussionGroupDir, commentParser, ct).ToListAsync();

        return commentParser.BuildComments();
    }

    protected override async IAsyncEnumerable<Media> ParseChannel(IReadOnlyCollection<CommentChain> comments, [EnumeratorCancellation] CancellationToken ct)
    {
        var postParser = new PostParser(Logger, runOptions, comments);
        await foreach (var media in ParseDirectory<Post>(runOptions.ChannelDir, postParser, ct))
            yield return media;
    }

    protected override void IncreaseDaysCount()
    {
        base.IncreaseDaysCount();
        if (_daysParsed % DAYS_CHUNK_SIZE == 0)
            Logger.Information("Parsed {count} days, still going...", _daysParsed);
    }

    protected override MetadataCache BuildCache(string file, out FileInfo fileInfo)
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

    private async IAsyncEnumerable<Media> ParseDirectory<T>(string dir, IItemParser itemParser, [EnumeratorCancellation] CancellationToken ct) where T : Item, new()
    {
        var progress = new Progress<int>(i => _totalCacheWrites += i);

        foreach (var dayDir in Directory.EnumerateDirectories(dir))
        {
            await foreach (var media in ParseDay<T>(itemParser, dayDir, ct, progress))
                yield return media;
        }
    }
}
