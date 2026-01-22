using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public interface IMetadataParser
{
    Task Init();
    IAsyncEnumerable<Media> GetUnrecognizedMedia(CancellationToken ct);
}

