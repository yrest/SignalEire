using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Identity;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Api.Worker;

public class DigestJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DigestJob> _logger;

    private static readonly TimeZoneInfo IrelandTz = GetIrelandTimeZone();

    // Track last digest sent date per user
    private readonly Dictionary<string, DateOnly> _lastDigestDate = new();

    public DigestJob(IServiceScopeFactory scopeFactory, ILogger<DigestJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DigestJob started.");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunDigestAsync(stoppingToken);
        }
    }

    internal async Task RunDigestAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();

            var irelandNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IrelandTz);
            var today = DateOnly.FromDateTime(irelandNow);
            var currentTime = TimeOnly.FromDateTime(irelandNow);

            var users = await db.Users
                .OfType<ApplicationUser>()
                .Where(u => u.DigestEnabled)
                .ToListAsync(ct);

            foreach (var user in users)
            {
                // Check if it's time to send digest
                if (currentTime < user.DigestTime) continue;

                // Check if already sent today
                if (_lastDigestDate.TryGetValue(user.Id, out var lastDate) && lastDate == today) continue;

                await SendDigestForUserAsync(db, user, today, ct);
                _lastDigestDate[user.Id] = today;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DigestJob failed.");
        }
    }

    private async Task SendDigestForUserAsync(GridDbContext db, ApplicationUser user,
        DateOnly today, CancellationToken ct)
    {
        // Find last digest date for this user to determine cutoff
        var lastDate = _lastDigestDate.TryGetValue(user.Id, out var d)
            ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTimeOffset.UtcNow.AddDays(-1).UtcDateTime;

        var since = new DateTimeOffset(lastDate, TimeSpan.Zero);

        // Get unfired alert firings since last digest
        var firings = await db.AlertFirings
            .Where(f => f.UserId == user.Id && !f.IncludedInDigest && f.FiredAtUtc >= since)
            .OrderBy(f => f.FiredAtUtc)
            .ToListAsync(ct);

        // Get recent anomalies
        var cutoffDate = DateOnly.FromDateTime(lastDate);
        var anomalies = await db.SignalAnomalies
            .Where(a => a.Date >= cutoffDate)
            .OrderByDescending(a => a.DetectedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (firings.Count == 0 && anomalies.Count == 0)
        {
            _logger.LogDebug("No digest content for user {UserId} on {Date}.", user.Id, today);
            return;
        }

        var email = BuildDigestEmail(user, today, firings, anomalies);

        // Log the digest (in production would send via email provider)
        _logger.LogInformation(
            "Digest for user {UserId} ({Email}) on {Date}: {Firings} alerts, {Anomalies} anomalies.{NewLine}Subject: {Subject}",
            user.Id, user.Email, today, firings.Count, anomalies.Count, Environment.NewLine, email.Subject);

        // Mark firings as included in digest
        foreach (var firing in firings)
        {
            firing.IncludedInDigest = true;
        }
        await db.SaveChangesAsync(ct);
    }

    private static (string Subject, string Body) BuildDigestEmail(
        ApplicationUser user, DateOnly date,
        List<AlertFiring> firings, List<SignalAnomaly> anomalies)
    {
        var sb = new System.Text.StringBuilder();
        var subject = $"Your SignalEire daily summary — {date:dd MMM yyyy}";

        if (anomalies.Count > 0)
        {
            sb.AppendLine("UNUSUAL PATTERNS DETECTED");
            sb.AppendLine("──────────────────────────────────────────");
            foreach (var anomaly in anomalies.Take(3))
            {
                sb.AppendLine($"• {anomaly.ExplanationText}");
            }
            sb.AppendLine();
        }

        var gridFirings = firings.Where(f => f.Co2Value.HasValue).ToList();
        var transitFirings = firings.Where(f => !f.Co2Value.HasValue && f.RenewablesPercent == null).ToList();

        sb.AppendLine("GRID ALERTS");
        sb.AppendLine("──────────────────────────────────────────");
        if (gridFirings.Count > 0)
        {
            var byRule = gridFirings.GroupBy(f => f.AlertRuleId);
            foreach (var ruleGroup in byRule)
            {
                sb.AppendLine($"\"{ruleGroup.Key}\" fired {ruleGroup.Count()} time(s):");
                foreach (var f in ruleGroup)
                {
                    var co2 = f.Co2Value.HasValue ? $"CO₂ at {f.Co2Value:F0} g/kWh" : "";
                    var ren = f.RenewablesPercent.HasValue ? $", renewables {f.RenewablesPercent:F0}%" : "";
                    sb.AppendLine($"  • {f.FiredAtUtc:HH:mm} — {co2}{ren}.");
                }
            }
        }
        else
        {
            sb.AppendLine("No grid alerts fired.");
        }
        sb.AppendLine();

        sb.AppendLine("TRANSIT ALERTS");
        sb.AppendLine("──────────────────────────────────────────");
        if (transitFirings.Count > 0)
        {
            var byRule = transitFirings.GroupBy(f => f.AlertRuleId);
            foreach (var ruleGroup in byRule)
            {
                sb.AppendLine($"\"{ruleGroup.Key}\" fired {ruleGroup.Count()} time(s).");
            }
        }
        else
        {
            sb.AppendLine("No transit alerts fired.");
        }
        sb.AppendLine();

        sb.AppendLine("──────────────────────────────────────────");
        sb.AppendLine("Manage your alerts: https://yourdomain.ie/alerts");

        return (subject, sb.ToString());
    }

    private static TimeZoneInfo GetIrelandTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
    }
}
