using IrelandLiveSignals.MauiClient.Models;

namespace IrelandLiveSignals.MauiClient.Services;

public sealed class AuthService : IAuthService
{
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string TokenExpiryKey = "token_expiry";
    private const string UserIdKey = "user_id";
    private const string DisplayNameKey = "display_name";

    private readonly ISignalEireApiClient _apiClient;

    public event EventHandler? AuthStateChanged;

    public AuthService(ISignalEireApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public bool IsAuthenticated
    {
        get
        {
            try
            {
                var token = SecureStorage.Default.GetAsync(AccessTokenKey).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(token))
                    return false;

                var expiryStr = Preferences.Default.Get(TokenExpiryKey, string.Empty);
                if (string.IsNullOrWhiteSpace(expiryStr))
                    return false;

                if (DateTime.TryParse(expiryStr, out var expiry))
                    return DateTime.UtcNow < expiry;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? UserId => Preferences.Default.Get(UserIdKey, (string?)null);

    public string? DisplayName => Preferences.Default.Get(DisplayNameKey, (string?)null);

    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _apiClient.LoginAsync(email, password, DeviceInfo.Current.Name);
            if (response is null)
                return false;

            await PersistTokensAsync(response);
            OnAuthStateChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RefreshAsync()
    {
        try
        {
            var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var response = await _apiClient.RefreshTokenAsync(refreshToken);
            if (response is null)
            {
                TriggerLogout();
                return false;
            }

            await PersistTokensAsync(response);
            OnAuthStateChanged();
            return true;
        }
        catch
        {
            TriggerLogout();
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            var platform = DeviceInfo.Current.Platform.ToString();
            await _apiClient.LogoutAsync(refreshToken, platform);
        }
        catch
        {
            // swallow — we still clear local state
        }
        finally
        {
            ClearLocalState();
            OnAuthStateChanged();
        }
    }

    public void TriggerLogout()
    {
        ClearLocalState();
        OnAuthStateChanged();
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(AccessTokenKey);
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistTokensAsync(LoginResponse response)
    {
        await SecureStorage.Default.SetAsync(AccessTokenKey, response.AccessToken);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, response.RefreshToken);
        Preferences.Default.Set(TokenExpiryKey, response.ExpiresAt.ToString("O"));
        Preferences.Default.Set(UserIdKey, response.UserId);
        Preferences.Default.Set(DisplayNameKey, response.DisplayName);
    }

    private void ClearLocalState()
    {
        try
        {
            SecureStorage.Default.Remove(AccessTokenKey);
            SecureStorage.Default.Remove(RefreshTokenKey);
        }
        catch { /* ignore */ }

        Preferences.Default.Remove(TokenExpiryKey);
        Preferences.Default.Remove(UserIdKey);
        Preferences.Default.Remove(DisplayNameKey);
    }

    private void OnAuthStateChanged() =>
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
}
