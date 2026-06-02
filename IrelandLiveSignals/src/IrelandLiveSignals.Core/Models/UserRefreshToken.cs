namespace IrelandLiveSignals.Core.Models;

public record UserRefreshToken
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string TokenHash { get; init; } = string.Empty;
    public string DeviceLabel { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
}
