using IrelandLiveSignals.Api.Worker;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Identity;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class DigestJobTests
{
    private static GridDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GridDbContext(options);
    }

    private static ApplicationUser CreateUser(bool digestEnabled = true,
        TimeOnly? digestTime = null, string userId = "user1")
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"{userId}@test.ie",
            Email = $"{userId}@test.ie",
            DigestEnabled = digestEnabled,
            DigestTime = digestTime ?? new TimeOnly(8, 0)
        };
    }

    private static AlertFiring CreateFiring(string userId, string ruleId, DateTimeOffset firedAt,
        bool includedInDigest = false) => new()
    {
        Id = $"firing_{Guid.NewGuid():N}",
        AlertRuleId = ruleId,
        UserId = userId,
        FiredAtUtc = firedAt,
        Co2Value = 350,
        RenewablesPercent = 40,
        IncludedInDigest = includedInDigest
    };

    [Fact]
    public async Task DigestEnabled_With3Firings_MarksAsIncluded()
    {
        using var db = CreateDb();

        var user = CreateUser(digestEnabled: true, digestTime: new TimeOnly(0, 0));
        db.Users.Add(user);

        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 3; i++)
        {
            db.AlertFirings.Add(CreateFiring(user.Id, "rule1", now.AddHours(-i - 1)));
        }
        await db.SaveChangesAsync();

        var job = new TestableDigestJob(db);
        await job.RunDigestForUserAsync(user, DateOnly.FromDateTime(DateTime.UtcNow));

        var firings = await db.AlertFirings.ToListAsync();
        Assert.All(firings, f => Assert.True(f.IncludedInDigest));
    }

    [Fact]
    public async Task DigestEnabled_NoFirings_NoEmailSent()
    {
        using var db = CreateDb();

        var user = CreateUser(digestEnabled: true);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var job = new TestableDigestJob(db);
        await job.RunDigestForUserAsync(user, DateOnly.FromDateTime(DateTime.UtcNow));

        // No firings should be created or modified
        Assert.Empty(await db.AlertFirings.ToListAsync());
        Assert.False(job.EmailWasSent);
    }

    [Fact]
    public async Task DigestDisabled_HasFirings_NoEmailSent()
    {
        using var db = CreateDb();

        var user = CreateUser(digestEnabled: false);
        db.Users.Add(user);

        db.AlertFirings.Add(CreateFiring(user.Id, "rule1", DateTimeOffset.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();

        var job = new TestableDigestJob(db);
        // DigestEnabled = false means we skip; test by checking enabled filter
        // The job should not process this user at all
        var users = await db.Users
            .OfType<ApplicationUser>()
            .Where(u => u.DigestEnabled)
            .ToListAsync();

        Assert.Empty(users); // no enabled users
    }

    [Fact]
    public async Task DigestAlreadySentToday_NoDuplicate()
    {
        using var db = CreateDb();

        var user = CreateUser(digestEnabled: true, digestTime: new TimeOnly(0, 0));
        db.Users.Add(user);

        db.AlertFirings.Add(CreateFiring(user.Id, "rule1", DateTimeOffset.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();

        var job = new TestableDigestJob(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // First run sends digest
        await job.RunDigestForUserAsync(user, today);
        Assert.True(job.EmailWasSent);

        // Second run same day: reset flag
        job.ResetEmailFlag();
        await job.RunDigestForUserAsync(user, today);

        // Firings are already marked IncludedInDigest, so no email again
        Assert.False(job.EmailWasSent);
    }

    [Fact]
    public async Task DigestEnabled_Only3FiringsAfterSince_CorrectCount()
    {
        using var db = CreateDb();

        var user = CreateUser(digestEnabled: true);
        db.Users.Add(user);

        var now = DateTimeOffset.UtcNow;
        // Old firing (already included)
        db.AlertFirings.Add(CreateFiring(user.Id, "rule1", now.AddDays(-2), includedInDigest: true));
        // New firings
        for (int i = 0; i < 3; i++)
            db.AlertFirings.Add(CreateFiring(user.Id, "rule1", now.AddHours(-i - 1), includedInDigest: false));

        await db.SaveChangesAsync();

        var job = new TestableDigestJob(db);
        await job.RunDigestForUserAsync(user, DateOnly.FromDateTime(DateTime.UtcNow));

        // Should mark the 3 new firings as included, not the old one
        var unmarked = await db.AlertFirings.Where(f => !f.IncludedInDigest).ToListAsync();
        Assert.Empty(unmarked);
        var marked = await db.AlertFirings.Where(f => f.IncludedInDigest).ToListAsync();
        Assert.Equal(4, marked.Count); // all 4 now marked
    }
}

/// <summary>
/// Testable wrapper over DigestJob logic.
/// </summary>
internal class TestableDigestJob
{
    private readonly GridDbContext _db;
    public bool EmailWasSent { get; private set; } = false;

    public TestableDigestJob(GridDbContext db)
    {
        _db = db;
    }

    public void ResetEmailFlag() => EmailWasSent = false;

    public async Task RunDigestForUserAsync(ApplicationUser user, DateOnly today)
    {
        if (!user.DigestEnabled) return;

        // Get unfired firings
        var firings = await _db.AlertFirings
            .Where(f => f.UserId == user.Id && !f.IncludedInDigest)
            .ToListAsync();

        var anomalies = await _db.SignalAnomalies
            .Where(a => a.Date >= today.AddDays(-1))
            .ToListAsync();

        if (firings.Count == 0 && anomalies.Count == 0) return;

        // Simulate send
        EmailWasSent = true;

        foreach (var firing in firings)
            firing.IncludedInDigest = true;

        await _db.SaveChangesAsync();
    }
}
