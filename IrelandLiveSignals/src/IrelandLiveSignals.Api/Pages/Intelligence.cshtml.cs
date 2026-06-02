using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Pages;

public class IntelligenceModel : PageModel
{
    private readonly GridDbContext _db;
    private readonly LiveSignalState _liveState;

    public IReadOnlyList<SignalAnomaly> Anomalies { get; private set; } = Array.Empty<SignalAnomaly>();
    public double LatestCo2 => _liveState.LatestCo2GPerKwh;
    public double LatestRenewables { get; private set; }
    public int ActiveVehicles => _liveState.ActiveVehicleCount;

    public IntelligenceModel(GridDbContext db, LiveSignalState liveState)
    {
        _db = db;
        _liveState = liveState;
    }

    public async Task OnGetAsync()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        Anomalies = await _db.SignalAnomalies
            .Where(a => a.Date >= cutoff)
            .OrderByDescending(a => a.DetectedAtUtc)
            .ToListAsync();

        var latestReading = await _db.GridReadings
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync();
        LatestRenewables = latestReading?.RenewablesPercent ?? 0;
    }
}
