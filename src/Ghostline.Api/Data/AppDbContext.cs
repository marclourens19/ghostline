using Microsoft.EntityFrameworkCore;
using Ghostline.Api.Models;

namespace Ghostline.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RunRecord> Runs => Set<RunRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.PlayerName)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.ReplayData)
                .IsRequired();

            // Index for fast leaderboard queries (highest verified score first)
            entity.HasIndex(e => new { e.IsVerified, e.VerifiedScore, e.CreatedAtUtc });
        });
    }
}