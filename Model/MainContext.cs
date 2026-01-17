using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace TgBackupSearch.Model;

public class MainContext : DbContext
{
    public DbSet<Post> Posts { get; set; }
    public DbSet<Media> Media { get; set; }
    public DbSet<Comment> Comments { get; set; }

    public MainContext(ILogger logger, DbContextOptions<MainContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseLazyLoadingProxies(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Media>()
            .Property(m => m.Type)
            .HasConversion<int>();

        modelBuilder.Entity<Media>()
            .HasOne(m => m.Recognition)
            .WithOne(r => r.Media)
            .HasForeignKey<Recognition>(r => r.MediaId)
            .IsRequired(false);
    }
}

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

public class Post : Item
{
    [InverseProperty(nameof(Comment.Post))]
    public List<Comment> Comments { get; } = new();
}

public class Media
{
    [Key]
    public int MediaId { get; set; }

    public int ItemId { get; set; }

    [ForeignKey(nameof(ItemId))]
    public Item Item { get; set; }

    public Recognition Recognition { get; set; }

    public long TelegramId { get; set; }
    public DateTimeOffset DT { get; set; }
    public string FilePath { get; set; }
    public MediaType Type { get; set; }
}

public class Recognition
{
    [Key]
    public int RecognitionId { get; set; }

    public int MediaId { get; set; }

    [ForeignKey(nameof(MediaId))]
    public Media Media { get; set; }

    public string Text { get; set; }
}

public enum MediaType { Invalid, Photo, Document }

public class Comment : Item
{
    public int PostId { get; set; }

    [ForeignKey(nameof(PostId))]
    public Post Post { get; set; }
}

