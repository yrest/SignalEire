using System.Text.Json;
using SQLite;

namespace IrelandLiveSignals.MauiClient.Services;

[Table("CacheEntries")]
public sealed class CacheEntry
{
    [PrimaryKey, Column("key")]
    public string Key { get; set; } = string.Empty;

    [Column("json")]
    public string Json { get; set; } = string.Empty;

    [Column("expiresAtUtc")]
    public string ExpiresAtUtc { get; set; } = string.Empty;
}

public sealed class LocalCacheService : ILocalCacheService, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private bool _initialised;

    public LocalCacheService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "signals_cache.db");
        _db = new SQLiteAsyncConnection(dbPath,
            SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
    }

    private async Task EnsureInitialisedAsync()
    {
        if (_initialised)
            return;
        await _db.CreateTableAsync<CacheEntry>();
        _initialised = true;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        await EnsureInitialisedAsync();

        var json = JsonSerializer.Serialize(value);
        var entry = new CacheEntry
        {
            Key = key,
            Json = json,
            ExpiresAtUtc = (DateTime.UtcNow + ttl).ToString("O")
        };

        await _db.InsertOrReplaceAsync(entry);
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        await EnsureInitialisedAsync();

        var entry = await _db.FindAsync<CacheEntry>(key);
        if (entry is null)
            return null;

        if (!DateTime.TryParse(entry.ExpiresAtUtc, out var expiry) || DateTime.UtcNow >= expiry)
        {
            await _db.DeleteAsync(entry);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(entry.Json);
        }
        catch
        {
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        await EnsureInitialisedAsync();
        await _db.DeleteAsync<CacheEntry>(key);
    }

    public async Task ClearAsync()
    {
        await EnsureInitialisedAsync();
        await _db.DeleteAllAsync<CacheEntry>();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.CloseAsync();
    }
}
