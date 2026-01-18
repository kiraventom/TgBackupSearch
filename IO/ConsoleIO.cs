using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public class ConsoleIO(ChannelInfo channelInfo) : IIOInterface
{
    public Task<string> GetInput()
    {
        Console.WriteLine("Enter your prompt: ");
        return Task.FromResult(Console.ReadLine());
    }

    public Task SetOutput(IQueryable<Item> items)
    {
        int count = 1;
        foreach (var item in items)
        {
            var link = item.BuildLink(channelInfo);
            Console.Write(count);
            Console.WriteLine($". {link}");
        }

        return Task.CompletedTask;
    }
}
