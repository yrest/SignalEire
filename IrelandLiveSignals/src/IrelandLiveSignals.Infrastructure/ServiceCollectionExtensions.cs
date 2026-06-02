using System.Text;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.EirGrid;
using IrelandLiveSignals.Infrastructure.Identity;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Push;
using IrelandLiveSignals.Infrastructure.Qdrant;
using IrelandLiveSignals.Infrastructure.Transit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IrelandLiveSignals.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EirGridOptions>(opts =>
        {
            opts.BaseUrl = configuration["GridPoller:EirGridBaseUrl"] ?? opts.BaseUrl;
            opts.Region = configuration["GridPoller:Region"] ?? opts.Region;
            opts.RawSnapshotPath = configuration["RawSnapshotPath"] ?? opts.RawSnapshotPath;
        });

        services.Configure<NtaTransitOptions>(opts =>
        {
            var section = configuration.GetSection("NtaApi");
            opts.BaseUrl = section["BaseUrl"] ?? opts.BaseUrl;
            opts.ApiKey = section["ApiKey"] ?? Environment.GetEnvironmentVariable("NTA_API_KEY") ?? string.Empty;
            opts.ApiKeyHeader = section["ApiKeyHeader"] ?? opts.ApiKeyHeader;
            opts.TripUpdatesPath = section["TripUpdatesPath"] ?? opts.TripUpdatesPath;
            opts.VehiclesPath = section["VehiclesPath"] ?? opts.VehiclesPath;
            opts.AlertsPath = section["AlertsPath"] ?? opts.AlertsPath;
            opts.GtfsStaticUrl = section["GtfsStaticUrl"] ?? opts.GtfsStaticUrl;
            if (int.TryParse(section["VehiclesPollIntervalSeconds"], out var v)) opts.VehiclesPollIntervalSeconds = v;
            if (int.TryParse(section["TripUpdatesPollIntervalSeconds"], out var t)) opts.TripUpdatesPollIntervalSeconds = t;
            if (int.TryParse(section["AlertsPollIntervalSeconds"], out var a)) opts.AlertsPollIntervalSeconds = a;
            if (int.TryParse(section["GtfsStaticRefreshDays"], out var d)) opts.GtfsStaticRefreshDays = d;
            if (int.TryParse(section["FeedStaggerSeconds"], out var s)) opts.FeedStaggerSeconds = s;
        });

        services.AddHttpClient<EirGridAdapter>();
        services.AddScoped<IGridDataAdapter, EirGridAdapter>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("SqlServer")
            ?? "Data Source=signaleire.db";

        services.AddDbContext<GridDbContext>(opts => opts.UseSqlite(connectionString));
        services.AddDbContextFactory<GridDbContext>(opts => opts.UseSqlite(connectionString),
            ServiceLifetime.Scoped);

        services.AddScoped<IGridReadingRepository, GridReadingRepository>();
        services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();
        services.AddScoped<ITransitRepository, TransitRepository>();

        services.AddHttpClient<NtaRealtimeAdapter>();
        services.AddScoped<NtaRealtimeAdapter>();
        services.AddScoped<GtfsStaticImporter>();

        services.AddSingleton<IQdrantSummaryIndexer, NullQdrantSummaryIndexer>();

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.SignIn.RequireConfirmedEmail = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddEntityFrameworkStores<GridDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IPushNotificationService, VapidPushNotificationService>();

        var firebasePath = configuration["Firebase:ServiceAccountPath"];
        if (!string.IsNullOrEmpty(firebasePath) && File.Exists(firebasePath))
        {
            services.AddScoped<IMobilePushService, FirebaseMobilePushService>();
        }
        else
        {
            services.AddScoped<IMobilePushService, NullMobilePushService>();
        }

        return services;
    }

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrEmpty(secret))
        {
            // JWT not configured — skip (app still works with cookie auth)
            return services;
        }

        services.AddAuthentication()
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = configuration["Jwt:Issuer"],
                    ValidAudience            = configuration["Jwt:Audience"],
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(secret)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        return services;
    }
}
