using Serilog;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public class NetworkParser(ILogger logger, ChannelInfo channelInfo, ChannelContext context) : MetadataParser(logger, context)
{
    protected override Task ParseChannel(IReadOnlyCollection<CommentChain> comments, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    protected override Task<IReadOnlyCollection<CommentChain>> ParseComments(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

