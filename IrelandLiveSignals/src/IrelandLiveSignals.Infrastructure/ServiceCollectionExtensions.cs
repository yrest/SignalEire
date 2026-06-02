using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.EirGrid;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Transit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        var connectionString = configuration.GetConnectionString("SqlServer")
            ?? "Server=(localdb)\\mssqllocaldb;Database=SignalEire;Trusted_Connection=True;";

        services.AddDbContext<GridDbContext>(opts => opts.UseSqlServer(connectionString));
        services.AddDbContextFactory<GridDbContext>(opts => opts.UseSqlServer(connectionString),
            ServiceLifetime.Scoped);

        services.AddScoped<IGridReadingRepository, GridReadingRepository>();
        services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();
        services.AddScoped<ITransitRepository, TransitRepository>();

        services.AddHttpClient<NtaRealtimeAdapter>();
        services.AddScoped<NtaRealtimeAdapter>();
        services.AddScoped<GtfsStaticImporter>();

        return services;
    }
}
