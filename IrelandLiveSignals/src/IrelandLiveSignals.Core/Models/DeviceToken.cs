namespace IrelandLiveSignals.Core.Models;

public record DeviceToken
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
