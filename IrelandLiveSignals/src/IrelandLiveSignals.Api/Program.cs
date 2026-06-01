using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<GridPollerService>();
builder.Services.AddRazorPages();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// ── Grid readings ─────────────────────────────────────────────────────────

app.MapGet("/api/grid/current", async (IGridReadingRepository repo) =>
{
    var reading = await repo.GetLatestAsync();
    if (reading is null)
        return Results.Json(new { error = "No grid data available yet." }, statusCode: 503);

    return Results.Ok(new
    {
        region = reading.Region,
        timestampUtc = reading.TimestampUtc,
        dataFreshnessSeconds = reading.DataFreshnessSeconds,
        systemDemandMw = reading.SystemDemandMw,
        windGenerationMw = reading.WindGenerationMw,
        solarGenerationMw = reading.SolarGenerationMw,
        renewablesPercent = reading.RenewablesPercent,
        co2IntensityGPerKwh = reading.Co2IntensityGPerKwh,
        greenScore = reading.GreenScore,
        status = reading.GreenStatus,
        recommendation = reading.Recommendation
    });
});

app.MapGet("/api/grid/history", async (IGridReadingRepository repo,
    string? region,
    DateTimeOffset? from,
    DateTimeOffset? to) =>
{
    var f = from ?? DateTimeOffset.UtcNow.AddDays(-1);
    var t = to   ?? DateTimeOffset.UtcNow;
    var readings = await repo.GetRangeAsync(f, t);
    return Results.Ok(readings.Select(r => new
    {
        r.Id, r.Region, r.TimestampUtc, r.SystemDemandMw, r.WindGenerationMw,
        r.RenewablesPercent, r.Co2IntensityGPerKwh, r.GreenScore, r.GreenStatus,
        r.DataFreshnessSeconds, r.QualityStatus
    }));
});

app.MapGet("/api/grid/health", async (IGridReadingRepository repo) =>
{
    var reading = await repo.GetLatestAsync();
    var secondsSince = reading is null
        ? (int?)null
        : (int)(DateTimeOffset.UtcNow - reading.TimestampUtc).TotalSeconds;

    return Results.Ok(new
    {
        status = "ok",
        lastReadingUtc = reading?.TimestampUtc,
        secondsSinceLastReading = secondsSince,
        workerRunning = GridPollerService.IsRunning
    });
});

// ── Best window (simple GET without EV parameters) ────────────────────────

app.MapGet("/api/grid/best-window", async (IGridReadingRepository repo,
    int durationMinutes = 60,
    string mode = "balanced",
    string tariff = "standard",
    DateTimeOffset? deadline = null) =>
{
    var history = await repo.GetRecentAsync(7 * 48); // 7 days of 30-min slots
    if (history.Count == 0)
        return Results.Json(new { error = "Insufficient grid history for recommendation." }, statusCode: 503);

    var now = DateTimeOffset.UtcNow;
    var dl = deadline ?? now.AddHours(12);
    var forecast = GridForecastService.Forecast(history, now, dl);
    var tariffPlan = TariffCatalogue.GetById(tariff);
    var (best, currentCo2) = WindowRecommendationService.FindBestWindow(forecast, durationMinutes, mode, tariffPlan);

    if (best is null)
        return Results.Json(new { error = "No suitable window found within the deadline." }, statusCode: 422);

    return Results.Ok(new
    {
        recommendedStartUtc = best.Start,
        recommendedEndUtc = best.End,
        durationMinutes,
        averageCo2GPerKwh = best.AverageCo2,
        averageRenewablesPercent = best.AverageRenewables,
        averageGreenScore = best.AverageGreenScore,
        confidence = best.Confidence,
        mode,
        tariff
    });
});

// ── EV charge recommendation ──────────────────────────────────────────────

app.MapPost("/api/grid/recommendations/ev-charge", async (IGridReadingRepository repo, EvChargeRequest request) =>
{
    if (request.RequiredKwh <= 0 || request.ChargerKw <= 0)
        return Results.BadRequest(new { error = "requiredKwh and chargerKw must be positive." });

    var durationMinutes = (int)Math.Ceiling((request.RequiredKwh / request.ChargerKw) * 60);
    var now = DateTimeOffset.UtcNow;

    if (request.DeadlineUtc <= now.AddMinutes(durationMinutes))
        return Results.BadRequest(new { error = "Deadline does not allow enough time to complete charging." });

    var history = await repo.GetRecentAsync(7 * 48);
    if (history.Count == 0)
        return Results.Json(new { error = "Insufficient grid history for recommendation." }, statusCode: 503);

    var current = history.Last();
    var forecast = GridForecastService.Forecast(history, now, request.DeadlineUtc);
    var tariffPlan = TariffCatalogue.GetById(request.TariffPlan ?? "standard");

    TimeOnly? quietStart = request.QuietHoursStart.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursStart.Value) : null;
    TimeOnly? quietEnd   = request.QuietHoursEnd.HasValue   ? TimeOnly.FromTimeSpan(request.QuietHoursEnd.Value)   : null;

    var (best, currentCo2) = WindowRecommendationService.FindBestWindow(
        forecast, durationMinutes, request.Mode ?? "balanced", tariffPlan, quietStart, quietEnd);

    if (best is null)
        return Results.Json(new { error = "No suitable charging window found within the deadline." }, statusCode: 422);

    var saving = CarbonSavingsEstimator.EstimateSavingKg(request.RequiredKwh, currentCo2, best.AverageCo2);
    var decision = best.Start <= now.AddMinutes(30) ? "start_now" : "wait";

    var explanation = new List<string>();
    if (decision == "wait")
        explanation.Add($"Current CO₂ intensity is {currentCo2:F0} g/kWh. A cleaner window starts around {best.Start:HH:mm} UTC.");
    else
        explanation.Add("Current grid conditions are already near-optimal. Starting now is recommended.");
    if (saving > 0.01)
        explanation.Add($"Estimated CO₂ saving vs charging now: {saving:F2} kg.");
    explanation.Add($"Recommended window satisfies the deadline of {request.DeadlineUtc:HH:mm} UTC.");

    var recommendation = new GridRecommendation
    {
        Id = $"rec_{Guid.NewGuid():N}",
        Region = "ROI",
        DeviceType = "ev",
        CreatedAtUtc = now,
        RequiredKwh = request.RequiredKwh,
        ChargerKw = request.ChargerKw,
        DeadlineUtc = request.DeadlineUtc,
        Mode = request.Mode ?? "balanced",
        TariffPlan = request.TariffPlan ?? "standard",
        Decision = decision,
        RecommendedStartUtc = best.Start,
        RecommendedEndUtc = best.End,
        RequiredDurationMinutes = durationMinutes,
        EstimatedAverageCo2GPerKwh = best.AverageCo2,
        EstimatedSavingKgCo2 = saving,
        Confidence = best.Confidence,
        Explanation = explanation.ToArray()
    };

    return Results.Ok(new
    {
        recommendation = recommendation.Decision,
        recommendedStartUtc = recommendation.RecommendedStartUtc,
        recommendedEndUtc = recommendation.RecommendedEndUtc,
        requiredDurationMinutes = recommendation.RequiredDurationMinutes,
        estimatedAverageCo2GPerKwh = recommendation.EstimatedAverageCo2GPerKwh,
        estimatedSavingKgCo2 = recommendation.EstimatedSavingKgCo2,
        confidence = recommendation.Confidence,
        explanation = recommendation.Explanation
    });
});

// ── Alert rules ───────────────────────────────────────────────────────────

app.MapPost("/api/grid/alerts", async (IAlertRuleRepository repo, AlertRuleRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RuleName))
        return Results.BadRequest(new { error = "ruleName is required." });

    var rule = new AlertRule
    {
        Id = $"rule_{Guid.NewGuid():N}",
        RuleName = request.RuleName,
        Region = "ROI",
        Co2BelowGPerKwh = request.Co2BelowGPerKwh,
        RenewablesAbovePercent = request.RenewablesAbovePercent,
        GreenScoreAbove = request.GreenScoreAbove,
        QuietHoursStart = request.QuietHoursStart.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursStart.Value) : null,
        QuietHoursEnd = request.QuietHoursEnd.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursEnd.Value) : null,
        MaxAlertsPerDay = request.MaxAlertsPerDay ?? 2,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    await repo.SaveAsync(rule);
    return Results.Created($"/api/grid/alerts/{rule.Id}", new { rule.Id, rule.RuleName });
});

app.MapGet("/api/grid/alerts", async (IAlertRuleRepository repo) =>
{
    var rules = await repo.GetActiveAsync();
    return Results.Ok(rules);
});

app.MapDelete("/api/grid/alerts/{id}", async (IAlertRuleRepository repo, string id) =>
{
    var rule = await repo.GetByIdAsync(id);
    if (rule is null) return Results.NotFound();
    await repo.DeleteAsync(id);
    return Results.NoContent();
});

app.MapGet("/api/grid/alerts/deliveries", async (IAlertRuleRepository repo, int limit = 50) =>
{
    var deliveries = await repo.GetRecentDeliveriesAsync(limit);
    return Results.Ok(deliveries);
});

app.Run();

// ── Request types ─────────────────────────────────────────────────────────

record EvChargeRequest(
    double RequiredKwh,
    double ChargerKw,
    DateTimeOffset DeadlineUtc,
    string? Mode,
    string? TariffPlan,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd
);

record AlertRuleRequest(
    string RuleName,
    double? Co2BelowGPerKwh,
    double? RenewablesAbovePercent,
    double? GreenScoreAbove,
    int? MaxAlertsPerDay,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd
);
