using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public class WebIO : IIOInterface
{
    public Task<string> GetInput()
    {
        // TODO
        return Task.FromResult(string.Empty);
    }

    public Task SetOutput(IQueryable<Item> items)
    {
        // TODO
        return Task.CompletedTask;
    }
}
