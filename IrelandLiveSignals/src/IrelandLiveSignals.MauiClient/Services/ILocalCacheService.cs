namespace IrelandLiveSignals.MauiClient.Services;

public interface ILocalCacheService
{
    Task SetAsync<T>(string key, T value, TimeSpan ttl);
    Task<T?> GetAsync<T>(string key) where T : class;
    Task RemoveAsync(string key);
    Task ClearAsync();
}
