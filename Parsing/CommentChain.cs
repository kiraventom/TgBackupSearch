using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public record CommentChain(int TopId, IReadOnlyDictionary<int, Comment> Comments);


