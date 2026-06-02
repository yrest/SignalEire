namespace IrelandLiveSignals.Core.Models;

public class PushSubscription
{
    public string Id { get; set; } = "";
    public string? UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256Dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTimeOffset SubscribedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
