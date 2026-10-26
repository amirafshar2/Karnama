using Karnama.Application.Abstractions;
using Karnama.Application.Catalog;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Qa;

/// <summary>
/// The first crowd of docs/PROMPT3.md §1 (83 people) on top of the demo install. Phones are 0991xxxxxxx so they never
/// collide with demo or real numbers; every person is flagged demo (sample data). Idempotent.
/// </summary>
internal sealed class QaCrowdSeed(AppDbContext db, TimeProvider clock) : IQaSeeder
{
    public const string Prefix = "0991";

    public async Task<QaCrowd> SeedAsync(CancellationToken ct)
    {
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == "09910000001", ct))
            await CreateAsync(ct);
        return await DescribeAsync(ct);
    }

    private async Task CreateAsync(CancellationToken ct)
    {
        var roles = await db.Roles.ToListAsync(ct);
        Role ByName(string fa) => roles.First(r => r.Name.Get("fa") == fa);
        var owner = roles.First(r => r.SystemKey == SystemRoles.Owner);
        var clientRole = roles.First(r => r.SystemKey == SystemRoles.Client);
        var kiosk = roles.FirstOrDefault(r => r.Name.Get("fa") == "کیوسک QA");
        if (kiosk is null)
        {
            kiosk = Role.Create(new LocalizedText(new Dictionary<string, string> { ["fa"] = "کیوسک QA", ["en"] = "QA kiosk" }), "تبلت ورود دفتر (تست جمعیتی)",
                [new PermissionGrant(Permissions.AttendanceKiosk, PermissionScope.All)]).Value!;
            db.Roles.Add(kiosk);
        }

        var n = 0;
        User Add(string group, string name, UserKind kind, Role role)
        {
            var u = User.Create($"{Prefix}{group}{++n:D6}", name, kind, isDemo: true).Value!;
            u.AssignRole(role.Id);
            db.Users.Add(u);
            return u;
        }

        Add("0", "مالک خانم (QA)", UserKind.Staff, owner);
        Add("0", "مالک آقا (QA)", UserKind.Staff, owner);
        Add("0", "مدیر (QA)", UserKind.Staff, ByName("مدیر"));
        for (var i = 1; i <= 8; i++) Add("1", $"تدوینگر {i} (QA)", UserKind.Staff, ByName("تدوینگر"));
        for (var i = 1; i <= 5; i++) Add("1", $"عکاس و متن‌نویس {i} (QA)", UserKind.Staff, ByName("عکاس و متن‌نویس"));
        for (var i = 1; i <= 3; i++) Add("1", $"هوش مصنوعی {i} (QA)", UserKind.Staff, ByName("کار با هوش مصنوعی"));
        for (var i = 1; i <= 4; i++) Add("2", $"فیلمبردار بیرون {i} (QA)", UserKind.Staff, ByName("تدوینگر"));
        for (var i = 1; i <= 5; i++) Add("3", $"بازاریاب {i} (QA)", UserKind.Staff, ByName("بازاریاب"));
        for (var i = 1; i <= 2; i++) Add("4", $"نویسنده‌ی بلاگ {i} (QA)", UserKind.Staff, ByName("عکاس و متن‌نویس"));
        Add("5", "کیوسک دفتر (QA)", UserKind.Staff, kiosk);
        await db.SaveChangesAsync(ct);

        // 50 clients: 25 FullAccess, 25 UsernameOnly; 10 of them end within a week.
        // The demo packages are capped at 10 buyers; a QA database lifts the cap so the crowd (and scenarios) can still buy them.
        await db.Packages.ExecuteUpdateAsync(u => u.SetProperty(p => p.Capacity, (int?)null), ct);
        var packages = await db.Packages.Include(p => p.Quotas).ToListAsync(ct);
        var full = packages.Where(p => p.Access == PageAccess.FullAccess).OrderBy(p => p.Price).ToList();
        var userOnly = packages.Where(p => p.Access == PageAccess.UsernameOnly).OrderBy(p => p.Price).ToList();
        var names = (await db.ServiceTypes.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get("fa"));
        var instagram = await db.Platforms.AsNoTracking().Where(p => p.Key == "instagram").Select(p => p.Id).FirstAsync(ct);
        var now = clock.GetUtcNow();
        string[] trades = ["کافه", "پوشاک", "زیبایی", "شیرینی", "کتاب", "گل", "رستوران", "عینک", "کفش", "طلا"];
        for (var i = 1; i <= 50; i++)
        {
            var fullAccess = i <= 25;
            var pkg = fullAccess ? full[i % full.Count] : userOnly[i % userOnly.Count];
            var trade = trades[i % trades.Length];
            var phone = $"{Prefix}6{++n:D6}";
            var user = User.Create(phone, $"{trade} {i} (QA)", UserKind.Client, isDemo: true).Value!;
            user.AssignRole(clientRole.Id);
            db.Users.Add(user);
            var client = Client.Create(new ClientSpec($"{trade} {i} (QA)", trade, $"مسئول {i}", phone, "شیراز", "مشتری تست جمعیتی", ClientStatus.Active,
                [new ClientAccountSpec(instagram, $"qa_{i}", fullAccess ? PageManagement.AgencyManaged : PageManagement.ClientManaged)])).Value!;
            client.LinkPortalUser(user.Id);
            db.Clients.Add(client);
            // Clients 41–50 end in 1–7 days (expiry notices); the others are mid-period.
            var daysLeft = i > 40 ? 1 + (i - 41) % 7 : 10 + i % 15;
            var startsAt = now.AddDays(-(pkg.ValidityDays - daysLeft));
            db.ClientPackages.Add(ClientPackage.Assign(client.Id, pkg, startsAt, PackageService.SnapshotQuotas(pkg, names), now, "QA").Value!);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<QaCrowd> DescribeAsync(CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Phone.StartsWith(Prefix))
            .Select(u => new QaPerson(u.Phone, u.DisplayName, u.Kind == UserKind.Client ? "client" : u.Phone.Substring(4, 1)))
            .ToListAsync(ct);
        var roleOf = new Dictionary<string, string> { ["0"] = "lead", ["1"] = "office", ["2"] = "field", ["3"] = "marketer", ["4"] = "writer", ["5"] = "kiosk", ["client"] = "client" };
        return new QaCrowd(people.Select(p => p with { Group = roleOf.GetValueOrDefault(p.Group, p.Group) }).OrderBy(p => p.Phone).ToList());
    }
}
