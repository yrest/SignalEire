using IrelandLiveSignals.Core.Interfaces;

namespace IrelandLiveSignals.Infrastructure.Push;

public sealed class NullMobilePushService : IMobilePushService
{
    public Task SendAsync(string userId, string title, string body, string url, CancellationToken ct)
        => Task.CompletedTask;
}
