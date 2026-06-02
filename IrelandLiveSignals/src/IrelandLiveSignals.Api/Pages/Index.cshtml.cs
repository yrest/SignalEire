using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IrelandLiveSignals.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IGridReadingRepository _gridRepo;
    private readonly ITransitRepository _transitRepo;

    public GridReading? Reading { get; private set; }
    public IReadOnlyList<TransitReliabilityAggregate> TopRoutes { get; private set; } = [];
    public int ActiveAlertCount { get; private set; }
    public int UserReportCount { get; private set; }

    public IndexModel(IGridReadingRepository gridRepo, ITransitRepository transitRepo)
    {
        _gridRepo = gridRepo;
        _transitRepo = transitRepo;
    }

    public async Task OnGetAsync()
    {
        Reading = await _gridRepo.GetLatestAsync();
        TopRoutes = await _transitRepo.GetTopReliableRoutesAsync(5);
        var alerts = await _transitRepo.GetActiveAlertsAsync();
        ActiveAlertCount = alerts.Count;
        var reports = await _transitRepo.GetUserReportsAsync(null, null, DateTimeOffset.UtcNow.AddDays(-7));
        UserReportCount = reports.Count;
    }
}
