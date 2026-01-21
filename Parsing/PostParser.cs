using System.Text.Json;
using Serilog;
using TgChannelLib.Model;

namespace TgChannelRecognize.Parsing;

public class PostParser(ILogger logger, RunOptions runOptions, IReadOnlyCollection<CommentChain> comments) : IItemParser
{
    public void ParseItem(Item item, JsonElement rootEl)
    {
        if (!runOptions.HasDiscussionGroup)
            return;

        if (item is not Post post)
            return;

        // Album group metadata already parsed
        if (post.Comments.Count != 0)
            return;

        if (!rootEl.TryGetProperty("replies", out var repliesEl))
            return;

        if (repliesEl.ValueKind != JsonValueKind.Object)
            return;

        if (!repliesEl.TryGetProperty("max_id", out var maxIdEl))
        {
            logger.Warning("Post {id} has replies, but does not have max_id", post.TelegramId);
            return;
        }

        var maxId = maxIdEl.GetInt32();

        if (maxId != 0)
        {
            var commentChain = comments.FirstOrDefault(c => c.Comments.ContainsKey(maxId));
            if (commentChain is null)
            {
                logger.Warning("{item} has max_id={max_id}, but no comment chain with such comment was found", post.TelegramId, maxId);
                return;
            }

            foreach (var comment in commentChain.Comments.Values)
            {
                post.Comments.Add(comment);
            }
        }
    }
}


