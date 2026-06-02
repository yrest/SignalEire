using Microsoft.AspNetCore.Identity;

namespace IrelandLiveSignals.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string PreferredRegion { get; set; } = "ROI";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool PushNotificationsEnabled { get; set; } = false;
    // Phase 6 digest fields:
    public bool DigestEnabled { get; set; } = false;
    public TimeOnly DigestTime { get; set; } = new(08, 00);
    public string DigestSchedule { get; set; } = "daily";

    // Phase 8 — tariff preference
    public string? PreferredTariffPlanId { get; set; }
}
