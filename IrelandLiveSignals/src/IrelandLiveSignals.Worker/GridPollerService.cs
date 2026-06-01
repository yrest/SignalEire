using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var adapter    = scope.ServiceProvider.GetRequiredService<IGridDataAdapter>();
            var readingRepo = scope.ServiceProvider.GetRequiredService<IGridReadingRepository>();
            var alertRepo  = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();

            _logger.LogInformation("Polling EirGrid...");
            var (_, reading) = await adapter.FetchLatestAsync(ct);

            await readingRepo.SaveAsync(reading, ct);

            _logger.LogInformation(
                "Grid reading saved. Region={Region} Renewables={Renewables:F1}% CO2={Co2:F0}g/kWh Score={Score:F2} Status={Status}",
                reading.Region, reading.RenewablesPercent, reading.Co2IntensityGPerKwh, reading.GreenScore, reading.GreenStatus);

            // Evaluate alert rules
            var rules = await alertRepo.GetActiveAsync(ct);
            int fired = 0;
            foreach (var rule in rules)
            {
                var todayCount = await alertRepo.CountDeliveriesForRuleTodayAsync(rule.Id, ct);
                var result = AlertEvaluationService.Evaluate(rule, reading, todayCount);
                if (result.Fired && result.Delivery is not null)
                {
                    await alertRepo.SaveDeliveryAsync(result.Delivery, ct);
                    fired++;
                    _logger.LogInformation("Alert fired: Rule={RuleName} Message={Message}", rule.RuleName, result.Delivery.Message);
                }
            }

            if (rules.Count > 0)
                _logger.LogInformation("Alert evaluation complete. Rules={Total} Fired={Fired}", rules.Count, fired);
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
