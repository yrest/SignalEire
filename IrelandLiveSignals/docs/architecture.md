# SignalEire — Architecture Notes (Phase 1)

## Deployment target

- **URL:** `https://signal.pointit.ie` (subdomain root)
- **Model:** Single ASP.NET Core host, no path base prefix required
- All asset and API paths are relative — no hardcoded origins
- `appsettings.Production.json` should override `ConnectionStrings:Sqlite` and `RawSnapshotPath` to absolute server paths

## Project layout

| Project | Role |
|---|---|
| `Core` | Domain models, interfaces, `GreenScoringService` (pure, no I/O) |
| `Infrastructure` | EirGrid HTTP adapter, EF Core / SQLite persistence |
| `Worker` | `GridPollerService : BackgroundService` |
| `Api` | Combined host — minimal API endpoints + Razor Pages dashboard |
| `Tests` | Unit tests (scoring, normalizer) |
| `IntegrationTests` | Full cycle tests using recorded sample payloads |

## EirGrid data source

Base URL: `https://www.smartgriddashboard.com/DashboardService.svc/data`

Queried areas per poll cycle:

| area= | Data |
|---|---|
| `generationactual` | Wind, hydro, gas, coal, net import, total generation |
| `co2intensity` | CO₂ g/kWh |
| `interconnection` | Net interconnector flow MW |
| `demandactual` | System demand MW |

- `region=ROI` for Republic of Ireland
- Date range: today 00:00–23:59; latest row taken as current reading
- No dedicated "current" endpoint — newest row in today's response is used
- Adapter is behind `IGridDataAdapter` in Core; only `EirGridAdapter` in Infrastructure needs updating if endpoints change

## Green scoring

```
greenScore =
    0.45 × (renewablesPercent / 100)
  + 0.35 × (1 - clamp(co2GPerKwh / 600, 0, 1))
  + 0.10 × (1 - clamp(freshnessSeconds / 300, 0, 1))
  + 0.10 × 0.5   ← trendScore hardcoded for MVP; replace once history exists
```

| Score | Status | Recommendation |
|---|---|---|
| ≥ 0.65 | good | Good time for flexible electricity use. |
| ≥ 0.40 | moderate | Grid conditions are average. Non-urgent loads can wait. |
| < 0.40 | poor | Grid is carbon-heavy. Defer flexible loads if possible. |

## Data paths

Paths are relative to the executable's working directory (i.e., the app's current directory at runtime):

- SQLite DB: `data/signals.db`
- Raw JSON snapshots: `data/raw/grid/YYYY/MM/DD/HHmmss.json`
- Sample payloads (tests): `data/samples/`

On a hosted server, set working directory or override via `appsettings.Production.json`.
