using IrelandLiveSignals.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IrelandLiveSignals.Api.Pages.Account;

[Authorize]
public class ManageModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ManageModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public bool PushNotificationsEnabled { get; set; }
    [BindProperty] public bool DigestEnabled { get; set; }
    [BindProperty] public string DigestTime { get; set; } = "08:00";
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return;

        DisplayName = user.DisplayName;
        PushNotificationsEnabled = user.PushNotificationsEnabled;
        DigestEnabled = user.DigestEnabled;
        DigestTime = user.DigestTime.ToString("HH:mm");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Forbid();

        user.DisplayName = DisplayName;
        user.PushNotificationsEnabled = PushNotificationsEnabled;
        user.DigestEnabled = DigestEnabled;
        if (TimeOnly.TryParse(DigestTime, out var t))
            user.DigestTime = t;

        await _userManager.UpdateAsync(user);
        StatusMessage = "Settings saved.";
        return Page();
    }
}
