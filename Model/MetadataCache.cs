using System.ComponentModel.DataAnnotations;

namespace TgBackupSearch.Model;

public class MetadataCache
{
    [Key]
    public int FileCacheId { get; set; }
    
    public string Path { get; set; }

    public long Size { get; set; }
    public DateTimeOffset LastWriteDT { get; set; }

    public override bool Equals(object obj)
    {
        return obj is MetadataCache fc && fc.Path == Path && fc.LastWriteDT == LastWriteDT && fc.Size == Size;
    }

    public override int GetHashCode() => HashCode.Combine(Path, LastWriteDT, Size);
}

