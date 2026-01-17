using System.Text.Json;
using TgBackupSearch.Model;

namespace TgBackupSearch.Parsing;

public interface IItemParser
{
    void ParseItem(Item item, JsonElement rootEl);
}


