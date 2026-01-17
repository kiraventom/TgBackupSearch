using TgBackupSearch.Model;

namespace TgBackupSearch.Parsing;

public record CommentChain(int TopId, IReadOnlyDictionary<int, Comment> Comments);


