using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IApiKeyService
{
    Task<(DeveloperApiKey Record, string PlaintextKey)> CreateAsync(string name, string ownerEmail, int rateLimitPerMinute, CancellationToken ct = default);
    Task<DeveloperApiKey?> ValidateAsync(string plaintextKey, CancellationToken ct = default);
    Task RecordUsageAsync(string keyId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKeyUsageLog>> GetUsageAsync(string keyId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<DeveloperApiKey>> GetAllAsync(CancellationToken ct = default);
    Task SetActiveAsync(string keyId, bool active, CancellationToken ct = default);
    Task DeleteAsync(string keyId, CancellationToken ct = default);
}
