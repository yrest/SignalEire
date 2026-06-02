namespace IrelandLiveSignals.MauiClient.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password);
    Task<bool> RefreshAsync();
    Task LogoutAsync();
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? DisplayName { get; }
    event EventHandler AuthStateChanged;
    void TriggerLogout();
}
