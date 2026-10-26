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
public sealed class DataSeeder(AppDbContext db, IOptions<SeedOptions> options, ILogger<DataSeeder> logger, Karnama.Application.Abstractions.IVault vault)
{
    public const string OwnerDemoPhone = "09000000000";

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedLanguagesAsync(ct);
        var (owner, client) = await SeedSystemRolesAsync(ct);
        await SeedPlatformsAsync(ct);
        await SeedCrmDefaultsAsync(ct);
        if (!await db.AssetCategories.AnyAsync(ct))
        {
            var order = 0;
            foreach (var (id, name) in Karnama.Domain.Assets.AssetDefaults.Categories)
                db.AssetCategories.Add(Karnama.Domain.Assets.AssetCategory.Create(name, order += 10, id).Value!);
        }
        await db.SaveChangesAsync(ct);
        await AttendanceSeed.SeedAsync(db, options.Value.Mode == SeedMode.Demo, ct);
        await HelpSeed.SeedAsync(db, ct);

        if (options.Value.Mode == SeedMode.Demo)
        {
            await SeedDemoAsync(owner, client, ct);
            await db.SaveChangesAsync(ct);
            await DemoCatalogSeed.SeedAsync(db, ct);
            await DemoWorkSeed.SeedAsync(db, ct);
            await DemoWorkSeed.SeedPublishingAsync(db, ct);
            await DemoBlogSeed.SeedAsync(db, ct);
            await DemoSiteSeed.SeedAsync(db, ct);
            await DemoVaultSeed.SeedAsync(db, vault, ct);
            await DemoMediaSeed.SeedAsync(db, ct);
            await EnsureDemoMarketerAsync(ct);
            await DemoCrmSeed.SeedAsync(db, ct);
            await DemoHistorySeed.SeedAsync(db, ct);
            await DemoAssetSeed.SeedAsync(db, ct);
            await DemoDeviceSeed.SeedAsync(db, ct);
            await SyncDemoAdminAsync(ct);
        }
        logger.LogInformation("Seed completed (mode: {Mode})", options.Value.Mode);
    }

    /// <summary>Starting sales pipeline and lead sources (also in empty installs; editable in the panel).</summary>
    private async Task SeedCrmDefaultsAsync(CancellationToken ct)
    {
        if (!await db.LeadStages.AnyAsync(ct))
            foreach (var s in Karnama.Domain.Crm.CrmDefaults.Stages)
                db.LeadStages.Add(Karnama.Domain.Crm.LeadStage.Create(s.Name, s.Kind, s.Order, s.Id).Value!);
        if (!await db.LeadSources.AnyAsync(ct))
        {
            var i = 0;
            foreach (var name in Karnama.Domain.Crm.CrmDefaults.Sources)
                db.LeadSources.Add(Karnama.Domain.Crm.LeadSource.Create(name, i += 10).Value!);
        }
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
            new(Permissions.ChatUse, PermissionScope.Own),
            new(Permissions.ScoresView, PermissionScope.Own),
            new(Permissions.AssetsView, PermissionScope.Own),
        ];
        var editorRole = Role.Create(Name("تدوینگر", "Video editor"), "تدوین ریلز و ویدیو", worker).Value;
        var writerRole = Role.Create(Name("عکاس و متن‌نویس", "Photographer & copywriter"), "عکاسی، کپشن و متن", [.. worker, new(Permissions.BlogWrite, PermissionScope.Own)]).Value;
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

    /// <summary>The «بازاریاب» role (PROMPT2 §4) and its demo user; also added to demo installs made before N7.</summary>
    private async Task EnsureDemoMarketerAsync(CancellationToken ct)
    {
        var phone = DemoAccounts.Find("marketer")!.Phone;
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone, ct)) return;
        var role = Role.Create(Name("بازاریاب", "Marketer"), "سرنخ‌های خودش، تماس و پیگیری",
        [
            new(Permissions.CrmLeads, PermissionScope.Own),
            new(Permissions.ChatUse, PermissionScope.Own),
            new(Permissions.AttendanceSelf, PermissionScope.Own),
            new(Permissions.AttendanceView, PermissionScope.Own),
            new(Permissions.ReportsView, PermissionScope.Own),
            new(Permissions.AssetsView, PermissionScope.Own),
        ]).Value;
        db.Roles.Add(role);
        AddUser(phone, "نیما (بازاریاب نمونه)", UserKind.Staff, role.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The demo admin shows everything, so it follows the catalog as new modules add permissions.</summary>
    private async Task SyncDemoAdminAsync(CancellationToken ct)
    {
        var phone = DemoAccounts.Find("admin")!.Phone;
        var roleId = await db.Users.Where(u => u.Phone == phone && u.IsDemo).SelectMany(u => u.Roles.Select(r => r.RoleId)).FirstOrDefaultAsync(ct);
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId && r.SystemKey == null, ct);
        if (role is not null && role.Permissions.Count != Permissions.Catalog.Count)
            role.Update(role.Name, role.Description, Permissions.Catalog.Select(p => new PermissionGrant(p.Code, p.MaxScope)));

        // Demo installs made before a permission existed: give the demo team the new everyday ones (chat).
        var demoRoleIds = await db.Users.Where(u => u.IsDemo && u.Kind == UserKind.Staff).SelectMany(u => u.Roles.Select(r => r.RoleId)).Distinct().ToListAsync(ct);
        foreach (var r in await db.Roles.Include(x => x.Permissions).Where(x => demoRoleIds.Contains(x.Id) && x.SystemKey == null).ToListAsync(ct))
        {
            var grants = r.Permissions.Select(g => new PermissionGrant(g.Code, g.Scope)).ToList();
            var manager = grants.Any(g => g.Code == Permissions.WorkAssign);
            PermissionGrant[] wanted = manager
                ? [new(Permissions.ChatGroupsManage, PermissionScope.All), new(Permissions.ScoresView, PermissionScope.All),
                   new(Permissions.AssetsView, PermissionScope.All), new(Permissions.AssetsManage, PermissionScope.All), new(Permissions.AssetsAssign, PermissionScope.All),
                   new(Permissions.DevicesManage, PermissionScope.All)]
                : [new(Permissions.ChatUse, PermissionScope.Own), new(Permissions.ScoresView, PermissionScope.Own), new(Permissions.AssetsView, PermissionScope.Own)];
            var missing = wanted.Where(w => grants.All(g => g.Code != w.Code) && !(w.Code == Permissions.ChatUse && grants.Any(g => g.Code == Permissions.ChatGroupsManage))).ToList();
            if (missing.Count == 0) continue;
            r.Update(r.Name, r.Description, grants.Concat(missing));
        }
        await db.SaveChangesAsync(ct);
    }

    private void AddUser(string phone, string name, UserKind kind, Guid roleId)
    {
        var user = User.Create(phone, name, kind, isDemo: true).Value;
        user.AssignRole(roleId);
        db.Users.Add(user);
    }

    private static LocalizedText Name(string fa, string en) => new(new Dictionary<string, string> { ["fa"] = fa, ["en"] = en });
}
