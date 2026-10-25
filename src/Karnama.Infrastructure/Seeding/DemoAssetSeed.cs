using Karnama.Application.Attendance;
using Karnama.Domain.Assets;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo-only equipment (PROMPT2 §3): some on the shelf, some with علی and مریم (one overdue, one waiting for acceptance), one in repair.</summary>
internal static class DemoAssetSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Assets.IgnoreQueryFilters().AnyAsync(ct)) return;
        var users = await db.Users.AsNoTracking().Where(u => u.IsDemo).ToListAsync(ct);
        Guid? Find(string prefix) => users.FirstOrDefault(u => u.DisplayName.StartsWith(prefix, StringComparison.Ordinal))?.Id;
        var admin = Find("سارا");
        var ali = Find("علی");
        var maryam = Find("مریم");
        if (admin is null || ali is null || maryam is null) return;

        var now = DateTimeOffset.UtcNow;
        var today = Iran.Today(now);
        var n = 0;
        Asset Add(string name, Guid cat, string brand, string model, string serial, int boughtDaysAgo, long price, int? warrantyDays, string? note = null)
        {
            var a = Asset.Create(new AssetSpec(name, cat, brand, model, serial, $"KN-{++n:0000}", today.AddDays(-boughtDaysAgo), price,
                warrantyDays is { } w ? today.AddDays(w) : null, note), $"KN-{n:0000}").Value!;
            db.Assets.Add(a);
            db.AssetEvents.Add(AssetEvent.Log(a.Id, admin, AssetEventKind.Created, now.AddDays(-boughtDaysAgo)));
            return a;
        }
        void Give(Asset a, Guid to, int daysAgo, int? returnInDays, string condition, string[] accessories, bool accepted)
        {
            var at = now.AddDays(-daysAgo);
            var x = AssetAssignment.HandOver(a, to, admin.Value, returnInDays is { } r ? today.AddDays(r) : null, condition, accessories, at, accepted).Value!;
            db.AssetAssignments.Add(x);
            db.AssetEvents.Add(AssetEvent.Log(a.Id, admin, AssetEventKind.HandedOver, at, x.Id, to, condition));
            if (accepted) db.AssetEvents.Add(AssetEvent.Log(a.Id, to, AssetEventKind.Accepted, at.AddMinutes(12), x.Id, to));
        }

        var cam = Add("دوربین سونی A7 IV", AssetDefaults.Camera, "Sony", "ILCE-7M4", "SN-4471902", 400, 1_650_000_000, 20, "بدنه‌ی اصلی استودیو");
        var lens = Add("لنز ۲۴-۷۰ سونی", AssetDefaults.Lens, "Sony", "SEL2470GM2", "SN-1180342", 400, 980_000_000, 300);
        var laptop = Add("لپ‌تاپ مک‌بوک پرو ۱۴", AssetDefaults.Laptop, "Apple", "M3 Pro", "C02XK1Y2", 200, 1_200_000_000, 165, "برای تدوین");
        var mic = Add("میکروفون یقه‌ای رود", AssetDefaults.Mic, "RØDE", "Wireless GO II", "RD-55821", 300, 145_000_000, 65);
        var light = Add("نور LED گودکس", AssetDefaults.Light, "Godox", "SL60W", "GD-77412", 500, 38_000_000, null);
        var gimbal = Add("گیمبال دی‌جی‌آی", AssetDefaults.Tripod, "DJI", "RS 3 Mini", "DJ-3M0912", 250, 92_000_000, 115);
        Add("هارد اکسترنال ۲ ترابایت", AssetDefaults.Storage, "WD", "My Passport", "WX12A3456", 150, 21_000_000, 580);
        var tripod = Add("سه‌پایه منفروتو", AssetDefaults.Tripod, "Manfrotto", "MT190X", "MF-0019", 700, 18_000_000, null);

        Give(laptop, ali.Value, 40, null, "سالم، یک خط ریز روی درب", ["شارژر", "کیف"], true);
        Give(mic, ali.Value, 12, -2, "سالم", ["دو فرستنده", "کابل USB-C"], true);       // overdue: was due 2 days ago
        Give(cam, maryam.Value, 6, 10, "سالم", ["باتری ×۲", "بند"], true);
        Give(gimbal, maryam.Value, 0, 7, "سالم، داخل کیف", ["کیف"], false);               // waiting for مریم to accept
        tripod.SetStatus(AssetStatus.InRepair);
        db.AssetEvents.Add(AssetEvent.Log(tripod.Id, admin, AssetEventKind.StatusChanged, now.AddDays(-3), note: "در انبار → در تعمیر — قفل پایه شل است"));
        _ = lens; _ = light;
        await db.SaveChangesAsync(ct);
    }
}
