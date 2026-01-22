using System.Runtime.CompilerServices;
using Serilog;
using TgChannelBackup.Core;
using TgChannelLib.Model;
using TL;

namespace TgChannelRecognize.Parsing;

public class NetworkParser(ILogger logger, RunOptions runOptions, ChannelContext context, TelegramService telegramService, MessageProcessor messageProcessor) : MetadataParser(logger, context)
{
    protected override bool IgnoreMismatch => true;

    public override Task Init() => telegramService.LogIn();

    protected override async IAsyncEnumerable<Media> ParseChannel(IReadOnlyCollection<CommentChain> comments, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = await telegramService.GetChannelById(runOptions.ChannelId);
        var startId = Context.Posts.Any() ? Context.Posts.Select(p => p.TelegramId).Max() : 0;
        var postParser = new PostParser(Logger, runOptions, comments);

        string lastDayPath = null;
        await foreach (var result in ProcessMessages(channel, startId, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (!result.Success)
                continue;

            var postPath = Path.GetDirectoryName(result.Path);
            var dayPath = Path.GetDirectoryName(postPath);

            if (lastDayPath == dayPath)
                continue;

            if (lastDayPath is null)
            {
                lastDayPath = dayPath;
                continue;
            }

            if (lastDayPath != dayPath)
            {
                await foreach (var media in ParseDay<Post>(postParser, lastDayPath, ct))
                    yield return media;

                Directory.Delete(lastDayPath, recursive: true);
                lastDayPath = dayPath;
            }
        }

        if (lastDayPath != null)
        {
            await foreach (var media in ParseDay<Post>(postParser, lastDayPath, ct))
                yield return media;

            Directory.Delete(lastDayPath, recursive: true);
        }
    }

    private async IAsyncEnumerable<DownloadResult> ProcessMessages(InputPeerChannel channel, long startId, [EnumeratorCancellation] CancellationToken ct)
    {
        var basePath = Path.Combine(Path.GetTempPath(), Program.PROJECT_NAME);

        await foreach (var messageBase in telegramService.ScrollHistory(channel, (int)startId))
        {
            ct.ThrowIfCancellationRequested();

            if (messageBase is not Message message)
                continue;
            
            var postPath = MessageProcessor.BuildPath(basePath, message);

            DownloadResult result;
            try
            {
                result = await messageProcessor.ProcessMessage(message, postPath, false, ct);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to fetch {message_id}", message.ID);
                result = new DownloadResult() { Success = false };
            }

            yield return result;
        }
    }

    private async Task<InputPeerChannel> GetDiscussionGroup(InputPeerChannel channel)
    {
        InputPeerChannel discussionGroup = null;
       
        if (runOptions.HasDiscussionGroup)
        {
            discussionGroup = await telegramService.GetCommentsGroup(channel);
            if (discussionGroup is null)
            {
                Logger.Error("Failed to get discussion group of channel {channelId}", runOptions.ChannelId);
            }
            else if (discussionGroup.ID != runOptions.DiscussionGroupId)
            {
                Logger.Error("Discussion group of channel has ID={groupId}, but user specified {userId}", discussionGroup.ID, runOptions.DiscussionGroupId);
                discussionGroup = null;
            }
        }

        return discussionGroup;
    }

    protected override async Task<IReadOnlyCollection<CommentChain>> ParseComments(CancellationToken ct)
    {
        var channel = await telegramService.GetChannelById(runOptions.ChannelId);
        var discussionGroup = GetDiscussionGroup(channel);

        if (discussionGroup is null)
        {
            throw new NotSupportedException("Can't parse comments: discusssion group is null");
        }

        Logger.Warning("Comments parsing is yet to implemented");
        return [];
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

        return new MetadataCache
        {
            Path = file,
        };
    }

}

