using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;

namespace IrelandLiveSignals.Api.Worker;

/// <summary>
/// Runs hourly. Aggregates user reports into per-(route, stop) reliability summaries.
/// Also prunes old vehicle trail data.
/// Aggregates improve automatically as more observations and user reports accumulate.
/// </summary>
public class ReliabilityAggregationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReliabilityAggregationService> _logger;

    private static readonly TimeSpan TrailRetention = TimeSpan.FromHours(48);
    private static readonly TimeSpan ReportWindow = TimeSpan.FromDays(30);

    public ReliabilityAggregationService(IServiceScopeFactory scopeFactory, ILogger<ReliabilityAggregationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITransitRepository>();
            var now = DateTimeOffset.UtcNow;

            await repo.PruneTrailPointsAsync(now - TrailRetention, ct);

            var reports = await repo.GetUserReportsAsync(null, null, now - ReportWindow, ct);
            if (!reports.Any())
            {
                _logger.LogDebug("No user reports to aggregate.");
                return;
            }

            var groups = reports.GroupBy(r => (r.RouteId, r.StopId));
            int updated = 0;

            foreach (var group in groups)
            {
                var (routeId, stopId) = group.Key;
                var groupList = group.ToList();

                var confirmed = groupList.Count(r => r.ReportType == "bus_seen");
                var ghost = groupList.Count(r => r.ReportType is "bus_not_appeared" or "bus_passed_full");
                var total = groupList.Count;

                var score = TransitReliabilityService.ComputeScore(confirmed, total - confirmed - ghost, ghost, groupList);

                var existing = await repo.GetReliabilityAsync(routeId, stopId, ct);

                var agg = new TransitReliabilityAggregate
                {
                    RouteId = routeId,
                    StopId = stopId,
                    TotalObservations = total,
                    VehicleConfirmedCount = confirmed,
                    TimetableOnlyCount = total - confirmed - ghost,
                    GhostCount = ghost,
                    AverageDelaySeconds = existing?.AverageDelaySeconds ?? 0,
                    ReliabilityScore = score,
                    LastUpdatedUtc = now,
                    OldestObservationUtc = groupList.Min(r => r.ReportedAtUtc)
                };

                await repo.UpsertReliabilityAggregateAsync(agg, ct);
                updated++;
            }

            _logger.LogInformation("Reliability aggregation: updated {Count} route/stop pairs.", updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reliability aggregation failed");
        }
    }
}
