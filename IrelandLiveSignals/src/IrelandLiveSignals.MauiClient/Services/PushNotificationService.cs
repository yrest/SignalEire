using Plugin.Firebase.CloudMessaging;

namespace IrelandLiveSignals.MauiClient.Services;

public static class PushNotificationService
{
    public static async Task InitialiseAsync(ISignalEireApiClient apiClient)
    {
        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var platform = DeviceInfo.Current.Platform == DevicePlatform.Android
                    ? "android"
                    : "ios";
                await apiClient.RegisterDeviceTokenAsync(token, platform);
            }

            CrossFirebaseCloudMessaging.Current.TokenChanged += async (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Token))
                {
                    var platform = DeviceInfo.Current.Platform == DevicePlatform.Android
                        ? "android"
                        : "ios";
                    await apiClient.RegisterDeviceTokenAsync(e.Token, platform);
                }
            };
        }
        catch
        {
            // Push notifications are non-critical — swallow initialisation failures
        }
    }
}
