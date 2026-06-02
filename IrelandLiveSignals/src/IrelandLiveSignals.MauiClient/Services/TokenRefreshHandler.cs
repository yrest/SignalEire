using System.Net;
using System.Net.Http.Headers;

namespace IrelandLiveSignals.MauiClient.Services;

public sealed class TokenRefreshHandler : DelegatingHandler
{
    private readonly IAuthService _authService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public TokenRefreshHandler(IAuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Attach access token from secure storage
        var token = await GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // 401 — attempt token refresh (serialize concurrent refresh attempts)
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Check if another concurrent request already refreshed the token
            var newToken = await GetAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(newToken) && newToken != token)
            {
                // Already refreshed — retry with new token
                return await RetryRequestAsync(request, newToken, cancellationToken);
            }

            var refreshed = await _authService.RefreshAsync();
            if (!refreshed)
            {
                _authService.TriggerLogout();
                return response;
            }

            var refreshedToken = await GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(refreshedToken))
            {
                _authService.TriggerLogout();
                return response;
            }

            return await RetryRequestAsync(request, refreshedToken, cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<HttpResponseMessage> RetryRequestAsync(
        HttpRequestMessage original,
        string token,
        CancellationToken cancellationToken)
    {
        // HttpRequestMessage is not reusable — clone it
        var clone = await CloneRequestAsync(original);
        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(clone, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        clone.Version = original.Version;

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync("access_token");
        }
        catch
        {
            return null;
        }
    }
}
