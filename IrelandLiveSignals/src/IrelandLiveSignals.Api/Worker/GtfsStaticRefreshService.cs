using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.Transit;
using Microsoft.Extensions.Options;

namespace IrelandLiveSignals.Api.Worker;

public class GtfsStaticRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NtaTransitOptions _opts;
    private readonly ILogger<GtfsStaticRefreshService> _logger;

    public GtfsStaticRefreshService(IServiceScopeFactory scopeFactory, IOptions<NtaTransitOptions> opts, ILogger<GtfsStaticRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to let DB migrations settle
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRefreshAsync(stoppingToken);
            // Check once a day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITransitRepository>();
            var importer = scope.ServiceProvider.GetRequiredService<GtfsStaticImporter>();

            var lastImport = await repo.GetLastStaticImportAsync(ct);
            var hasData = await repo.HasStaticDataAsync(ct);
            var refreshDue = lastImport is null
                || (DateTimeOffset.UtcNow - lastImport.Value).TotalDays >= _opts.GtfsStaticRefreshDays;

            if (!hasData || refreshDue)
            {
                _logger.LogInformation("Starting GTFS static refresh. Last import: {LastImport}", lastImport?.ToString("u") ?? "never");
                await importer.ImportAsync(ct);
                await repo.SetLastStaticImportAsync(DateTimeOffset.UtcNow, ct);
            }
            else
            {
                _logger.LogDebug("GTFS static data is current (last import: {LastImport})", lastImport?.ToString("u"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GTFS static refresh failed");
        }
    }
}
