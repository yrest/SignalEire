namespace IrelandLiveSignals.Core.Interfaces;

public interface IPushNotificationService
{
    Task SendAsync(string userId, string title, string body, string url, CancellationToken ct);
}
