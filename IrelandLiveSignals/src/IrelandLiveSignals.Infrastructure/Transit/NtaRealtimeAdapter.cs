using IrelandLiveSignals.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace IrelandLiveSignals.Infrastructure.Transit;

/// <summary>
/// Fetches and parses all three NTA GTFS-Realtime feeds.
/// Rate limit: 1 req/60s per token — callers must stagger invocations.
/// </summary>
public class NtaRealtimeAdapter
{
    private readonly HttpClient _http;
    private readonly NtaTransitOptions _opts;
    private readonly ILogger<NtaRealtimeAdapter> _logger;

    public NtaRealtimeAdapter(HttpClient http, IOptions<NtaTransitOptions> opts, ILogger<NtaRealtimeAdapter> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;

        _http.DefaultRequestHeaders.Remove(_opts.ApiKeyHeader);
        _http.DefaultRequestHeaders.Add(_opts.ApiKeyHeader, _opts.ApiKey);
    }

    public async Task<IReadOnlyList<VehicleObservation>> FetchVehiclesAsync(CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/{_opts.VehiclesPath}";
        _logger.LogDebug("Fetching vehicle positions from {Url}", url);

        var bytes = await _http.GetByteArrayAsync(url, ct);
        TransitRealtime.FeedMessage feed;
        using (var ms = new MemoryStream(bytes))
            feed = Serializer.Deserialize<TransitRealtime.FeedMessage>(ms);

        var now = DateTimeOffset.UtcNow;

        return (feed.Entities ?? [])
            .Where(e => e.Vehicle != null)
            .Select(e =>
            {
                var v = e.Vehicle!;
                var ts = v.Timestamp;
                var feedTs = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)ts) : now;
                var ageSeconds = (int)Math.Max(0, (now - feedTs).TotalSeconds);

                return new VehicleObservation
                {
                    VehicleId = v.Vehicle?.Id ?? e.Id ?? string.Empty,
                    TripId = v.Trip?.TripId,
                    RouteId = v.Trip?.RouteId,
                    ObservedAtUtc = now,
                    Lat = v.Position?.Latitude ?? 0,
                    Lon = v.Position?.Longitude ?? 0,
                    Bearing = v.Position?.Bearing,
                    SpeedKph = v.Position != null ? v.Position.Speed * 3.6f : null,
                    GpsAgeSeconds = ageSeconds,
                    QualityStatus = ageSeconds > 300 ? "stale" : "ok"
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyList<(string TripId, string StopId, int DelaySeconds)>> FetchTripUpdatesAsync(CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/{_opts.TripUpdatesPath}";
        _logger.LogDebug("Fetching trip updates from {Url}", url);

        var bytes = await _http.GetByteArrayAsync(url, ct);
        TransitRealtime.FeedMessage feed;
        using (var ms = new MemoryStream(bytes))
            feed = Serializer.Deserialize<TransitRealtime.FeedMessage>(ms);

        var results = new List<(string TripId, string StopId, int DelaySeconds)>();
        foreach (var entity in (feed.Entities ?? []).Where(e => e.TripUpdate != null))
        {
            var tu = entity.TripUpdate!;
            var tripId = tu.Trip?.TripId ?? string.Empty;
            if (string.IsNullOrEmpty(tripId)) continue;

            foreach (var stu in tu.StopTimeUpdates ?? [])
            {
                var stopId = stu.StopId ?? string.Empty;
                var delay = (int)(stu.Arrival?.Delay ?? stu.Departure?.Delay ?? 0);
                results.Add((tripId, stopId, delay));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ServiceAlertRecord>> FetchAlertsAsync(CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/{_opts.AlertsPath}";
        _logger.LogDebug("Fetching service alerts from {Url}", url);

        var bytes = await _http.GetByteArrayAsync(url, ct);
        TransitRealtime.FeedMessage feed;
        using (var ms = new MemoryStream(bytes))
            feed = Serializer.Deserialize<TransitRealtime.FeedMessage>(ms);

        var now = DateTimeOffset.UtcNow;

        return (feed.Entities ?? [])
            .Where(e => e.Alert != null)
            .Select(e =>
            {
                var a = e.Alert!;
                var informed = a.InformedEntities ?? [];
                var routeIds = informed
                    .Where(ie => !string.IsNullOrEmpty(ie.RouteId))
                    .Select(ie => ie.RouteId!)
                    .Distinct().ToArray();
                var stopIds = informed
                    .Where(ie => !string.IsNullOrEmpty(ie.StopId))
                    .Select(ie => ie.StopId!)
                    .Distinct().ToArray();

                DateTimeOffset? activeUntil = null;
                var periods = a.ActivePeriods ?? [];
                if (periods.Count > 0 && periods[0].End > 0)
                    activeUntil = DateTimeOffset.FromUnixTimeSeconds((long)periods[0].End);

                var headerText = a.HeaderText?.Translations?.FirstOrDefault()?.Text ?? string.Empty;
                var descText = a.DescriptionText?.Translations?.FirstOrDefault()?.Text ?? string.Empty;

                return new ServiceAlertRecord
                {
                    AlertId = e.Id ?? string.Empty,
                    FetchedAtUtc = now,
                    HeaderText = headerText,
                    DescriptionText = descText,
                    Effect = a.effect.ToString(),
                    AffectedRouteIds = routeIds,
                    AffectedStopIds = stopIds,
                    ActiveUntilUtc = activeUntil
                };
            })
            .ToList();
    }
}
