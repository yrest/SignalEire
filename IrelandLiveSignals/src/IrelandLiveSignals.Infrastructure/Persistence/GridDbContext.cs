using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class GridDbContext : IdentityDbContext<ApplicationUser>
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

    // Phase 4 — reliability layer
    public DbSet<VehicleTrailPoint> VehicleTrailPoints => Set<VehicleTrailPoint>();
    public DbSet<TransitUserReport> TransitUserReports => Set<TransitUserReport>();
    public DbSet<TransitReliabilityAggregate> TransitReliabilityAggregates => Set<TransitReliabilityAggregate>();

    // Phase 5/6
    public DbSet<SignalAnomaly> SignalAnomalies => Set<SignalAnomaly>();
    public DbSet<AlertFiring> AlertFirings => Set<AlertFiring>();

    // Phase 5 user features
    public DbSet<FavouriteStop> FavouriteStops => Set<FavouriteStop>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // Phase 7 — JWT auth + mobile push
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

        modelBuilder.Entity<VehicleTrailPoint>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.VehicleId, p.ObservedAtUtc });
            e.HasIndex(p => p.ObservedAtUtc); // for pruning
        });

        modelBuilder.Entity<TransitUserReport>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.RouteId, r.StopId });
            e.HasIndex(r => r.ReportedAtUtc);
        });

        modelBuilder.Entity<TransitReliabilityAggregate>(e =>
        {
            e.HasKey(a => new { a.RouteId, a.StopId });
            e.HasIndex(a => a.ReliabilityScore);
        });

        modelBuilder.Entity<SignalAnomaly>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.Module, a.Date });
            e.HasIndex(a => a.DetectedAtUtc);
        });

        modelBuilder.Entity<AlertFiring>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.AlertRuleId);
            e.HasIndex(f => f.FiredAtUtc);
            e.HasIndex(f => f.IncludedInDigest);
        });

        modelBuilder.Entity<FavouriteStop>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.UserId);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Endpoint).IsUnique();
            e.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<UserRefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.UserId);
            e.HasIndex(t => t.TokenHash).IsUnique();
        });

        modelBuilder.Entity<DeviceToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.UserId);
            e.HasIndex(t => new { t.Token, t.Platform }).IsUnique();
        });
    }
}
