namespace IrelandLiveSignals.Core.Models;

public class DeveloperApiKey
{
    public string Id { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int RateLimitPerMinute { get; set; } = 200;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}

public class ApiKeyUsageLog
{
    public string Id { get; set; } = string.Empty;
    public string ApiKeyId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int RequestCount { get; set; }
}
