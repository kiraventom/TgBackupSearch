using Serilog;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public class OfflineParser(ILogger logger, RunOptions runOptions, ChannelContext context) : MetadataParser(logger, context)
{
    private const int DAYS_CHUNK_SIZE = 250;

    protected override async Task<IReadOnlyCollection<CommentChain>> ParseComments(CancellationToken ct)
    {
        if (runOptions.DiscussionGroupDir is null)
            return [];

        var commentParser = new CommentParser(Logger);
        await ParseDirectory<Comment>(runOptions.DiscussionGroupDir, commentParser, ct);

        return commentParser.BuildComments();
    }

    protected override async Task ParseChannel(IReadOnlyCollection<CommentChain> comments, CancellationToken ct)
    {
        var postParser = new PostParser(Logger, runOptions, comments);
        await ParseDirectory<Post>(runOptions.ChannelDir, postParser, ct);
    }

    protected override void IncreaseDaysCount()
    {
        base.IncreaseDaysCount();
        if (_daysParsed % DAYS_CHUNK_SIZE == 0)
            Logger.Information("Parsed {count} days, still going...", _daysParsed);
    }

    private async Task ParseDirectory<T>(string dir, IItemParser itemParser, CancellationToken ct) where T : Item, new()
    {
        var progress = new Progress<int>(i => _totalCacheWrites += i);

        foreach (var dayDir in Directory.EnumerateDirectories(dir))
        {
            await ParseDay<T>(itemParser, dayDir, ct, progress);
        }
    }
}
