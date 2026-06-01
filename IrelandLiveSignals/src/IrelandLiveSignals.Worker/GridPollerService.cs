using IrelandLiveSignals.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace IrelandLiveSignals.Worker;

public class GridPollerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GridPollerService> _logger;
    private readonly TimeSpan _interval;

    public static DateTimeOffset? LastRunUtc { get; private set; }
    public static bool IsRunning { get; private set; }

    public GridPollerService(IServiceScopeFactory scopeFactory, ILogger<GridPollerService> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = configuration.GetValue("GridPoller:IntervalMinutes", 5);
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IsRunning = true;
        _logger.LogInformation("GridPollerService started. Poll interval: {Interval}", _interval);

        // Poll immediately on startup, then on interval
        await PollAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAsync(stoppingToken);
        }

        IsRunning = false;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        LastRunUtc = DateTimeOffset.UtcNow;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var adapter = scope.ServiceProvider.GetRequiredService<IGridDataAdapter>();
            var repository = scope.ServiceProvider.GetRequiredService<IGridReadingRepository>();

            _logger.LogInformation("Polling EirGrid...");
            var (snapshot, reading) = await adapter.FetchLatestAsync(ct);

            await repository.SaveAsync(reading, ct);

            _logger.LogInformation(
                "Grid reading saved. Region={Region} Renewables={Renewables:F1}% CO2={Co2:F0}g/kWh Score={Score:F2} Status={Status}",
                reading.Region, reading.RenewablesPercent, reading.Co2IntensityGPerKwh, reading.GreenScore, reading.GreenStatus);
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grid poll failed. Will retry at next interval.");
        }
    }
}
