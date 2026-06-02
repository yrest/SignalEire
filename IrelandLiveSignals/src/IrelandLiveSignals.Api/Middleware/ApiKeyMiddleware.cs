using IrelandLiveSignals.Core.Interfaces;

namespace IrelandLiveSignals.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (context.Request.Headers.TryGetValue("X-Api-Key", out var rawKey))
        {
            var apiKey = await apiKeyService.ValidateAsync(rawKey.ToString());
            if (apiKey == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Invalid or inactive API key.",
                    docs = "/developer"
                });
                return;
            }
            context.Items["ApiKey"] = apiKey;
            context.Items["RateLimitKey"] = $"key_{apiKey.Id}";
            _ = apiKeyService.RecordUsageAsync(apiKey.Id);
        }
        else
        {
            context.Items["RateLimitKey"] = $"ip_{context.Connection.RemoteIpAddress}";
        }

        await _next(context);
    }
}
