using Serilog;

namespace TgBackupSearch;

public class ChannelInfo
{
    public long ChannelId { get; }
    public long? DiscussionGroupId { get; }

    public ChannelInfo(ILogger logger, Paths paths)
    {
        long channelId;
        long? discussionGroupId = null;

        var channelDirName = Path.GetFileName(paths.ChannelDir.TrimEnd(Path.DirectorySeparatorChar));
        var channelIdStr = channelDirName.Substring(channelDirName.IndexOf('_') + 1);
        if (!long.TryParse(channelIdStr, out channelId))
            throw new NotSupportedException($"Failed to parse \"{channelIdStr}\" as long");

        if (!string.IsNullOrEmpty(paths.DiscussionGroupDir))
        {
            var discussionDirName = Path.GetFileName(paths.DiscussionGroupDir.TrimEnd(Path.DirectorySeparatorChar));
            var underscoreIndex = discussionDirName.IndexOf('_');
            if (underscoreIndex != -1)
            {
                var discussionIdStr = discussionDirName.Substring( + 1);
                if (long.TryParse(discussionIdStr, out var di))
                    discussionGroupId = di;
                else
                    logger.Warning("Failed to parse \"{discussion}\" as discussion group id", discussionIdStr);
            }
            else
            {
                logger.Warning("\"{discussion}\" is not a valid discussion group folder name", paths.DiscussionGroupDir);
            }
        }

        ChannelId = channelId;
        DiscussionGroupId = discussionGroupId;
    }
}
