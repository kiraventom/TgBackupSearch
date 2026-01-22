using Serilog;
using TgChannelLib;

namespace TgChannelRecognize;

public class ChannelInfo : IChannelInfo
{
    public long ChannelId { get; }
    public long? DiscussionGroupId { get; }

    public bool HasDiscussionGroup => DiscussionGroupId is not null;

    public ChannelInfo(ILogger logger, RunOptions runOptions)
    {
        if (runOptions.RunMode == RunMode.Network)
        {
            ChannelId = runOptions.ChannelId;
            DiscussionGroupId = runOptions.DiscussionGroupId;
            return;
        }

        long channelId;
        long? discussionGroupId = null;

        var channelDirName = Path.GetFileName(runOptions.ChannelDir.TrimEnd(Path.DirectorySeparatorChar));
        var channelIdStr = channelDirName.Substring(channelDirName.IndexOf('_') + 1);
        if (!long.TryParse(channelIdStr, out channelId))
            throw new NotSupportedException($"Failed to parse \"{channelIdStr}\" as long");

        if (!string.IsNullOrEmpty(runOptions.DiscussionGroupDir))
        {
            var discussionDirName = Path.GetFileName(runOptions.DiscussionGroupDir.TrimEnd(Path.DirectorySeparatorChar));
            var underscoreIndex = discussionDirName.IndexOf('_');
            if (underscoreIndex != -1)
            {
                var discussionIdStr = discussionDirName.Substring(+1);
                if (long.TryParse(discussionIdStr, out var di))
                    discussionGroupId = di;
                else
                    logger.Error("Failed to parse \"{discussion}\" as discussion group id", discussionIdStr);
            }
            else
            {
                logger.Error("\"{discussion}\" is not a valid discussion group folder name", runOptions.DiscussionGroupDir);
            }
        }

        ChannelId = channelId;
        DiscussionGroupId = discussionGroupId;
    }
}
