using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Worker;
using Microsoft.EntityFrameworkCore;

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

app.Run();
