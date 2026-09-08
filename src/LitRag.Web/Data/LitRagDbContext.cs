using LitRag.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LitRag.Web.Data;

public class LitRagDbContext(DbContextOptions<LitRagDbContext> options) : DbContext(options)
{
    public DbSet<Paper> Papers => Set<Paper>();
    public DbSet<PaperChunk> PaperChunks => Set<PaperChunk>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Paper>(entity =>
        {
            entity.HasIndex(p => p.Title);
            entity.HasMany(p => p.Chunks)
                .WithOne(c => c.Paper)
                .HasForeignKey(c => c.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.ChatMessages)
                .WithOne(m => m.Paper)
                .HasForeignKey(m => m.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaperChunk>(entity =>
        {
            entity.HasIndex(c => new { c.PaperId, c.ChunkIndex });
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(m => new { m.PaperId, m.CreatedAtUtc });
        });
    }
}
