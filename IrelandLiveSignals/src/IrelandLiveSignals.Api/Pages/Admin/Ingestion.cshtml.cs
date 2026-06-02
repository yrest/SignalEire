using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Pages.Admin;

public class IngestionModel : PageModel
{
    private readonly GridDbContext _db;

    public int GridReadingsLast24h { get; private set; }
    public int VehicleObservationsLast24h { get; private set; }
    public int ServiceAlertsLast24h { get; private set; }
    public int AlertDeliveriesLast24h { get; private set; }

    public IngestionModel(GridDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);

        GridReadingsLast24h = await _db.GridReadings
            .CountAsync(r => r.TimestampUtc >= cutoff);

        VehicleObservationsLast24h = await _db.VehicleObservations
            .CountAsync(v => v.ObservedAtUtc >= cutoff);

        ServiceAlertsLast24h = await _db.ServiceAlerts
            .CountAsync(a => a.FetchedAtUtc >= cutoff);

        AlertDeliveriesLast24h = await _db.AlertDeliveries
            .CountAsync(d => d.FiredAtUtc >= cutoff);
    }
}
