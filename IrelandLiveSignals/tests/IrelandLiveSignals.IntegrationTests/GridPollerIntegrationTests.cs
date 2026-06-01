using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

/// <summary>
/// Simulates a full fetch → normalize → persist → retrieve cycle using
/// recorded sample payloads — no live network calls.
/// </summary>
public class GridPollerIntegrationTests
{
    private static (GridDbContext db, Microsoft.Data.Sqlite.SqliteConnection conn) CreateInMemoryDb()
    {
        // Must keep the connection open for in-memory SQLite to persist between calls
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new GridDbContext(opts);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static GridReading BuildReadingFromSamples()
    {
        // Load sample payloads (relative to test project output dir)
        var baseDir = AppContext.BaseDirectory;
        // Walk up to solution root to find data/samples
        var solutionRoot = FindSolutionRoot(baseDir);
        var samplesPath = Path.Combine(solutionRoot, "data", "samples");

        var generationJson    = File.ReadAllText(Path.Combine(samplesPath, "generation_roi_sample.json"));
        var co2Json           = File.ReadAllText(Path.Combine(samplesPath, "co2_roi_sample.json"));
        var interconnectJson  = File.ReadAllText(Path.Combine(samplesPath, "interconnect_roi_sample.json"));
        var demandJson        = File.ReadAllText(Path.Combine(samplesPath, "demand_roi_sample.json"));

        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var genData    = System.Text.Json.JsonSerializer.Deserialize<SampleResponse>(generationJson, opts)!;
        var co2Data    = System.Text.Json.JsonSerializer.Deserialize<SampleResponse>(co2Json, opts)!;
        var interData  = System.Text.Json.JsonSerializer.Deserialize<SampleResponse>(interconnectJson, opts)!;
        var demandData = System.Text.Json.JsonSerializer.Deserialize<SampleResponse>(demandJson, opts)!;

        var latestGen    = genData.Rows.Last();
        var latestCo2    = co2Data.Rows.Last();
        var latestInter  = interData.Rows.Last();
        var latestDemand = demandData.Rows.Last();

        var dataTimestamp = DateTimeOffset.Parse(latestGen.EffectiveTime!, System.Globalization.CultureInfo.InvariantCulture);
        var fetchedAt = DateTimeOffset.UtcNow;
        var freshnessSeconds = (int)(fetchedAt - dataTimestamp).TotalSeconds;
        if (freshnessSeconds < 0) freshnessSeconds = 0;

        var renewablesMw = (latestGen.WindGeneration ?? 0)
                         + (latestGen.HydroGeneration ?? 0)
                         + (latestGen.PumpStorageGeneration ?? 0);
        var totalGen = latestGen.ActualGeneration ?? latestDemand.Value ?? 1;
        var renewablesPercent = (renewablesMw / totalGen) * 100.0;

        var netImport = latestInter.Value;
        double? importMw = netImport > 0 ? netImport : null;
        double? exportMw = netImport < 0 ? -netImport : null;

        var co2 = latestCo2.Value ?? 300;
        var (score, status, recommendation) = GreenScoringService.Compute(renewablesPercent, co2, freshnessSeconds);

        return new GridReading
        {
            Id = $"integration_test_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Region = "ROI",
            TimestampUtc = dataTimestamp,
            SystemDemandMw = latestDemand.Value ?? 0,
            WindGenerationMw = latestGen.WindGeneration ?? 0,
            SolarGenerationMw = null,
            RenewablesPercent = renewablesPercent,
            Co2IntensityGPerKwh = co2,
            InterconnectorImportMw = importMw,
            InterconnectorExportMw = exportMw,
            DataFreshnessSeconds = freshnessSeconds,
            GreenScore = score,
            GreenStatus = status,
            Recommendation = recommendation,
            QualityStatus = freshnessSeconds > 600 ? "stale" : "ok"
        };
    }

    [Fact]
    public async Task FetchNormalizePersistRetrieve_ProducesValidReading()
    {
        var (db, conn) = CreateInMemoryDb();
        using var _ = conn;
        using var _db = db;
        var repo = new GridReadingRepository(db);

        var reading = BuildReadingFromSamples();

        // Persist
        await repo.SaveAsync(reading);

        // Retrieve
        var retrieved = await repo.GetLatestAsync();

        Assert.NotNull(retrieved);
        Assert.Equal("ROI", retrieved.Region);
        Assert.Equal(4380.0, retrieved.SystemDemandMw);
        Assert.Equal(2120.0, retrieved.WindGenerationMw);
        Assert.Equal(214.0, retrieved.Co2IntensityGPerKwh);
        Assert.True(retrieved.RenewablesPercent > 0);
        Assert.True(retrieved.GreenScore >= 0.0 && retrieved.GreenScore <= 1.0);
        Assert.Contains(retrieved.GreenStatus, new[] { "good", "moderate", "poor" });
        Assert.NotEmpty(retrieved.Recommendation);
    }

    [Fact]
    public async Task GetLatest_ReturnsNull_WhenNoReadings()
    {
        var (db, conn) = CreateInMemoryDb();
        using var _ = conn;
        using var _db = db;
        var repo = new GridReadingRepository(db);

        var result = await repo.GetLatestAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatest_ReturnsMostRecent_WhenMultipleExist()
    {
        var (db, conn) = CreateInMemoryDb();
        using var _ = conn;
        using var _db = db;
        var repo = new GridReadingRepository(db);

        var (score1, s1, r1) = GreenScoringService.Compute(40, 300, 60);
        var (score2, s2, r2) = GreenScoringService.Compute(70, 150, 30);

        var older = new GridReading
        {
            Id = "older",
            Region = "ROI",
            TimestampUtc = DateTimeOffset.UtcNow.AddHours(-2),
            SystemDemandMw = 4000, WindGenerationMw = 1500,
            RenewablesPercent = 40, Co2IntensityGPerKwh = 300,
            DataFreshnessSeconds = 60, GreenScore = score1,
            GreenStatus = s1, Recommendation = r1, QualityStatus = "ok"
        };
        var newer = new GridReading
        {
            Id = "newer",
            Region = "ROI",
            TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            SystemDemandMw = 4380, WindGenerationMw = 2120,
            RenewablesPercent = 70, Co2IntensityGPerKwh = 150,
            DataFreshnessSeconds = 30, GreenScore = score2,
            GreenStatus = s2, Recommendation = r2, QualityStatus = "ok"
        };

        await repo.SaveAsync(older);
        await repo.SaveAsync(newer);

        var result = await repo.GetLatestAsync();
        Assert.Equal("newer", result!.Id);
    }

    private static string FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Solution root not found.");
    }

    // Minimal DTOs for deserialising sample files
    private class SampleResponse
    {
        public List<SampleRow> Rows { get; set; } = new();
    }

    private class SampleRow
    {
        public string? EffectiveTime { get; set; }
        public double? Value { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("WIND_GENERATION")]
        public double? WindGeneration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("HYDRO_GENERATION")]
        public double? HydroGeneration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PUMP_STORAGE_GENERATION")]
        public double? PumpStorageGeneration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ACTUAL_GENERATION")]
        public double? ActualGeneration { get; set; }
    }
}
