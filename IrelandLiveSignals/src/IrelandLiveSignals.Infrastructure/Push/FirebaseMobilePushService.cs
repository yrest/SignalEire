using FirebaseAdmin.Messaging;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IrelandLiveSignals.Infrastructure.Push;

public sealed class FirebaseMobilePushService : IMobilePushService
{
    private readonly IDbContextFactory<GridDbContext> _dbFactory;
    private readonly ILogger<FirebaseMobilePushService> _logger;

    public FirebaseMobilePushService(
        IDbContextFactory<GridDbContext> dbFactory,
        ILogger<FirebaseMobilePushService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string title, string body, string url, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tokens = await db.DeviceTokens
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);

        if (tokens.Count == 0) return;

        var staleIds = new List<string>();

        foreach (var deviceToken in tokens)
        {
            try
            {
                var message = new Message
                {
                    Token = deviceToken.Token,
                    Notification = new Notification { Title = title, Body = body },
                    Data = new Dictionary<string, string> { ["url"] = url }
                };
                await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
                deviceToken.LastSeenAtUtc = DateTimeOffset.UtcNow;
            }
            catch (FirebaseMessagingException ex)
                when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered
                                            or MessagingErrorCode.InvalidArgument)
            {
                _logger.LogWarning("Stale FCM token for user {UserId}, removing.", userId);
                staleIds.Add(deviceToken.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM send failed for user {UserId}.", userId);
            }
        }

        if (staleIds.Count > 0)
        {
            var stale = db.DeviceTokens.Where(t => staleIds.Contains(t.Id));
            db.DeviceTokens.RemoveRange(stale);
        }

        await db.SaveChangesAsync(ct);
    }
}
