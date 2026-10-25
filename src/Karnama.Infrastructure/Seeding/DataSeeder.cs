using Karnama.Application.Auth;
using Karnama.Application.Common;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Localization;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Idempotent seed. Always: languages and system roles (owner grants are re-synced with the permission catalog).
/// Demo mode only: sample roles and demo accounts (IsDemo = true). Every text is sample data, editable from the panel.
/// </summary>
public sealed class DataSeeder(AppDbContext db, IOptions<SeedOptions> options, ILogger<DataSeeder> logger)
{
    public const string OwnerDemoPhone = "09000000000";

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedLanguagesAsync(ct);
        var (owner, client) = await SeedSystemRolesAsync(ct);
        await SeedPlatformsAsync(ct);
        await db.SaveChangesAsync(ct);

        if (options.Value.Mode == SeedMode.Demo)
        {
            await SeedDemoAsync(owner, client, ct);
            await db.SaveChangesAsync(ct);
            await DemoCatalogSeed.SeedAsync(db, ct);
            await DemoWorkSeed.SeedAsync(db, ct);
        }
        logger.LogInformation("Seed completed (mode: {Mode})", options.Value.Mode);
    }

    private async Task SeedLanguagesAsync(CancellationToken ct)
    {
        if (await db.Languages.AnyAsync(ct)) return;
        db.Languages.Add(Language.Create("fa", "فارسی", TextDirection.Rtl, CalendarKind.Persian, DigitStyle.Persian, isDefault: true, sortOrder: 0).Value);
        db.Languages.Add(Language.Create("en", "English", TextDirection.Ltr, CalendarKind.Gregorian, DigitStyle.Latin, sortOrder: 1).Value);
    }

    /// <summary>Common Iranian networks, editable from the panel (also in empty installs).</summary>
    private async Task SeedPlatformsAsync(CancellationToken ct)
    {
        if (await db.Platforms.IgnoreQueryFilters().AnyAsync(ct)) return;
        (string Key, string Fa, string En, string? Url)[] list =
        [
            ("instagram", "اینستاگرام", "Instagram", "https://instagram.com/{handle}"),
            ("telegram", "تلگرام", "Telegram", "https://t.me/{handle}"),
            ("eitaa", "ایتا", "Eitaa", "https://eitaa.com/{handle}"),
            ("bale", "بله", "Bale", "https://ble.ir/{handle}"),
            ("rubika", "روبیکا", "Rubika", "https://rubika.ir/{handle}"),
        ];
        for (var i = 0; i < list.Length; i++)
            db.Platforms.Add(Karnama.Domain.Catalog.Platform.Create(list[i].Key, Name(list[i].Fa, list[i].En), list[i].Url, i).Value);
    }

    private async Task<(Role Owner, Role Client)> SeedSystemRolesAsync(CancellationToken ct)
    {
        var allGrants = Permissions.Catalog.Select(p => new PermissionGrant(p.Code, p.MaxScope)).ToList();

        var owner = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.SystemKey == SystemRoles.Owner, ct);
        if (owner is null)
        {
            owner = Role.CreateSystem(SystemRoles.Owner, Name("مالک", "Owner"), allGrants);
            db.Roles.Add(owner);
        }
        else
        {
            owner.SyncSystemGrants(allGrants);  // the catalog may have grown: the owner always has everything
        }

        var client = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.SystemKey == SystemRoles.Client, ct);
        if (client is null)
        {
            client = Role.CreateSystem(SystemRoles.Client, Name("مشتری", "Client"), []);
            db.Roles.Add(client);
        }
        return (owner, client);
    }

    private async Task SeedDemoAsync(Role owner, Role client, CancellationToken ct)
    {
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.IsDemo, ct)) return;

        var all = Permissions.Catalog.Select(p => new PermissionGrant(p.Code, p.MaxScope)).ToList();
        var manager = Role.Create(Name("مدیر", "Manager"), "ادمین دوم: همه‌چیز جز مدیریت نقش‌ها", all.Where(g => g.Code != Permissions.RolesManage)).Value;

        PermissionGrant[] worker =
        [
            new(Permissions.WorkView, PermissionScope.Assigned),
            new(Permissions.WorkRun, PermissionScope.Assigned),
            new(Permissions.OrdersView, PermissionScope.Assigned),
            new(Permissions.AttendanceSelf, PermissionScope.Own),
            new(Permissions.AttendanceView, PermissionScope.Own),
            new(Permissions.ReportsView, PermissionScope.Own),
        ];
        var editorRole = Role.Create(Name("تدوینگر", "Video editor"), "تدوین ریلز و ویدیو", worker).Value;
        var writerRole = Role.Create(Name("عکاس و متن‌نویس", "Photographer & copywriter"), "عکاسی، کپشن و متن", worker).Value;
        var aiRole = Role.Create(Name("کار با هوش مصنوعی", "AI specialist"), "تولید محتوا با ابزارهای هوش مصنوعی", worker).Value;
        db.Roles.AddRange(manager, editorRole, writerRole, aiRole);

        // Admin demo = manager role (never the Owner role) so it can't touch real keys or users.
        // It holds every grant so roles management can be shown; demo installs contain only sample data.
        var demoAdminRole = Role.Create(Name("ادمین نمایشی", "Demo admin"), "فقط برای نمایش", all).Value;
        db.Roles.Add(demoAdminRole);

        AddUser(OwnerDemoPhone, "مالک آژانس (نمونه)", UserKind.Staff, owner.Id);
        AddUser(DemoAccounts.Find("admin")!.Phone, "سارا (مدیر نمونه)", UserKind.Staff, demoAdminRole.Id);
        AddUser(DemoAccounts.Find("editor")!.Phone, "علی (تدوینگر نمونه)", UserKind.Staff, editorRole.Id);
        AddUser(DemoAccounts.Find("writer")!.Phone, "مریم (متن‌نویس نمونه)", UserKind.Staff, writerRole.Id);
        AddUser(DemoAccounts.Find("client")!.Phone, "کافه نمونه", UserKind.Client, client.Id);
        AddUser("09000000005", "رضا (تدوینگر دوم نمونه)", UserKind.Staff, editorRole.Id);
        AddUser("09000000006", "نگار (هوش مصنوعی نمونه)", UserKind.Staff, aiRole.Id);
        AddUser("09000000007", "محمد (مدیر نمونه)", UserKind.Staff, manager.Id);
    }

    private void AddUser(string phone, string name, UserKind kind, Guid roleId)
    {
        var user = User.Create(phone, name, kind, isDemo: true).Value;
        user.AssignRole(roleId);
        db.Users.Add(user);
    }

    private static LocalizedText Name(string fa, string en) => new(new Dictionary<string, string> { ["fa"] = fa, ["en"] = en });
}
