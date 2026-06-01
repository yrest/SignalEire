using IrelandLiveSignals.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class GridDbContext : DbContext
{
    public GridDbContext(DbContextOptions<GridDbContext> options) : base(options) { }

    public DbSet<GridReading> GridReadings => Set<GridReading>();
    public DbSet<GridRecommendation> GridRecommendations => Set<GridRecommendation>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertDelivery> AlertDeliveries => Set<AlertDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite does not support DateTimeOffset natively — store as UTC ticks (long)
        var dtoConv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        var dtoNullConv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, long?>(
            v => v == null ? null : (long?)v.Value.UtcTicks,
            v => v == null ? null : (DateTimeOffset?)new DateTimeOffset(v.Value, TimeSpan.Zero));

        modelBuilder.Entity<GridReading>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.TimestampUtc);
            e.Property(r => r.TimestampUtc).HasConversion(dtoConv);
        });

        modelBuilder.Entity<GridRecommendation>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.CreatedAtUtc);
            e.Property(r => r.CreatedAtUtc).HasConversion(dtoConv);
            e.Property(r => r.DeadlineUtc).HasConversion(dtoConv);
            e.Property(r => r.RecommendedStartUtc).HasConversion(dtoNullConv);
            e.Property(r => r.RecommendedEndUtc).HasConversion(dtoNullConv);
            // Store string[] as semicolon-delimited text
            e.Property(r => r.Explanation)
             .HasConversion(
                 v => string.Join(";", v),
                 v => v.Split(';', StringSplitOptions.RemoveEmptyEntries));
        });

        modelBuilder.Entity<AlertRule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.CreatedAtUtc).HasConversion(dtoConv);
            // Store TimeOnly? as int? (minutes since midnight)
            e.Property(r => r.QuietHoursStart)
             .HasConversion(
                 v => v.HasValue ? (int?)v.Value.ToTimeSpan().TotalMinutes : null,
                 v => v.HasValue ? (TimeOnly?)TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(v.Value)) : null);
            e.Property(r => r.QuietHoursEnd)
             .HasConversion(
                 v => v.HasValue ? (int?)v.Value.ToTimeSpan().TotalMinutes : null,
                 v => v.HasValue ? (TimeOnly?)TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(v.Value)) : null);
        });

        modelBuilder.Entity<AlertDelivery>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.AlertRuleId);
            e.HasIndex(d => d.FiredAtUtc);
            e.Property(d => d.FiredAtUtc).HasConversion(dtoConv);
        });
    }
}
