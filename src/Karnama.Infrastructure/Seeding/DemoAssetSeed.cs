using Karnama.Application.Attendance;
using Karnama.Domain.Assets;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo-only equipment (PROMPT2 §3): some on the shelf, some with Lukas and Marie (one overdue, one waiting for acceptance), one in repair.</summary>
internal static class DemoAssetSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Assets.IgnoreQueryFilters().AnyAsync(ct)) return;
        var users = await db.Users.AsNoTracking().Where(u => u.IsDemo).ToListAsync(ct);
        Guid? Find(string prefix) => users.FirstOrDefault(u => u.DisplayName.StartsWith(prefix, StringComparison.Ordinal))?.Id;
        var admin = Find("Sarah");
        var ali = Find("Lukas");
        var maryam = Find("Marie");
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

        var cam = Add("Kamera Sony A7 IV", AssetDefaults.Camera, "Sony", "ILCE-7M4", "SN-4471902", 400, 2499, 20, "Studio-Hauptkamera");
        var lens = Add("Objektiv Sony 24-70", AssetDefaults.Lens, "Sony", "SEL2470GM2", "SN-1180342", 400, 2199, 300);
        var laptop = Add("Laptop MacBook Pro 14", AssetDefaults.Laptop, "Apple", "M3 Pro", "C02XK1Y2", 200, 2399, 165, "Zum Schnitt");
        var mic = Add("Ansteckmikrofon Rode", AssetDefaults.Mic, "RØDE", "Wireless GO II", "RD-55821", 300, 299, 65);
        var light = Add("LED-Licht Godox", AssetDefaults.Light, "Godox", "SL60W", "GD-77412", 500, 199, null);
        var gimbal = Add("Gimbal DJI", AssetDefaults.Tripod, "DJI", "RS 3 Mini", "DJ-3M0912", 250, 369, 115);
        Add("Externe Festplatte 2 TB", AssetDefaults.Storage, "WD", "My Passport", "WX12A3456", 150, 89, 580);
        var tripod = Add("Stativ Manfrotto", AssetDefaults.Tripod, "Manfrotto", "MT190X", "MF-0019", 700, 189, null);

        Give(laptop, ali.Value, 40, null, "Unbeschädigt, ein feiner Kratzer am Deckel", ["Ladegerät", "Tasche"], true);
        Give(mic, ali.Value, 12, -2, "Unbeschädigt", ["Zwei Absender", "USB-C-Kabel"], true);       // overdue: was due 2 days ago
        Give(cam, maryam.Value, 6, 10, "Unbeschädigt", ["Akku ×2", "Gurt"], true);
        Give(gimbal, maryam.Value, 0, 7, "Unbeschädigt, in der Tasche", ["Tasche"], false);               // waiting for Marie to accept
        tripod.SetStatus(AssetStatus.InRepair);
        db.AssetEvents.Add(AssetEvent.Log(tripod.Id, admin, AssetEventKind.StatusChanged, now.AddDays(-3), note: "Im Lager → In Reparatur – Stativverschluss locker"));
        _ = lens; _ = light;
        await db.SaveChangesAsync(ct);
    }
}
