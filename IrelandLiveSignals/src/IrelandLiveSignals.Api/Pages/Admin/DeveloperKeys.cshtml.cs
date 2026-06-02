using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IrelandLiveSignals.Api.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DeveloperKeysModel : PageModel
{
    private readonly IApiKeyService _apiKeyService;
    public IReadOnlyList<DeveloperApiKey> Keys { get; private set; } = [];
    public string? NewKeyPlaintext { get; private set; }
    [BindProperty] public DevKeyInput NewKey { get; set; } = new();

    public DeveloperKeysModel(IApiKeyService apiKeyService) { _apiKeyService = apiKeyService; }

    public async Task OnGetAsync() { Keys = await _apiKeyService.GetAllAsync(); }

    public async Task<IActionResult> OnPostAsync()
    {
        var (_, plaintext) = await _apiKeyService.CreateAsync(
            NewKey.Name, NewKey.OwnerEmail, NewKey.RateLimitPerMinute ?? 200);
        NewKeyPlaintext = plaintext;
        Keys = await _apiKeyService.GetAllAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        await _apiKeyService.DeleteAsync(id);
        return RedirectToPage();
    }
}

public class DevKeyInput
{
    public string Name { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int? RateLimitPerMinute { get; set; }
}
