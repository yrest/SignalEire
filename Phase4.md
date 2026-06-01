# SignalEire — Phase 4 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 3 complete and passing; at least 48 hours of real VehicleObservation data collected  
**Scope:** Route shape geometry + advanced plausibility + reliability aggregation + historical confidence  
**Do not build anything outside this document.**

---

## Pre-conditions — do not start without these

1. Phase 3 is complete and all tests pass.
2. At least 48 hours of real `VehicleObservation` data is in SQLite — the reliability aggregation job is meaningless without it.
3. `shapes.txt` is present in the GTFS static zip (confirm it exists in your NTA export before running the import).

---

## What Phase 4 adds

1. **`shapes.txt` import** — route polylines stored as sequences of lat/lon points
2. **Cross-track distance** — how far a vehicle is from its expected route geometry
3. **Bearing vs route tangent** — is the vehicle heading in the right direction along the shape?
4. **Jump detection** — did a vehicle teleport between consecutive observations?
5. **Full physical plausibility score** — replaces the Phase 3 `basicPlausibilityScore`
6. **Nightly reliability aggregation job** — populates `TransitReliabilityAggregates`
7. **`historicalReliabilityScore` in confidence formula** — uses real historical data
8. **Reliability dashboard views** — per stop and per route

---

## Shape import (Infrastructure project)

### Add to existing GTFS static import command

Extend `import-gtfs` to also parse `shapes.txt` if present.  
If `shapes.txt` is absent from the zip, log a warning and continue — do not fail the import.

### Domain model (Core project)

```csharp
public record RouteShapePoint
{
    public string ShapeId { get; init; }
    public int Sequence { get; init; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    public double? DistTravelled { get; init; }   // shape_dist_traveled if present
}
```

`TransitTrip` already has a `ShapeId` field — add it if missing from Phase 3 model:
```csharp
public string? ShapeId { get; init; }   // links TransitTrip → RouteShapePoint sequence
```

### Database addition

```
RouteShapePoints    ← index on (ShapeId, Sequence)
```

---

## Geometry helpers (Core project)

Add to `GeoMath`:

```csharp
public static class GeoMath
{
    // ... existing Phase 3 methods ...

    // Nearest point on a polyline segment (P1→P2) to a query point Q
    // Returns the point and the along-track distance from P1
    public static (double Lat, double Lon, double AlongTrackMetres)
        NearestPointOnSegment(
            double qLat, double qLon,
            double p1Lat, double p1Lon,
            double p2Lat, double p2Lon);

    // Cross-track distance from Q to polyline (array of points)
    // Returns distance in metres and the index of the nearest segment
    public static (double CrossTrackMetres, int NearestSegmentIndex)
        CrossTrackDistance(
            double qLat, double qLon,
            IReadOnlyList<(double Lat, double Lon)> polyline);

    // Tangent bearing of polyline at a given segment index
    // (bearing from segment[i] to segment[i+1])
    public static double PolylineTangentBearing(
        IReadOnlyList<(double Lat, double Lon)> polyline,
        int segmentIndex);
}
```

### Algorithm for cross-track distance

For each consecutive pair of polyline points (segment):
1. Project Q onto the segment using the `NearestPointOnSegment` formula
2. Compute haversine from Q to the projected point
3. Track the minimum across all segments

This is O(n) in the number of shape points. For typical GTFS shapes (~200–500 points per route) this is fast enough; no spatial index required in Phase 4.

### Unit tests (Tests project)

**GeoMathPhase4Tests**
- Cross-track distance: point directly on a segment → 0m
- Cross-track distance: point perpendicular to segment midpoint → matches analytical result
- Cross-track distance: point beyond segment end → clamps to endpoint
- Tangent bearing: horizontal segment (west to east) → ~90°
- Tangent bearing: vertical segment (south to north) → ~0°
- Full polyline: known vehicle off-route by 200m → returns ~200m ± tolerance

---

## Full physical plausibility score (Core project)

Phase 4 **replaces** `basicPlausibilityScore` in the confidence formula with `fullPlausibilityScore`.

The `ArrivalConfidenceInput` gains two new optional fields:
```csharp
public IReadOnlyList<RouteShapePoint>? ShapePoints { get; init; }   // null = use basic fallback
public VehicleObservation? PreviousObservation { get; init; }        // null = no jump check
```

### Sub-checks (all combined as weighted minimum)

**Cross-track distance check**  
Requires `ShapePoints` to be non-null and non-empty.

| Cross-track distance | Score |
|---|---:|
| ≤ 50m | 1.0 |
| 51–150m | 0.8 |
| 151–300m | 0.5 |
| 301–500m | 0.25 |
| > 500m | 0.1 |
| No shape data (null) | use Phase 3 bearing-to-stop fallback |

**Route tangent bearing check**  
Requires `ShapePoints` and vehicle `Bearing`.

Find nearest segment index, get tangent bearing, compare to vehicle bearing:

| |delta| vs tangent | Score |
|---|---:|
| ≤ 30° | 1.0 |
| 31–60° | 0.8 |
| 61–120° | 0.5 |
| > 120° | 0.2 |
| No bearing or no shape | use Phase 3 bearing-to-stop fallback |

**Jump detection check**  
Requires `PreviousObservation`.

```
timeDeltaSeconds = (current.TimestampUtc - previous.TimestampUtc).TotalSeconds
distanceMetres   = HaversineMetres(current, previous)
impliedSpeedKph  = (distanceMetres / timeDeltaSeconds) × 3.6
```

| Implied speed | Score |
|---|---:|
| ≤ 120 km/h | 1.0 |
| 121–200 km/h | 0.4 |
| > 200 km/h | 0.0 (teleport — treat as bad GPS) |
| timeDelta > 300s | 1.0 (gap too large to judge) |
| No previous observation | 1.0 (first sighting) |

**Combined `fullPlausibilityScore`**

```
fullPlausibilityScore = min(crossTrackScore, tangentBearingScore, jumpScore)
```

If shape data is unavailable, fall back to Phase 3 `basicPlausibilityScore` transparently.

### Backward compatibility

The scorer interface does not change. New fields on `ArrivalConfidenceInput` are nullable. If both are null, behaviour is identical to Phase 3. Phase 3 tests must remain green without modification.

---

## Nightly reliability aggregation job (Worker project)

### ReliabilityAggregationService

Run once per day, at 03:00 Ireland local time (Europe/Dublin).

```csharp
public class ReliabilityAggregationService : BackgroundService
{
    // Wakes at 03:00 Europe/Dublin, runs aggregation for yesterday, sleeps until next 03:00
}
```

### Aggregation logic

For each `(StopId, RouteId)` pair that had scheduled arrivals yesterday:

1. Count total scheduled arrivals (from `TransitStopTimes` for yesterday's active service IDs)
2. Count how many had a `VehicleObservation` within ±5 minutes of scheduled arrival time (`VehicleConfirmedCount`)
3. Count how many had no vehicle observation and no `TripUpdateObservation` (`TimetableOnlyCount`)
4. Count `TransitUserReports` with `reportType = bus_not_appeared` for that stop/route/date (`GhostCount`)
5. Compute `AverageConfidence` from `StopArrivalPrediction.Confidence` values stored during the day
6. Compute `ReliabilityRate = VehicleConfirmedCount / TotalScheduledArrivals`

Upsert into `TransitReliabilityAggregates` (one row per stop/route/date).

### StopArrivalPrediction persistence

Phase 3 built predictions on-the-fly. Phase 4 needs to **persist** them for aggregation.

Add to the `TransitPollerService` (Worker): after building the stop board for active stops, save each `StopArrivalPrediction` to a new `PersistedArrivalPredictions` table. Prune rows older than 30 days.

```
PersistedArrivalPredictions   ← index on (StopId, ScheduledArrivalUtc)
```

---

## Historical reliability score in confidence formula

Once `TransitReliabilityAggregates` has data, wire it into the scorer.

Add to `ArrivalConfidenceInput`:
```csharp
public double? HistoricalReliabilityRate { get; init; }   // null = no history yet
```

Add `historicalReliabilityScore` to the formula:

```
confidence = vehiclePresenceScore
           × gpsFreshnessScore
           × tripMatchScore
           × fullPlausibilityScore
           × serviceAlertScore
           × historicalReliabilityScore
```

**historicalReliabilityScore** mapping:

| HistoricalReliabilityRate | Score |
|---|---:|
| null (no data) | 1.0 (neutral) |
| ≥ 0.85 | 1.0 |
| 0.70–0.84 | 0.9 |
| 0.50–0.69 | 0.75 |
| 0.30–0.49 | 0.55 |
| < 0.30 | 0.35 |

The API layer loads the relevant `TransitReliabilityAggregate` before calling the scorer and populates this field.

---

## API additions (Api project)

```
GET /api/transit/reliability?routeId=220&stopId=stop_123
```
Returns the most recent `TransitReliabilityAggregate` for the combination, plus the last 14 days of daily aggregates.

```
GET /api/transit/routes/{routeId}/reliability
```
Returns daily reliability aggregates for the route across all stops, last 14 days.

```
GET /api/transit/stops/{stopId}/reliability
```
Returns daily reliability aggregates for the stop across all routes, last 14 days.

---

## Dashboard additions (Web project)

Still Razor Pages + plain CSS. No new JS frameworks.

### Stop reliability page: `/transit/stops/{stopId}/reliability`

- Reliability rate per route at this stop (table + simple SVG bar chart)
- Last 14 days trend for the top 3 routes
- Ghost count and confirmed count per day
- Link back to stop board

### Route reliability page: `/transit/routes/{routeId}/reliability`

- Reliability rate per day (SVG line chart, last 14 days)
- Top 5 stops by lowest reliability rate (potential problem stops)
- Ghost count trend

### Stop board upgrades

- Add "Historical reliability: 94%" line per arrival (sourced from `TransitReliabilityAggregate`)
- Show `n/a` if no history yet (expected for first weeks)

### Map upgrades

- Stop marker colour reflects reliability: green (≥ 85%), amber (50–84%), red (< 50%), grey (no data)
- Reliability tooltip on stop marker click: "Route 220: 91% reliable (last 14 days)"

---

## Tests (Phase 4 additions)

### Unit tests

**GeoMathPhase4Tests** — as specified in the geometry helpers section above

**FullPlausibilityScorerTests**
- Vehicle on route (crossTrack ≤ 50m, bearing aligned) → score = 1.0
- Vehicle 400m from route → cross-track penalty applied
- Vehicle moving in wrong direction along shape → tangent penalty
- Vehicle jumped 300 km in 10 seconds → jump score = 0.0
- No shape data → falls back to Phase 3 basic plausibility
- No previous observation → jump score = 1.0

**HistoricalReliabilityScoreTests**
- null rate → 1.0 (neutral)
- 0.9 rate → 1.0
- 0.6 rate → 0.75
- 0.2 rate → 0.35

**ReliabilityAggregationLogicTests**
- Given mock scheduled arrivals and mock vehicle observations, verify correct counts
- VehicleConfirmedCount: vehicle within ±5 minutes counts; outside window does not
- GhostCount: user report of type `bus_not_appeared` counted correctly

### Integration tests

**ShapeImportTests** — import GTFS zip with `shapes.txt`, verify shape points stored for a known shape_id

---

## Constraints

1. **Backward compatibility.** Phase 3 tests must pass without modification. New inputs to the scorer are nullable and default to neutral scores.
2. **No shape library.** All geometry in `GeoMath`. No NuGet spatial packages.
3. **Aggregation job runs at 03:00 local time.** Use `TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin")` — handle DST correctly.
4. **Aggregation job skips if data is empty.** Do not write zero-row aggregates.
5. **Reliability views show "No data yet" gracefully.** Do not crash on empty tables.
6. **No auth, no user accounts.** Still single-tenant.
7. **No Grid code changes.** Phase 1 and 2 tests remain green.

---

## Definition of done for Phase 4

- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test` passes — all Phase 1–4 tests green
- [ ] `shapes.txt` imported; `RouteShapePoints` table has rows
- [ ] Cross-track distance computed for a live vehicle observation (log it)
- [ ] Jump detection fires on a synthetic bad-GPS observation in integration test
- [ ] `fullPlausibilityScore` replaces `basicPlausibilityScore` in confidence output
- [ ] Reliability aggregation job runs and produces rows in `TransitReliabilityAggregates` (requires 48h+ of data)
- [ ] `GET /api/transit/reliability?routeId=...&stopId=...` returns data
- [ ] Stop board shows historical reliability percentage (or "n/a" if no data)
- [ ] Stop map markers are coloured by reliability
- [ ] `/transit/stops/{stopId}/reliability` page renders

---

## What comes after Phase 4

- User accounts and per-user favourite stops / alert rules
- Tariff-aware grid scoring (Night Rate / Smart Tariff)
- PWA / MAUI mobile client
- Postgres + TimescaleDB migration for scale
- OpenTelemetry observability
- RAG / LLM summary layer for daily reliability reports
