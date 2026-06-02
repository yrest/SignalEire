using IrelandLiveSignals.Core.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IrelandLiveSignals.Api.Pages.Admin;

public class SourcesModel : PageModel
{
    private readonly FeedHealthStore _feedHealth;

    public IReadOnlyList<FeedHealthEntry> Entries { get; private set; } = Array.Empty<FeedHealthEntry>();

    public SourcesModel(FeedHealthStore feedHealth)
    {
        _feedHealth = feedHealth;
    }

    public void OnGet()
    {
        Entries = _feedHealth.GetAll().OrderBy(e => e.Source).ToList();
    }
}
