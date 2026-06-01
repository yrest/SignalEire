# SignalEire — Phase 6 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 5 complete, application live at production URL  
**Scope:** OpenTelemetry + Grafana Cloud + alert digest mode + RAG anomaly explanations + admin observability  
**Character:** Backend/infra only. No database migration, no MAUI, no tariff scoring.

---

## What Phase 6 adds

1. **OpenTelemetry instrumentation** — traces, metrics, and structured logs exported via OTLP
2. **Grafana Cloud integration** — dashboards for ingestion health, grid intelligence, transit intelligence, user activity
3. **Custom metrics** — per-feed, per-module business metrics beyond the default ASP.NET Core instrumentation
4. **Admin observability page** — live source health, ingestion stats, error rates in the Razor dashboard
5. **Alert digest mode** — daily summary email grouping all firings instead of per-event delivery
6. **RAG anomaly detection and explanation** — detect statistical anomalies in grid and transit data, generate natural-language explanations, surface in Intelligence dashboard and digest emails

---

## NuGet packages required

Add to `IrelandLiveSignals.Web` / `IrelandLiveSignals.Worker`:

```
OpenTelemetry
OpenTelemetry.Extensions.Hosting
OpenTelemetry.Instrumentation.AspNetCore
OpenTelemetry.Instrumentation.Http
OpenTelemetry.Instrumentation.EntityFrameworkCore
OpenTelemetry.Exporter.OpenTelemetryProtocol
OpenTelemetry.Exporter.Prometheus.AspNetCore
```

---

## Part 1 — OpenTelemetry setup

### Grafana Cloud configuration

Use Grafana Cloud free tier (10k active metrics, 50GB logs, 50GB traces — sufficient for this scale). No self-hosted Grafana needed.

From the Grafana Cloud portal, obtain:
- OTLP endpoint: `https://otlp-gateway-prod-{region}.grafana.net/otlp`
- Instance ID and API key (base64-encoded as `{instanceId}:{apiKey}`)

Add to `/etc/ireland-live-signals/environment`:
```bash
Telemetry__OtlpEndpoint=https://otlp-gateway-prod-eu-west-0.grafana.net/otlp
Telemetry__OtlpAuthHeader=Basic base64({instanceId}:{apiKey})
Telemetry__ServiceName=ireland-live-signals
Telemetry__Environment=production
```

Add to `appsettings.json` (non-secret structure only):
```json
"Telemetry": {
  "OtlpEndpoint": "",
  "OtlpAuthHeader": "",
  "ServiceName": "ireland-live-signals",
  "Environment": "development",
  "EnablePrometheusEndpoint": true
}
```

### Program.cs wiring

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(
            serviceName: config["Telemetry:ServiceName"]!,
            serviceVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = config["Telemetry:Environment"] ?? "unknown"
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(o =>
        {
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/metrics")
                           && !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = false)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(config["Telemetry:OtlpEndpoint"]!);
            o.Headers = $"Authorization={config["Telemetry:OtlpAuthHeader"]}";
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("IrelandLiveSignals.Grid")
        .AddMeter("IrelandLiveSignals.Transit")
        .AddMeter("IrelandLiveSignals.Alerts")
        .AddMeter("IrelandLiveSignals.Feeds")
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(config["Telemetry:OtlpEndpoint"]!);
            o.Headers = $"Authorization={config["Telemetry:OtlpAuthHeader"]}";
        })
        .AddPrometheusExporter())
    .WithLogging(logging => logging
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(config["Telemetry:OtlpEndpoint"]!);
            o.Headers = $"Authorization={config["Telemetry:OtlpAuthHeader"]}";
        }));

// Prometheus scrape endpoint (for local Grafana Agent or debugging)
if (config.GetValue<bool>("Telemetry:EnablePrometheusEndpoint"))
{
    app.MapPrometheusScrapingEndpoint("/metrics");
}
```

If `OtlpEndpoint` is empty, skip OTLP exporters entirely and log a startup warning. The application must start cleanly without telemetry configured.

---

## Part 2 — Custom metrics

Define metrics using `System.Diagnostics.Metrics`. Create a single `SignalEireMetrics` service registered as singleton.

```csharp
public sealed class SignalEireMetrics : IDisposable
{
    private readonly Meter _gridMeter     = new("IrelandLiveSignals.Grid", "1.0");
    private readonly Meter _transitMeter  = new("IrelandLiveSignals.Transit", "1.0");
    private readonly Meter _alertsMeter   = new("IrelandLiveSignals.Alerts", "1.0");
    private readonly Meter _feedsMeter    = new("IrelandLiveSignals.Feeds", "1.0");

    // Grid metrics
    public Counter<long>       GridReadingsIngested   { get; }
    public Histogram<double>   GridCo2Intensity       { get; }   // g/kWh per reading
    public Histogram<double>   GridGreenScore         { get; }   // 0.0–1.0 per reading
    public ObservableGauge<double> GridCo2Latest      { get; }   // live value

    // Transit metrics
    public Counter<long>       VehicleObservationsIngested  { get; }
    public Counter<long>       TripMatchesTotal              { get; }  // tag: match_source
    public Histogram<double>   ArrivalConfidence             { get; }  // per scored arrival
    public ObservableGauge<double> ActiveVehicleCount        { get; }  // live count

    // Alert metrics
    public Counter<long>       AlertsFired      { get; }   // tag: channel (email/telegram/push/log)
    public Counter<long>       AlertsDelivered  { get; }   // tag: channel
    public Counter<long>       AlertsSuppressed { get; }   // tag: reason (quiet_hours/max_per_day)

    // Feed metrics
    public Histogram<double>   FeedFetchDurationMs  { get; }   // tag: source
    public Counter<long>       FeedFetchFailures    { get; }   // tag: source
    public ObservableGauge<double> FeedStalenessSeconds { get; } // tag: source
}
```

Instantiate all instruments in the constructor. Register `SignalEireMetrics` as a singleton in DI. Inject it into the Worker services and API layer — call `.Add()` / `.Record()` at the appropriate points in the ingestion pipeline.

### Where to record

| Metric | Record when |
|---|---|
| `GridReadingsIngested` | After successful SQLite write in `GridPollerService` |
| `GridCo2Intensity` | Same tick — record the CO₂ value of each reading |
| `GridGreenScore` | Same tick |
| `VehicleObservationsIngested` | After each VehiclePositions parse in `TransitPollerService` |
| `TripMatchesTotal` | After trip matching, tag with `match_source` value |
| `ArrivalConfidence` | After scoring each `StopArrivalPrediction` |
| `AlertsFired` | In `IAlertDeliveryService` before delivery attempt |
| `AlertsDelivered` | After successful delivery |
| `AlertsSuppressed` | In `IAlertRuleEngine` when suppression triggers |
| `FeedFetchDurationMs` | Around each adapter `FetchAsync` call, tag with source name |
| `FeedFetchFailures` | In adapter catch blocks |

Observable gauges (`GridCo2Latest`, `ActiveVehicleCount`, `FeedStalenessSeconds`) read from an in-memory state store updated by the workers. Create a `LiveSignalState` singleton that workers update and gauges read from.

---

## Part 3 — Grafana Cloud dashboards

Create a dashboard JSON file at `docs/grafana/signaleire-dashboard.json` that can be imported directly into Grafana Cloud.

The dashboard has five rows:

### Row 1 — System Health
- API request rate (req/s) — from `http.server.request.duration`
- API error rate (% 5xx) — from `http.server.request.duration` filtered by status code
- API p95 latency — histogram percentile
- Uptime / last restart — from process start time

### Row 2 — Data Ingestion
- Feed fetch success rate per source (EirGrid, VehiclePositions, TripUpdates, ServiceAlerts) — stat panels, green/red
- Feed fetch latency per source — bar gauge
- Seconds since last successful fetch per source — alert if > 2× polling interval
- Raw snapshots written per hour — time series

### Row 3 — Grid Intelligence
- Live CO₂ intensity — large stat panel with colour threshold (green < 200, amber 200–350, red > 350)
- Live green score — gauge panel 0–100
- CO₂ intensity over 24h — time series
- Green score over 24h — time series
- Alert fire rate (grid rules) — time series

### Row 4 — Transit Intelligence
- Active vehicle count — stat panel
- Trip match source breakdown (VehicleAndTripUpdate / VehicleOnly / TripUpdateOnly / StaticOnly) — pie chart
- Arrival confidence distribution — histogram
- Ghost risk breakdown (vehicle_confirmed / timetable_only / stale_gps / implausible) — bar chart
- Alert fire rate (transit rules) — time series

### Row 5 — User Activity
- Registered users — stat (from DB query via recording rule or admin API)
- Active push subscriptions — stat
- User transit reports submitted today — stat
- Alert deliveries by channel (email / telegram / push / log) — stacked bar

---

## Part 4 — Admin observability page

Extend the existing `/admin` section with a proper source health page.

### GET /admin/sources

Razor Page. Displays a table:

| Source | Last Fetch | Staleness | Status | Failures (24h) | Avg Latency |
|---|---|---|---|---|---|
| EirGrid Dashboard | 14s ago | 14s | ✓ Live | 0 | 840ms |
| NTA VehiclePositions | 8s ago | 8s | ✓ Live | 2 | 320ms |
| NTA TripUpdates | 12s ago | 12s | ✓ Live | 0 | 410ms |
| NTA ServiceAlerts | 45s ago | 45s | ✓ Live | 0 | 290ms |

Row colour: green if staleness < 2× polling interval, amber if 2–5×, red if > 5× or last fetch was a failure.

Data sourced from a new `FeedHealthStore` singleton updated by each adapter on every fetch attempt:

```csharp
public class FeedHealthStore
{
    private readonly ConcurrentDictionary<string, FeedHealthEntry> _entries = new();

    public void RecordSuccess(string source, TimeSpan latency);
    public void RecordFailure(string source, string error);
    public IReadOnlyList<FeedHealthEntry> GetAll();
}

public record FeedHealthEntry
{
    public string Source { get; init; }
    public DateTimeOffset LastSuccessUtc { get; set; }
    public DateTimeOffset LastAttemptUtc { get; set; }
    public bool LastAttemptSucceeded { get; set; }
    public TimeSpan LastLatency { get; set; }
    public int FailureCount24h { get; set; }
    public double AverageLatencyMs { get; set; }
}
```

`FeedHealthStore` is registered as a singleton. Both the admin page and `SignalEireMetrics` read from it.

### GET /admin/ingestion

Razor Page. Displays ingestion statistics for the last 24 hours:

- Grid readings ingested: N
- Vehicle observations ingested: N
- Trip matches by source (table)
- Arrival predictions scored: N
- Average confidence score: X%
- Alerts fired: N (by channel)
- Push subscriptions active: N

These figures come from SQLite queries — no separate stats store needed.

### GET /admin/anomalies

Razor Page. Lists detected anomalies (see Part 6). Links to Intelligence dashboard entries.

---

## Part 5 — Alert digest mode

### Schema changes

Add to `AlertRule`:
```csharp
public string DeliveryMode { get; set; } = "immediate";   // "immediate" | "digest"
public string DigestSchedule { get; set; } = "daily";     // "daily" | "weekly"
public TimeOnly DigestTime { get; set; } = new(08, 00);   // local time to send digest
```

Add to `ApplicationUser`:
```csharp
public bool DigestEnabled { get; set; } = false;
public TimeOnly DigestTime { get; set; } = new(08, 00);
public string DigestSchedule { get; set; } = "daily";
```

### DigestJob : BackgroundService

Runs every hour, checks whether any user has a digest due:

```
For each user with DigestEnabled = true:
    If current Ireland local time >= user.DigestTime and no digest sent today:
        Collect all AlertFiring records for this user since last digest
        If any firings exist:
            Group by AlertRule
            Generate digest email
            Send via existing IAlertDeliveryService (email channel)
            Mark firings as included in digest
```

### Digest email format

```
Subject: Your SignalEire daily summary — {date}

Your alerts summary for {date}:

GRID ALERTS
──────────────────────────────────────────
"Dishwasher Green Window" fired 2 times:
  • 02:14 — CO₂ at 198 g/kWh, renewables 63%. 
  • 04:47 — CO₂ at 181 g/kWh, renewables 71%.

TRANSIT ALERTS
──────────────────────────────────────────
No transit alerts fired.

──────────────────────────────────────────
Manage your alerts: https://yourdomain.ie/alerts
Unsubscribe from digest: https://yourdomain.ie/account/preferences
```

### Dashboard change

On the alert rule management page (`/alerts`), add a "Delivery" toggle per rule: Immediate / Daily Digest.  
On the account preferences page, add a "Daily digest" toggle with time picker.

---

## Part 6 — RAG anomaly detection and explanation

*Builds on Phase 5 Qdrant integration. If Phase 5 RAG was not completed, implement Phase 5's Qdrant indexer first, then this section.*

### Anomaly model

```csharp
public record SignalAnomaly
{
    public string Id { get; init; }
    public string Module { get; init; }          // "grid" | "transit"
    public string AnomalyType { get; init; }     // see types below
    public string Region { get; init; }
    public string? RouteId { get; init; }        // transit only
    public string? StopId { get; init; }         // transit only
    public DateOnly Date { get; init; }
    public double ObservedValue { get; init; }
    public double BaselineValue { get; init; }
    public double DeviationZScore { get; init; }
    public string ExplanationText { get; init; }
    public bool IndexedToQdrant { get; init; }
    public DateTimeOffset DetectedAtUtc { get; init; }
}
```

### Anomaly types

| Type | Module | Trigger |
|---|---|---|
| `high_co2_day` | grid | Daily average CO₂ > 2σ above 14-day rolling mean |
| `low_co2_day` | grid | Daily average CO₂ > 2σ below 14-day rolling mean (good news) |
| `low_renewables_day` | grid | Daily average renewables % > 2σ below 14-day mean |
| `route_reliability_drop` | transit | Route daily reliability rate > 15pp below 14-day average |
| `stop_ghost_spike` | transit | Stop ghost count > 3× 14-day average for that day-of-week |
| `feed_gap` | both | Source had zero successful fetches for > 30 minutes |

### AnomalyDetectionJob

Runs nightly at 04:30 Ireland local time (after the reliability aggregation at 03:00 and RAG summary job at 04:00).

```
For grid anomalies:
    Read GridReadings for yesterday, compute daily averages
    Read GridReadings for prior 14 days, compute rolling mean + std dev
    If |observed - mean| > 2 × stddev: create SignalAnomaly record

For transit anomalies:
    Read TransitReliabilityAggregates for yesterday
    For each route with ≥ 7 days of history:
        Compare yesterday's reliability to 14-day rolling mean
        If deviation > 15pp: create SignalAnomaly record
    For each stop:
        Compare yesterday's ghost count to 14-day same-day-of-week mean
        If > 3×: create SignalAnomaly record

For each new anomaly:
    Generate explanation text (see below)
    Save to SignalAnomalies table
    Index to Qdrant via IQdrantSummaryIndexer
```

### Explanation generation

Do **not** call an external LLM for each anomaly. Generate explanation text programmatically from the anomaly data, using templates. This is reliable, fast, and free.

```csharp
public static class AnomalyExplainer
{
    public static string Explain(SignalAnomaly anomaly) => anomaly.AnomalyType switch
    {
        "high_co2_day" => $"Grid carbon intensity on {anomaly.Date:dd MMM} averaged " +
            $"{anomaly.ObservedValue:F0} g/kWh — {anomaly.DeviationZScore:F1} standard deviations " +
            $"above the recent baseline of {anomaly.BaselineValue:F0} g/kWh. " +
            $"This suggests heavier reliance on fossil generation than usual.",

        "low_co2_day" => $"Grid carbon intensity on {anomaly.Date:dd MMM} averaged " +
            $"{anomaly.ObservedValue:F0} g/kWh — unusually clean, " +
            $"{anomaly.DeviationZScore:F1} standard deviations below the recent baseline. " +
            $"High renewable output likely contributed.",

        "route_reliability_drop" => $"Route {anomaly.RouteId} had a vehicle-confirmed arrival " +
            $"rate of {anomaly.ObservedValue:P0} on {anomaly.Date:dd MMM}, compared to a " +
            $"recent average of {anomaly.BaselineValue:P0}. " +
            $"This may indicate GPS feed gaps, operator issues, or service disruption.",

        "stop_ghost_spike" => $"Stop {anomaly.StopId} recorded {anomaly.ObservedValue:F0} " +
            $"unconfirmed arrivals on {anomaly.Date:dd MMM}, significantly above the typical " +
            $"{anomaly.BaselineValue:F1} for this day of the week.",

        "feed_gap" => $"The {anomaly.RouteId ?? anomaly.Module} feed had a data gap of " +
            $"more than 30 minutes on {anomaly.Date:dd MMM}. Predictions during this period " +
            $"may have lower confidence.",

        _ => $"An anomaly of type '{anomaly.AnomalyType}' was detected on {anomaly.Date:dd MMM}."
    };
}
```

The Qdrant index entry for each anomaly uses the explanation text as the document body, with metadata matching the Phase 5 `SignalSummary` structure.

### Surface anomalies in the Intelligence dashboard

Extend `/intelligence` (Phase 5):
- "Anomalies" tab alongside "Search" and "Recent summaries"
- Lists all anomalies from the last 30 days, most recent first
- Colour-coded by type (red for bad, green for low_co2_day)
- Click → expanded explanation + link to relevant history chart

### Surface anomalies in digest emails

If any anomalies were detected since the last digest, include an "Unusual patterns" section at the top of the digest email before the alert firings:

```
UNUSUAL PATTERNS DETECTED
──────────────────────────────────────────
• Grid: Yesterday's carbon intensity averaged 412 g/kWh — unusually high. 
  Heavy reliance on fossil generation. Consider deferring flexible loads today.
• Transit: Route 220 had a vehicle-confirmed rate of 61% — well below its
  recent average of 84%.
```

### SignalAnomalies database table

```
SignalAnomalies    ← index on (Module, Date), (AnomalyType, Date)
```

---

## Tests (Phase 6 additions)

### Unit tests

**AnomalyDetectionTests**
- High CO₂ day: inject 14 days of baseline readings + 1 outlier day → anomaly detected
- Normal day (within 1.5σ) → no anomaly
- Insufficient history (< 7 days) → no anomaly produced
- Route reliability drop > 15pp → `route_reliability_drop` anomaly
- Route reliability drop < 15pp → no anomaly
- Same-day-of-week ghost spike 4× → `stop_ghost_spike` anomaly

**AnomalyExplainerTests**
- Each anomaly type produces a non-empty, non-null explanation string
- Explanation references the observed value and baseline value
- No string format exceptions for edge-case values (0%, 100%, NaN-guarded)

**DigestJobTests**
- User with DigestEnabled, rule with 3 firings since last digest → digest sent, firings marked
- User with DigestEnabled, no firings → no email sent
- User with DigestEnabled = false → no email sent
- Digest already sent today → no duplicate

**FeedHealthStoreTests**
- RecordSuccess updates LastSuccessUtc and LastLatency
- RecordFailure increments FailureCount24h
- FailureCount24h resets after 24 hours

### Integration tests

**MetricsIntegrationTests** — using `WebApplicationFactory`, verify `/metrics` endpoint returns 200 and contains `signaleire_grid_readings_ingested_total`

---

## Constraints

1. **OTLP export is optional at runtime.** If `Telemetry:OtlpEndpoint` is empty, skip OTLP exporters. App starts and runs fully without Grafana Cloud configured.
2. **Prometheus `/metrics` endpoint is unauthenticated.** Add it to the nginx config to be accessible only from localhost, or accept that it's public (metrics are non-sensitive).
3. **Anomaly explanations are template-generated.** No external LLM calls in the anomaly job. Keep it fast and free.
4. **Digest does not replace immediate delivery.** Rules with `DeliveryMode = "immediate"` continue to fire as before. Only rules explicitly set to `"digest"` are batched.
5. **No Postgres.** SQLite throughout.
6. **No MAUI.** Deferred to Phase 7.
7. **All Phase 1–5 tests must remain green.**

---

## Definition of done for Phase 6

- [ ] `dotnet build` and `dotnet test` pass — all Phase 1–6 tests green
- [ ] `/metrics` endpoint returns Prometheus-format metrics including `signaleire_grid_*` and `signaleire_transit_*`
- [ ] Metrics visible in Grafana Cloud after configuring OTLP endpoint
- [ ] Feed health dashboard at `/admin/sources` shows live staleness for all four adapters
- [ ] Ingestion stats page at `/admin/ingestion` shows yesterday's counts
- [ ] Anomaly detection job runs and produces a `SignalAnomaly` row for a synthetically injected outlier reading
- [ ] Anomaly explanation text is non-empty and references correct values
- [ ] Anomalies listed on `/intelligence` anomalies tab
- [ ] Alert rule with `DeliveryMode = "digest"` does not send immediate email on firing
- [ ] Digest email sent at configured time, containing grouped firings
- [ ] Digest email includes anomaly section when anomalies were detected
- [ ] App starts and runs with no OTLP endpoint configured (no crash, warning logged)

---

## What comes in Phase 7

- MAUI native client (C#, cross-platform, consumes the existing API)
- Tariff-aware grid scoring (Electric Ireland / Bord Gáis Night Rate, once tariff config is ready)
- Postgres + TimescaleDB migration (if and when real load justifies it)
- Multi-region support (ROI + NI all-island view)
- Public developer API keys and external API documentation
