using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Attendance;

/// <summary>Agency-wide attendance settings (one JSON row in site_settings).</summary>
public sealed record AttendanceSettingsDto(
    List<string> OfficeNetworks, bool AutoClockInOnTimer, int OfflineToleranceMinutes, int ForgottenAfterHours,
    int SelfieRetentionDays, int LocationRetentionDays, bool AskEarlyLeaveReason, int AbsenceReasonHours = 48)
{
    public static AttendanceSettingsDto Default => new([], false, 10, 4, 30, 90, true);
}

public static class Iran
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateTimeOffset Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);
    public static DateOnly Today(DateTimeOffset utc) => DateOnly.FromDateTime(Local(utc).DateTime);
    public static DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>1404/08/05 with Persian digits, for texts that leave the app (notifications, history notes).</summary>
    public static string Jalali(DateOnly day)
    {
        var cal = new System.Globalization.PersianCalendar();
        var d = day.ToDateTime(TimeOnly.MinValue);
        var s = $"{cal.GetYear(d)}/{cal.GetMonth(d):00}/{cal.GetDayOfMonth(d):00}";
        return string.Concat(s.Select(ch => ch is >= '0' and <= '9' ? (char)('۰' + (ch - '0')) : ch));
    }
}

public sealed class AttendanceSettingsStore(IAppDbContext db, ISecretProtector protector)
{
    public const string Key = "attendance";
    private const string KioskKeyName = "attendance.kioskKey";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AttendanceSettingsDto> GetAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        return row is null ? AttendanceSettingsDto.Default : JsonSerializer.Deserialize<AttendanceSettingsDto>(row.Json, Json) ?? AttendanceSettingsDto.Default;
    }

    public async Task SaveAsync(AttendanceSettingsDto s, Guid? actor, DateTimeOffset now, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(s, Json);
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (row is null) db.SiteSettings.Add(new SiteSetting(Key, json));
        else row.Replace(json, actor, now);
    }

    /// <summary>HMAC key of the office QR kiosk: random, created on first use, stored encrypted.</summary>
    public async Task<byte[]> KioskKeyAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == KioskKeyName, ct);
        if (row is not null) return Convert.FromBase64String(protector.Unprotect(JsonSerializer.Deserialize<string>(row.Json)!));
        var key = RandomNumberGenerator.GetBytes(32);
        db.SiteSettings.Add(new SiteSetting(KioskKeyName, JsonSerializer.Serialize(protector.Protect(Convert.ToBase64String(key)))));
        await db.SaveChangesAsync(ct);
        return key;
    }

    /// <summary>"1.2.3.4" or "5.6.7.0/24" (IPv4 or IPv6).</summary>
    public static bool InNetworks(IPAddress? ip, IEnumerable<string> networks)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        foreach (var n in networks)
        {
            if (IPNetwork.TryParse(n.Contains('/') ? n : $"{n}/{(n.Contains(':') ? 128 : 32)}", out var net) && net.Contains(ip)) return true;
        }
        return false;
    }
}
