using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TgBackupSearch.Model;

public class Media
{
    [Key]
    public int MediaId { get; set; }

    public int ItemId { get; set; }

    [ForeignKey(nameof(ItemId))]
    public Item Item { get; set; }

    [InverseProperty(nameof(Recognition.Media))]
    public List<Recognition> Recognitions { get; } = new();

    public long TelegramId { get; set; }
    public DateTime DT { get; set; }
    public string FilePath { get; set; }
    public MediaType Type { get; set; }
}
