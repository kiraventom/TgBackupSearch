using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TgBackupSearch.Model;

public class Recognition
{
    [Key]
    public int RecognitionId { get; set; }

    public int MediaId { get; set; }

    [ForeignKey(nameof(MediaId))]
    public Media Media { get; set; }

    public float Confidence { get; set; }

    public string Text { get; set; }
}
