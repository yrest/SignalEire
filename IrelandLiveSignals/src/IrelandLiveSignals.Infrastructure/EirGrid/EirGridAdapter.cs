using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IrelandLiveSignals.Infrastructure.EirGrid;

public class EirGridAdapter : IGridDataAdapter
{
    private readonly HttpClient _http;
    private readonly EirGridOptions _options;
    private readonly ILogger<EirGridAdapter> _logger;
    private readonly FeedHealthStore? _feedHealth;
    private readonly SignalEireMetrics? _metrics;

    private const string SourceName = "eirgrid";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public EirGridAdapter(HttpClient http, IOptions<EirGridOptions> options, ILogger<EirGridAdapter> logger,
        FeedHealthStore? feedHealth = null, SignalEireMetrics? metrics = null)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _feedHealth = feedHealth;
        _metrics = metrics;
    }

    public async Task<(RawGridSnapshot Snapshot, GridReading Reading)> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;
        try
        {
        var dateStr = now.ToString("dd-MMM-yyyy");
        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/DashboardService.svc/data";
        var region = _options.Region;

        var generationJson = await FetchAreaAsync(baseUrl, "generationactual", region, dateStr, cancellationToken);
        var co2Json        = await FetchAreaAsync(baseUrl, "co2intensity",     region, dateStr, cancellationToken);
        var interconnectJson = await FetchAreaAsync(baseUrl, "interconnection", region, dateStr, cancellationToken);
        var demandJson     = await FetchAreaAsync(baseUrl, "demandactual",     region, dateStr, cancellationToken);

        var combined = $"{{\"generation\":{generationJson},\"co2\":{co2Json},\"interconnect\":{interconnectJson},\"demand\":{demandJson}}}";
        var hash = ComputeSha256(combined);

        var relPath = Path.Combine("grid", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), now.ToString("HHmmss") + ".json");
        var fullPath = Path.Combine(_options.RawSnapshotPath, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, combined, cancellationToken);

        var snapshotId = $"grid_raw_{now:yyyyMMdd_HHmmss}";
        var snapshot = new RawGridSnapshot
        {
            Id = snapshotId,
            Source = "eirgrid_smart_grid_dashboard",
            SourceUrl = baseUrl,
            RetrievedAtUtc = now,
            RawPayloadPath = relPath,
            Hash = hash
        };

        var reading = ParseReading(generationJson, co2Json, interconnectJson, demandJson, now, region, snapshotId);
        sw.Stop();
        _feedHealth?.RecordSuccess(SourceName, sw.Elapsed);
        _metrics?.FeedFetchDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        return (snapshot, reading);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _feedHealth?.RecordFailure(SourceName, ex.Message);
            _metrics?.FeedFetchFailures.Add(1);
            throw;
        }
    }

    private async Task<string> FetchAreaAsync(string baseUrl, string area, string region, string dateStr, CancellationToken ct)
    {
        var url = $"{baseUrl}?area={area}&region={region}&datefrom={Uri.EscapeDataString(dateStr + " 00:00")}&dateto={Uri.EscapeDataString(dateStr + " 23:59")}";
        _logger.LogDebug("Fetching {Area} from {Url}", area, url);
        var response = await _http.GetStringAsync(url, ct);
        return response;
    }

    private GridReading ParseReading(string generationJson, string co2Json, string interconnectJson, string demandJson,
        DateTimeOffset fetchedAt, string region, string snapshotId)
    {
        var genData     = JsonSerializer.Deserialize<EirGridResponse>(generationJson, JsonOpts);
        var co2Data     = JsonSerializer.Deserialize<EirGridResponse>(co2Json, JsonOpts);
        var interData   = JsonSerializer.Deserialize<EirGridResponse>(interconnectJson, JsonOpts);
        var demandData  = JsonSerializer.Deserialize<EirGridResponse>(demandJson, JsonOpts);

        var latestGen     = genData?.Rows.LastOrDefault();
        var latestCo2     = co2Data?.Rows.LastOrDefault();
        var latestInter   = interData?.Rows.LastOrDefault();
        var latestDemand  = demandData?.Rows.LastOrDefault();

        var dataTimestamp = ParseEffectiveTime(latestGen?.EffectiveTime ?? latestDemand?.EffectiveTime) ?? fetchedAt;
        var freshnessSeconds = (int)(fetchedAt - dataTimestamp).TotalSeconds;
        if (freshnessSeconds < 0) freshnessSeconds = 0;

        var windMw      = latestGen?.WindGeneration ?? 0;
        var demandMw    = latestDemand?.Value ?? latestGen?.ActualGeneration ?? 0;
        var co2         = latestCo2?.Value ?? 300;

        // Renewables = (wind + hydro + pump storage) / actual generation
        var renewablesMw = (latestGen?.WindGeneration ?? 0)
                         + (latestGen?.HydroGeneration ?? 0)
                         + (latestGen?.PumpStorageGeneration ?? 0);
        var totalGen = latestGen?.ActualGeneration ?? demandMw;
        var renewablesPercent = totalGen > 0 ? (renewablesMw / totalGen) * 100.0 : 0;

        var netImport = latestInter?.Value ?? latestGen?.NetImport;
        double? importMw = netImport > 0 ? netImport : null;
        double? exportMw = netImport < 0 ? -netImport : null;

        var qualityStatus = freshnessSeconds > 600 ? "stale" : "ok";

        var (score, status, recommendation) = GreenScoringService.Compute(renewablesPercent, co2, freshnessSeconds);

        return new GridReading
        {
            Id = snapshotId,
            Region = region,
            TimestampUtc = dataTimestamp,
            SystemDemandMw = demandMw,
            WindGenerationMw = windMw,
            SolarGenerationMw = null,
            RenewablesPercent = renewablesPercent,
            Co2IntensityGPerKwh = co2,
            InterconnectorImportMw = importMw,
            InterconnectorExportMw = exportMw,
            DataFreshnessSeconds = freshnessSeconds,
            GreenScore = score,
            GreenStatus = status,
            Recommendation = recommendation,
            QualityStatus = qualityStatus
        };
    }

    private static DateTimeOffset? ParseEffectiveTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Format from EirGrid: "01-Jun-2026 13:30:00"
        if (DateTimeOffset.TryParseExact(raw, "dd-MMM-yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
            return result;
        if (DateTimeOffset.TryParse(raw, out var fallback))
            return fallback;
        return null;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
