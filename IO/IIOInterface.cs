using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public interface IIOInterface
{
    Task<ISearchQuery> GetInput();
    Task SetOutput(IReadOnlyCollection<Item> items);
}
