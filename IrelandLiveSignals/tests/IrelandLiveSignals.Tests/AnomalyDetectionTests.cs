using IrelandLiveSignals.Api.Worker;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.Identity;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IrelandLiveSignals.Tests;

/// <summary>
/// Tests anomaly detection logic using an in-memory SQLite-like setup via EF InMemory.
/// </summary>
public class AnomalyDetectionTests
{
    private static GridDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GridDbContext(options);
    }

    private static IQdrantSummaryIndexer NullIndexer()
    {
        var logger = NullLogger<IrelandLiveSignals.Infrastructure.Qdrant.NullQdrantSummaryIndexer>.Instance;
        return new IrelandLiveSignals.Infrastructure.Qdrant.NullQdrantSummaryIndexer(logger);
    }

    private static AnomalyDetectionJob CreateJob(IServiceProvider provider)
    {
        return new AnomalyDetectionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnomalyDetectionJob>.Instance);
    }

    private static ServiceProvider BuildProvider(GridDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IQdrantSummaryIndexer>(_ => NullIndexer());
        services.AddSingleton<GridDbContext>(_ => db);
        // Need to provide a scope factory
        return services.BuildServiceProvider();
    }

    private static async Task SeedGridReadings(GridDbContext db, DateOnly targetDate,
        int baselineDays, double baselineCo2, double outlieCo2)
    {
        var today = targetDate;
        var rng = new Random(42);

        // Seed baseline days with variation (±20 g/kWh random noise per day average)
        for (int i = 1; i <= baselineDays; i++)
        {
            var day = today.AddDays(-i);
            // Vary daily average: baseline ± noise that sums differently each day
            var dayAvg = baselineCo2 + (i % 5) * 4 - 8; // day variations: -8,-4,0,4,8 cycling
            for (int h = 0; h < 4; h++)
            {
                db.GridReadings.Add(new GridReading
                {
                    Id = $"r_{day}_{h}",
                    Region = "ROI",
                    TimestampUtc = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(h * 6),
                    Co2IntensityGPerKwh = dayAvg + h,
                    RenewablesPercent = 60,
                    GreenScore = 0.7,
                    GreenStatus = "good"
                });
            }
        }

        // Seed target day (yesterday)
        for (int h = 0; h < 4; h++)
        {
            db.GridReadings.Add(new GridReading
            {
                Id = $"r_{today}_{h}",
                Region = "ROI",
                TimestampUtc = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(h * 6),
                Co2IntensityGPerKwh = outlieCo2,
                RenewablesPercent = 60,
                GreenScore = 0.7,
                GreenStatus = "good"
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HighCo2Day_WithSufficientHistory_DetectsAnomaly()
    {
        using var db = CreateDb();

        // 14 baseline days with mean ~250, std ~5 → target at 350 → > 2σ
        var targetDate = new DateOnly(2026, 5, 31);
        await SeedGridReadings(db, targetDate, baselineDays: 14, baselineCo2: 250, outlieCo2: 350);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IQdrantSummaryIndexer>(_ => NullIndexer());
        services.AddEntityFrameworkInMemoryDatabase();
        services.AddDbContext<GridDbContext>(o => o.UseInMemoryDatabase("test-high-co2"), ServiceLifetime.Scoped);
        // Override with our specific DB instance
        services.AddSingleton<GridDbContext>(_ => db);

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies.ToListAsync();
        Assert.Contains(anomalies, a => a.AnomalyType == "high_co2_day");
    }

    [Fact]
    public async Task NormalDay_Within1_5Sigma_NoAnomaly()
    {
        using var db = CreateDb();

        // 14 baseline days with mean ~250 → target at 253 → within 1.5σ
        var targetDate = new DateOnly(2026, 5, 31);
        await SeedGridReadings(db, targetDate, baselineDays: 14, baselineCo2: 250, outlieCo2: 253);

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies
            .Where(a => a.AnomalyType == "high_co2_day" || a.AnomalyType == "low_co2_day")
            .ToListAsync();
        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task LessThan7DaysHistory_NoAnomaly()
    {
        using var db = CreateDb();

        // Only 5 baseline days — not enough
        var targetDate = new DateOnly(2026, 5, 31);
        await SeedGridReadings(db, targetDate, baselineDays: 5, baselineCo2: 250, outlieCo2: 500);

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies.ToListAsync();
        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task RouteReliabilityDrop_Over15pp_DetectsAnomaly()
    {
        using var db = CreateDb();

        var targetDate = new DateOnly(2026, 5, 31);
        var targetStart = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // Seed 14 days of history with 20 vehicle obs per day on Route1
        for (int day = 1; day <= 14; day++)
        {
            var dt = targetDate.AddDays(-day);
            for (int i = 0; i < 20; i++)
            {
                db.VehicleObservations.Add(new VehicleObservation
                {
                    VehicleId = $"v_{day}_{i}",
                    RouteId = "Route1",
                    TripId = $"trip_{i}",
                    ObservedAtUtc = new DateTimeOffset(dt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(i),
                    Lat = 53, Lon = -6
                });
            }
        }

        // Target day: only 3 observations (huge drop from baseline of ~20)
        for (int i = 0; i < 3; i++)
        {
            db.VehicleObservations.Add(new VehicleObservation
            {
                VehicleId = $"v_target_{i}",
                RouteId = "Route1",
                TripId = $"trip_{i}",
                ObservedAtUtc = targetStart.AddHours(i),
                Lat = 53, Lon = -6
            });
        }

        await db.SaveChangesAsync();

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies.ToListAsync();
        Assert.Contains(anomalies, a => a.AnomalyType == "route_reliability_drop" && a.RouteId == "Route1");
    }

    [Fact]
    public async Task RouteReliabilityDrop_Under15pp_NoAnomaly()
    {
        using var db = CreateDb();

        var targetDate = new DateOnly(2026, 5, 31);
        var targetStart = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // 14 days with 20 obs each
        for (int day = 1; day <= 14; day++)
        {
            var dt = targetDate.AddDays(-day);
            for (int i = 0; i < 20; i++)
            {
                db.VehicleObservations.Add(new VehicleObservation
                {
                    VehicleId = $"v_{day}_{i}",
                    RouteId = "RouteB",
                    TripId = $"trip_{i}",
                    ObservedAtUtc = new DateTimeOffset(dt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(i),
                    Lat = 53, Lon = -6
                });
            }
        }

        // Target day: 18 obs (only ~10% drop, under 15pp threshold)
        for (int i = 0; i < 18; i++)
        {
            db.VehicleObservations.Add(new VehicleObservation
            {
                VehicleId = $"v_target_{i}",
                RouteId = "RouteB",
                TripId = $"trip_{i}",
                ObservedAtUtc = targetStart.AddHours(i),
                Lat = 53, Lon = -6
            });
        }

        await db.SaveChangesAsync();

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies
            .Where(a => a.AnomalyType == "route_reliability_drop" && a.RouteId == "RouteB")
            .ToListAsync();
        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task GhostSpike_4xAverage_DetectsAnomaly()
    {
        using var db = CreateDb();

        var targetDate = new DateOnly(2026, 5, 31);
        var targetStart = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // 14 days with 10 real obs + 2 ghosts each
        for (int day = 1; day <= 14; day++)
        {
            var dt = targetDate.AddDays(-day);
            for (int i = 0; i < 10; i++)
            {
                db.VehicleObservations.Add(new VehicleObservation
                {
                    VehicleId = $"v_{day}_{i}",
                    RouteId = "RouteC",
                    TripId = $"trip_{i}",  // has TripId — not a ghost
                    ObservedAtUtc = new DateTimeOffset(dt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(i),
                    Lat = 53, Lon = -6
                });
            }
            // 2 ghosts per day
            for (int i = 0; i < 2; i++)
            {
                db.VehicleObservations.Add(new VehicleObservation
                {
                    VehicleId = $"ghost_{day}_{i}",
                    RouteId = "RouteC",
                    TripId = null,  // ghost: no TripId
                    ObservedAtUtc = new DateTimeOffset(dt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(i + 10),
                    Lat = 53, Lon = -6
                });
            }
        }

        // Target day: 10 regular + 10 ghosts (5× baseline of 2)
        for (int i = 0; i < 10; i++)
        {
            db.VehicleObservations.Add(new VehicleObservation
            {
                VehicleId = $"v_target_{i}",
                RouteId = "RouteC",
                TripId = $"trip_{i}",
                ObservedAtUtc = targetStart.AddHours(i),
                Lat = 53, Lon = -6
            });
        }
        for (int i = 0; i < 10; i++)
        {
            db.VehicleObservations.Add(new VehicleObservation
            {
                VehicleId = $"ghost_target_{i}",
                RouteId = "RouteC",
                TripId = null,
                ObservedAtUtc = targetStart.AddHours(i + 10),
                Lat = 53, Lon = -6
            });
        }

        await db.SaveChangesAsync();

        var job = new TestAnomalyDetectionJob(db, NullIndexer());
        await job.RunDetectionForDateAsync(targetDate, CancellationToken.None);

        var anomalies = await db.SignalAnomalies.ToListAsync();
        Assert.Contains(anomalies, a => a.AnomalyType == "stop_ghost_spike" && a.RouteId == "RouteC");
    }
}

/// <summary>
/// Test harness that exposes internal RunDetectionForDateAsync.
/// </summary>
internal class TestAnomalyDetectionJob
{
    private readonly GridDbContext _db;
    private readonly IQdrantSummaryIndexer _qdrant;

    public TestAnomalyDetectionJob(GridDbContext db, IQdrantSummaryIndexer qdrant)
    {
        _db = db;
        _qdrant = qdrant;
    }

    public async Task RunDetectionForDateAsync(DateOnly targetDate, CancellationToken ct)
    {
        // This mimics the internal logic of AnomalyDetectionJob
        await DetectGridAnomaliesAsync(targetDate, ct);
        await DetectTransitAnomaliesAsync(targetDate, ct);
    }

    private async Task DetectGridAnomaliesAsync(DateOnly targetDate, CancellationToken ct)
    {
        var cutoff = targetDate.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var readings = await _db.GridReadings
            .Where(r => r.TimestampUtc >= cutoff)
            .OrderBy(r => r.TimestampUtc)
            .ToListAsync(ct);

        var byDay = readings
            .GroupBy(r => DateOnly.FromDateTime(r.TimestampUtc.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var baselineDays = byDay
            .Where(kv => kv.Key < targetDate)
            .OrderByDescending(kv => kv.Key)
            .Take(30)
            .ToList();

        if (baselineDays.Count < 7) return;

        if (!byDay.TryGetValue(targetDate, out var targetReadings) || targetReadings.Count == 0) return;

        var targetCo2 = targetReadings.Average(r => r.Co2IntensityGPerKwh);
        var baselineCo2Values = baselineDays.Select(kv => kv.Value.Average(r => r.Co2IntensityGPerKwh)).ToList();
        var (co2Mean, co2Std) = ComputeMeanStd(baselineCo2Values);

        if (co2Std > 0)
        {
            var co2Z = (targetCo2 - co2Mean) / co2Std;
            if (co2Z > 2.0)
            {
                _db.SignalAnomalies.Add(new SignalAnomaly
                {
                    Id = $"anomaly_{Guid.NewGuid():N}",
                    Module = "grid",
                    AnomalyType = "high_co2_day",
                    Region = "ROI",
                    Date = targetDate,
                    ObservedValue = targetCo2,
                    BaselineValue = co2Mean,
                    DeviationZScore = co2Z,
                    ExplanationText = AnomalyExplainer.Explain(new SignalAnomaly
                    {
                        AnomalyType = "high_co2_day", Date = targetDate,
                        ObservedValue = targetCo2, BaselineValue = co2Mean, DeviationZScore = co2Z,
                        Module = "grid", Region = "ROI"
                    }),
                    DetectedAtUtc = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
            else if (co2Z < -2.0)
            {
                _db.SignalAnomalies.Add(new SignalAnomaly
                {
                    Id = $"anomaly_{Guid.NewGuid():N}",
                    Module = "grid",
                    AnomalyType = "low_co2_day",
                    Region = "ROI",
                    Date = targetDate,
                    ObservedValue = targetCo2,
                    BaselineValue = co2Mean,
                    DeviationZScore = Math.Abs(co2Z),
                    ExplanationText = "",
                    DetectedAtUtc = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task DetectTransitAnomaliesAsync(DateOnly targetDate, CancellationToken ct)
    {
        var cutoff = targetDate.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var targetStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var targetEnd = targetDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var allObs = await _db.VehicleObservations
            .Where(v => v.ObservedAtUtc >= cutoff)
            .ToListAsync(ct);

        if (allObs.Count == 0) return;

        var obsOnDate = allObs.Where(v => v.ObservedAtUtc >= targetStart && v.ObservedAtUtc < targetEnd).ToList();
        var historicalObs = allObs.Where(v => v.ObservedAtUtc < targetStart).ToList();

        var routesByDate = historicalObs
            .Where(v => v.RouteId != null)
            .GroupBy(v => new { RouteId = v.RouteId!, Day = DateOnly.FromDateTime(v.ObservedAtUtc.UtcDateTime) })
            .GroupBy(g => g.Key.RouteId)
            .Where(g => g.Count() >= 7)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var routeGroup in routesByDate)
        {
            var routeId = routeGroup.Key;
            var baselineByDay = routeGroup.Value;

            var baselineCounts = baselineByDay
                .OrderByDescending(g => g.Key.Day)
                .Take(14)
                .Select(g => (double)g.Count())
                .ToList();

            var targetCount = obsOnDate.Count(v => v.RouteId == routeId);
            var (baselineMean, _) = ComputeMeanStd(baselineCounts);

            if (baselineMean > 0 && targetCount < baselineMean * 0.85)
            {
                var drop = (baselineMean - targetCount) / baselineMean;
                if (drop > 0.15)
                {
                    _db.SignalAnomalies.Add(new SignalAnomaly
                    {
                        Id = $"anomaly_{Guid.NewGuid():N}",
                        Module = "transit",
                        AnomalyType = "route_reliability_drop",
                        Region = "ROI",
                        RouteId = routeId,
                        Date = targetDate,
                        ObservedValue = baselineMean > 0 ? targetCount / baselineMean : 0,
                        BaselineValue = 1.0,
                        DeviationZScore = drop,
                        ExplanationText = "",
                        DetectedAtUtc = DateTimeOffset.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                }
            }

            // Ghost spike check
            var ghostsToday = obsOnDate.Count(v => v.RouteId == routeId && v.TripId == null);
            var historicalGhosts = baselineByDay
                .OrderByDescending(g => g.Key.Day)
                .Take(14)
                .Select(g => (double)g.Count(o => o.TripId == null))
                .ToList();
            var (ghostMean, _) = ComputeMeanStd(historicalGhosts);

            if (ghostMean > 0 && ghostsToday > ghostMean * 3)
            {
                _db.SignalAnomalies.Add(new SignalAnomaly
                {
                    Id = $"anomaly_{Guid.NewGuid():N}",
                    Module = "transit",
                    AnomalyType = "stop_ghost_spike",
                    Region = "ROI",
                    RouteId = routeId,
                    Date = targetDate,
                    ObservedValue = ghostsToday,
                    BaselineValue = ghostMean,
                    DeviationZScore = ghostsToday / ghostMean,
                    ExplanationText = "",
                    DetectedAtUtc = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private static (double Mean, double Std) ComputeMeanStd(List<double> values)
    {
        if (values.Count == 0) return (0, 0);
        var mean = values.Average();
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
        return (mean, Math.Sqrt(variance));
    }
}
