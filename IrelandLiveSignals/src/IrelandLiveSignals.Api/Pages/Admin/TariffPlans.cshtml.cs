using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TariffPlansModel : PageModel
{
    private readonly GridDbContext _db;
    public List<TariffPlanEntity> Plans { get; private set; } = [];
    [BindProperty] public TariffPlanCreateInput NewPlan { get; set; } = new();

    public TariffPlansModel(GridDbContext db) { _db = db; }

    public async Task OnGetAsync()
    {
        Plans = await _db.TariffPlans.Include(p => p.Periods).ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var plan = new TariffPlanEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = NewPlan.Name,
            Provider = NewPlan.Provider ?? "Generic",
            PlanType = NewPlan.PlanType ?? "custom",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _db.TariffPlans.Add(plan);
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var plan = await _db.TariffPlans.Include(p => p.Periods).FirstOrDefaultAsync(p => p.Id == id);
        if (plan is not null) { _db.TariffPlans.Remove(plan); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}

public class TariffPlanCreateInput
{
    public string Name { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? PlanType { get; set; }
}
