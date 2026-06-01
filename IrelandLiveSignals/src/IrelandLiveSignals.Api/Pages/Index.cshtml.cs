using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IrelandLiveSignals.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IGridReadingRepository _repo;

    public GridReading? Reading { get; private set; }

    public IndexModel(IGridReadingRepository repo) => _repo = repo;

    public async Task OnGetAsync() =>
        Reading = await _repo.GetLatestAsync();
}
