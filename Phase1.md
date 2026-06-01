# SignalEire — Phase 1 Build Brief
**For:** Claude Code  
**Scope:** Green Grid MVP — Milestone 1 only  
**Stack:** ASP.NET Core 8, C#, SQLite, Razor Pages  
**Do not build anything outside this document.**

---

## What you are building

A backend-first platform that ingests live Irish electricity grid data, stores it, scores it, and exposes it via a minimal API and dashboard.

This is **not** a mobile app. This is **not** a transit app. Those come later.

---

## Repository structure

Create this solution layout:

```
IrelandLiveSignals/
├── src/
│   ├── IrelandLiveSignals.Core/          # Domain models, interfaces, scoring logic
│   ├── IrelandLiveSignals.Infrastructure/ # EirGrid adapter, SQLite persistence
│   ├── IrelandLiveSignals.Worker/         # Background polling service
│   ├── IrelandLiveSignals.Api/            # ASP.NET Core minimal API
│   └── IrelandLiveSignals.Web/            # Razor Pages dashboard (minimal)
├── data/
│   ├── raw/                               # Raw JSON snapshots saved to disk
│   └── samples/                           # Sample payloads for testing
├── docs/
│   └── architecture.md
└── tests/
    ├── IrelandLiveSignals.Tests/
    └── IrelandLiveSignals.IntegrationTests/
```

One `.sln` file at the root tying all projects together.

---

## Milestone 1 — the only deliverable

```
✓ Worker fetches EirGrid grid data every 5 minutes
✓ Raw payload saved to disk as JSON snapshot
✓ Normalized GridReading saved to SQLite
✓ GET /api/grid/current returns latest reading
✓ Razor dashboard shows latest reading
✓ Basic greenScore computed and included in API response
```

Nothing else. No alerts. No users. No auth. No transit. No mobile.

---

## Data source

**EirGrid Smart Grid Dashboard**  
URL: `https://www.smartgriddashboard.com/`

> ⚠️ This dashboard's JSON endpoints are not a guaranteed stable public API. Treat all EirGrid fetching as a **fragile adapter** that must be isolated behind an interface. If the endpoint changes, only the adapter should need updating.

The adapter must:
- Accept a configurable base URL (for test overrides)
- Save the raw response to disk before parsing
- Log failures without crashing the worker
- Be replaceable without touching Core or API layers

---

## Domain models (Core project)

### RawGridSnapshot
```csharp
public record RawGridSnapshot
{
    public string Id { get; init; }           // e.g. "grid_raw_20260601_133000"
    public string Source { get; init; }       // "eirgrid_smart_grid_dashboard"
    public string SourceUrl { get; init; }
    public DateTimeOffset RetrievedAtUtc { get; init; }
    public string RawPayloadPath { get; init; }  // relative path under data/raw/
    public string Hash { get; init; }            // SHA-256 of raw payload
}
```

### GridReading
```csharp
public record GridReading
{
    public string Id { get; init; }
    public string Region { get; init; }           // "ROI"
    public DateTimeOffset TimestampUtc { get; init; }
    public double SystemDemandMw { get; init; }
    public double WindGenerationMw { get; init; }
    public double? SolarGenerationMw { get; init; }
    public double RenewablesPercent { get; init; }
    public double Co2IntensityGPerKwh { get; init; }
    public double? InterconnectorImportMw { get; init; }
    public double? InterconnectorExportMw { get; init; }
    public int DataFreshnessSeconds { get; init; }
    public double GreenScore { get; init; }       // computed 0.0–1.0
    public string GreenStatus { get; init; }      // "good" | "moderate" | "poor"
    public string Recommendation { get; init; }   // human-readable
    public string QualityStatus { get; init; }    // "ok" | "stale" | "partial"
}
```

---

## Green scoring algorithm (Core project)

Implement as a pure static/injectable service with no I/O.

```
greenScore =
    0.45 × normalizedRenewablesPercent
  + 0.35 × inverseNormalizedCo2Intensity
  + 0.10 × dataFreshnessScore
  + 0.10 × trendScore
```

Where:
- `normalizedRenewablesPercent` = `renewablesPercent / 100` (clamped 0–1)
- `inverseNormalizedCo2Intensity` = `1 - clamp(co2GPerKwh / 600, 0, 1)` (600 g/kWh = worst case baseline)
- `dataFreshnessScore` = `1 - clamp(freshnessSeconds / 300, 0, 1)` (5 min = zero freshness score)
- `trendScore` = `0.5` for MVP (no history yet); replace with real trend once history exists

Map score to status:
- `>= 0.65` → `"good"`, recommendation: `"Good time for flexible electricity use."`
- `>= 0.40` → `"moderate"`, recommendation: `"Grid conditions are average. Non-urgent loads can wait."`
- `< 0.40` → `"poor"`, recommendation: `"Grid is carbon-heavy. Defer flexible loads if possible."`

The scoring logic must be **unit tested** — this is the core intelligence.

---

## API endpoint (Api project)

### GET /api/grid/current

Response shape:
```json
{
  "region": "ROI",
  "timestampUtc": "2026-06-01T13:30:00Z",
  "dataFreshnessSeconds": 45,
  "systemDemandMw": 4380,
  "windGenerationMw": 2120,
  "solarGenerationMw": 310,
  "renewablesPercent": 58.1,
  "co2IntensityGPerKwh": 214,
  "greenScore": 0.72,
  "status": "good",
  "recommendation": "Good time for flexible electricity use."
}
```

Return `503` with a JSON error body if no reading is available yet.

### GET /api/grid/health

Returns:
```json
{
  "status": "ok",
  "lastReadingUtc": "2026-06-01T13:30:00Z",
  "secondsSinceLastReading": 45,
  "workerRunning": true
}
```

No other endpoints in Phase 1.

---

## Background worker (Worker project)

- `GridPollerService : BackgroundService`
- Poll interval: configurable via `appsettings.json`, default 5 minutes
- On each tick:
  1. Fetch from EirGrid adapter
  2. Save raw snapshot to `data/raw/grid/YYYY/MM/DD/HHmmss.json`
  3. Compute SHA-256 hash of raw payload
  4. Normalize to `GridReading`
  5. Compute green score
  6. Persist `GridReading` to SQLite
  7. Log result (structured logging, Serilog or Microsoft.Extensions.Logging)
- On fetch failure: log warning, do not throw, do not crash

---

## Dashboard (Web project)

Razor Pages only. One page: `Index.cshtml`

Display:
- CO₂ intensity (large, prominent)
- Renewable share %
- Green score (0–100 display, coloured: green/amber/red)
- Status label and recommendation text
- Wind generation MW
- Solar generation MW (if available)
- System demand MW
- Data timestamp and freshness in seconds
- "Last updated: X seconds ago" (auto-refreshes every 60s via meta refresh or minimal JS)

No Blazor. No React. No npm. Razor + plain CSS only for Phase 1.

---

## Persistence (Infrastructure project)

- SQLite via EF Core
- One table: `GridReadings`
- Keep all columns from the `GridReading` model
- On startup: `EnsureCreated()` (migrations not required for Phase 1)
- Index on `TimestampUtc`

---

## Configuration (appsettings.json)

```json
{
  "GridPoller": {
    "IntervalMinutes": 5,
    "Region": "ROI",
    "EirGridBaseUrl": "https://www.smartgriddashboard.com/"
  },
  "ConnectionStrings": {
    "Sqlite": "Data Source=data/signals.db"
  },
  "RawSnapshotPath": "data/raw"
}
```

---

## Tests

### Unit tests (Tests project)
- `GreenScoringServiceTests` — test score computation across boundary cases:
  - all renewables, low CO₂ → score near 1.0
  - all fossil, high CO₂ → score near 0.0
  - stale data (freshnessSeconds = 400) → freshness penalty applied
  - status label thresholds
- `GridReadingNormalizerTests` — test that raw adapter output maps correctly to `GridReading`

### Integration tests (IntegrationTests project)
- `GridPollerIntegrationTests` — uses a sample payload from `data/samples/` to simulate a full fetch → normalize → persist → retrieve cycle
- No live network calls in tests

---

## Constraints and rules

1. **No live network calls in tests.** Use recorded payloads from `data/samples/`.
2. **EirGrid adapter is behind an interface.** `IGridDataAdapter` in Core. Implementation in Infrastructure.
3. **Scoring is pure.** No I/O in `GreenScoringService`.
4. **Raw snapshots are always saved before parsing.** If parsing fails, the raw file still exists for debugging.
5. **No auth, no users, no sessions** in Phase 1.
6. **No transit code** in Phase 1. If transit types appear anywhere, that is a mistake.
7. **No Kafka, no Redis, no message bus.** SQLite and background worker only.
8. **Worker and API can run as a single combined host** (Worker + Api + Web in one `Program.cs`) for simplicity.

---

## Definition of done for Phase 1

- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test` passes all unit and integration tests
- [ ] Worker polls and writes a `GridReading` to SQLite
- [ ] `GET /api/grid/current` returns a valid JSON response
- [ ] Dashboard renders current grid state in a browser
- [ ] Raw snapshot file exists on disk after first poll
- [ ] `GET /api/grid/health` reports worker status

When all of the above are true, Phase 1 is complete. Do not proceed to Phase 2 features.

---

## What comes after Phase 1 (do not build yet)

- Alert rules and notification delivery
- `/api/grid/history` endpoint
- EV charge window recommendation engine
- Transit Trust Engine (separate module, separate spec)
- PWA / mobile client
- Multi-user support and auth
