using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class TransitReliabilityServiceTests
{
    [Fact]
    public void AllVehicleConfirmed_NoGhosts_ReturnsHighScore()
    {
        var score = TransitReliabilityService.ComputeScore(10, 0, 0, []);
        Assert.True(score > 0.75, $"Expected > 0.75, got {score}");
        Assert.Equal("reliable", TransitReliabilityService.ReliabilityLabel(score));
    }

    [Fact]
    public void AllGhosts_ReturnsLowScore()
    {
        var score = TransitReliabilityService.ComputeScore(0, 0, 10, []);
        Assert.True(score < 0.25, $"Expected < 0.25, got {score}");
        Assert.Equal("unreliable", TransitReliabilityService.ReliabilityLabel(score));
    }

    [Fact]
    public void NoData_ReturnsNeutralScore()
    {
        var score = TransitReliabilityService.ComputeScore(0, 0, 0, []);
        Assert.Equal(0.5, score);
    }

    [Fact]
    public void PositiveUserReports_BoostScore()
    {
        var baseScore = TransitReliabilityService.ComputeScore(5, 5, 2, []);
        var reports = Enumerable.Range(0, 5).Select(_ => new TransitUserReport
        {
            Id = Guid.NewGuid().ToString(),
            RouteId = "r1", StopId = "s1",
            ReportType = "bus_seen",
            ReportedAtUtc = DateTimeOffset.UtcNow,
            TrustWeight = 0.4
        }).ToList();
        var boostedScore = TransitReliabilityService.ComputeScore(5, 5, 2, reports);
        Assert.True(boostedScore >= baseScore);
    }

    [Fact]
    public void NegativeUserReports_ReduceScore()
    {
        var baseScore = TransitReliabilityService.ComputeScore(5, 5, 2, []);
        var reports = Enumerable.Range(0, 5).Select(_ => new TransitUserReport
        {
            Id = Guid.NewGuid().ToString(),
            RouteId = "r1", StopId = "s1",
            ReportType = "bus_not_appeared",
            ReportedAtUtc = DateTimeOffset.UtcNow,
            TrustWeight = 0.4
        }).ToList();
        var reducedScore = TransitReliabilityService.ComputeScore(5, 5, 2, reports);
        Assert.True(reducedScore <= baseScore);
    }

    [Fact]
    public void BuildReport_MapsFieldsCorrectly()
    {
        var agg = new TransitReliabilityAggregate
        {
            RouteId = "220",
            StopId = "CORK_001",
            TotalObservations = 20,
            VehicleConfirmedCount = 16,
            TimetableOnlyCount = 2,
            GhostCount = 2,
            AverageDelaySeconds = 45,
            ReliabilityScore = 0.8,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };

        var report = TransitReliabilityService.BuildReport(agg, []);

        Assert.Equal("220", report.RouteId);
        Assert.Equal("CORK_001", report.StopId);
        Assert.Equal(20, report.TotalObservations);
        Assert.Equal(80.0, report.VehicleConfirmedPercent, 1);
        Assert.Equal(10.0, report.GhostPercent, 1);
        Assert.Equal(45.0, report.AverageDelaySeconds);
        Assert.NotEmpty(report.ReliabilityLabel);
    }

    [Fact]
    public async Task TransitRepository_UpsertReliability_StoresAndRetrievesAggregate()
    {
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GridDbContext(opts);
        var repo = new TransitRepository(db);

        var agg = new TransitReliabilityAggregate
        {
            RouteId = "220",
            StopId = "S001",
            TotalObservations = 10,
            VehicleConfirmedCount = 8,
            TimetableOnlyCount = 1,
            GhostCount = 1,
            ReliabilityScore = 0.75,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };

        await repo.UpsertReliabilityAggregateAsync(agg);
        var retrieved = await repo.GetReliabilityAsync("220", "S001");

        Assert.NotNull(retrieved);
        Assert.Equal(0.75, retrieved.ReliabilityScore);
        Assert.Equal(10, retrieved.TotalObservations);
    }

    [Fact]
    public async Task TransitRepository_SaveUserReport_PersistsAndRetrievesWithFilters()
    {
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GridDbContext(opts);
        var repo = new TransitRepository(db);

        var report = new TransitUserReport
        {
            Id = "report_test_1",
            StopId = "STOP_X",
            RouteId = "220",
            ReportType = "bus_seen",
            ReportedAtUtc = DateTimeOffset.UtcNow,
            TrustWeight = 0.4
        };

        await repo.SaveUserReportAsync(report);

        var results = await repo.GetUserReportsAsync("220", null, DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Single(results);
        Assert.Equal("bus_seen", results[0].ReportType);

        var noResults = await repo.GetUserReportsAsync("999", null, DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Empty(noResults);
    }

    [Fact]
    public async Task TransitRepository_VehicleTrail_AppendAndRetrieve()
    {
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GridDbContext(opts);
        var repo = new TransitRepository(db);

        var point = new VehicleTrailPoint
        {
            VehicleId = "V001",
            TripId = "T001",
            RouteId = "220",
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Lat = 53.34,
            Lon = -6.26,
            GpsAgeSeconds = 15
        };

        await repo.AppendTrailPointAsync(point);

        var trail = await repo.GetVehicleTrailAsync("V001", DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Single(trail);
        Assert.Equal(53.34, trail[0].Lat);
    }
}
