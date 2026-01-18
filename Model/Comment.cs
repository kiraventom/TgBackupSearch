using System.ComponentModel.DataAnnotations.Schema;

namespace TgBackupSearch.Model;

public class Comment : Item
{
    public int PostId { get; set; }

    [ForeignKey(nameof(PostId))]
    public Post Post { get; set; }

    public override string BuildLink(ChannelInfo info) => $"https://t.me/c/{info.DiscussionGroupId}/{TelegramId}";
}
