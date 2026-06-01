using IrelandLiveSignals.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class GridDbContext : DbContext
{
    public GridDbContext(DbContextOptions<GridDbContext> options) : base(options) { }

    public DbSet<GridReading> GridReadings => Set<GridReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite does not support DateTimeOffset ordering natively — store as ticks (long)
        modelBuilder.Entity<GridReading>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.TimestampUtc);
            e.Property(r => r.TimestampUtc)
             .HasConversion(
                 v => v.UtcTicks,
                 v => new DateTimeOffset(v, TimeSpan.Zero));
        });
    }
}
