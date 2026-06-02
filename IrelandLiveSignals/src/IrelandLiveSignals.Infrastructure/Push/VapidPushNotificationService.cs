using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebPush;

namespace IrelandLiveSignals.Infrastructure.Push;

public class VapidPushNotificationService : IPushNotificationService
{
    private readonly GridDbContext _db;
    private readonly ILogger<VapidPushNotificationService> _logger;
    private readonly string _vapidPublicKey;
    private readonly string _vapidPrivateKey;
    private readonly string _vapidSubject;

    public VapidPushNotificationService(
        GridDbContext db,
        IConfiguration configuration,
        ILogger<VapidPushNotificationService> logger)
    {
        _db = db;
        _logger = logger;
        _vapidPublicKey = configuration["WebPush:VapidPublicKey"] ?? "";
        _vapidPrivateKey = configuration["WebPush:VapidPrivateKey"] ?? "";
        _vapidSubject = configuration["WebPush:VapidSubject"] ?? "mailto:admin@yourdomain.ie";
    }

    public async Task SendAsync(string userId, string title, string body, string url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_vapidPublicKey) || string.IsNullOrEmpty(_vapidPrivateKey))
        {
            _logger.LogWarning("VAPID keys not configured — push notification skipped for user {UserId}.", userId);
            return;
        }

        var subscriptions = await _db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        if (!subscriptions.Any())
            return;

        var client = new WebPushClient();
        var vapidDetails = new VapidDetails(_vapidSubject, _vapidPublicKey, _vapidPrivateKey);
        var payload = JsonSerializer.Serialize(new { title, body, url });

        var toRemove = new List<string>();

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256Dh, sub.Auth);
                await client.SendNotificationAsync(pushSub, payload, vapidDetails);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogInformation("Push subscription {Endpoint} is gone (410). Removing.", sub.Endpoint);
                toRemove.Add(sub.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification to endpoint {Endpoint}.", sub.Endpoint);
            }
        }

        if (toRemove.Any())
        {
            var expired = await _db.PushSubscriptions
                .Where(s => toRemove.Contains(s.Id))
                .ToListAsync(ct);
            _db.PushSubscriptions.RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
        }
    }
}
