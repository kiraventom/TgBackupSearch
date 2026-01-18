using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TgBackupSearch.Model;

public abstract class Item
{
    [Key]
    public int ItemId { get; set; }

    [InverseProperty(nameof(Model.Media.Item))]
    public List<Media> Media { get; } = new();

    [NotMapped]
    public bool IsAlbum => Media.Count > 1;

    [NotMapped]
    public bool IsTextOnly => Media.Count == 0;

    public int TelegramId { get; set; }
    public DateTimeOffset DT { get; set; }
    public string Text { get; set; }
    public string DirPath { get; set; }
}
