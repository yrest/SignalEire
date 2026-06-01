using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Infrastructure.EirGrid;
using IrelandLiveSignals.Infrastructure.Persistence;
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

        services.AddHttpClient<EirGridAdapter>();
        services.AddScoped<IGridDataAdapter, EirGridAdapter>();

        var connectionString = configuration.GetConnectionString("Sqlite") ?? "Data Source=data/signals.db";
        services.AddDbContext<GridDbContext>(opts => opts.UseSqlite(connectionString));
        services.AddScoped<IGridReadingRepository, GridReadingRepository>();

        return services;
    }
}
