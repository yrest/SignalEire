namespace IrelandLiveSignals.Core.Interfaces;

public interface IMobilePushService
{
    Task SendAsync(string userId, string title, string body, string url, CancellationToken ct);
}
