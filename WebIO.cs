using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public class WebIO : IIOInterface
{
    public Task<ISearchQuery> GetInput()
    {
        // TODO
        return Task.FromResult<ISearchQuery>(new SearchQuery());
    }

    public Task SetOutput(IReadOnlyCollection<Item> items)
    {
        // TODO
        return Task.CompletedTask;
    }
}
