using Karnama.Domain.Attendance;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo-only: an office fingerprint device (push) and a "file import" device, with the demo team's PINs 1–6.</summary>
internal static class DemoDeviceSeed
{
    public const string DemoSerial = "DEMO0001";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.BiometricDevices.AnyAsync(ct)) return;
        db.BiometricDevices.Add(BiometricDevice.Create("دستگاه اثر انگشت دفتر (نمونه)", DemoSerial, DeviceKind.Adms, PunchMode.Toggle, "کنار در ورودی", []).Value!);
        db.BiometricDevices.Add(BiometricDevice.Create("فایل نرم‌افزار حضور و غیاب (نمونه)", "", DeviceKind.FileImport, PunchMode.Toggle, null, []).Value!);
        var users = await db.Users.AsNoTracking().Where(u => u.IsDemo && u.Kind == Karnama.Domain.Identity.UserKind.Staff).ToListAsync(ct);
        string[] order = ["علی", "مریم", "رضا", "نگار", "نیما"];
        var pin = 0;
        foreach (var prefix in order)
            if (users.FirstOrDefault(u => u.DisplayName.StartsWith(prefix, StringComparison.Ordinal)) is { } u)
                db.DevicePins.Add(DevicePin.Create((++pin).ToString(System.Globalization.CultureInfo.InvariantCulture), u.Id).Value!);
        await db.SaveChangesAsync(ct);
    }
}
