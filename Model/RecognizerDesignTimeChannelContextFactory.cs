using TgChannelLib.Model;
using TgChannelLib;

namespace TgChannelRecognize.Model;

public class RecognizerDesignTimeChannelContextFactory : ChannelContextDesignTimeFactory
{
    protected override IAppPaths BuildAppPaths() => new AppPaths(Program.CreateAppDataDir(), null, null);

    protected override bool TryGetChannelInfo(string[] args, out IChannelInfo channelInfo)
    {
        channelInfo = null;

        var didParse = Program.TryGetRunOptions(args, out var runOptions);

        if (didParse == false)
            return false;

        channelInfo = new ChannelInfo(new LoggerStub(), runOptions);
        return true;
    }
}

