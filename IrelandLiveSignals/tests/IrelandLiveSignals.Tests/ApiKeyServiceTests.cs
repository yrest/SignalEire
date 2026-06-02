using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.Tests;

/// <summary>
/// Unit tests for ApiKeyService using an EF Core in-memory database.
/// Each test gets a fresh DB via a unique name to avoid cross-test pollution.
/// </summary>
public class ApiKeyServiceTests
{
    // ApiKeyService requires IDbContextFactory<GridDbContext>.
    // We create a minimal implementation backed by a fixed in-memory database.
    private sealed class DirectDbContextFactory : IDbContextFactory<GridDbContext>
    {
        private readonly DbContextOptions<GridDbContext> _options;
        public DirectDbContextFactory(DbContextOptions<GridDbContext> options) => _options = options;
        public GridDbContext CreateDbContext() => new GridDbContext(_options);
        public Task<GridDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new GridDbContext(_options));
    }

    private static (ApiKeyService service, DbContextOptions<GridDbContext> options) CreateService()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Ensure schema is created
        using var seed = new GridDbContext(options);
        seed.Database.EnsureCreated();

        var factory = new DirectDbContextFactory(options);
        var service = new ApiKeyService(factory);
        return (service, options);
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsPlaintextKeyStartingWithSie()
    {
        var (svc, _) = CreateService();

        var (record, plaintext) = await svc.CreateAsync("Test Key", "dev@example.com", 200);

        Assert.StartsWith("sie_", plaintext);
    }

    [Fact]
    public async Task CreateAsync_StoresHashedKey_NotPlaintext()
    {
        var (svc, options) = CreateService();

        var (record, plaintext) = await svc.CreateAsync("Test Key", "dev@example.com", 200);

        // The stored hash must never equal the plaintext
        Assert.NotEqual(plaintext, record.KeyHash);
        Assert.False(string.IsNullOrWhiteSpace(record.KeyHash));
    }

    // ── ValidateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_WithCorrectKey_ReturnsRecord()
    {
        var (svc, _) = CreateService();
        var (created, plaintext) = await svc.CreateAsync("Valid Key", "dev@example.com", 200);

        var result = await svc.ValidateAsync(plaintext);

        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task ValidateAsync_WithWrongKey_ReturnsNull()
    {
        var (svc, _) = CreateService();
        await svc.CreateAsync("Some Key", "dev@example.com", 200);

        var result = await svc.ValidateAsync("sie_totallyWrongKey");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WithInactiveKey_ReturnsNull()
    {
        var (svc, _) = CreateService();
        var (record, plaintext) = await svc.CreateAsync("Inactive Key", "dev@example.com", 200);

        await svc.SetActiveAsync(record.Id, active: false);

        var result = await svc.ValidateAsync(plaintext);

        Assert.Null(result);
    }

    // ── RecordUsageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecordUsageAsync_CreatesUsageLogWithRequestCountOne()
    {
        var (svc, options) = CreateService();
        var (record, _) = await svc.CreateAsync("Usage Key", "dev@example.com", 200);

        await svc.RecordUsageAsync(record.Id);

        await using var db = new GridDbContext(options);
        var log = await db.ApiKeyUsageLogs
            .SingleOrDefaultAsync(l => l.ApiKeyId == record.Id);

        Assert.NotNull(log);
        Assert.Equal(1, log!.RequestCount);
    }

    [Fact]
    public async Task RecordUsageAsync_CalledTwiceSameDay_RequestCountIsTwo()
    {
        var (svc, options) = CreateService();
        var (record, _) = await svc.CreateAsync("Upsert Key", "dev@example.com", 200);

        await svc.RecordUsageAsync(record.Id);
        await svc.RecordUsageAsync(record.Id);

        await using var db = new GridDbContext(options);
        var log = await db.ApiKeyUsageLogs
            .SingleOrDefaultAsync(l => l.ApiKeyId == record.Id);

        Assert.NotNull(log);
        Assert.Equal(2, log!.RequestCount);
    }
}
