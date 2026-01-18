using System.ComponentModel.DataAnnotations.Schema;

namespace TgBackupSearch.Model;

public class Post : Item
{
    [InverseProperty(nameof(Comment.Post))]
    public List<Comment> Comments { get; } = new();
}
