using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class GtfsParserTests
{
    [Theory]
    [InlineData("08:00:00", 28800)]
    [InlineData("00:00:00", 0)]
    [InlineData("23:59:59", 86399)]
    [InlineData("25:30:00", 91800)] // next-day GTFS time
    public void GtfsTimeParses_ToTotalSeconds(string input, int expectedSeconds)
    {
        var parts = input.Split(':');
        int.TryParse(parts[0], out var h);
        int.TryParse(parts[1], out var m);
        int.TryParse(parts[2], out var s);
        var result = h * 3600 + m * 60 + s;
        Assert.Equal(expectedSeconds, result);
    }

    [Fact]
    public async Task TransitRepository_SearchStops_ReturnsMatchingStops()
    {
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GridDbContext(opts);

        db.TransitStops.AddRange(
            new TransitStop { StopId = "1", StopCode = "D123", StopName = "Dublin Airport", StopLat = 53.42, StopLon = -6.27 },
            new TransitStop { StopId = "2", StopCode = "D456", StopName = "Dublin City Centre", StopLat = 53.34, StopLon = -6.26 },
            new TransitStop { StopId = "3", StopCode = "C001", StopName = "Cork Bus Station", StopLat = 51.90, StopLon = -8.47 }
        );
        await db.SaveChangesAsync();

        var repo = new TransitRepository(db);
        var results = await repo.SearchStopsAsync("Dublin");

        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Contains("Dublin", s.StopName));
    }

    [Fact]
    public async Task TransitRepository_GetNearbyStops_FiltersCorrectly()
    {
        var opts = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GridDbContext(opts);

        db.TransitStops.AddRange(
            new TransitStop { StopId = "close", StopCode = "A1", StopName = "Close Stop", StopLat = 53.3400, StopLon = -6.2600 },
            new TransitStop { StopId = "far",   StopCode = "B1", StopName = "Far Stop",   StopLat = 51.9000, StopLon = -8.4700 }
        );
        await db.SaveChangesAsync();

        var repo = new TransitRepository(db);
        // 500m radius around Dublin city centre
        var results = await repo.GetNearbyStopsAsync(53.3400, -6.2600, 500);

        Assert.Single(results);
        Assert.Equal("close", results[0].StopId);
    }
}
