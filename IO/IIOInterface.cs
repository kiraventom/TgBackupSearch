using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public interface IIOInterface
{
    Task<string> GetInput();
    Task SetOutput(IQueryable<Item> items);
}
