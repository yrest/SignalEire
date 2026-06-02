using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Worker;

public class AnomalyDetectionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnomalyDetectionJob> _logger;

    private static readonly TimeZoneInfo IrelandTz = GetIrelandTimeZone();

    public AnomalyDetectionJob(IServiceScopeFactory scopeFactory, ILogger<AnomalyDetectionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnomalyDetectionJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowIreland = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IrelandTz);
            var nextRun = new DateTime(nowIreland.Year, nowIreland.Month, nowIreland.Day, 4, 30, 0);
            if (nowIreland >= nextRun) nextRun = nextRun.AddDays(1);

            var delay = nextRun - nowIreland;
            _logger.LogInformation("AnomalyDetectionJob next run in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunDetectionAsync(stoppingToken);
        }
    }

    internal async Task RunDetectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running anomaly detection...");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
            var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantSummaryIndexer>();

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IrelandTz));
            var yesterday = today.AddDays(-1);

            await DetectGridAnomaliesAsync(db, qdrant, yesterday, ct);
            await DetectTransitAnomaliesAsync(db, qdrant, yesterday, ct);

            _logger.LogInformation("Anomaly detection complete.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Anomaly detection failed.");
        }
    }

    private async Task DetectGridAnomaliesAsync(GridDbContext db, IQdrantSummaryIndexer qdrant, DateOnly targetDate, CancellationToken ct)
    {
        // Need at least 7 days of history
        var cutoff = targetDate.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var readings = await db.GridReadings
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

        if (baselineDays.Count < 7)
        {
            _logger.LogInformation("Not enough history for grid anomaly detection (have {Days} days, need 7).", baselineDays.Count);
            return;
        }

        if (!byDay.TryGetValue(targetDate, out var targetReadings) || targetReadings.Count == 0)
        {
            _logger.LogInformation("No readings for {Date}, skipping grid anomaly detection.", targetDate);
            return;
        }

        var targetCo2 = targetReadings.Average(r => r.Co2IntensityGPerKwh);
        var targetRenewables = targetReadings.Average(r => r.RenewablesPercent);

        var baselineCo2Values = baselineDays.Select(kv => kv.Value.Average(r => r.Co2IntensityGPerKwh)).ToList();
        var baselineRenewablesValues = baselineDays.Select(kv => kv.Value.Average(r => r.RenewablesPercent)).ToList();

        var (co2Mean, co2Std) = ComputeMeanStd(baselineCo2Values);
        var (renewMean, renewStd) = ComputeMeanStd(baselineRenewablesValues);

        var anomalies = new List<SignalAnomaly>();

        if (co2Std > 0)
        {
            var co2Z = (targetCo2 - co2Mean) / co2Std;
            if (co2Z > 2.0)
            {
                anomalies.Add(CreateAnomaly("grid", "high_co2_day", "ROI", targetDate,
                    targetCo2, co2Mean, co2Z));
            }
            else if (co2Z < -2.0)
            {
                anomalies.Add(CreateAnomaly("grid", "low_co2_day", "ROI", targetDate,
                    targetCo2, co2Mean, Math.Abs(co2Z)));
            }
        }

        if (renewStd > 0)
        {
            var renewZ = (targetRenewables - renewMean) / renewStd;
            if (renewZ < -2.0)
            {
                anomalies.Add(CreateAnomaly("grid", "low_renewables_day", "ROI", targetDate,
                    targetRenewables / 100.0, renewMean / 100.0, Math.Abs(renewZ)));
            }
        }

        await SaveAndIndexAnomaliesAsync(db, qdrant, anomalies, ct);
    }

    private async Task DetectTransitAnomaliesAsync(GridDbContext db, IQdrantSummaryIndexer qdrant, DateOnly targetDate, CancellationToken ct)
    {
        var cutoff = targetDate.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var targetStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var targetEnd = targetDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var allObs = await db.VehicleObservations
            .Where(v => v.ObservedAtUtc >= cutoff)
            .ToListAsync(ct);

        if (allObs.Count == 0) return;

        var obsOnDate = allObs.Where(v => v.ObservedAtUtc >= targetStart && v.ObservedAtUtc < targetEnd).ToList();
        var historicalObs = allObs.Where(v => v.ObservedAtUtc < targetStart).ToList();

        // Route reliability drop: confirmed (vehicle GPS) vs total trip schedule
        var routesByDate = historicalObs
            .Where(v => v.RouteId != null)
            .GroupBy(v => new { RouteId = v.RouteId!, Day = DateOnly.FromDateTime(v.ObservedAtUtc.UtcDateTime) })
            .GroupBy(g => g.Key.RouteId)
            .Where(g => g.Count() >= 7)
            .ToDictionary(g => g.Key, g => g.ToList());

        var anomalies = new List<SignalAnomaly>();

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
                    anomalies.Add(new SignalAnomaly
                    {
                        Id = $"anomaly_{Guid.NewGuid():N}",
                        Module = "transit",
                        AnomalyType = "route_reliability_drop",
                        Region = "ROI",
                        RouteId = routeId,
                        Date = targetDate,
                        ObservedValue = targetCount / baselineMean,
                        BaselineValue = 1.0,
                        DeviationZScore = drop,
                        ExplanationText = "",
                        IndexedToQdrant = false,
                        DetectedAtUtc = DateTimeOffset.UtcNow
                    });
                }
            }

            // Ghost spike: unmatched observations (no TripId)
            var ghostsToday = obsOnDate.Count(v => v.RouteId == routeId && v.TripId == null);
            var historicalGhosts = baselineByDay
                .OrderByDescending(g => g.Key.Day)
                .Take(14)
                .Select(g => (double)g.Count(o => o.TripId == null))
                .ToList();
            var (ghostMean, _) = ComputeMeanStd(historicalGhosts);

            if (ghostMean > 0 && ghostsToday > ghostMean * 3)
            {
                anomalies.Add(new SignalAnomaly
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
                    IndexedToQdrant = false,
                    DetectedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await SaveAndIndexAnomaliesAsync(db, qdrant, anomalies, ct);
    }

    private async Task SaveAndIndexAnomaliesAsync(GridDbContext db, IQdrantSummaryIndexer qdrant,
        List<SignalAnomaly> anomalies, CancellationToken ct)
    {
        foreach (var anomaly in anomalies)
        {
            anomaly.ExplanationText = AnomalyExplainer.Explain(anomaly);

            var existing = await db.SignalAnomalies
                .FirstOrDefaultAsync(a => a.Module == anomaly.Module
                    && a.AnomalyType == anomaly.AnomalyType
                    && a.Date == anomaly.Date
                    && a.RouteId == anomaly.RouteId
                    && a.StopId == anomaly.StopId, ct);

            if (existing is null)
            {
                db.SignalAnomalies.Add(anomaly);
                await db.SaveChangesAsync(ct);

                var summary = new SignalSummary
                {
                    Id = Guid.NewGuid(),
                    Module = anomaly.Module,
                    Region = anomaly.Region,
                    Date = anomaly.Date,
                    SummaryText = anomaly.ExplanationText,
                    Metadata = new Dictionary<string, object>
                    {
                        ["anomalyType"] = anomaly.AnomalyType,
                        ["observedValue"] = anomaly.ObservedValue,
                        ["baselineValue"] = anomaly.BaselineValue
                    }
                };

                await qdrant.IndexAsync(summary, ct);
                anomaly.IndexedToQdrant = true;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Anomaly detected: {Type} on {Date} ({Module})",
                    anomaly.AnomalyType, anomaly.Date, anomaly.Module);
            }
        }
    }

    private static SignalAnomaly CreateAnomaly(string module, string type, string region,
        DateOnly date, double observed, double baseline, double zScore) => new()
    {
        Id = $"anomaly_{Guid.NewGuid():N}",
        Module = module,
        AnomalyType = type,
        Region = region,
        Date = date,
        ObservedValue = observed,
        BaselineValue = baseline,
        DeviationZScore = zScore,
        ExplanationText = "",
        IndexedToQdrant = false,
        DetectedAtUtc = DateTimeOffset.UtcNow
    };

    private static (double Mean, double Std) ComputeMeanStd(List<double> values)
    {
        if (values.Count == 0) return (0, 0);
        var mean = values.Average();
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
        return (mean, Math.Sqrt(variance));
    }

    private static TimeZoneInfo GetIrelandTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
    }
}
