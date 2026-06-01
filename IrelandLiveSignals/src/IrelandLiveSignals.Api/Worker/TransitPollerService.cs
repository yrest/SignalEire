using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.Transit;
using Microsoft.Extensions.Options;

namespace IrelandLiveSignals.Api.Worker;

public class TransitPollerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NtaTransitOptions _opts;
    private readonly ILogger<TransitPollerService> _logger;

    private DateTimeOffset _lastVehicles = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTripUpdates = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAlerts = DateTimeOffset.MinValue;

    public TransitPollerService(IServiceScopeFactory scopeFactory, IOptions<NtaTransitOptions> opts, ILogger<TransitPollerService> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            _logger.LogWarning("NTA API key not configured (NTA_API_KEY env var or NtaApi:ApiKey). Transit polling disabled.");
            return;
        }

        _logger.LogInformation("Transit poller started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            if ((now - _lastVehicles).TotalSeconds >= _opts.VehiclesPollIntervalSeconds)
            {
                await PollVehiclesAsync(stoppingToken);
                _lastVehicles = DateTimeOffset.UtcNow;
            }

            if ((now - _lastTripUpdates).TotalSeconds >= _opts.TripUpdatesPollIntervalSeconds)
            {
                await Task.Delay(TimeSpan.FromSeconds(_opts.FeedStaggerSeconds), stoppingToken);
                await PollTripUpdatesAsync(stoppingToken);
                _lastTripUpdates = DateTimeOffset.UtcNow;
            }

            if ((now - _lastAlerts).TotalSeconds >= _opts.AlertsPollIntervalSeconds)
            {
                await Task.Delay(TimeSpan.FromSeconds(_opts.FeedStaggerSeconds), stoppingToken);
                await PollAlertsAsync(stoppingToken);
                _lastAlerts = DateTimeOffset.UtcNow;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task PollVehiclesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var adapter = scope.ServiceProvider.GetRequiredService<NtaRealtimeAdapter>();
            var repo = scope.ServiceProvider.GetRequiredService<ITransitRepository>();

            var vehicles = await adapter.FetchVehiclesAsync(ct);
            foreach (var v in vehicles)
                await repo.UpsertVehicleObservationAsync(v, ct);

            _logger.LogDebug("Vehicles polled: {Count}", vehicles.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error polling vehicle positions");
        }
    }

    private async Task PollTripUpdatesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var adapter = scope.ServiceProvider.GetRequiredService<NtaRealtimeAdapter>();
            var repo = scope.ServiceProvider.GetRequiredService<ITransitRepository>();

            var updates = await adapter.FetchTripUpdatesAsync(ct);
            var byTrip = updates.GroupBy(u => u.TripId);
            foreach (var group in byTrip)
            {
                var delays = group.Select(u => (u.StopId, u.DelaySeconds)).ToList();
                await repo.SaveTripDelaysAsync(group.Key, delays, ct);
            }

            _logger.LogDebug("Trip updates polled: {Count} stop-time entries", updates.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error polling trip updates");
        }
    }

    private async Task PollAlertsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var adapter = scope.ServiceProvider.GetRequiredService<NtaRealtimeAdapter>();
            var repo = scope.ServiceProvider.GetRequiredService<ITransitRepository>();

            var alerts = await adapter.FetchAlertsAsync(ct);
            await repo.SaveServiceAlertsAsync(alerts, ct);

            _logger.LogDebug("Alerts polled: {Count}", alerts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error polling service alerts");
        }
    }
}
