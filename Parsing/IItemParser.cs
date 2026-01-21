using System.Text.Json;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public interface IItemParser
{
    void ParseItem(Item item, JsonElement rootEl);
}


