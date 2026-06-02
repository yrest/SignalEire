using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Pages.Admin;

public class AnomaliesModel : PageModel
{
    private readonly GridDbContext _db;

    public IReadOnlyList<SignalAnomaly> Anomalies { get; private set; } = Array.Empty<SignalAnomaly>();

    public AnomaliesModel(GridDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        Anomalies = await _db.SignalAnomalies
            .Where(a => a.Date >= cutoff)
            .OrderByDescending(a => a.DetectedAtUtc)
            .ToListAsync();
    }
}
