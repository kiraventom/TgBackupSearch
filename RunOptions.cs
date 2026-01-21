using TgChannelLib;

namespace TgChannelRecognize;

public enum RunMode { Offline, Network }

public class RunOptions
{
    public RunMode RunMode { get; }
    public bool HasDiscussionGroup => 
        RunMode == RunMode.Offline 
        ? DiscussionGroupDir != null
        : DiscussionGroupId != null;

    public string ChannelDir { get; }
    public string DiscussionGroupDir { get; }

    public long ChannelId { get; }
    public long? DiscussionGroupId { get; }

    public RunOptions(string channelDir, string discussionGroupDir = null)
    {
        ChannelDir = channelDir;
        DiscussionGroupDir = discussionGroupDir;

        RunMode = RunMode.Offline;
    }

    public RunOptions(long channelId, long? discussionGroupId = null)
    {
        ChannelId = channelId;
        DiscussionGroupId = discussionGroupId;

        RunMode = RunMode.Network;
    }
}
