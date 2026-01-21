namespace TgChannelRecognize.Parsing;

public interface IMetadataParser
{
    Task ParseMetadata(CancellationToken ct);
}

