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
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == "015110000001", ct))
            await CreateAsync(ct);
        return await DescribeAsync(ct);
    }

    private async Task CreateAsync(CancellationToken ct)
    {
        var roles = await db.Roles.ToListAsync(ct);
        Role ByName(string fa) => roles.First(r => r.Name.Get("de") == fa);
        var owner = roles.First(r => r.SystemKey == SystemRoles.Owner);
        var clientRole = roles.First(r => r.SystemKey == SystemRoles.Client);
        var kiosk = roles.FirstOrDefault(r => r.Name.Get("de") == "Kiosk QA");
        if (kiosk is null)
        {
            kiosk = Role.Create(new LocalizedText(new Dictionary<string, string> { ["de"] = "Kiosk QA", ["en"] = "QA kiosk" }), "Check-in-Tablet im Büro (Massentest)",
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

        Add("0", "Inhaberin Frau (QA)", UserKind.Staff, owner);
        Add("0", "Inhaber Herr (QA)", UserKind.Staff, owner);
        Add("0", "Manager (QA)", UserKind.Staff, ByName("Manager"));
        for (var i = 1; i <= 8; i++) Add("1", $"Cutter {i} (QA)", UserKind.Staff, ByName("Cutter"));
        for (var i = 1; i <= 5; i++) Add("1", $"Fotograf & Texter {i} (QA)", UserKind.Staff, ByName("Fotograf & Texter"));
        for (var i = 1; i <= 3; i++) Add("1", $"KI-Spezialist {i} (QA)", UserKind.Staff, ByName("KI-Spezialist"));
        for (var i = 1; i <= 4; i++) Add("2", $"Kameramann außer Haus {i} (QA)", UserKind.Staff, ByName("Cutter"));
        for (var i = 1; i <= 5; i++) Add("3", $"Vertrieb {i} (QA)", UserKind.Staff, ByName("Vertrieb"));
        for (var i = 1; i <= 2; i++) Add("4", $"Blog-Autor {i} (QA)", UserKind.Staff, ByName("Fotograf & Texter"));
        Add("5", "Büro-Kiosk (QA)", UserKind.Staff, kiosk);
        await db.SaveChangesAsync(ct);

        // 50 clients: 25 FullAccess, 25 UsernameOnly; 10 of them end within a week.
        // The demo packages are capped at 10 buyers; a QA database lifts the cap so the crowd (and scenarios) can still buy them.
        await db.Packages.ExecuteUpdateAsync(u => u.SetProperty(p => p.Capacity, (int?)null), ct);
        var packages = await db.Packages.Include(p => p.Quotas).ToListAsync(ct);
        var full = packages.Where(p => p.Access == PageAccess.FullAccess).OrderBy(p => p.Price).ToList();
        var userOnly = packages.Where(p => p.Access == PageAccess.UsernameOnly).OrderBy(p => p.Price).ToList();
        var names = (await db.ServiceTypes.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get("de"));
        var instagram = await db.Platforms.AsNoTracking().Where(p => p.Key == "instagram").Select(p => p.Id).FirstAsync(ct);
        var now = clock.GetUtcNow();
        string[] trades = ["Café", "Mode", "Beauty", "Konditorei", "Bücher", "Blumen", "Restaurant", "Optiker", "Schuhe", "Gold"];
        for (var i = 1; i <= 50; i++)
        {
            var fullAccess = i <= 25;
            var pkg = fullAccess ? full[i % full.Count] : userOnly[i % userOnly.Count];
            var trade = trades[i % trades.Length];
            var phone = $"{Prefix}6{++n:D6}";
            var user = User.Create(phone, $"{trade} {i} (QA)", UserKind.Client, isDemo: true).Value!;
            user.AssignRole(clientRole.Id);
            db.Users.Add(user);
            var client = Client.Create(new ClientSpec($"{trade} {i} (QA)", trade, $"Verantwortlich {i}", phone, "Stuttgart", "Kunde Massentest", ClientStatus.Active,
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
