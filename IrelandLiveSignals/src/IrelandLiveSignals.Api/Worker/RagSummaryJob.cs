using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Worker;

public class RagSummaryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RagSummaryJob> _logger;

    public RagSummaryJob(IServiceScopeFactory scopeFactory, ILogger<RagSummaryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRun = NextRunTime();
                var delay = nextRun - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                await RunSummaryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RagSummaryJob failed. Will retry at next scheduled time.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private static DateTimeOffset NextRunTime()
    {
        // 04:00 Ireland local time (Europe/Dublin, approximately UTC+0 or UTC+1)
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var target = new DateTimeOffset(now.Date.AddHours(4), now.Offset);
        if (now >= target)
            target = target.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(target.DateTime, tz);
    }

    private async Task RunSummaryAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<IQdrantSummaryIndexer>();

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var from = new DateTimeOffset(yesterday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);

        // Grid summary
        var gridReadings = await db.GridReadings
            .Where(r => r.TimestampUtc >= from && r.TimestampUtc < to)
            .ToListAsync(ct);

        if (gridReadings.Count > 0)
        {
            var avgCo2 = gridReadings.Average(r => r.Co2IntensityGPerKwh);
            var peakRenewables = gridReadings.Max(r => r.RenewablesPercent);
            var lowestCo2Window = gridReadings.MinBy(r => r.Co2IntensityGPerKwh);

            var gridSummary = new SignalSummary
            {
                Id = Guid.NewGuid(),
                Module = "grid",
                Region = "ROI",
                Date = yesterday,
                SummaryText = $"Grid summary for {yesterday:yyyy-MM-dd}: Average CO₂ {avgCo2:F0} g/kWh, " +
                              $"Peak renewable share {peakRenewables:F1}%, " +
                              $"Lowest CO₂ window at {lowestCo2Window?.TimestampUtc:HH:mm} UTC ({lowestCo2Window?.Co2IntensityGPerKwh:F0} g/kWh).",
                Metadata = new Dictionary<string, object>
                {
                    ["avgCo2GPerKwh"] = avgCo2,
                    ["peakRenewablesPercent"] = peakRenewables,
                    ["readingCount"] = gridReadings.Count
                }
            };

            try { await indexer.IndexAsync(gridSummary, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to index grid summary (Qdrant may not be configured)."); }
        }
        else
        {
            _logger.LogInformation("No grid readings for {Date} — skipping grid summary.", yesterday);
        }

        // Transit summary
        var reliabilityAggs = await db.TransitReliabilityAggregates.ToListAsync(ct);
        var ghostRiskCount = await db.TransitUserReports
            .Where(r => r.ReportedAtUtc >= from && r.ReportedAtUtc < to && r.ReportType == "bus_not_appeared")
            .CountAsync(ct);

        if (reliabilityAggs.Count > 0)
        {
            var avgReliability = reliabilityAggs.Average(a => a.ReliabilityScore);
            var transitSummary = new SignalSummary
            {
                Id = Guid.NewGuid(),
                Module = "transit",
                Region = "ROI",
                Date = yesterday,
                SummaryText = $"Transit summary for {yesterday:yyyy-MM-dd}: " +
                              $"Average route reliability {avgReliability:F1}%, " +
                              $"Ghost risk reports: {ghostRiskCount}.",
                Metadata = new Dictionary<string, object>
                {
                    ["avgReliabilityPercent"] = avgReliability,
                    ["ghostRiskCount"] = ghostRiskCount,
                    ["routeCount"] = reliabilityAggs.Count
                }
            };

            try { await indexer.IndexAsync(transitSummary, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to index transit summary (Qdrant may not be configured)."); }
        }

        _logger.LogInformation("RagSummaryJob completed for {Date}.", yesterday);
    }
}
