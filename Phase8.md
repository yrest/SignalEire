# SignalEire — Phase 8 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 7 complete and passing  
**Scope:** Tariff-aware grid scoring + NI grid region + public developer API keys + OpenAPI documentation  
**Character:** Backend-first. MAUI changes are additive and minimal. No Translink/NI transit.

---

## What Phase 8 adds

### Track 1 — Tariff-aware scoring
1. `TariffPlan` and `TariffRatePeriod` models — admin-managed, time-of-use rate schedules
2. Pre-seeded plans: Night Rate (generic) and Flat Rate (generic)
3. User tariff plan selection (web + MAUI)
4. EV recommendation engine updated — `"cheapest"` and `"balanced"` priorities now use real tariff data
5. `EvChargeRecommendation` extended with cost estimates

### Track 2 — NI grid region
1. Second `GridPollerService` loop — fetches NI data from a separately configured EirGrid endpoint
2. `GridReading.Region = "NI"` — stored alongside existing ROI readings
3. All grid API endpoints accept `?region=ROI|NI` query parameter (ROI remains default)
4. New `GET /api/grid/compare` endpoint — side-by-side ROI and NI snapshot
5. Dashboard region selector
6. MAUI region preference respected in Grid tab

### Track 3 — Developer API
1. `DeveloperApiKey` model — admin-created, hashed, per-key rate limits
2. `ApiKeyMiddleware` — validates `X-Api-Key` header, logs usage, applies per-key rate limit
3. Admin page `/admin/developer-keys` — create, disable, view usage
4. OpenAPI/Swagger — `Swashbuckle.AspNetCore`, served at `/api-docs`
5. Developer portal page — `/developer`, public-facing, explains the API
6. Per-key usage tracking

---

## Part 1 — Tariff plan management

### NuGet packages (none new required)

### Domain models (Core project)

```csharp
public record TariffPlan
{
    public string Id { get; init; }
    public string Name { get; init; }           // "Night Rate (Generic)" / "Flat Rate"
    public string Provider { get; init; }        // "Generic" | "Electric Ireland" | "Bord Gáis" etc.
    public string PlanType { get; init; }        // "night_rate" | "flat" | "time_of_use" | "custom"
    public bool IsActive { get; init; } = true;
    public bool IsDefault { get; init; } = false;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public List<TariffRatePeriod> Periods { get; init; } = [];
}

public record TariffRatePeriod
{
    public string Id { get; init; }
    public string TariffPlanId { get; init; }
    public string PeriodName { get; init; }      // "Night Rate" | "Day Rate" | "Peak" | "Off-Peak"
    public string DayType { get; init; }         // "all" | "weekday" | "weekend"
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }       // if StartTime > EndTime, period spans midnight
    public decimal RatePerKwh { get; init; }     // euro, e.g. 0.1256 for 12.56 c/kWh
}
```

### Midnight-spanning period rule

When `StartTime > EndTime` (e.g. 23:00 → 08:00), the period spans midnight.

```csharp
public static bool IsInPeriod(TimeOnly time, TariffRatePeriod period) =>
    period.StartTime <= period.EndTime
        ? time >= period.StartTime && time < period.EndTime      // same-day period
        : time >= period.StartTime || time < period.EndTime;     // midnight-spanning
```

### Rate lookup

```csharp
public interface ITariffRateService
{
    // Returns the applicable rate in €/kWh at a given Ireland local time
    decimal GetRateAt(TariffPlan plan, TimeOnly irelandLocalTime, DayOfWeek dayOfWeek);

    // Returns average rate across a window (list of 5-minute slots)
    decimal GetAverageRateForWindow(TariffPlan plan, DateTimeOffset windowStart, DateTimeOffset windowEnd);
}
```

`GetRateAt` logic:
1. Filter periods by `DayType` (match `all`, `weekday` (Mon–Fri), `weekend` (Sat–Sun))
2. Among matching periods, find one where `IsInPeriod(irelandLocalTime, period)` is true
3. If multiple match (should not happen if plan is valid), take the first
4. If none match, return the plan's first period rate as a fallback (log a warning)

`GetAverageRateForWindow`:
1. Enumerate 5-minute slots from `windowStart` to `windowEnd` (Ireland local time)
2. For each slot, call `GetRateAt`
3. Return arithmetic mean

Both are pure — no I/O.

### Pre-seeded tariff plans

Create on first startup (inside a `TariffPlanSeeder` called from `Program.cs`) if the `TariffPlans` table is empty:

**Plan 1 — Night Rate (Generic)**
```
Provider: Generic
PlanType: night_rate
IsDefault: true
Periods:
  - PeriodName: "Night Rate", DayType: "all", Start: 23:00, End: 08:00, Rate: 0.1256
  - PeriodName: "Day Rate",   DayType: "all", Start: 08:00, End: 23:00, Rate: 0.4321
```

**Plan 2 — Flat Rate (Generic)**
```
Provider: Generic
PlanType: flat
Periods:
  - PeriodName: "Standard Rate", DayType: "all", Start: 00:00, End: 00:00, Rate: 0.3850
    (Start == End means all-day — handle as a special case: always returns this rate)
```

Include a prominent note in the admin UI: *"Rates shown are illustrative. Update them to match your actual plan."*

### Database additions

```
TariffPlans
TariffRatePeriods    ← index on TariffPlanId
```

Add to `ApplicationUser`:
```csharp
public string? PreferredTariffPlanId { get; set; }  // null = no plan selected
```

### Admin tariff management page (`/admin/tariff`)

Razor Page. Admin-only (`[Authorize(Roles = "Admin")]`).

**List view:**
- Table of all plans: Name, Provider, Type, Periods count, IsDefault, IsActive
- "Add plan" button
- Edit / Delete buttons per plan

**Add/Edit form (inline or modal):**
- Plan name, provider, type (select), description
- Periods table: add/remove rows; each row: period name, day type, start time, end time, rate (€/kWh)
- Midnight-spanning periods shown with a midnight-crossing indicator
- "Set as default" checkbox

**Validation:**
- Each plan must have at least one period
- No two periods within a plan may overlap for the same day type
- Rate must be > 0

### User tariff plan selection (`/account/preferences`)

Add a "Electricity plan" section to the existing account preferences page:
- Dropdown showing all active `TariffPlan` records (Name + Provider)
- "None / I don't know" option (null → engine falls back to balanced)
- Brief explainer: "Your plan is used to estimate charging costs and find the cheapest EV charging windows."
- Save button → `PUT /api/me/tariff-plan`

---

## Part 2 — Tariff-aware EV recommendation engine

### Updated interface

```csharp
EvChargeRecommendation Recommend(
    EvChargeRequest request,
    IReadOnlyList<GridReading> historicalReadings,
    GridReading currentReading,
    TariffPlan? userTariffPlan = null);    // null = tariff data unavailable
```

The engine is still pure — `TariffPlan` is passed in; the caller loads it.

### Updated `EvChargeRecommendation` model

Add:
```csharp
public decimal? EstimatedCostEuro { get; init; }       // cost at recommended window
public decimal? EstimatedSavingEuro { get; init; }     // vs charging now
public string? TariffPlanName { get; init; }           // which plan was used
```

### Priority logic changes

**`priority = "cleanest"`** — unchanged. Tariff data ignored even if available.

**`priority = "cheapest"`**:
- Requires `userTariffPlan != null`
- If null: behave like `"balanced"` + add explanation: `"No tariff plan selected. Showing balanced recommendation. Set your electricity plan in account preferences."`
- Rank candidate windows by `GetAverageRateForWindow` (ascending — cheapest first)
- `EstimatedCostEuro = requiredKwh × averageRateAtRecommendedWindow`
- `EstimatedSavingEuro = requiredKwh × (currentRate - recommendedWindowRate)` (clamp to 0)

**`priority = "balanced"`**:
- If `userTariffPlan != null`: score = `0.5 × inverseNormalizedCo2Score + 0.5 × inverseNormalizedTariffScore`
- If `userTariffPlan == null`: score = `co2Score` only (existing behaviour, no change)
- Normalise tariff score: `inverseNormalizedTariffScore = 1 - normalize(rate, minRate, maxRate)` where min/max are the min and max rates in the plan

### Updated explanation strings

When `priority = "cheapest"` and tariff available:
```
"Cheapest window found: {startTime}–{endTime} at an estimated {rate}c/kWh average.
 Estimated charging cost: €{cost}. Estimated saving vs charging now: €{saving}."
```

When `priority = "cheapest"` and no tariff:
```
"No electricity plan selected — showing balanced (CO₂ + cost) recommendation.
 Set your plan in account preferences to get cost-aware recommendations."
```

### API endpoint update

`POST /api/grid/recommendations/ev-charge` — load `userTariffPlan` from `ApplicationUser.PreferredTariffPlanId` if the request is authenticated. Pass to the engine.

Add to response:
```json
{
  ...existing fields...,
  "estimatedCostEuro": 2.14,
  "estimatedSavingEuro": 0.87,
  "tariffPlanName": "Night Rate (Generic)"
}
```

### New tariff API endpoints

```
GET  /api/tariff/plans                    → list active plans (name, provider, type only — no rates)
PUT  /api/me/tariff-plan                  → { "tariffPlanId": "..." | null }
GET  /api/me/tariff-plan                  → { "tariffPlanId": "...", "planName": "..." }

POST   /api/admin/tariff/plans            → create plan (Admin role only)
PUT    /api/admin/tariff/plans/{id}       → update plan
DELETE /api/admin/tariff/plans/{id}       → delete plan
```

`GET /api/tariff/plans` is public (unauthenticated) — allows MAUI to fetch the plan list without auth. Does not expose actual rates (rate data is admin-only).

---

## Part 3 — NI grid region

### Pre-condition

Before building: verify that the EirGrid smart grid dashboard has a separate fetchable endpoint or parameter for Northern Ireland data. The existing `EirGrid:BaseUrl` config is for ROI. Add:

```json
"GridPoller": {
  "IntervalMinutes": 5,
  "Regions": [
    { "Name": "ROI", "EirGridUrl": "https://www.smartgriddashboard.com/..." },
    { "Name": "NI",  "EirGridUrl": "https://www.smartgriddashboard.com/..." }
  ]
}
```

If the NI URL cannot be confirmed before starting, stub it with the same ROI URL and add a `// TODO: replace with verified NI endpoint` comment. Do not fabricate endpoint behaviour.

### Worker changes

`GridPollerService` runs one loop per configured region. Each loop is independent — a NI fetch failure does not affect ROI polling.

Existing `GridReading.Region` field (already "ROI" by default) is populated from config. NI readings have `Region = "NI"`.

Both regions write to the same `GridReadings` SQLite table — differentiated by `Region` column.

### API changes

Add optional `region` query parameter (default: `"ROI"`) to all existing grid endpoints:

```
GET /api/grid/current?region=NI
GET /api/grid/history?region=NI&from=...&to=...
GET /api/grid/health?region=NI
```

Backward compatibility: callers that omit `?region` get ROI data as before.

### New comparison endpoint

```
GET /api/grid/compare?regions=ROI,NI
```

Returns:
```json
{
  "snapshots": [
    {
      "region": "ROI",
      "timestampUtc": "...",
      "co2IntensityGPerKwh": 214,
      "renewablesPercent": 58.1,
      "greenScore": 0.72,
      "status": "good"
    },
    {
      "region": "NI",
      "timestampUtc": "...",
      "co2IntensityGPerKwh": 287,
      "renewablesPercent": 44.2,
      "greenScore": 0.54,
      "status": "moderate"
    }
  ]
}
```

Returns `404` for any region not configured. Returns `503` if no reading available for a requested region.

### Dashboard changes

**Grid dashboard home** — add a region toggle (ROI / NI / Compare) above the existing cards.
- ROI (default) — existing single-region view
- NI — same layout, NI data
- Compare — two-column view using `/api/grid/compare`

**Alert rules** — add `Region` field to `AlertRule` (nullable, default: `"ROI"`). NI alert rules evaluate against NI `GridReading` records only.

### MAUI changes

`ApplicationUser.PreferredRegion` already exists (from Phase 7). The Grid tab already has the region preference concept.

Add to the Account tab → Preferences section:
- "Grid region" picker: ROI / NI
- Stored preference used in `GET /api/grid/current?region={preference}`

---

## Part 4 — Developer API keys

### Domain models (Core project)

```csharp
public record DeveloperApiKey
{
    public string Id { get; init; }
    public string KeyHash { get; init; }           // SHA-256 of plaintext key
    public string Name { get; init; }              // "Cork App Developer" / "My Side Project"
    public string OwnerEmail { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
    public int RateLimitPerMinute { get; init; } = 200;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}

public record ApiKeyUsageLog
{
    public string Id { get; init; }
    public string ApiKeyId { get; init; }
    public DateOnly Date { get; init; }
    public int RequestCount { get; set; }
}
```

New tables: `DeveloperApiKeys`, `ApiKeyUsageLogs` (index on `ApiKeyId, Date`).

### Key generation

Admin-only. Returns the plaintext key **once** — never stored, never retrievable again.

```csharp
public interface IApiKeyService
{
    Task<(DeveloperApiKey record, string plaintextKey)> CreateAsync(string name, string ownerEmail, int rateLimitPerMinute);
    Task<DeveloperApiKey?> ValidateAsync(string plaintextKey);     // returns null if invalid/inactive
    Task RecordUsageAsync(string keyId);
    Task<IReadOnlyList<ApiKeyUsageLog>> GetUsageAsync(string keyId, DateOnly from, DateOnly to);
}
```

Generation:
```csharp
var plaintextKey = $"sie_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    .Replace('+', '-').Replace('/', '_').Replace("=", "")}";
var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey))).ToLowerInvariant();
```

Prefix `sie_` makes it obvious in logs what kind of credential it is.

### ApiKeyMiddleware

```csharp
public class ApiKeyMiddleware
{
    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService, IRateLimiterService rateLimiter)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) { await _next(context); return; }

        if (context.Request.Headers.TryGetValue("X-Api-Key", out var rawKey))
        {
            var apiKey = await apiKeyService.ValidateAsync(rawKey!);
            if (apiKey == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or inactive API key." });
                return;
            }
            context.Items["ApiKey"] = apiKey;
            context.Items["RateLimitKey"] = $"key_{apiKey.Id}";
            await apiKeyService.RecordUsageAsync(apiKey.Id);
        }
        else
        {
            context.Items["RateLimitKey"] = $"ip_{context.Connection.RemoteIpAddress}";
        }

        await _next(context);
    }
}
```

Register before routing middleware. The rate limiter (Phase 5) is keyed on `context.Items["RateLimitKey"]`:
- IP-based requests: 60 req/min (existing)
- API-key requests: `apiKey.RateLimitPerMinute` (default 200)

Usage log upsert: `INSERT OR REPLACE INTO ApiKeyUsageLogs ... ON CONFLICT(ApiKeyId, Date) DO UPDATE SET RequestCount = RequestCount + 1`

### Admin developer keys page (`/admin/developer-keys`)

Razor Page. Admin-only.

**List view:**
- Table: Name, Owner email, Rate limit, Last used, Status (Active/Inactive), Requests today
- "Create key" button

**Create key form:**
- Name, owner email, description, rate limit per minute (default 200, max 1000)
- On submit: show the plaintext key **once** in a highlighted, copy-to-clipboard box
- Warning: *"This key will not be shown again. Copy it now."*
- After copy-acknowledgement, return to list

**Per-key actions:**
- Disable / Enable toggle
- View usage (last 30 days — bar chart or table)
- Delete (irreversible)

### 429 response format

```json
{
  "error": "Rate limit exceeded.",
  "retryAfterSeconds": 15,
  "docs": "https://yourdomain.ie/developer"
}
```

Include `Retry-After` header (seconds).

---

## Part 5 — OpenAPI / Swagger

### NuGet package

Add to `IrelandLiveSignals.Api` / `IrelandLiveSignals.Web`:
```
Swashbuckle.AspNetCore
```

### Configuration

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Ireland Live Signals API",
        Version     = "v1",
        Description = "Live Irish electricity grid and public transit intelligence. " +
                      "Free for non-commercial use. Attribution required. " +
                      "See https://yourdomain.ie/developer for details.",
        Contact     = new OpenApiContact { Email = "api@yourdomain.ie" }
    });

    // API key security scheme
    o.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type        = SecuritySchemeType.ApiKey,
        In          = ParameterLocation.Header,
        Name        = "X-Api-Key",
        Description = "Optional. Provides higher rate limits (200 req/min vs 60 req/min anonymous)."
    });

    // JWT Bearer (for /api/me/* endpoints)
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type   = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Required for /api/me/* endpoints. Obtain from POST /api/auth/login."
    });

    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) o.IncludeXmlComments(xmlPath);

    // Exclude admin endpoints from public docs
    o.DocInclusionPredicate((_, api) =>
        !api.RelativePath?.StartsWith("admin/") == true &&
        !api.RelativePath?.StartsWith("api/admin/") == true);
});
```

Enable in `Program.cs`:
```csharp
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "SignalEire API v1");
    o.RoutePrefix = "api-docs";
    o.DocumentTitle = "Ireland Live Signals API";
});
```

### XML doc comments

Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to the API project `.csproj`.

Add XML doc comments to all public API endpoints:

```csharp
/// <summary>Returns the latest grid reading for the specified region.</summary>
/// <param name="region">Grid region: ROI (Republic of Ireland) or NI (Northern Ireland). Default: ROI.</param>
/// <response code="200">Current grid reading with green score and CO₂ intensity.</response>
/// <response code="503">No reading available yet — poller may not have run.</response>
[HttpGet("current")]
[ProducesResponseType<GridReadingResponse>(200)]
[ProducesResponseType(503)]
public async Task<IActionResult> GetCurrent([FromQuery] string region = "ROI") { ... }
```

All public endpoints must have at minimum: `<summary>`, at least one `<response>`, and `[ProducesResponseType]` attributes.

---

## Part 6 — Developer portal page (`/developer`)

Static Razor Page. Publicly accessible. No auth required.

Sections:
1. **What it is** — one paragraph
2. **Rate limits** — table: Anonymous (60 req/min, IP-keyed), API key holder (200 req/min, key-keyed)
3. **Authentication** — how to use `X-Api-Key` header; how to request a key (email link to admin)
4. **Available endpoints** — summary table linking to `/api-docs` for full detail
5. **Code examples** — three tabbed examples:
   - JavaScript `fetch`
   - C# `HttpClient`
   - Python `requests`
6. **Attribution requirements** — text developers must include when using the API publicly:
   > "Data sourced from Ireland Live Signals (yourdomain.ie), incorporating data from the National Transport Authority of Ireland and EirGrid plc."
7. **Terms of use** — bullet points:
   - Non-commercial use only without prior agreement
   - Do not cache responses for more than 5 minutes
   - Do not represent the data as your own
   - Anthropic/EirGrid/NTA attribution required
   - Rate limits will be enforced; abuse results in key revocation
8. **Link to full API docs** → `/api-docs`

---

## Part 7 — MAUI changes

All changes are additive. No existing ViewModels modified beyond adding new properties.

### Tariff plan picker

Add to `AccountViewModel`:
```csharp
[ObservableProperty] private ObservableCollection<TariffPlanSummary> _availablePlans = [];
[ObservableProperty] private TariffPlanSummary? _selectedPlan;

[RelayCommand]
private async Task LoadTariffPlansAsync()
{
    var plans = await _apiClient.GetTariffPlansAsync();   // GET /api/tariff/plans
    AvailablePlans = new ObservableCollection<TariffPlanSummary>(plans ?? []);
    SelectedPlan = AvailablePlans.FirstOrDefault(p => p.Id == _authService.PreferredTariffPlanId);
}

[RelayCommand]
private async Task SaveTariffPlanAsync()
{
    await _apiClient.SetTariffPlanAsync(SelectedPlan?.Id);  // PUT /api/me/tariff-plan
}
```

Add a `TariffPlanSummary` model (Id, Name, Provider — no rates) to the shared models.

Add to `AccountPage.xaml` under the Preferences section:
- "Electricity plan" label
- `Picker` bound to `AvailablePlans`, selected item bound to `SelectedPlan`
- "None / I don't know" as first item (null plan)
- Save button

### EV recommendation update

`StopBoardPage` is unrelated. The EV recommendation screen is on the web only for now — no MAUI EV screen exists yet. No MAUI change needed for the engine update; the response shape change (new `estimatedCostEuro` field) is additive and backward-compatible.

### Region preference

`AccountPage.xaml` already has a region picker (Phase 7). No change needed — the existing `PreferredRegion` preference is sent with grid requests.

Add `ISignalEireApiClient` methods:
```csharp
Task<List<TariffPlanSummary>?> GetTariffPlansAsync();
Task SetTariffPlanAsync(string? tariffPlanId);
```

---

## Tests

### Unit tests (Tests project)

**TariffRateServiceTests**
- Night Rate plan: time in night window → returns night rate
- Night Rate plan: time in day window → returns day rate
- Night Rate plan: boundary times (22:59, 23:00, 07:59, 08:00) → correct rate
- Midnight-spanning window: 23:30 → night rate; 00:30 → night rate; 08:30 → day rate
- Flat rate plan: any time → single rate returned
- Weekend DayType: Saturday 14:00 → weekend rate; Monday 14:00 → weekday rate
- `GetAverageRateForWindow`: 4-hour overnight window spanning Night Rate boundary → weighted average correct

**EvRecommendationEnginePhase8Tests**
- `priority = "cheapest"`, tariff plan provided → cheapest window selected (not necessarily cleanest)
- `priority = "cheapest"`, tariff plan provided → `EstimatedCostEuro` populated correctly
- `priority = "cheapest"`, no tariff plan → falls back to balanced, explanation contains tariff prompt
- `priority = "balanced"`, tariff plan provided → window balances CO₂ and cost (neither cheapest nor cleanest wins alone)
- `priority = "balanced"`, no tariff plan → CO₂-only scoring (no regression from Phase 4 behaviour)
- `EstimatedSavingEuro` clamped to 0 when recommended window is more expensive than now

**ApiKeyServiceTests**
- Create key → returns plaintext key and record
- Validate with correct plaintext key → returns record
- Validate with incorrect key → returns null
- Validate with inactive key → returns null
- RecordUsage → upserts `ApiKeyUsageLog` correctly

**ApiKeyMiddlewareTests** — using `WebApplicationFactory`
- No `X-Api-Key` header → IP rate limit key set
- Valid `X-Api-Key` → key rate limit used, usage recorded
- Invalid `X-Api-Key` → 401 returned, pipeline does not continue

**GridRegionTests**
- ROI reading: `Region = "ROI"`
- NI reading: `Region = "NI"`
- `GET /api/grid/current` (no param) → returns ROI reading
- `GET /api/grid/current?region=NI` → returns NI reading
- `GET /api/grid/compare?regions=ROI,NI` → both snapshots present

**TariffRatePeriodValidationTests**
- Plan with no periods → validation error
- Two overlapping periods for same DayType → validation error
- Rate ≤ 0 → validation error
- Midnight-spanning period alongside same-day period → validated correctly

### Integration tests

**TariffPlanSeederTests** — empty DB on startup → two pre-seeded plans present

---

## Constraints

1. **`priority = "cleanest"` behaviour unchanged.** Tariff data is ignored for cleanest priority even when available. No regression.
2. **API key validation is timing-safe.** Use `CryptographicOperations.FixedTimeEquals` when comparing key hashes to prevent timing attacks.
3. **Plaintext key never stored or logged.** Only the SHA-256 hash is persisted. Never appear in structured logs.
4. **Admin endpoints excluded from public Swagger docs.** `DocInclusionPredicate` filters them. Confirm no `/api/admin/*` endpoint appears at `/api-docs`.
5. **NI region does not affect ROI data.** Separate polling loops; NI fetch failure produces no side effect on ROI records.
6. **Tariff rates not exposed via public API.** `GET /api/tariff/plans` returns name, provider, type only. No `RatePerKwh` values in the public response.
7. **All Phase 1–7 tests remain green.**
8. **No Translink GTFS.** NI transit is deferred. If any transit models reference NI, that is a mistake.

---

## Definition of done for Phase 8

**Tariff track:**
- [ ] Two pre-seeded tariff plans in DB on fresh startup
- [ ] Admin can create, edit, and delete tariff plans at `/admin/tariff`
- [ ] Night Rate period correctly identifies midnight-spanning window in unit tests
- [ ] User can select a tariff plan at `/account/preferences`
- [ ] `POST /api/grid/recommendations/ev-charge` with `priority=cheapest` and authenticated user with saved plan → response includes `estimatedCostEuro`
- [ ] `POST /api/grid/recommendations/ev-charge` with `priority=cheapest` and no plan → explanation prompts user to set plan
- [ ] MAUI tariff plan picker shows available plans and saves selection

**NI grid track:**
- [ ] NI polling loop configured and running (or stubbed with comment if endpoint unverified)
- [ ] `GET /api/grid/current?region=NI` returns a reading with `Region = "NI"`
- [ ] `GET /api/grid/current` (no param) still returns ROI reading
- [ ] `GET /api/grid/compare?regions=ROI,NI` returns both snapshots
- [ ] Dashboard region toggle switches between ROI and NI views

**Developer API track:**
- [ ] Admin can create a developer API key at `/admin/developer-keys`
- [ ] Plaintext key shown once, not retrievable again
- [ ] `X-Api-Key` header with valid key → request proceeds, usage logged
- [ ] `X-Api-Key` header with invalid key → 401
- [ ] Anonymous IP request at 61 req/min → 429 with `Retry-After` header
- [ ] API key holder at 201 req/min → 429
- [ ] `/api-docs` renders Swagger UI with all public endpoints documented
- [ ] No admin endpoints visible in `/api-docs`
- [ ] `/developer` page renders with code examples and attribution requirements

**General:**
- [ ] `dotnet build` and `dotnet test` pass — all Phase 1–8 tests green
- [ ] MAUI builds clean (zero warnings) on both Android and iOS targets

---

## What comes after Phase 8

- Translink GTFS + NI transit (the deferred half of multi-region)
- Postgres + TimescaleDB migration (if load justifies it)
- App Store and Play Store submission (signing, review, screenshots)
- Digest mode for developer API usage reports (weekly email to key holders)
- Smart tariff integration (half-hourly pricing, if a data source becomes available)
- All-island CO₂ comparison and time-shift recommendations
