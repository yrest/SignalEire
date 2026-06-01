# Ireland Live Infrastructure Intelligence Platform

**Working Title:** Ireland Live Signals  
**Initial Modules:** Transit Reliability + Green Grid Timing  
**Primary Country:** Ireland  
**Document Type:** Product / Technical Specification  
**Version:** 1.0  
**Date:** 2026-06-01  
**Prepared For:** Early-stage architecture, MVP scoping, and implementation planning

---

## 1. Executive Summary

This document specifies a more mature version of two related mobile-app ideas:

1. **LiveTransit IRE** — a transit reliability system focused on eliminating or exposing “ghost bus” uncertainty.
2. **EcoGrid Sync** — a green-grid timing system that helps users consume electricity during cleaner grid windows.

The stronger architectural concept is not merely two separate mobile applications. The better long-term product is a reusable **live civic infrastructure intelligence platform** for Ireland.

The core platform ingests public or semi-public live data feeds, normalizes them, assigns confidence scores, stores historical observations, exposes APIs, and drives dashboards, alerts, and mobile/PWA clients.

The first two use cases are:

- **Transit Trust Engine**: “Is this bus actually likely to arrive?”
- **Green Grid Timing Engine**: “When is the cleanest practical time to run a high-load appliance, charge an EV, or shift demand?”

The strategic direction is to build a reusable event-ingestion and scoring platform, not a one-off app.

---

## 2. Strategic Product Positioning

### 2.1 Core Thesis

Ireland has several public live-data signals:

- public transport schedules and real-time vehicle/departure information,
- electricity demand and generation mix,
- CO₂ intensity,
- weather,
- aviation/airport feeds,
- public service alerts,
- potentially road/traffic and local authority feeds.

Most consumer-facing tools show raw or lightly processed information. The opportunity is to convert raw signals into **confidence, recommendations, and historical reliability intelligence**.

### 2.2 Product Category

This should be treated as:

> A live infrastructure intelligence platform with user-facing dashboards, APIs, alerts, and optional mobile/PWA clients.

Not:

> A generic transit app or generic green-energy app.

### 2.3 Why This Matters

The commercial weakness of simple mobile apps is that incumbents can copy UI features quickly. The defensible layer is:

- historical observations,
- confidence scoring,
- event correlation,
- reliability modelling,
- personal alert rules,
- integrations,
- derived intelligence.

---

## 3. Product Vision

### 3.1 Long-Term Vision

Build a platform that can answer questions like:

- “Is the 220 bus at my stop likely to actually arrive?”
- “What is the cleanest time to charge my EV before 07:30 tomorrow?”
- “Which Irish airports currently have disruption risk?”
- “Is my commute likely to be affected by public transport, weather, or energy disruption?”
- “What live signals should I care about right now based on my location and preferences?”

### 3.2 Initial Product Modules

| Module | Purpose | MVP Priority |
|---|---|---:|
| Green Grid Timing | Recommend cleaner electricity-use windows | 1 |
| Transit Trust Engine | Score bus/departure reliability | 2 |
| Alert Engine | Notify users when thresholds or conditions are met | 1 |
| Live Dashboard | Visualize current signal state | 1 |
| Historical Store | Preserve raw and normalized readings | 1 |
| API Layer | Serve internal UI and future external clients | 1 |
| Mobile App | Native/PWA client | 3 |

---

## 4. External Context and Data Sources

### 4.1 Transport Data Sources

The National Transport Authority developer portal provides access to GTFS and GTFS-Realtime style transport data. GTFS-Realtime provides real-time information about service disruptions, vehicle locations, and expected arrival times, subject to fair usage policy.

Potential transport inputs:

- GTFS static schedules,
- GTFS-Realtime VehiclePositions,
- GTFS-Realtime TripUpdates,
- GTFS-Realtime ServiceAlerts,
- route and stop metadata,
- optional crowd/user confirmations.

TFI Live is already a major incumbent. It provides real-time departures for Bus Éireann, Dublin Bus, Go-Ahead Ireland, Luas, and Iarnród Éireann Irish Rail services, as well as timetables, maps, accessibility information, and saved journeys/stops. TFI also publicly reports 1.5M+ downloads and around 400,000 daily users.

This means the product should not compete as “another TFI app.” It needs to become a **trust, confidence, and reliability layer**.

### 4.2 Energy Data Sources

EirGrid provides real-time system information through the Smart Grid Dashboard, showing energy production, demand, consumption, interconnection, and other grid signals for Ireland, Northern Ireland, and all-island views.

The Smart Grid Dashboard includes CO₂ intensity, measured in grams of CO₂ per kWh of demand.

Potential energy inputs:

- current system demand,
- wind generation,
- solar generation,
- fuel mix,
- CO₂ intensity,
- interconnector flows,
- forecast demand/wind where available,
- historical quarter-hourly system data,
- optional electricity tariff data.

### 4.3 Important Caveat on “APIs”

Do not assume that every dashboard has a stable public API intended for third-party production use.

For each data source, classify access as:

| Access Type | Meaning | Production Risk |
|---|---|---:|
| Official API | Explicit developer API with terms | Low |
| Downloadable data | CSV/XLS/report endpoints | Medium |
| Dashboard JSON endpoint | Used internally by web dashboard | Medium/High |
| Scraped HTML | Screen scraping | High |
| User-provided data | Manual/export/import | Low/Medium |

The architecture must isolate source-specific code behind adapters so that the system is not fragile when sources change.

---

## 5. System Concept

### 5.1 Platform Name

**Ireland Live Signals**

Alternative names:

- CivicSignals IE
- LivePulse Ireland
- InfraPulse IE
- GridTransit Intelligence
- SignalLayer Ireland

### 5.2 High-Level Architecture

```text
External Data Sources
    │
    ├── NTA / TFI transport feeds
    ├── EirGrid / Smart Grid Dashboard data
    ├── Weather feeds
    ├── Airport feeds
    └── Future civic/infrastructure feeds
        │
        ▼
Source Adapters
        │
        ▼
Raw Event Store
        │
        ▼
Normalizer
        │
        ▼
Signal State Store
        │
        ▼
Scoring Engines
        ├── Transit Trust Engine
        ├── Grid Cleanliness Engine
        ├── Alert Rule Engine
        └── Future modules
        │
        ▼
API Layer
        │
        ├── Web Dashboard
        ├── PWA / Mobile App
        ├── Notifications
        ├── RAG Annotation Layer
        └── External Integrations
```

### 5.3 Recommended Implementation Philosophy

Start with a boring, reliable backend-first architecture.

Do **not** begin with a polished native mobile app. That creates UI work before the intelligence engine is proven.

Recommended first path:

1. ASP.NET Core backend.
2. Background worker pollers.
3. Local raw snapshot archive.
4. SQLite/Postgres normalized store.
5. Razor/Blazor/PWA dashboard.
6. API endpoints.
7. Alerts.
8. Mobile app only once the data product proves itself.

---

## 6. Module 1 — Green Grid Timing Engine

## 6.1 Product Summary

The Green Grid Timing Engine helps users identify the cleanest practical times to consume electricity.

Basic version:

> “The grid is relatively clean right now.”

Better version:

> “You need 30 kWh in your EV by 07:30. The cleanest recommended charging window is 02:00–05:30.”

### 6.2 Target Users

| User Type | Need |
|---|---|
| EV owner | Charge during cleaner and/or cheaper periods |
| Smart-home user | Schedule dishwasher, washing machine, immersion, heat pump |
| Environmentally conscious household | Reduce carbon impact without manually watching grid dashboards |
| Data/energy enthusiast | Monitor live Irish grid state |
| Small business | Shift flexible loads when cleaner/cheaper |
| Future API user | Integrate green-grid signal into automation systems |

### 6.3 Core Value Proposition

The platform converts grid data into decision support:

- current grid cleanliness,
- best next green window,
- CO₂ avoided estimate,
- alert triggers,
- device-specific recommendations.

### 6.4 MVP Features

#### 6.4.1 Current Grid Dashboard

Display:

- current CO₂ intensity,
- current renewable share estimate,
- current demand,
- current wind generation,
- current solar generation if available,
- interconnection status if available,
- timestamp and data freshness.

#### 6.4.2 Green Action Alerts

User can define alert rules:

```json
{
  "ruleName": "Dishwasher Green Window",
  "co2Below": 220,
  "renewablesAbovePercent": 60,
  "quietHours": {
    "start": "23:00",
    "end": "07:00"
  },
  "maxAlertsPerDay": 2
}
```

Example notifications:

- “Grid CO₂ intensity is low. Good time to run flexible loads.”
- “Wind generation is high tonight. Consider charging the EV after 01:30.”
- “Cleaner window expected before your 07:30 deadline.”

#### 6.4.3 EV Charge Window Recommendation

Inputs:

```json
{
  "requiredKwh": 30,
  "chargerKw": 7.4,
  "deadline": "2026-06-02T07:30:00+01:00",
  "priority": "cleanest"
}
```

Output:

```json
{
  "recommendation": "wait",
  "start": "2026-06-02T02:00:00+01:00",
  "end": "2026-06-02T06:15:00+01:00",
  "estimatedAverageCo2gPerKwh": 185,
  "confidence": 0.74
}
```

#### 6.4.4 Carbon Savings Estimator

Estimate CO₂ difference between immediate consumption and deferred consumption.

Formula:

```text
CO₂ saved = kWh × (currentIntensity - recommendedWindowIntensity)
```

Example:

```text
Load: 30 kWh
Current intensity: 320 gCO₂/kWh
Recommended window intensity: 190 gCO₂/kWh

Estimated saving:
30 × (320 - 190) = 3,900 gCO₂ = 3.9 kgCO₂
```

### 6.5 Green Grid Data Model

#### 6.5.1 RawGridSnapshot

```json
{
  "id": "grid_raw_20260601_143000",
  "source": "eirgrid_smart_grid_dashboard",
  "sourceUrl": "https://www.smartgriddashboard.com/",
  "retrievedAtUtc": "2026-06-01T13:30:00Z",
  "rawPayloadPath": "data/raw/grid/2026/06/01/133000.json",
  "hash": "sha256..."
}
```

#### 6.5.2 GridReading

```json
{
  "id": "grid_reading_20260601_143000_roi",
  "region": "ROI",
  "timestampUtc": "2026-06-01T13:30:00Z",
  "systemDemandMw": 4380,
  "windGenerationMw": 2120,
  "solarGenerationMw": 310,
  "renewablesPercent": 58.1,
  "co2IntensityGPerKwh": 214,
  "interconnectorImportMw": 180,
  "interconnectorExportMw": 0,
  "dataFreshnessSeconds": 45,
  "quality": {
    "status": "ok",
    "missingFields": [],
    "sourceLatencySeconds": 45
  }
}
```

#### 6.5.3 GridRecommendation

```json
{
  "id": "grid_rec_20260601_user123_ev",
  "userId": "user123",
  "deviceType": "ev",
  "createdAtUtc": "2026-06-01T13:32:00Z",
  "requiredKwh": 30,
  "deadlineUtc": "2026-06-02T06:30:00Z",
  "recommendedStartUtc": "2026-06-02T01:00:00Z",
  "recommendedEndUtc": "2026-06-02T05:15:00Z",
  "estimatedAverageCo2GPerKwh": 185,
  "estimatedSavingKgCo2": 3.9,
  "confidence": 0.74,
  "explanation": "Wind generation forecast and recent trend suggest lower CO₂ intensity overnight."
}
```

### 6.6 Grid Scoring Algorithm

Initial scoring can be rule-based.

```text
greenScore =
    0.45 × normalizedRenewablesPercent
  + 0.35 × inverseNormalizedCo2Intensity
  + 0.10 × dataFreshnessScore
  + 0.10 × trendScore
```

Where:

- `normalizedRenewablesPercent` = current renewable share scaled 0–1
- `inverseNormalizedCo2Intensity` = lower CO₂ receives higher score
- `dataFreshnessScore` penalizes stale data
- `trendScore` rewards improving conditions

### 6.7 Tariff-Aware Extension

The product becomes much more useful when it can balance carbon and cost.

```text
bestWindowScore =
    carbonWeight × greenScore
  + priceWeight × cheapnessScore
  + deadlineWeight × feasibilityScore
```

Example user profiles:

| Profile | Carbon Weight | Price Weight |
|---|---:|---:|
| Eco-first | 0.8 | 0.2 |
| Balanced | 0.5 | 0.5 |
| Cost-first | 0.2 | 0.8 |

### 6.8 Grid API Endpoints

```http
GET /api/grid/current?region=ROI
GET /api/grid/history?region=ROI&from=2026-06-01T00:00:00Z&to=2026-06-02T00:00:00Z
GET /api/grid/best-window?region=ROI&durationMinutes=180&deadline=2026-06-02T07:30:00+01:00
POST /api/grid/recommendations/ev-charge
POST /api/grid/alerts
GET /api/grid/alerts
DELETE /api/grid/alerts/{id}
```

---

## 7. Module 2 — Transit Trust Engine

## 7.1 Product Summary

The Transit Trust Engine does not merely show buses on a map. It scores the reliability of an expected arrival.

Basic version:

> “Bus due in 6 minutes.”

Better version:

> “Bus due in 6 minutes, vehicle confirmed 900m away, GPS updated 22 seconds ago, high confidence.”

Or:

> “Scheduled arrival in 6 minutes, but no vehicle has been seen for this trip. Treat as low confidence.”

### 7.2 Product Differentiator

The differentiator is **confidence**, not maps.

TFI already has real-time departures and maps. Therefore, this system must expose what typical apps hide:

- GPS freshness,
- whether the trip is vehicle-confirmed,
- whether the arrival is schedule-derived,
- whether the vehicle is physically plausible,
- whether similar trips historically fail,
- whether user confirmations contradict feed data.

### 7.3 Target Users

| User Type | Need |
|---|---|
| Daily commuters | Know whether to wait or walk/taxi |
| Students | Avoid unreliable routes/stops |
| Rural/intercity passengers | Detect low-frequency service uncertainty |
| Accessibility users | Avoid being stranded |
| Data enthusiasts | Analyze route reliability |
| Local authorities/operators | Understand reliability problems |

### 7.4 MVP Features

#### 7.4.1 Stop Search

User searches for:

- stop name,
- stop number,
- route,
- current location nearby stops.

#### 7.4.2 Stop Board with Confidence

Each arrival shows:

| Field | Example |
|---|---|
| Route | 220 |
| Destination | Cork City |
| Scheduled arrival | 08:42 |
| Predicted arrival | 08:47 |
| Vehicle confirmed | Yes |
| GPS age | 18 seconds |
| Confidence | 87% |
| Ghost risk | Low |

#### 7.4.3 Vehicle Detail View

Show:

- current vehicle position,
- last seen timestamp,
- route/trip association,
- distance to selected stop,
- bearing plausibility,
- expected stop sequence,
- data freshness.

#### 7.4.4 Ghost Risk Indicator

Possible statuses:

| Status | Meaning |
|---|---|
| Vehicle confirmed | Vehicle has live location and plausible trip assignment |
| Likely active | Trip update exists but vehicle position may be stale |
| Timetable-only | No live vehicle confirmation |
| Stale GPS | Vehicle location too old |
| Implausible | Position/trip/stop sequence inconsistent |
| Cancelled/disrupted | Service alert indicates issue |

#### 7.4.5 User Confirmation

Simple buttons:

- “I saw this bus”
- “Bus did not appear”
- “Bus passed full”
- “Wrong destination”
- “GPS marker wrong”

This data should not be trusted blindly but can improve confidence models over time.

### 7.5 Transit Data Model

#### 7.5.1 RawTransitSnapshot

```json
{
  "id": "transit_raw_vehiclepositions_20260601_073000",
  "source": "nta_gtfs_realtime_vehiclepositions",
  "retrievedAtUtc": "2026-06-01T06:30:00Z",
  "feedType": "VehiclePositions",
  "rawPayloadPath": "data/raw/transit/vehiclepositions/2026/06/01/063000.pb",
  "hash": "sha256..."
}
```

#### 7.5.2 VehicleObservation

```json
{
  "vehicleId": "vehicle_12345",
  "routeId": "route_220",
  "tripId": "trip_abc",
  "timestampUtc": "2026-06-01T06:30:00Z",
  "lat": 51.8985,
  "lon": -8.4756,
  "bearing": 83,
  "speedKph": 32,
  "sourceFeed": "VehiclePositions",
  "gpsAgeSeconds": 20,
  "quality": {
    "status": "ok",
    "tripMatched": true,
    "positionPlausible": true
  }
}
```

#### 7.5.3 StopArrivalPrediction

```json
{
  "stopId": "stop_123",
  "routeId": "route_220",
  "tripId": "trip_abc",
  "scheduledArrivalUtc": "2026-06-01T07:42:00Z",
  "predictedArrivalUtc": "2026-06-01T07:47:00Z",
  "vehicleId": "vehicle_12345",
  "vehicleConfirmed": true,
  "gpsAgeSeconds": 18,
  "distanceToStopMeters": 920,
  "confidence": 0.87,
  "ghostRisk": "low",
  "explanation": "Vehicle position was updated 18 seconds ago and is moving toward the stop."
}
```

#### 7.5.4 UserTransitReport

```json
{
  "id": "report_123",
  "userId": "anonymous_or_registered",
  "stopId": "stop_123",
  "routeId": "route_220",
  "tripId": "trip_abc",
  "reportType": "bus_seen",
  "timestampUtc": "2026-06-01T07:44:00Z",
  "lat": 51.899,
  "lon": -8.476,
  "trustWeight": 0.4
}
```

### 7.6 Transit Confidence Scoring

Initial confidence should be explainable and rule-based.

```text
confidence =
    vehiclePresenceScore
  × gpsFreshnessScore
  × tripMatchScore
  × physicalPlausibilityScore
  × serviceAlertScore
  × historicalReliabilityScore
```

#### 7.6.1 Vehicle Presence Score

| Condition | Score |
|---|---:|
| Live vehicle matched to trip | 1.0 |
| Trip update but no vehicle | 0.6 |
| Timetable only | 0.3 |
| Cancelled | 0.0 |

#### 7.6.2 GPS Freshness Score

| GPS Age | Score |
|---|---:|
| 0–30 sec | 1.0 |
| 31–90 sec | 0.8 |
| 91–180 sec | 0.5 |
| 181–300 sec | 0.25 |
| >300 sec | 0.1 |

#### 7.6.3 Physical Plausibility

Checks:

- is the vehicle moving toward the stop?
- is the vehicle on or near expected route geometry?
- is the stop sequence plausible?
- is the predicted arrival physically possible given distance/speed?
- has the vehicle jumped unrealistically?

### 7.7 Transit API Endpoints

```http
GET /api/transit/stops/search?q=patrick%20street
GET /api/transit/stops/nearby?lat=51.8985&lon=-8.4756&radiusMeters=500
GET /api/transit/stops/{stopId}/arrivals
GET /api/transit/routes/{routeId}/vehicles
GET /api/transit/trips/{tripId}/trust
POST /api/transit/reports
GET /api/transit/reliability?routeId=220&stopId=stop_123
```

---

## 8. Shared Platform Architecture

## 8.1 Recommended Stack

### 8.1.1 MVP Stack

| Layer | Recommended Choice | Reason |
|---|---|---|
| Backend | ASP.NET Core | Matches existing skillset and infrastructure |
| Workers | .NET Worker Service / BackgroundService | Excellent for polling feeds |
| Storage | SQLite first, Postgres later | Fast MVP, easy migration |
| Raw archive | File system | Cheap, auditable, replayable |
| API | ASP.NET Core Minimal APIs / Controllers | Simple and testable |
| Dashboard | Razor Pages / MVC / Blazor | Faster than native app |
| Maps | Leaflet/OpenStreetMap first | Avoid early Mapbox/Google cost/lock-in |
| Notifications | Email/Telegram first, FCM/APNs later | Faster proof |
| Hosting | Local/IIS/Docker | Matches existing environment |
| RAG | Existing Cosmos/Qdrant/Ollama pipeline | Optional but strategically aligned |

### 8.1.2 Later Stack

| Layer | Option |
|---|---|
| Mobile | Flutter, MAUI, Swift/Kotlin, or PWA wrapper |
| Queue | RabbitMQ, Azure Service Bus, or simple file queue |
| Cache | Redis |
| Storage | Postgres + TimescaleDB |
| Observability | OpenTelemetry + Grafana |
| Event streaming | Kafka/Redpanda only if genuinely needed |
| Auth | Microsoft Entra ID / Auth0 / local accounts |
| Push | Firebase Cloud Messaging + APNs |

## 8.2 Source Adapter Pattern

Each external feed should implement a common adapter interface.

```csharp
public interface ISignalSourceAdapter
{
    string SourceName { get; }
    string SourceType { get; }
    Task<RawSignalSnapshot> FetchAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NormalizedSignal>> NormalizeAsync(
        RawSignalSnapshot snapshot,
        CancellationToken cancellationToken);
}
```

Example adapters:

```text
Adapters/
├── Transit/
│   ├── NtaGtfsStaticAdapter
│   ├── NtaGtfsRealtimeVehicleAdapter
│   ├── NtaGtfsRealtimeTripUpdateAdapter
│   └── NtaGtfsRealtimeServiceAlertAdapter
├── Grid/
│   ├── EirGridDashboardAdapter
│   ├── EirGridHistoricalReportAdapter
│   └── TariffAdapter
└── Weather/
    └── MetEireannAdapter
```

## 8.3 Raw Data Archive

Never throw away raw data during MVP.

Suggested layout:

```text
C:\data\ireland-live-signals\
├── raw\
│   ├── transit\
│   │   ├── vehiclepositions\
│   │   ├── tripupdates\
│   │   └── servicealerts\
│   ├── grid\
│   │   ├── current\
│   │   └── historical\
│   └── weather\
├── normalized\
├── summaries\
├── alerts\
└── logs\
```

Advantages:

- replay historical data,
- debug parser issues,
- prove feed quality,
- train future models,
- generate reliability reports,
- support idempotent ingestion.

## 8.4 Database Tables

### Core Tables

```sql
SignalSource
RawSnapshot
NormalizedSignal
IngestionRun
AlertRule
AlertDelivery
UserProfile
```

### Grid Tables

```sql
GridReading
GridRecommendation
GridDeviceProfile
GridTariffWindow
```

### Transit Tables

```sql
TransitStop
TransitRoute
TransitTrip
VehicleObservation
StopArrivalPrediction
TransitUserReport
TransitReliabilityAggregate
```

## 8.5 Idempotency

Each fetched snapshot should be content-hashed.

```text
snapshot_hash = SHA256(source_name + feed_type + source_timestamp + raw_payload)
```

If the same hash already exists, skip reprocessing unless forced.

## 8.6 Observability

Track:

- source fetch success/failure,
- latency,
- payload size,
- number of records normalized,
- number of stale readings,
- number of alerts fired,
- confidence score distribution,
- API response times,
- dashboard errors.

Minimum logs:

```text
[2026-06-01 14:00:00] FetchStarted source=EirGrid
[2026-06-01 14:00:01] FetchCompleted source=EirGrid bytes=12345 latencyMs=840
[2026-06-01 14:00:01] NormalizeCompleted source=EirGrid readings=1
[2026-06-01 14:00:01] AlertEvaluationCompleted rules=42 fired=3
```

---

## 9. User Experience

## 9.1 Dashboard Home

The dashboard should show cards such as:

```text
┌───────────────────────────────┐
│ Grid Cleanliness              │
│ CO₂: 214 g/kWh                │
│ Renewables: 58%               │
│ Recommendation: Good window   │
└───────────────────────────────┘

┌───────────────────────────────┐
│ Transit Trust                 │
│ Stop: Patrick Street          │
│ 220 due 08:47                 │
│ Confidence: 87%               │
└───────────────────────────────┘
```

## 9.2 Grid UX

Screens:

1. Current grid state.
2. Best next window.
3. Device setup.
4. Alert rules.
5. Carbon savings history.

## 9.3 Transit UX

Screens:

1. Nearby stops.
2. Stop board with confidence.
3. Vehicle map.
4. Reliability history.
5. Report issue.

## 9.4 Alert UX

Alert types:

| Alert Type | Example |
|---|---|
| Green window | “Grid is cleaner now. Good time to run flexible loads.” |
| EV charge | “Start charging at 02:10 to meet your 07:30 target.” |
| Transit warning | “Your expected bus is timetable-only. No vehicle confirmed.” |
| Stale data | “Live feed appears stale. Treat predictions cautiously.” |

---

## 10. RAG / LLM Layer

This is optional for MVP but aligned with the broader architecture.

## 10.1 Purpose

Use the existing local RAG pipeline to generate:

- daily transit reliability summaries,
- grid cleanliness summaries,
- anomaly explanations,
- user-friendly alert text,
- operational diagnostics,
- developer-facing data quality notes.

## 10.2 Example Daily Summary

```markdown
# Cork Transit Reliability Summary — 2026-06-01

Route 220 showed low-confidence arrivals between 07:30 and 09:00 at several central stops.
The main cause was missing vehicle-position updates for active trip IDs.
No matching service alert was present in the feed.

Recommended follow-up:
- compare TripUpdates against VehiclePositions,
- inspect vehicle_id/trip_id matching,
- calculate recurrence across weekdays.
```

## 10.3 Vector Metadata

For each generated summary:

```json
{
  "source": "ireland-live-signals",
  "module": "transit",
  "region": "cork",
  "routeId": "220",
  "date": "2026-06-01",
  "summaryType": "daily_reliability",
  "rawSnapshotRefs": [
    "transit_raw_vehiclepositions_20260601_073000"
  ]
}
```

---

## 11. MVP Build Plan

## 11.1 Phase 0 — Discovery and Feed Validation

Duration: 1–2 weeks.

Goals:

- register for NTA developer access,
- inspect GTFS static and GTFS-Realtime feeds,
- inspect EirGrid/Smart Grid Dashboard data access,
- confirm update frequency,
- confirm usage limits,
- record sample payloads,
- build prototype parsers.

Deliverables:

```text
/samples
    /nta
    /eirgrid
/docs
    data-source-notes.md
    field-map.md
    risks.md
```

Exit criteria:

- can fetch and store raw transit snapshots,
- can fetch and store raw grid snapshots,
- can parse at least one normalized reading from each.

## 11.2 Phase 1 — Green Grid MVP

Duration: 1–2 weeks.

Build:

- EirGrid adapter,
- raw snapshot archive,
- GridReading table,
- dashboard card,
- current grid API,
- simple green-score calculation,
- basic alert rule engine.

Exit criteria:

- dashboard shows current grid state,
- historical readings are stored,
- alerts can be evaluated,
- API returns current and historical grid values.

## 11.3 Phase 2 — EV/Device Recommendation Engine

Duration: 1–2 weeks.

Build:

- device profile model,
- EV charging window recommendation,
- dishwasher/washing machine generic load recommendation,
- carbon savings estimator,
- quiet-hours support,
- notification suppression.

Exit criteria:

- user can enter kWh/deadline,
- system recommends a window,
- result includes estimated CO₂ saving and confidence.

## 11.4 Phase 3 — Transit Feed Prototype

Duration: 2–4 weeks.

Build:

- GTFS static import,
- VehiclePositions ingestion,
- TripUpdates ingestion,
- stop search,
- stop arrivals endpoint,
- basic confidence score,
- stale GPS detection,
- timetable-only detection.

Exit criteria:

- user can search a stop,
- arrivals are shown with confidence,
- at least one route can be inspected end-to-end.

## 11.5 Phase 4 — Transit Reliability Layer

Duration: 3–6 weeks.

Build:

- historical reliability aggregates,
- route/stop reliability reports,
- user confirmations,
- ghost-risk classification,
- vehicle trail view,
- alert rules for selected stops/routes.

Exit criteria:

- system can classify arrivals as vehicle-confirmed, timetable-only, stale, or high-risk,
- dashboard can show historical reliability per stop/route.

## 11.6 Phase 5 — PWA/Mobile Client

Duration: later.

Only start after backend value is proven.

Options:

| Option | Pros | Cons |
|---|---|---|
| PWA | Fast, cheap, cross-platform | Less native push capability |
| MAUI | Uses C# skillset | Mobile ecosystem friction |
| Flutter | Strong cross-platform UI | Newer stack burden |
| Native Swift/Kotlin | Best device integration | Highest cost |

---

## 12. Security, Abuse, and Privacy

## 12.1 API Key Safety

External source keys must be stored server-side only.

Do not ship NTA/EirGrid keys in mobile apps.

## 12.2 User Location

Location data is sensitive.

Rules:

- request precise location only when needed,
- allow manual stop/device setup,
- do not store exact location unless required,
- aggregate or hash user reports where possible,
- clearly separate anonymous reports from registered-user reports.

## 12.3 Anti-Spam for User Transit Reports

Use:

- rate limiting,
- device/session trust,
- location plausibility,
- duplicate report suppression,
- weighted trust scores,
- outlier detection.

## 12.4 Alert Fatigue

Every alert system needs suppression.

Rules:

- max alerts per rule per day,
- cooldown windows,
- quiet hours,
- severity levels,
- digest mode.

---

## 13. Commercial Model

## 13.1 Green Grid Module

Potential monetization:

| Model | Fit |
|---|---|
| Free consumer dashboard | Good for acquisition |
| Premium EV/smart-home rules | Good |
| API access | Strong |
| Business dashboard | Medium |
| White-label widget | Medium/High |
| Smart-home integration | Strong |

## 13.2 Transit Module

Potential monetization:

| Model | Fit |
|---|---|
| Free public commuter app | Good for usage, weak revenue |
| Premium commuter alerts | Weak/Medium |
| Reliability reports | Stronger |
| Local authority/operator analytics | Strong |
| Open-data intelligence dashboard | Medium |
| Civic-tech grant/research funding | Strong |

## 13.3 Best Business Direction

The strongest monetizable direction is probably:

> API + dashboard + analytics for Irish live infrastructure signals.

Not:

> Charging commuters €2.99/month for a bus map.

---

## 14. Risks

## 14.1 Data Source Risk

| Risk | Impact | Mitigation |
|---|---|---|
| API changes | High | Adapter isolation, raw archive, tests |
| Rate limits | Medium/High | Cache aggressively, fair-use compliance |
| Missing data | High | Confidence scoring, stale indicators |
| Unofficial endpoints break | High | Prefer official APIs/downloads |
| Feed quality varies | High | Display source freshness and confidence |

## 14.2 Product Risk

| Risk | Impact | Mitigation |
|---|---|---|
| TFI already dominates transit UX | High | Do not compete on generic journey planning |
| Users may not understand confidence scores | Medium | Use clear language and visual labels |
| Alert fatigue | Medium | Suppression and personalization |
| Green users may care more about price | High | Add tariff-aware scoring |
| Mobile app may consume too much effort | High | Build backend/PWA first |

## 14.3 Technical Risk

| Risk | Impact | Mitigation |
|---|---|---|
| Trip matching complexity | High | Start with limited routes/stops |
| Historical data volume | Medium | Partition by date/source |
| Map performance | Medium | Use clustering and server filtering |
| Push notifications complexity | Medium | Start with email/Telegram |
| Overengineering | High | Keep Phase 1 boring and useful |

---

## 15. Recommended First Implementation

## 15.1 Build This First

Build **EcoGrid Sync backend + dashboard** first.

Reason:

- simpler data model,
- easier MVP,
- immediate useful output,
- fewer third-party dependency complexities,
- good fit for background polling,
- easy to connect to alerts,
- easy to extend to smart-home/EV use cases.

## 15.2 Do Not Build First

Do not begin with:

- native mobile app,
- full transit journey planner,
- real-time bus map clone,
- complex ML model,
- Kafka/event-streaming infrastructure,
- multi-tenant SaaS before signal quality is proven.

## 15.3 First Repository Structure

```text
IrelandLiveSignals/
├── src/
│   ├── IrelandLiveSignals.Api/
│   ├── IrelandLiveSignals.Worker/
│   ├── IrelandLiveSignals.Core/
│   ├── IrelandLiveSignals.Infrastructure/
│   └── IrelandLiveSignals.Web/
├── data/
│   ├── raw/
│   ├── normalized/
│   └── samples/
├── docs/
│   ├── architecture.md
│   ├── data-sources.md
│   ├── api.md
│   └── roadmap.md
└── tests/
    ├── IrelandLiveSignals.Tests/
    └── IrelandLiveSignals.IntegrationTests/
```

## 15.4 First Internal Milestone

```text
Milestone 1:
- Worker fetches grid data every 5 minutes.
- Raw payload saved to disk.
- Normalized GridReading saved to SQLite.
- Dashboard shows latest reading.
- API exposes /api/grid/current.
- Basic alert rule can be evaluated.
```

This is small enough to ship and valuable enough to keep.

---

## 16. Example API Contract

## 16.1 Current Grid Response

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

## 16.2 EV Recommendation Request

```json
{
  "region": "ROI",
  "requiredKwh": 30,
  "chargerKw": 7.4,
  "deadlineLocal": "2026-06-02T07:30:00+01:00",
  "mode": "balanced",
  "allowQuietHours": true
}
```

## 16.3 EV Recommendation Response

```json
{
  "recommendation": "wait",
  "recommendedStartLocal": "2026-06-02T02:00:00+01:00",
  "recommendedEndLocal": "2026-06-02T06:15:00+01:00",
  "requiredDurationMinutes": 244,
  "estimatedAverageCo2GPerKwh": 185,
  "estimatedSavingKgCo2": 3.9,
  "confidence": 0.74,
  "explanation": [
    "Current CO₂ intensity is higher than recent overnight lows.",
    "Wind generation trend suggests a cleaner window after 02:00.",
    "The proposed window satisfies the 07:30 deadline."
  ]
}
```

## 16.4 Stop Arrivals Response

```json
{
  "stopId": "stop_123",
  "stopName": "Patrick Street",
  "timestampUtc": "2026-06-01T07:35:00Z",
  "arrivals": [
    {
      "route": "220",
      "destination": "Cork City",
      "scheduledArrivalLocal": "2026-06-01T08:42:00+01:00",
      "predictedArrivalLocal": "2026-06-01T08:47:00+01:00",
      "vehicleConfirmed": true,
      "gpsAgeSeconds": 18,
      "distanceToStopMeters": 920,
      "confidence": 0.87,
      "ghostRisk": "low",
      "statusLabel": "Vehicle confirmed"
    },
    {
      "route": "245",
      "destination": "Fermoy",
      "scheduledArrivalLocal": "2026-06-01T08:50:00+01:00",
      "predictedArrivalLocal": null,
      "vehicleConfirmed": false,
      "gpsAgeSeconds": null,
      "distanceToStopMeters": null,
      "confidence": 0.31,
      "ghostRisk": "high",
      "statusLabel": "Timetable only"
    }
  ]
}
```

---

## 17. Testing Strategy

## 17.1 Unit Tests

Test:

- scoring algorithms,
- parser logic,
- stale-data detection,
- alert rule evaluation,
- idempotency hashing,
- time-zone handling.

## 17.2 Integration Tests

Test:

- feed fetch adapter with recorded payloads,
- normalization pipeline,
- database writes,
- API responses,
- alert delivery mocks.

## 17.3 Replay Tests

Use archived raw snapshots to replay a day of data.

```text
dotnet run -- replay --source grid --date 2026-06-01
dotnet run -- replay --source transit --route 220 --date 2026-06-01
```

Replay is essential for debugging and improving algorithms.

---

## 18. Production Readiness Checklist

Before public launch:

- [ ] Confirm data-source terms and fair usage.
- [ ] Server-side API key storage.
- [ ] Raw snapshot archive rotation.
- [ ] Database backup plan.
- [ ] Health checks.
- [ ] Source freshness monitoring.
- [ ] Alert suppression.
- [ ] User privacy policy.
- [ ] Error pages and degraded-mode UI.
- [ ] Observability dashboard.
- [ ] Rate limiting for public APIs.
- [ ] Admin page for source health.
- [ ] Data attribution page.
- [ ] Clear disclaimer: predictions are advisory, not guaranteed.

---

## 19. Source Notes

These sources informed the assumptions in this specification:

1. National Transport Authority developer portal — GTFS / GTFS-Realtime availability and fair usage.  
   https://developer.nationaltransport.ie/

2. NTA announcement on upgraded GTFS-Realtime APIs.  
   https://www.nationaltransport.ie/news/attention-developers-upgrade-to-gtfs-realtime-api/

3. Transport for Ireland announcement on GTFS-R feed for app developers.  
   https://www.transportforireland.ie/news/new-transport-data-feed-for-app-developers-now-online/

4. TFI Live app feature page — real-time departures, maps, timetables, personalization, downloads and daily users.  
   https://www.transportforireland.ie/available-apps/tfi-live/

5. TFI Live app listing, Google Play.  
   https://play.google.com/store/apps/details?id=com.trapezegroup.TFILive.nta

6. EirGrid real-time system information page.  
   https://www.eirgrid.ie/grid/real-time-system-information

7. EirGrid Smart Grid Dashboard.  
   https://www.smartgriddashboard.com/all/

8. Smart Grid Dashboard CO₂ intensity explanation.  
   https://www.smartgriddashboard.com/roi/co2/

9. EirGrid system and renewable data reports.  
   https://www.eirgrid.ie/grid/system-and-renewable-data-reports

---

## 20. Final Recommendation

Build the platform in this order:

1. **Green Grid MVP** — fastest route to useful, demonstrable value.
2. **Green Grid recommendations and alerts** — EV/device timing, tariff-aware scoring.
3. **Transit feed prototype** — limited stops/routes first.
4. **Transit trust scoring** — confidence and ghost-risk layer.
5. **Unified Ireland Live Signals dashboard**.
6. **PWA/mobile app only after backend value is proven**.

The real product is not a mobile app.

The real product is:

> A live Irish infrastructure signal platform that converts raw public data into confidence, recommendations, and timely user actions.
