using System.Security.Cryptography;
using System.Text;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly IDbContextFactory<GridDbContext> _dbFactory;

    public ApiKeyService(IDbContextFactory<GridDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<(DeveloperApiKey Record, string PlaintextKey)> CreateAsync(
        string name, string ownerEmail, int rateLimitPerMinute, CancellationToken ct = default)
    {
        var plaintext = $"sie_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
        var hash = HashKey(plaintext);

        var record = new DeveloperApiKey
        {
            Id = Guid.NewGuid().ToString("N"),
            KeyHash = hash,
            Name = name,
            OwnerEmail = ownerEmail,
            RateLimitPerMinute = rateLimitPerMinute,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DeveloperApiKeys.Add(record);
        await db.SaveChangesAsync(ct);

        return (record, plaintext);
    }

    public async Task<DeveloperApiKey?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
    {
        var hash = HashKey(plaintextKey);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var allKeys = await db.DeveloperApiKeys.Where(k => k.IsActive).ToListAsync(ct);
        // Timing-safe comparison
        return allKeys.FirstOrDefault(k =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(k.KeyHash),
                Encoding.UTF8.GetBytes(hash)));
    }

    public async Task RecordUsageAsync(string keyId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var log = await db.ApiKeyUsageLogs
            .FirstOrDefaultAsync(l => l.ApiKeyId == keyId && l.Date == today, ct);

        if (log is null)
        {
            log = new ApiKeyUsageLog
            {
                Id = Guid.NewGuid().ToString("N"),
                ApiKeyId = keyId,
                Date = today,
                RequestCount = 1
            };
            db.ApiKeyUsageLogs.Add(log);
        }
        else
        {
            log.RequestCount++;
        }

        var key = await db.DeveloperApiKeys.FindAsync([keyId], ct);
        if (key is not null) key.LastUsedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKeyUsageLog>> GetUsageAsync(
        string keyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ApiKeyUsageLogs
            .Where(l => l.ApiKeyId == keyId && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeveloperApiKey>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DeveloperApiKeys.OrderByDescending(k => k.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task SetActiveAsync(string keyId, bool active, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var key = await db.DeveloperApiKeys.FindAsync([keyId], ct);
        if (key is not null)
        {
            key.IsActive = active;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(string keyId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var key = await db.DeveloperApiKeys.FindAsync([keyId], ct);
        if (key is not null)
        {
            db.DeveloperApiKeys.Remove(key);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string HashKey(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
}
