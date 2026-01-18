using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TgBackupSearch.Model;

public class ChannelContextDesignTimeFactory : IDesignTimeDbContextFactory<ChannelContext>
{
    public ChannelContext CreateDbContext(string[] args)
    {
        var didParse = Program.TryGetRunOptions(args, out var channelDir, out _, out _);

        if (didParse == false)
            throw new NotSupportedException("Failed to parse args");

        var paths = new Paths(Program.CreateAppDataDir(), null, null, channelDir, null);

        var channelInfo = new ChannelInfo(new StubLogger(), paths);

        var builder = new DbContextOptionsBuilder<ChannelContext>();
        Program.SetContextOptions(builder, paths, channelInfo);
        return new ChannelContext(builder.Options);
    }
}

