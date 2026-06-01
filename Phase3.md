# SignalEire — Phase 3 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 2 complete and passing  
**Scope:** GTFS static import + GTFS-RT ingestion + stop board + confidence scoring + Google Maps display  
**Do not build anything outside this document.**

---

## Pre-conditions — do not start without these

Claude Code cannot fabricate protobuf structure. These files must exist before running this brief:

```
data/samples/transit/vehiclepositions.pb     ← real NTA VehiclePositions snapshot
data/samples/transit/tripupdates.pb          ← real NTA TripUpdates snapshot
data/samples/transit/servicealerts.pb        ← real NTA ServiceAlerts snapshot (can be empty feed)
data/samples/transit/gtfs-static.zip         ← real NTA GTFS static zip
```

If any of these are missing, stop and tell the user before proceeding.

---

## What Phase 3 adds

1. **GTFS static import** — one-time bulk load of stops, routes, trips, stop_times, calendar
2. **Three GTFS-RT adapters** — VehiclePositions, TripUpdates, ServiceAlerts (protobuf)
3. **Trip matching** — join live feeds to static trips
4. **Stop search** — by name, number, route; nearby by lat/lon
5. **Address search** — Google Geocoding API → lat/lon → nearby stops
6. **Stop board with confidence** — per-arrival scoring with ghost risk classification
7. **Vehicle detail view** — position, freshness, plausibility signals
8. **Google Maps dashboard** — vehicle and stop map via Maps JavaScript API
9. **Anonymous user confirmations** — cookie UUID, no auth required
10. **Reliability schema** — tables created, aggregation job stubbed, not yet populated

**Not in Phase 3:** route shape geometry, cross-track distance, reliability aggregation with real data, shape-snapping plausibility. Those are Phase 4.

---

## NuGet packages required

Add to `IrelandLiveSignals.Infrastructure`:
```
Google.Protobuf
GtfsRealtimeBindings
```

Add to `IrelandLiveSignals.Api` / `IrelandLiveSignals.Web`:
```
Microsoft.AspNetCore.Http (already present)
```

No Google Maps SDK needed — Maps JS and Geocoding are HTTP/CDN only.

---

## Google APIs — key strategy

**Two separate API keys** configured in `appsettings.json`:

| Key | Used where | Restriction |
|---|---|---|
| `GoogleMaps:BrowserKey` | Razor page `<script>` tag (client-side) | HTTP referrer restricted in Google Cloud Console |
| `GoogleMaps:ServerKey` | Geocoding API calls from ASP.NET Core (server-side) | IP restricted |

The browser key appears in rendered HTML — this is expected and normal. Restrict it by referrer. The server key must never appear in any client-rendered output.

```json
"GoogleMaps": {
  "BrowserKey": "",
  "ServerKey": "",
  "GeocodingBaseUrl": "https://maps.googleapis.com/maps/api/geocode/json"
}
```

---

## GTFS static import

### Command / startup task

Implement as a CLI command runnable via:
```
dotnet run -- import-gtfs --file data/samples/transit/gtfs-static.zip
```

Also expose as a Razor admin page button: `POST /admin/transit/import-gtfs`

### Files to import from the zip

| File | Import | Notes |
|---|---|---|
| `stops.txt` | ✓ | All columns below |
| `routes.txt` | ✓ | All columns below |
| `trips.txt` | ✓ | All columns below |
| `stop_times.txt` | ✓ | Large file — stream-parse, do not load into memory |
| `calendar.txt` | ✓ | |
| `calendar_dates.txt` | ✓ | |
| `shapes.txt` | ✗ | Deferred to Phase 4 |
| `agency.txt` | ✗ | Not needed yet |

### Domain models (Core project)

```csharp
public record TransitStop
{
    public string StopId { get; init; }
    public string StopCode { get; init; }    // user-facing stop number
    public string StopName { get; init; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    public string? ZoneId { get; init; }
    public DateTimeOffset ImportedAtUtc { get; init; }
}

public record TransitRoute
{
    public string RouteId { get; init; }
    public string ShortName { get; init; }   // e.g. "220"
    public string LongName { get; init; }    // e.g. "Cork - Fermoy"
    public string RouteType { get; init; }   // "3" = bus
    public string AgencyId { get; init; }
}

public record TransitTrip
{
    public string TripId { get; init; }
    public string RouteId { get; init; }
    public string ServiceId { get; init; }
    public string? TripHeadsign { get; init; }
    public string? DirectionId { get; init; }
    public string? BlockId { get; init; }
}

public record TransitStopTime
{
    public string TripId { get; init; }
    public string StopId { get; init; }
    public int StopSequence { get; init; }
    public TimeSpan? ArrivalTime { get; init; }   // nullable: some stops are timing points only
    public TimeSpan? DepartureTime { get; init; }
    public string? PickupType { get; init; }
    public string? DropoffType { get; init; }
}

public record ServiceCalendar
{
    public string ServiceId { get; init; }
    public bool Monday { get; init; }
    public bool Tuesday { get; init; }
    public bool Wednesday { get; init; }
    public bool Thursday { get; init; }
    public bool Friday { get; init; }
    public bool Saturday { get; init; }
    public bool Sunday { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

public record ServiceCalendarDate
{
    public string ServiceId { get; init; }
    public DateOnly Date { get; init; }
    public int ExceptionType { get; init; }   // 1=added, 2=removed
}
```

### Import behaviour
- Truncate and reload (not incremental) — GTFS static is replaced wholesale
- Log row counts for each table after import
- `stop_times.txt` must be stream-parsed (can be 1M+ rows); do not `File.ReadAllLines`
- Wrap entire import in a transaction — partial imports must roll back

---

## GTFS-RT adapters (Infrastructure project)

### Interface (already defined in Phase 1 pattern)
```csharp
public interface ITransitRealtimeAdapter
{
    string FeedName { get; }
    Task<RawTransitSnapshot> FetchAsync(CancellationToken ct);
    Task<TransitRealtimePayload> ParseAsync(RawTransitSnapshot snapshot);
}
```

### Three adapters

**NtaVehiclePositionsAdapter** — fetches VehiclePositions protobuf feed  
**NtaTripUpdatesAdapter** — fetches TripUpdates protobuf feed  
**NtaServiceAlertsAdapter** — fetches ServiceAlerts protobuf feed

All three:
- Save raw `.pb` bytes to disk before parsing (same pattern as grid raw snapshots)
- Use `FeedMessage.Parser.ParseFrom(bytes)` from `GtfsRealtimeBindings`
- Log fetch failure; do not throw; do not crash worker
- Accept configurable feed URLs via `appsettings.json`

```json
"TransitPoller": {
  "VehiclePositionsUrl": "",
  "TripUpdatesUrl": "",
  "ServiceAlertsUrl": "",
  "VehiclePositionsIntervalSeconds": 30,
  "TripUpdatesIntervalSeconds": 30,
  "ServiceAlertsIntervalSeconds": 60
}
```

### TransitRealtimePayload

```csharp
public record TransitRealtimePayload
{
    public IReadOnlyList<VehicleObservation> Vehicles { get; init; }
    public IReadOnlyList<TripUpdateObservation> TripUpdates { get; init; }
    public IReadOnlyList<ServiceAlertObservation> ServiceAlerts { get; init; }
}
```

### Domain models (Core project)

```csharp
public record VehicleObservation
{
    public string VehicleId { get; init; }
    public string? TripId { get; init; }        // null if not assigned
    public string? RouteId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    public float? Bearing { get; init; }         // degrees 0–360, null if unavailable
    public float? SpeedMps { get; init; }        // metres/second, null if unavailable
    public int GpsAgeSeconds { get; init; }      // now - vehicle timestamp
    public string QualityStatus { get; init; }   // "ok" | "stale" | "no_trip"
    public bool TripMatched { get; init; }
    public DateTimeOffset IngestedAtUtc { get; init; }
}

public record TripUpdateObservation
{
    public string TripId { get; init; }
    public string? RouteId { get; init; }
    public string? VehicleId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public IReadOnlyList<StopTimeEventObservation> StopTimeUpdates { get; init; }
}

public record StopTimeEventObservation
{
    public string StopId { get; init; }
    public int StopSequence { get; init; }
    public DateTimeOffset? ScheduledArrivalUtc { get; init; }
    public DateTimeOffset? PredictedArrivalUtc { get; init; }
    public DateTimeOffset? ScheduledDepartureUtc { get; init; }
    public DateTimeOffset? PredictedDepartureUtc { get; init; }
}

public record ServiceAlertObservation
{
    public string AlertId { get; init; }
    public string Effect { get; init; }          // "NO_SERVICE" | "REDUCED_SERVICE" | "SIGNIFICANT_DELAYS" etc.
    public string? HeaderText { get; init; }
    public string? DescriptionText { get; init; }
    public IReadOnlyList<string> AffectedRouteIds { get; init; }
    public IReadOnlyList<string> AffectedStopIds { get; init; }
    public DateTimeOffset? ActiveFrom { get; init; }
    public DateTimeOffset? ActiveUntil { get; init; }
    public DateTimeOffset IngestedAtUtc { get; init; }
}
```

---

## Trip matching (Core project)

The matcher is a pure service — no I/O.

```csharp
public interface ITripMatcher
{
    MatchedTrip? Match(
        VehicleObservation vehicle,
        IReadOnlyList<TripUpdateObservation> tripUpdates,
        IReadOnlyList<TransitTrip> staticTrips);
}

public record MatchedTrip
{
    public string TripId { get; init; }
    public string RouteId { get; init; }
    public string? VehicleId { get; init; }
    public VehicleObservation? Vehicle { get; init; }           // null if timetable-only
    public TripUpdateObservation? TripUpdate { get; init; }     // null if no RT update
    public TripMatchSource Source { get; init; }
}

public enum TripMatchSource
{
    VehicleAndTripUpdate,   // both feeds agree
    VehicleOnly,            // vehicle present, no matching TripUpdate
    TripUpdateOnly,         // TripUpdate present, no matching vehicle
    StaticOnly              // no RT data, schedule only
}
```

### Matching logic

1. Index `tripUpdates` by `TripId` and by `VehicleId` for O(1) lookup.
2. For each `VehicleObservation`:
   - If `vehicle.TripId` matches a `TripUpdate.TripId` → `VehicleAndTripUpdate`
   - If `vehicle.TripId` matches a static trip but no TripUpdate → `VehicleOnly`
   - If `vehicle.TripId` is null → attempt match by `RouteId` + proximity to expected stop sequence (best-effort; log ambiguous)
3. For remaining unmatched `TripUpdate` records → `TripUpdateOnly`
4. For scheduled trips with no RT at all → `StaticOnly`

**Handling null `trip_id` vehicles:** log as unmatched, do not guess, do not crash.

---

## Confidence scoring (Core project)

Pure service, no I/O.

```csharp
public interface IArrivalConfidenceScorer
{
    ArrivalConfidenceResult Score(ArrivalConfidenceInput input);
}

public record ArrivalConfidenceInput
{
    public MatchedTrip Trip { get; init; }
    public TransitStop Stop { get; init; }
    public DateTimeOffset NowUtc { get; init; }
    public bool HasActiveServiceAlert { get; init; }
    // Phase 4 will add: double HistoricalReliabilityRate
}

public record ArrivalConfidenceResult
{
    public double Confidence { get; init; }          // 0.0–1.0
    public string GhostRisk { get; init; }           // see classification below
    public string StatusLabel { get; init; }         // human-readable
    public double VehiclePresenceScore { get; init; }
    public double GpsFreshnessScore { get; init; }
    public double TripMatchScore { get; init; }
    public double BasicPlausibilityScore { get; init; }
    public double ServiceAlertScore { get; init; }
}
```

### Scoring formula

```
confidence = vehiclePresenceScore
           × gpsFreshnessScore
           × tripMatchScore
           × basicPlausibilityScore
           × serviceAlertScore
```

Note: multiplicative, not additive. One zero kills the score.

### Component scores

**vehiclePresenceScore**

| Condition | Score |
|---|---:|
| Live vehicle matched to trip | 1.0 |
| TripUpdate only (no vehicle) | 0.6 |
| Timetable only | 0.3 |
| Cancelled (service alert) | 0.0 |

**gpsFreshnessScore**

| GPS age | Score |
|---|---:|
| 0–30s | 1.0 |
| 31–90s | 0.8 |
| 91–180s | 0.5 |
| 181–300s | 0.25 |
| > 300s | 0.1 |
| No vehicle | 1.0 (not applicable — vehicle presence already penalised) |

**tripMatchScore**

| Match source | Score |
|---|---:|
| VehicleAndTripUpdate | 1.0 |
| VehicleOnly | 0.85 |
| TripUpdateOnly | 0.7 |
| StaticOnly | 0.4 |

**basicPlausibilityScore** (Phase 3 — no shape geometry)

Two sub-checks combined as minimum:

_Bearing toward stop:_
- Compute bearing from vehicle position to the target stop using haversine
- Compare to `vehicle.Bearing` (if available)
- |delta| ≤ 45° → 1.0
- |delta| ≤ 90° → 0.75
- |delta| > 90° (moving away) → 0.35
- No bearing data → 0.85 (uncertain, not heavily penalised)

_Distance/time plausibility:_
- Compute haversine distance from vehicle to stop
- Compute expected travel time at a nominal 20 km/h (urban) or 60 km/h (intercity)
- If predicted arrival is earlier than physically possible → 0.3
- Otherwise → 1.0
- No vehicle position → 1.0 (not applicable)

`basicPlausibilityScore = min(bearingScore, distancePlausibilityScore)`

**serviceAlertScore**

| Condition | Score |
|---|---:|
| No active alert for this route/stop | 1.0 |
| Active alert (delays/reduced service) | 0.6 |
| Active cancellation alert | 0.0 |

### Ghost risk classification

Derive from the component scores after calculation:

| Status | Condition |
|---|---|
| `vehicle_confirmed` | vehiclePresence = 1.0 AND gpsAge ≤ 90s AND tripMatch = VehicleAndTripUpdate AND plausibility ≥ 0.75 |
| `likely_active` | vehiclePresence = 1.0 AND (gpsAge 91–300s OR tripMatch = VehicleOnly) |
| `timetable_only` | tripMatch = StaticOnly |
| `stale_gps` | vehiclePresence = 1.0 AND gpsAge > 300s |
| `implausible` | basicPlausibilityScore < 0.4 |
| `cancelled` | serviceAlertScore = 0.0 |

Precedence (first match wins): `cancelled` > `implausible` > `stale_gps` > `vehicle_confirmed` > `likely_active` > `timetable_only`

### Haversine helper (Core project)

```csharp
public static class GeoMath
{
    // Returns distance in metres
    public static double HaversineMetres(double lat1, double lon1, double lat2, double lon2);

    // Returns bearing in degrees (0=North, 90=East, 180=South, 270=West)
    public static double BearingDegrees(double fromLat, double fromLon, double toLat, double toLon);

    // Returns angular difference between two bearings (-180 to +180)
    public static double BearingDelta(double bearing1, double bearing2);
}
```

No external libraries for geo math. Implement directly.

---

## Stop arrival prediction (Core project)

```csharp
public record StopArrivalPrediction
{
    public string StopId { get; init; }
    public string RouteId { get; init; }
    public string RouteShortName { get; init; }
    public string TripId { get; init; }
    public string? Destination { get; init; }          // TripHeadsign
    public DateTimeOffset ScheduledArrivalUtc { get; init; }
    public DateTimeOffset? PredictedArrivalUtc { get; init; }
    public string? VehicleId { get; init; }
    public bool VehicleConfirmed { get; init; }
    public int? GpsAgeSeconds { get; init; }
    public double? DistanceToStopMetres { get; init; }
    public float? VehicleBearing { get; init; }
    public double Confidence { get; init; }
    public string GhostRisk { get; init; }
    public string StatusLabel { get; init; }
    public ArrivalConfidenceResult ConfidenceDetail { get; init; }
}
```

---

## Stop board builder (Core project)

```csharp
public interface IStopBoardBuilder
{
    IReadOnlyList<StopArrivalPrediction> Build(
        string stopId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyList<TransitStopTime> scheduledTimes,
        IReadOnlyList<MatchedTrip> matchedTrips,
        IReadOnlyList<ServiceAlertObservation> activeAlerts);
}
```

Logic:
1. Get all scheduled arrivals at `stopId` within the window from `scheduledTimes`
2. For each, find a `MatchedTrip` by `TripId`
3. Get predicted arrival from `TripUpdate.StopTimeUpdates` if available
4. Score with `IArrivalConfidenceScorer`
5. Sort by predicted arrival (fallback to scheduled)
6. Return up to 10 arrivals

---

## Background worker (Worker project)

### TransitPollerService : BackgroundService

Three internal loops running independently on their own intervals:

1. **VehiclePositions loop** (every 30s): fetch → save raw → parse → upsert `VehicleObservation` to SQLite
2. **TripUpdates loop** (every 30s): fetch → save raw → parse → upsert `TripUpdateObservation` to SQLite
3. **ServiceAlerts loop** (every 60s): fetch → save raw → parse → replace `ServiceAlertObservation` table

On each VehiclePositions + TripUpdates tick, also run trip matching and update the `MatchedTrip` cache in memory (or in a SQLite staging table). This is the "current state" snapshot used by the API.

---

## API endpoints (Api project)

### Stop search

```
GET /api/transit/stops/search?q=patrick+street
```
Full-text search on `StopName` and `StopCode`. SQLite `LIKE` is acceptable for Phase 3.  
Returns array of `TransitStop`, max 20 results.

```
GET /api/transit/stops/nearby?lat=51.8985&lon=-8.4756&radiusMetres=500
```
Haversine filter in C# (pre-filter with bounding box SQL, then precise haversine sort).  
Returns array of `TransitStop` within radius, sorted by distance, max 20.

```
GET /api/transit/stops/by-address?address=MacCurtain+Street+Cork
```
1. Call Google Geocoding API server-side with `ServerKey`
2. Extract first result lat/lon
3. Delegate to the nearby-stops logic above
4. Return `{ geocodedAddress, lat, lon, stops[] }`  
Return `400` if geocoding returns no results. Return `502` if Geocoding API call fails.

### Stop board

```
GET /api/transit/stops/{stopId}/arrivals
```
Returns next 10 arrivals with full confidence detail.  
Response shape matches `StopArrivalPrediction` model.  
Return `404` if stopId not found.

### Vehicles

```
GET /api/transit/routes/{routeId}/vehicles
```
Returns all current `VehicleObservation` records for the route with `GpsAgeSeconds < 300`.

```
GET /api/transit/trips/{tripId}/trust
```
Returns full `ArrivalConfidenceResult` for the current state of a trip.

### User confirmations

```
POST /api/transit/reports
```
Request body:
```json
{
  "stopId": "stop_123",
  "routeId": "route_220",
  "tripId": "trip_abc",
  "reportType": "bus_seen",
  "lat": 51.899,
  "lon": -8.476,
  "anonymousUserId": "uuid-from-cookie"
}
```
`reportType` values: `bus_seen` | `bus_not_appeared` | `bus_passed_full` | `wrong_destination` | `gps_wrong`

Store in `TransitUserReports` table. `trustWeight = 0.4` for all anonymous reports in Phase 3.  
Return `201 Created`.

---

## Database additions (Infrastructure project)

New EF Core tables:

```
TransitStops
TransitRoutes
TransitTrips
TransitStopTimes          ← large; index on (StopId, ArrivalTime)
ServiceCalendars
ServiceCalendarDates
VehicleObservations       ← index on (TripId, TimestampUtc), prune rows > 2 hours old
TripUpdateObservations    ← index on TripId
ServiceAlertObservations  ← replace on each poll
TransitUserReports
TransitReliabilityAggregates  ← schema only, no rows yet (Phase 4 populates)
```

`TransitReliabilityAggregate` schema:
```csharp
public record TransitReliabilityAggregate
{
    public string Id { get; init; }
    public string StopId { get; init; }
    public string RouteId { get; init; }
    public DateOnly Date { get; init; }
    public int TotalScheduledArrivals { get; init; }
    public int VehicleConfirmedCount { get; init; }
    public int TimetableOnlyCount { get; init; }
    public int GhostCount { get; init; }              // bus_not_appeared confirmations
    public double AverageConfidence { get; init; }
    public double ReliabilityRate { get; init; }      // vehicleConfirmed / total
    public DateTimeOffset ComputedAtUtc { get; init; }
}
```

---

## Google Maps dashboard (Web project)

### Map page: `/transit/map`

Razor Page. Renders a full-viewport Google Maps JS map.

```html
<script src="https://maps.googleapis.com/maps/api/js?key=@Model.BrowserKey&callback=initMap" async defer></script>
```

`BrowserKey` injected server-side from configuration. Never hardcode.

Map features:
- Bus stop markers (blue pin) — click → show stop name and "View arrivals" link
- Vehicle markers (green bus icon) — click → show route, last GPS age, bearing arrow
- Vehicle markers auto-refresh every 30 seconds via `fetch('/api/transit/routes/.../vehicles')` — no full page reload
- Centre on Ireland (lat: 53.3, lon: -8.0, zoom: 7) by default

### Stop arrivals page: `/transit/stops/{stopId}`

Razor Page showing the stop board.

- Stop name and code
- Table of next 10 arrivals with all `StopArrivalPrediction` fields
- Confidence shown as coloured percentage (green ≥70%, amber 40–70%, red <40%)
- Ghost risk shown as a labelled badge
- User confirmation buttons per arrival row: "I saw this bus" / "Bus didn't appear" / "Passed full" / "Wrong destination"
- Confirmation POSTs to `/api/transit/reports` via fetch (no full page reload)
- Anonymous user UUID generated on first visit, stored in a cookie named `signal_uid`, sent with each confirmation

### Stop search page: `/transit/stops`

- Text input for stop name / number / route
- Second input for address search (calls `/api/transit/stops/by-address`)
- Results list links to `/transit/stops/{stopId}`

### Dashboard home update

Add a "Transit" card to the existing index dashboard:
```
┌───────────────────────────────┐
│ Transit                       │
│ Active vehicles: 284          │
│ Feeds: Live ✓                 │
│ Last update: 18s ago          │
│ → Search stops                │
└───────────────────────────────┘
```

---

## Configuration additions

```json
"TransitPoller": {
  "VehiclePositionsUrl": "",
  "TripUpdatesUrl": "",
  "ServiceAlertsUrl": "",
  "VehiclePositionsIntervalSeconds": 30,
  "TripUpdatesIntervalSeconds": 30,
  "ServiceAlertsIntervalSeconds": 60
},
"GoogleMaps": {
  "BrowserKey": "",
  "ServerKey": "",
  "GeocodingBaseUrl": "https://maps.googleapis.com/maps/api/geocode/json"
},
"GtfsStatic": {
  "DefaultImportPath": "data/samples/transit/gtfs-static.zip"
}
```

---

## Tests

### Unit tests (Tests project)

**GeoMathTests**
- Haversine distance: known coordinates with known distances
- Bearing calculation: cardinal directions
- Bearing delta: wrap-around cases (e.g. 350° vs 10° = 20° delta, not 340°)

**ConfidenceScorerTests**
- Vehicle confirmed, fresh GPS, plausible bearing → high confidence
- Timetable only → low confidence, `timetable_only` ghost risk
- Stale GPS (> 300s) → `stale_gps` ghost risk
- Active cancellation alert → confidence = 0.0, `cancelled` ghost risk
- Moving away from stop (bearing delta > 90°) → plausibility penalty
- Physically implausible ETA → plausibility penalty

**TripMatcherTests**
- VehiclePositions + TripUpdates agree → `VehicleAndTripUpdate`
- Vehicle with no TripUpdate → `VehicleOnly`
- TripUpdate with no vehicle → `TripUpdateOnly`
- Vehicle with null trip_id → unmatched, no crash

**StopBoardBuilderTests**
- Returns arrivals sorted by predicted time
- Arrivals beyond window excluded
- Max 10 results respected

### Integration tests (IntegrationTests project)

**GtfsStaticImportTests** — import from `data/samples/transit/gtfs-static.zip`, verify row counts and a known stop

**TransitAdapterTests** — parse sample `.pb` files from `data/samples/transit/`, verify known vehicle IDs or trip IDs present in the sample

No live network calls in tests.

---

## Constraints

1. **No live network calls in tests.** Use sample `.pb` and `.zip` files.
2. **All geo math in C#.** No external geo libraries.
3. **Server key never in client output.** `GoogleMaps:ServerKey` only used in server-side `HttpClient` calls.
4. **Browser key injected server-side.** Never hardcoded in `.cs` or `.js` files.
5. **Worker failures do not crash the host.** Log and continue.
6. **`stop_times.txt` is stream-parsed.** `File.ReadAllLines` on this file is forbidden.
7. **`TransitReliabilityAggregates` table is created but not populated.** Phase 4 builds the job.
8. **No shape geometry.** `shapes.txt` is not imported. Cross-track distance is Phase 4.
9. **No auth.** Anonymous UUID cookie only.
10. **No Grid code changes.** Phase 1 and 2 tests must remain green.

---

## Definition of done for Phase 3

- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test` passes — all Phase 1, 2, and 3 tests green
- [ ] GTFS static import runs to completion with sample zip; stop count logged
- [ ] `GET /api/transit/stops/search?q=...` returns stops from imported data
- [ ] `GET /api/transit/stops/nearby?lat=...&lon=...` returns sorted stops
- [ ] `GET /api/transit/stops/by-address?address=...` returns geocoded stops (requires live Google key)
- [ ] VehiclePositions and TripUpdates workers poll and store observations
- [ ] `GET /api/transit/stops/{stopId}/arrivals` returns confidence-scored arrivals
- [ ] At least one arrival in the stop board shows `vehicle_confirmed` ghost risk status
- [ ] At least one arrival shows `timetable_only`
- [ ] Google Maps page renders with stop and vehicle markers
- [ ] Vehicle markers refresh without page reload
- [ ] User confirmation POST stores a `TransitUserReport`
- [ ] `TransitReliabilityAggregates` table exists and is empty

---

## What comes in Phase 4 (do not build yet)

- `shapes.txt` import and polyline storage
- Cross-track distance (vehicle to route shape)
- Bearing vs route tangent plausibility
- Unrealistic position jump detection
- Nightly reliability aggregation job
- `historicalReliabilityScore` wired into confidence formula
- Reliability dashboard views per stop and route
