using Microsoft.EntityFrameworkCore;
using Serilog;

namespace TgBackupSearch.Model;

public class MainContext : DbContext
{
    public DbSet<Post> Posts { get; set; }
    public DbSet<Media> Media { get; set; }
    public DbSet<Comment> Comments { get; set; }
    public DbSet<MetadataCache> Cache { get; set; }

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

        modelBuilder.Entity<MetadataCache>()
            .HasIndex(c => c.Path)
            .IsUnique();

        modelBuilder.Entity<Item>()
            .HasIndex(i => i.DirPath)
            .IsUnique();
    }
}
