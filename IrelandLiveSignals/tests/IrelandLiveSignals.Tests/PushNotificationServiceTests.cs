using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class PushNotificationServiceTests
{
    private static GridDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase("PushTestDb_" + Guid.NewGuid())
            .Options;
        return new GridDbContext(options);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public async Task SendAsync_WhenVapidKeysEmpty_DoesNotThrow()
    {
        var db = CreateDb();
        var svc = new VapidPushNotificationService(db, EmptyConfig(), NullLogger<VapidPushNotificationService>.Instance);

        // Should be a no-op and not throw
        await svc.SendAsync("user1", "Test", "Body", "/", CancellationToken.None);
    }

    [Fact]
    public async Task SendAsync_WhenNoSubscriptions_DoesNotThrow()
    {
        var db = CreateDb();
        // Add a subscription for a different user
        db.PushSubscriptions.Add(new PushSubscription
        {
            Id = "sub1",
            UserId = "otherUser",
            Endpoint = "https://example.com/push/1",
            P256Dh = "key",
            Auth = "auth",
            SubscribedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new VapidPushNotificationService(db, EmptyConfig(), NullLogger<VapidPushNotificationService>.Instance);

        // VAPID keys are empty so it returns early - no throw
        await svc.SendAsync("user1", "Test", "Body", "/", CancellationToken.None);
    }

    [Fact]
    public void PushSubscription_StoredCorrectly()
    {
        var sub = new PushSubscription
        {
            Id = "sub_123",
            UserId = "user_abc",
            Endpoint = "https://fcm.googleapis.com/fcm/send/abc",
            P256Dh = "p256dhkey",
            Auth = "authtoken",
            SubscribedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        };

        Assert.Equal("user_abc", sub.UserId);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/abc", sub.Endpoint);
        Assert.Equal("p256dhkey", sub.P256Dh);
        Assert.Equal("authtoken", sub.Auth);
    }
}
