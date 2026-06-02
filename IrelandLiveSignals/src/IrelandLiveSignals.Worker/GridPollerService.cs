using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.EirGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IrelandLiveSignals.Worker;

public class GridPollerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GridPollerService> _logger;
    private readonly TimeSpan _interval;
    private readonly SignalEireMetrics? _metrics;
    private readonly LiveSignalState? _liveState;
    private readonly List<EirGridRegionConfig> _regionConfigs;

    public static DateTimeOffset? LastRunUtc { get; private set; }
    public static bool IsRunning { get; private set; }

    public GridPollerService(IServiceScopeFactory scopeFactory, ILogger<GridPollerService> logger,
        IConfiguration configuration,
        SignalEireMetrics? metrics = null,
        LiveSignalState? liveState = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = configuration.GetValue("GridPoller:IntervalMinutes", 5);
        _interval = TimeSpan.FromMinutes(minutes);
        _metrics = metrics;
        _liveState = liveState;

        var regionsList = configuration.GetSection("GridPoller:Regions")
            .Get<List<EirGridRegionConfig>>() ?? [];
        if (regionsList.Count == 0)
        {
            regionsList.Add(new EirGridRegionConfig
            {
                Name = configuration["GridPoller:Region"] ?? "ROI",
                EirGridUrl = configuration["GridPoller:EirGridBaseUrl"] ?? "https://www.smartgriddashboard.com/"
            });
        }
        _regionConfigs = regionsList;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IsRunning = true;
        _logger.LogInformation("GridPollerService started. Poll interval: {Interval}, Regions: {Regions}",
            _interval, string.Join(", ", _regionConfigs.Select(r => r.Name)));

        await PollAllRegionsAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAllRegionsAsync(stoppingToken);
        }

        IsRunning = false;
    }

    private async Task PollAllRegionsAsync(CancellationToken ct)
    {
        LastRunUtc = DateTimeOffset.UtcNow;
        var tasks = _regionConfigs.Select((regionConfig, idx) =>
            Task.Run(async () =>
            {
                // Stagger starts to avoid hammering the API simultaneously
                if (idx > 0) await Task.Delay(idx * 5000, ct);
                await PollRegionAsync(regionConfig, ct);
            }, ct));
        await Task.WhenAll(tasks);
    }

    private async Task PollRegionAsync(EirGridRegionConfig regionConfig, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var readingRepo = scope.ServiceProvider.GetRequiredService<IGridReadingRepository>();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            var adapterLogger = loggerFactory.CreateLogger<EirGridAdapter>();
            var opts = new EirGridOptions
            {
                BaseUrl = regionConfig.EirGridUrl,
                Region = regionConfig.Name,
                RawSnapshotPath = "data/raw"
            };
            var http = httpClientFactory.CreateClient();
            var feedHealth = scope.ServiceProvider.GetService<FeedHealthStore>();
            var metricsService = scope.ServiceProvider.GetService<SignalEireMetrics>();

            var adapter = new EirGridAdapter(http, Options.Create(opts), adapterLogger, feedHealth, metricsService);

            _logger.LogInformation("Polling EirGrid for region {Region}...", regionConfig.Name);
            var (_, reading) = await adapter.FetchLatestAsync(ct);

            await readingRepo.SaveAsync(reading, ct);

            _metrics?.GridReadingsIngested.Add(1);
            if (_liveState is not null)
                _liveState.LastSuccessfulFetch[$"eirgrid_{regionConfig.Name.ToLower()}"] = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Grid reading saved. Region={Region} Renewables={Renewables:F1}% CO2={Co2:F0}g/kWh Score={Score:F2}",
                reading.Region, reading.RenewablesPercent, reading.Co2IntensityGPerKwh, reading.GreenScore);

            // Only evaluate alerts for their matching region
            var rules = await alertRepo.GetActiveAsync(ct);
            foreach (var rule in rules)
            {
                var ruleRegion = rule.Region ?? "ROI";
                if (!string.Equals(ruleRegion, regionConfig.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var todayCount = await alertRepo.CountDeliveriesForRuleTodayAsync(rule.Id, ct);
                var result = AlertEvaluationService.Evaluate(rule, reading, todayCount);
                if (result.Fired && result.Delivery is not null)
                {
                    await alertRepo.SaveDeliveryAsync(result.Delivery, ct);
                    _metrics?.AlertsFired.Add(1);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grid poll failed for region {Region}. Will retry at next interval.", regionConfig.Name);
        }
    }
}
