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

    // Transit
    public DbSet<TransitStop> TransitStops => Set<TransitStop>();
    public DbSet<TransitRoute> TransitRoutes => Set<TransitRoute>();
    public DbSet<TransitTrip> TransitTrips => Set<TransitTrip>();
    public DbSet<StopTime> StopTimes => Set<StopTime>();
    public DbSet<ServiceCalendar> ServiceCalendars => Set<ServiceCalendar>();
    public DbSet<ServiceCalendarDate> ServiceCalendarDates => Set<ServiceCalendarDate>();
    public DbSet<VehicleObservation> VehicleObservations => Set<VehicleObservation>();
    public DbSet<ServiceAlertRecord> ServiceAlerts => Set<ServiceAlertRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GridReading>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.TimestampUtc);
        });

        modelBuilder.Entity<GridRecommendation>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.CreatedAtUtc);
            e.Property(r => r.Explanation)
             .HasConversion(
                 v => string.Join(";", v),
                 v => v.Split(';', StringSplitOptions.RemoveEmptyEntries));
        });

        modelBuilder.Entity<AlertRule>(e =>
        {
            e.HasKey(r => r.Id);
        });

        modelBuilder.Entity<AlertDelivery>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.AlertRuleId);
            e.HasIndex(d => d.FiredAtUtc);
        });

        modelBuilder.Entity<TransitStop>(e =>
        {
            e.HasKey(s => s.StopId);
            e.HasIndex(s => new { s.StopLat, s.StopLon });
            e.HasIndex(s => s.StopName);
        });

        modelBuilder.Entity<TransitRoute>(e =>
        {
            e.HasKey(r => r.RouteId);
        });

        modelBuilder.Entity<TransitTrip>(e =>
        {
            e.HasKey(t => t.TripId);
            e.HasIndex(t => t.RouteId);
            e.HasIndex(t => t.ServiceId);
        });

        modelBuilder.Entity<StopTime>(e =>
        {
            e.HasKey(st => new { st.TripId, st.StopSequence });
            e.HasIndex(st => new { st.StopId, st.ArrivalSeconds });
        });

        modelBuilder.Entity<ServiceCalendar>(e =>
        {
            e.HasKey(c => c.ServiceId);
        });

        modelBuilder.Entity<ServiceCalendarDate>(e =>
        {
            e.HasKey(cd => new { cd.ServiceId, cd.Date });
        });

        modelBuilder.Entity<VehicleObservation>(e =>
        {
            e.HasKey(v => v.VehicleId);
            e.HasIndex(v => v.TripId);
            e.HasIndex(v => v.RouteId);
            e.HasIndex(v => v.ObservedAtUtc);
        });

        modelBuilder.Entity<ServiceAlertRecord>(e =>
        {
            e.HasKey(a => a.AlertId);
            e.Property(a => a.AffectedRouteIds)
             .HasConversion(
                 v => string.Join(",", v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            e.Property(a => a.AffectedStopIds)
             .HasConversion(
                 v => string.Join(",", v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
        });
    }
}
