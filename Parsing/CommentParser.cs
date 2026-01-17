using System.Text.Json;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch.Parsing;

public class CommentParser(ILogger logger) : IItemParser
{
    private const int ORPHAN_TOP_ID = -1;
    private readonly Dictionary<int, Dictionary<int, Comment>> _comments = new();

    public IReadOnlyCollection<CommentChain> BuildComments()
    {
        return _comments.Select(c => new CommentChain(c.Key, c.Value)).ToList();
    }

    public void ParseItem(Item item, JsonElement rootEl)
    {
        if (item is not Comment comment)
            return;

        if (!(rootEl.GetProperty("reply_to") is { ValueKind: JsonValueKind.Object } replyTo))
        {
            // Just a message in a discussion group
            AddComment(ORPHAN_TOP_ID, comment);
            return;
        }

        if (replyTo.TryGetProperty("reply_to_top_id", out var topIdProp)
                && replyTo.TryGetProperty("reply_to_msg_id", out var msgIdProp))
        {
            var topId = topIdProp.GetInt32();
            var msgId = msgIdProp.GetInt32();
            if (topId == 0 && msgId != 0)
            {
                // Direct comment under post
                AddComment(msgId, comment);
            }
            else if (topId != 0 && msgId != 0)
            {
                // Reply to other comment
                AddComment(topId, comment);
            }
            else
            {
                logger.Warning("Comment {id} has reply_to, but is not top comment or a reply to comment", comment.TelegramId);
            }
        }
        else
        {
            logger.Warning("Comment {id} has reply_to, but does not have either reply_to_top_id or reply_to_msg_id", comment.TelegramId);
        }
    }

    private void AddComment(int topId, Comment comment)
    {
        if (_comments.ContainsKey(topId))
        {
            var chain = _comments[topId];
            if (chain.ContainsKey(comment.TelegramId))
                return;

            chain.Add(comment.TelegramId, comment);
        }
        else
        {
            _comments[topId] = new Dictionary<int, Comment>() { { comment.TelegramId, comment } };
        }
    }
}


