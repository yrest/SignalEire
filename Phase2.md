# SignalEire — Phase 2 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 1 complete and passing (`dotnet test` green, worker polling, `/api/grid/current` live)  
**Scope:** Alert rules + history API + EV/device recommendation engine  
**Do not build anything outside this document.**

---

## What Phase 2 adds

Phase 1 proved the data pipeline. Phase 2 makes it useful:

1. **History API** — query past `GridReading` records
2. **Alert Rule Engine** — evaluate stored rules against each new reading; fire when conditions met
3. **Alert delivery** — email and/or Telegram (configurable; at least one must work)
4. **EV charge window recommendation** — given kWh, charger power, and a deadline, return the cleanest window from historical data
5. **Carbon savings estimator** — how much CO₂ does shifting consumption save
6. **Dashboard upgrades** — history chart, alert rule management UI, recommendation form

No auth. No user accounts. Rules and device profiles are stored without a user identity for now — single-tenant localhost operation is fine. Auth is Phase 3+.

---

## New domain models (add to Core project)

### AlertRule
```csharp
public record AlertRule
{
    public string Id { get; init; }
    public string Name { get; init; }
    public double? Co2BelowGPerKwh { get; init; }       // null = not a condition
    public double? RenewablesAbovePercent { get; init; } // null = not a condition
    public double? GreenScoreAbove { get; init; }        // null = not a condition
    public TimeOnly? QuietHoursStart { get; init; }      // null = no quiet hours
    public TimeOnly? QuietHoursEnd { get; init; }
    public int MaxAlertsPerDay { get; init; } = 2;
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; }
}
```

### AlertFiring
```csharp
public record AlertFiring
{
    public string Id { get; init; }
    public string AlertRuleId { get; init; }
    public string GridReadingId { get; init; }
    public DateTimeOffset FiredAtUtc { get; init; }
    public string Message { get; init; }
    public string DeliveryChannel { get; init; }  // "email" | "telegram" | "log"
    public bool Delivered { get; init; }
    public string? DeliveryError { get; init; }
}
```

### DeviceProfile
```csharp
public record DeviceProfile
{
    public string Id { get; init; }
    public string Name { get; init; }              // e.g. "Tesla Model 3"
    public string DeviceType { get; init; }        // "ev" | "dishwasher" | "washer" | "generic"
    public double ChargerKw { get; init; }         // power draw in kW
    public double? CapacityKwh { get; init; }      // battery capacity (EVs)
    public string Priority { get; init; } = "balanced";  // "cleanest" | "balanced" | "cheapest"
}
```

### EvChargeRecommendation
```csharp
public record EvChargeRecommendation
{
    public string Id { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public double RequiredKwh { get; init; }
    public double ChargerKw { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public string Priority { get; init; }
    public string Action { get; init; }           // "charge_now" | "wait" | "insufficient_data"
    public DateTimeOffset? RecommendedStartUtc { get; init; }
    public DateTimeOffset? RecommendedEndUtc { get; init; }
    public int RequiredDurationMinutes { get; init; }
    public double? EstimatedAverageCo2GPerKwh { get; init; }
    public double? EstimatedSavingKgCo2 { get; init; }
    public double Confidence { get; init; }
    public List<string> Explanation { get; init; }
}
```

---

## Alert Rule Engine (Core project)

### Interface
```csharp
public interface IAlertRuleEngine
{
    IReadOnlyList<AlertFiring> Evaluate(GridReading reading, IReadOnlyList<AlertRule> rules, IReadOnlyList<AlertFiring> recentFirings);
}
```

### Evaluation logic

For each enabled rule:

1. **Condition check** — all specified conditions must be true simultaneously:
   - `Co2BelowGPerKwh`: `reading.Co2IntensityGPerKwh < rule.Co2BelowGPerKwh`
   - `RenewablesAbovePercent`: `reading.RenewablesPercent > rule.RenewablesAbovePercent`
   - `GreenScoreAbove`: `reading.GreenScore > rule.GreenScoreAbove`

2. **Quiet hours check** — if quiet hours are set, do not fire if current time (Ireland local, Europe/Dublin) falls within the quiet window. Handle midnight-spanning windows (e.g. 23:00–07:00).

3. **Suppression check** — count `AlertFiring` records for this rule in the past 24 hours. If count `>= MaxAlertsPerDay`, suppress.

4. If all checks pass → produce an `AlertFiring` with a generated message.

### Message generation

```text
Rule: "Dishwasher Green Window" (co2Below=220, renewablesAbove=60)
Message: "Grid is clean now — CO₂ at {co2} g/kWh, {renewables}% renewable. Good time for your dishwasher."

Rule: GreenScore only
Message: "Green score is {score}/100. Good time for flexible electricity use."
```

Keep message generation as a pure method on the engine — no I/O.

### Unit tests required
- All conditions true → fires
- One condition false → does not fire
- In quiet hours → does not fire  
- Midnight-spanning quiet hours → handled correctly
- `MaxAlertsPerDay` reached → suppressed
- Multiple rules → each evaluated independently

---

## Alert delivery (Infrastructure project)

### Interface
```csharp
public interface IAlertDeliveryService
{
    Task DeliverAsync(AlertFiring firing, CancellationToken ct);
}
```

### Delivery channels

Implement both; which channels fire is controlled by `appsettings.json`.

**Email** via `System.Net.Mail` / SMTP:
- configurable SMTP host, port, from address, to address
- plain text body; no HTML required
- log failure; do not throw; mark `AlertFiring.Delivered = false` with error

**Telegram** via Bot API:
- configurable `BotToken` and `ChatId`
- POST to `https://api.telegram.org/bot{token}/sendMessage`
- plain text message
- log failure; do not throw

**Log-only fallback** (always available):
- if neither email nor Telegram is configured, log the firing at Info level
- this must work with zero external config so the engine is testable without credentials

### Worker integration

After each `GridPoller` tick:
1. Load all enabled `AlertRule` records from SQLite
2. Load `AlertFiring` records for the past 24 hours
3. Call `IAlertRuleEngine.Evaluate(reading, rules, recentFirings)`
4. For each returned firing: save to `AlertFirings` table, then call `IAlertDeliveryService.DeliverAsync`
5. Update `Delivered` flag after delivery attempt

---

## EV Charge Window Recommendation Engine (Core project)

### Interface
```csharp
public interface IEvRecommendationEngine
{
    EvChargeRecommendation Recommend(
        EvChargeRequest request,
        IReadOnlyList<GridReading> historicalReadings,
        GridReading currentReading);
}
```

### Request
```csharp
public record EvChargeRequest
{
    public double RequiredKwh { get; init; }
    public double ChargerKw { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public string Priority { get; init; } = "balanced";  // "cleanest" | "balanced" | "cheapest"
}
```

### Algorithm

```
requiredDurationMinutes = ceil((requiredKwh / chargerKw) × 60)
```

1. **Check feasibility** — if `(deadline - now) < requiredDurationMinutes`, return `Action = "insufficient_time"` with explanation.

2. **Check data** — if `historicalReadings` has fewer than 12 readings (1 hour), return `Action = "insufficient_data"` with explanation.

3. **Build candidate windows** — slide a window of `requiredDurationMinutes` across the next 24 hours from now to deadline. For each window position, estimate average CO₂ using the most recent historical readings with the same time-of-day (rolling 7-day average at each 5-minute bucket). Use current reading for windows starting within the next 30 minutes.

4. **Score windows** — for `priority = "cleanest"`, rank by lowest estimated average CO₂. For `priority = "balanced"`, weight CO₂ and feasibility equally (tariff data not available yet — cheapest will behave like balanced until Phase 3).

5. **Select best window** — pick the top-ranked window that ends before `deadline`.

6. **Compare to now** — if charging now has a score within 10% of the best window, return `Action = "charge_now"`. Otherwise return `Action = "wait"` with the recommended window.

7. **Confidence** — scale by number of historical readings available:
   - `< 24 readings` (2 hrs): confidence = 0.3
   - `24–144 readings` (2–12 hrs): confidence = 0.5
   - `144–288 readings` (12–24 hrs): confidence = 0.65
   - `288+ readings` (24 hrs+): confidence = 0.8
   - Reduce by 0.1 if using time-of-day extrapolation (not real history)

8. **Carbon savings estimate**:
   ```
   savedKgCo2 = requiredKwh × (currentCo2 - recommendedWindowCo2) / 1000
   ```
   If negative (recommended window is worse than now), clamp to 0 and flip `Action` to `"charge_now"`.

### Unit tests required
- Insufficient time → correct action returned
- Insufficient data → correct action returned
- Current is best window → `charge_now`
- Clear overnight dip in mock history → `wait` with correct window
- Carbon saving calculation
- Confidence levels at each data-availability tier

---

## New API endpoints (Api project)

### GET /api/grid/history
```
GET /api/grid/history?region=ROI&from=2026-06-01T00:00:00Z&to=2026-06-02T00:00:00Z
```
- Returns array of `GridReading` records, ordered by `TimestampUtc` ascending
- Max 2,000 records per request; return `400` with message if range would exceed
- Both `from` and `to` are required

### GET /api/grid/best-window
```
GET /api/grid/best-window?region=ROI&durationMinutes=180&deadline=2026-06-02T07:30:00%2B01:00
```
- Returns a best-window result (start, end, estimated CO₂, confidence)
- Uses `IEvRecommendationEngine` internally with `requiredKwh` derived from `durationMinutes` × a nominal 7 kW load
- This is a convenience shortcut; the full EV endpoint is below

### POST /api/grid/recommendations/ev-charge
Request body:
```json
{
  "requiredKwh": 30,
  "chargerKw": 7.4,
  "deadlineLocal": "2026-06-02T07:30:00+01:00",
  "priority": "balanced"
}
```
Response: `EvChargeRecommendation` JSON (see model above)

### POST /api/grid/alerts
Create an alert rule. Body: `AlertRule` (without `Id` and `CreatedAtUtc` — server assigns).  
Returns `201 Created` with the created rule.

### GET /api/grid/alerts
Returns all `AlertRule` records.

### DELETE /api/grid/alerts/{id}
Deletes an alert rule. Returns `204 No Content`.

### GET /api/grid/alerts/history
```
GET /api/grid/alerts/history?from=...&to=...
```
Returns `AlertFiring` records for the given range.

---

## Database additions (Infrastructure project)

New EF Core tables:

```
AlertRules        — stores AlertRule records
AlertFirings      — stores AlertFiring records (index on RuleId + FiredAtUtc)
DeviceProfiles    — stores DeviceProfile records
```

No migrations required — `EnsureCreated()` still acceptable for Phase 2.

---

## Dashboard upgrades (Web project)

Still Razor Pages + plain CSS. No npm, no JS frameworks.

### New pages / sections

**History chart** (`/grid/history`):
- Simple SVG or `<canvas>` chart of CO₂ intensity over the last 24 hours
- Use the `/api/grid/history` endpoint via a `<script>` fetch or server-side render
- Show wind generation on the same chart as a secondary line

**Alert rules** (`/alerts`):
- List all alert rules (name, conditions, enabled state, firings today)
- Form to create a new rule (name, CO₂ threshold, renewables threshold, green score threshold, quiet hours, max alerts/day)
- Delete button per rule
- Recent firings table (last 20)

**EV recommendation** (`/grid/ev`):
- Form: required kWh, charger kW, deadline (datetime-local input), priority (select)
- Submit → POST to `/api/grid/recommendations/ev-charge` → render result inline
- Show: action, recommended window, estimated CO₂ saving, confidence, explanation bullets
- Plain form POST + server-side render is fine (no AJAX required)

---

## Configuration additions (appsettings.json)

```json
{
  "Alerts": {
    "Email": {
      "Enabled": false,
      "SmtpHost": "",
      "SmtpPort": 587,
      "FromAddress": "",
      "ToAddress": "",
      "Username": "",
      "Password": ""
    },
    "Telegram": {
      "Enabled": false,
      "BotToken": "",
      "ChatId": ""
    }
  }
}
```

If both `Email.Enabled` and `Telegram.Enabled` are false, alerts log only. The system must start cleanly with both disabled.

---

## Constraints (same as Phase 1 plus these)

1. **No live network calls in tests.** Mock `IAlertDeliveryService` and `IGridDataAdapter`.
2. **Recommendation engine is pure.** `IEvRecommendationEngine` takes data in, returns result. No I/O, no DB calls inside.
3. **Alert engine is pure.** `IAlertRuleEngine.Evaluate` takes data in, returns firings. No I/O.
4. **Delivery failures must not crash the worker.** Log and continue.
5. **No user auth, no sessions, no login page.** Single-tenant.
6. **No transit code.** If transit types appear anywhere, that is a mistake.
7. **Tariff/price data is not available yet.** `priority = "cheapest"` behaves identically to `"balanced"` — document this in the API response via an explanation string.

---

## Definition of done for Phase 2

- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test` passes — all Phase 1 tests still green, all Phase 2 tests green
- [ ] Alert rule can be created via the dashboard form and the API
- [ ] Alert fires (logged) when a grid reading matches a rule
- [ ] Alert suppression works (create a rule with `maxAlertsPerDay=1`, confirm second firing in same day is suppressed)
- [ ] `POST /api/grid/recommendations/ev-charge` returns a valid recommendation
- [ ] EV recommendation page renders a result for a sample request
- [ ] `GET /api/grid/history` returns paginated reading records
- [ ] History chart renders on the dashboard
- [ ] Quiet-hours suppression works correctly across midnight boundary

---

## What comes after Phase 2 (do not build yet)

- Tariff-aware scoring (Night Rate, Smart Tariff integration)
- User accounts and per-user alert rules / device profiles
- Transit Trust Engine (separate spec)
- PWA / mobile client
