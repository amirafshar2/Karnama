using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Clients;

/// <summary>
/// When package reminders go out (PROMPT2 §10.3), editable in the panel: days before the end, the last day itself,
/// "quota used up", and the end-of-contract password reminders (vault §10.2).
/// </summary>
public sealed record PackageNoticeSettings(List<int> DaysBefore, bool OnEndDay, bool QuotaUsedUp, bool ContractEndPasswords)
{
    public static PackageNoticeSettings Default => new([7, 3, 1], true, true, true);

    /// <summary>All thresholds in "calendar days left" (0 = today is the last day).</summary>
    public IReadOnlyCollection<int> Thresholds() => [.. DaysBefore.Where(d => d > 0).Distinct(), .. OnEndDay ? new[] { 0 } : []];
}

public sealed class PackageNoticeSettingsValidator : AbstractValidator<PackageNoticeSettings>
{
    public PackageNoticeSettingsValidator()
    {
        RuleFor(x => x.DaysBefore).NotNull().Must(d => d.Count <= 6).WithMessage("حداکثر ۶ یادآوری.");
        RuleForEach(x => x.DaysBefore).InclusiveBetween(1, 60).WithMessage("روز یادآوری باید بین ۱ تا ۶۰ باشد.");
    }
}

public sealed class PackageNoticeSettingsStore(IAppDbContext db, ICurrentUser me, TimeProvider clock, IAuditWriter audit)
{
    public const string Key = "packages.notices";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PackageNoticeSettings> GetAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null) return PackageNoticeSettings.Default;
        try { return JsonSerializer.Deserialize<PackageNoticeSettings>(row.Json, Json) ?? PackageNoticeSettings.Default; }
        catch (JsonException) { return PackageNoticeSettings.Default; }
    }

    public async Task<Result<PackageNoticeSettings>> SaveAsync(PackageNoticeSettings s, CancellationToken ct)
    {
        if (!me.Has(Permissions.PackagesManage, PermissionScope.All)) return Error.Forbidden();
        s = s with { DaysBefore = [.. (s.DaysBefore ?? []).Distinct().OrderDescending()] };
        var json = JsonSerializer.Serialize(s, Json);
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (row is null) db.SiteSettings.Add(new SiteSetting(Key, json));
        else row.Replace(json, me.UserId, clock.GetUtcNow());
        audit.Add("packages.noticesUpdated", nameof(SiteSetting), Key, s);
        await db.SaveChangesAsync(ct);
        return s;
    }
}
