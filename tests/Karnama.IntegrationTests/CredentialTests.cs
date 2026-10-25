using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Notify;
using Karnama.Domain.Clients;
using Karnama.Domain.Identity;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N4: credential vault (PROMPT2 §10.2 / §15), client profile, renewal request and package reminders (§10.3).</summary>
public sealed class CredentialTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.StatusCode == HttpStatusCode.NoContent ? default : await r.JsonAsync();
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Guid> ClientIdAsync(string name) => Db(db => db.Clients.Where(c => c.BusinessName == name).Select(c => c.Id).FirstAsync());
    private Task<Guid> InstagramAsync() => Db(db => db.Platforms.Where(p => p.Key == "instagram").Select(p => p.Id).FirstAsync());

    /// <summary>A real (non-demo) staff member with the manager role, logged in with a real SMS code.</summary>
    private async Task<(HttpClient Client, string Phone)> RealManagerAsync(string phone)
    {
        await Db(async db =>
        {
            if (await db.Users.AnyAsync(u => u.Phone == phone)) return 0;
            var role = (await db.Roles.ToListAsync()).First(r => r.Name.Get("fa") == "مدیر").Id;
            var user = User.Create(phone, "مدیر تست", UserKind.Staff).Value!;
            user.AssignRole(role);
            db.Users.Add(user);
            return await db.SaveChangesAsync();
        });
        var c = _f.CreateClient();
        var req = await Ok(await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone }));
        var code = await _f.Sms.WaitForCodeAsync(phone);
        var login = await Ok(await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = req.GetProperty("challengeId").GetGuid(), code }));
        return (c.WithToken(login.GetProperty("session").GetProperty("accessToken").GetString()!), phone);
    }

    [Fact]
    public async Task Reveal_needs_a_fresh_sms_code_shows_the_password_once_and_is_audited_and_reported_to_the_owner()
    {
        var (manager, phone) = await RealManagerAsync("09351110001");
        var cafe = await ClientIdAsync("کافه نمونه");
        var list = await Ok(await manager.GetAsync($"/api/v1/clients/{cafe}/credentials"));
        var cred = list.EnumerateArray().Single(x => x.GetProperty("hasSecret").GetBoolean());
        var id = cred.GetProperty("id").GetGuid();
        list.GetRawText().Should().NotContain("Nemoone").And.NotContain("cipher", "the list never carries the secret");

        // No code at all, or a login code, never opens it.
        (await manager.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId = Guid.CreateVersion7(), code = "123456" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // A valid login code for the same phone (written directly: the login resend timer would block a second SMS).
        var loginId = Guid.CreateVersion7();
        await Db(async db =>
        {
            var codes = _f.Services.GetRequiredService<Karnama.Application.Abstractions.IOtpCodeService>();
            db.OtpChallenges.Add(OtpChallenge.Create(loginId, phone, codes.Hash(loginId, phone, "424242"), _f.Clock.GetUtcNow()));
            return await db.SaveChangesAsync();
        });
        (await manager.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId = loginId, code = "424242" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a login code is not a reveal code");

        var previous = _f.Sms.LastCode[phone];
        var start = await Ok(await manager.PostAsync($"/api/v1/credentials/{id}/reveal/start", null));
        start.GetProperty("demoCode").ValueKind.Should().Be(JsonValueKind.Null, "real staff get the code by SMS only");
        var code = await _f.Sms.WaitForCodeAsync(phone, previous);
        var challengeId = start.GetProperty("challengeId").GetGuid();

        (await manager.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId, code = code == "000000" ? "111111" : "000000" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var revealed = await manager.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId, code });
        revealed.Headers.CacheControl!.NoStore.Should().BeTrue();
        var secret = await Ok(revealed);
        secret.GetProperty("password").GetString().Should().Be("Nemoone-1404!");
        secret.GetProperty("visibleSeconds").GetInt32().Should().Be(30);

        // Single use.
        (await manager.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId, code })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var audited = await Db(db => db.AuditLogs.CountAsync(a => a.Action == "credentials.revealed" && a.EntityId == id.ToString()));
        audited.Should().Be(1);
        var ownerTold = await Db(db => (from n in db.Notifications join u in db.Users on n.UserId equals u.Id
                                        where u.Phone == AppFixture.OwnerPhone && n.Kind == "credentials.revealed" select n).CountAsync());
        ownerTold.Should().Be(1);

        // Nothing readable at rest.
        var raw = await Db(db => db.ClientCredentials.AsNoTracking().FirstAsync(c => c.Id == id));
        Encoding.UTF8.GetString(raw.Cipher!).Should().NotContain("Nemoone");
    }

    [Fact]
    public async Task Workers_never_see_or_open_credentials()
    {
        var cafe = await ClientIdAsync("کافه نمونه");
        var editor = await As("editor");
        (await editor.GetAsync($"/api/v1/clients/{cafe}/credentials")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var id = await Db(db => db.ClientCredentials.Where(c => c.ClientId == cafe).Select(c => c.Id).FirstAsync());
        (await editor.PostAsync($"/api/v1/credentials/{id}/reveal/start", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PostAsJsonAsync($"/api/v1/credentials/{id}/reveal", new { challengeId = Guid.CreateVersion7(), code = "123456" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_client_manages_only_its_own_logins_write_only_and_another_clients_record_is_404()
    {
        var instagram = await InstagramAsync();
        var admin = await As("admin");
        var shop = await ClientIdAsync("کتاب‌فروشی نمونه");
        var other = await Ok(await admin.PostAsJsonAsync($"/api/v1/clients/{shop}/credentials",
            new { platformId = instagram, handle = "book_shop_test", businessSuiteAccess = false, secret = new { password = "Other-Secret-9" } }));
        var otherId = other.GetProperty("id").GetGuid();

        var client = await As("client");
        (await client.PutAsJsonAsync($"/api/v1/portal/credentials/{otherId}", new { platformId = instagram, handle = "x", businessSuiteAccess = true, secret = (object?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/v1/portal/credentials/{otherId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/clients/{shop}/credentials")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/v1/credentials/{otherId}/reveal/start", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The client changes its own password: it is stored, never echoed back, and the admin can open the new one.
        var mine = await Ok(await client.GetAsync("/api/v1/portal/credentials"));
        mine.GetRawText().Should().NotContain("book_shop_test").And.NotContain("Nemoone");
        var cafeCred = mine.EnumerateArray().First(x => x.GetProperty("hasSecret").GetBoolean());
        var changed = await Ok(await client.PutAsJsonAsync($"/api/v1/portal/credentials/{cafeCred.GetProperty("id").GetGuid()}",
            new { platformId = instagram, handle = cafeCred.GetProperty("handle").GetString(), businessSuiteAccess = false, secret = new { password = "Changed-By-Client-7", recoveryEmail = "new@example.com" } }));
        changed.GetRawText().Should().NotContain("Changed-By-Client-7");
        changed.GetProperty("source").GetString().Should().Be("client");

        var start = await Ok(await admin.PostAsync($"/api/v1/credentials/{cafeCred.GetProperty("id").GetGuid()}/reveal/start", null));
        var demoCode = start.GetProperty("demoCode").GetString();
        demoCode.Should().NotBeNullOrEmpty("demo accounts see the code on screen (fake phone numbers)");
        var secret = await Ok(await admin.PostAsJsonAsync($"/api/v1/credentials/{cafeCred.GetProperty("id").GetGuid()}/reveal", new { challengeId = start.GetProperty("challengeId").GetGuid(), code = demoCode }));
        secret.GetProperty("password").GetString().Should().Be("Changed-By-Client-7");
        secret.GetProperty("recoveryEmail").GetString().Should().Be("new@example.com");
        secret.GetProperty("twoFactorNote").GetString().Should().Contain("خانم رضایی", "fields left out keep their stored value");
    }

    [Fact]
    public async Task A_ciphertext_copied_to_another_row_does_not_open()
    {
        var admin = await As("admin");
        var instagram = await InstagramAsync();
        var a = await ClientIdAsync("گل‌فروشی نمونه");
        var x = await Ok(await admin.PostAsJsonAsync($"/api/v1/clients/{a}/credentials", new { platformId = instagram, handle = "flower_one", businessSuiteAccess = false, secret = new { password = "First-1" } }));
        var y = await Ok(await admin.PostAsJsonAsync($"/api/v1/clients/{a}/credentials", new { platformId = instagram, handle = "flower_two", businessSuiteAccess = false, secret = new { password = "Second-2" } }));
        await Db(async db =>
        {
            var one = await db.ClientCredentials.FirstAsync(c => c.Id == x.GetProperty("id").GetGuid());
            var two = await db.ClientCredentials.FirstAsync(c => c.Id == y.GetProperty("id").GetGuid());
            two.SetSecret(one.Cipher!, one.WrappedKey!, one.KeyVersion, CredentialSource.Staff, null, DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });
        var start = await Ok(await admin.PostAsync($"/api/v1/credentials/{y.GetProperty("id").GetGuid()}/reveal/start", null));
        var r = await admin.PostAsJsonAsync($"/api/v1/credentials/{y.GetProperty("id").GetGuid()}/reveal", new { challengeId = start.GetProperty("challengeId").GetGuid(), code = start.GetProperty("demoCode").GetString() });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // "Remove password" keeps the account row but wipes the secret.
        var cleared = await Ok(await admin.PutAsJsonAsync($"/api/v1/credentials/{y.GetProperty("id").GetGuid()}",
            new { platformId = instagram, handle = "flower_two", businessSuiteAccess = true, secret = (object?)null, removeSecret = true }));
        cleared.GetProperty("hasSecret").GetBoolean().Should().BeFalse();
        (await Db(db => db.ClientCredentials.Where(c => c.Id == y.GetProperty("id").GetGuid()).Select(c => c.Cipher).FirstAsync())).Should().BeNull();

        // Deleting really deletes (no soft-deleted ciphertext left behind).
        (await admin.DeleteAsync($"/api/v1/credentials/{x.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Db(db => db.ClientCredentials.IgnoreQueryFilters().AnyAsync(c => c.Id == x.GetProperty("id").GetGuid()))).Should().BeFalse();
    }

    [Fact]
    public async Task Package_reminders_follow_panel_days_go_out_once_and_contract_end_asks_to_delete_passwords()
    {
        var admin = await As("admin");
        await Ok(await admin.PutAsJsonAsync("/api/v1/settings/package-notices", new { daysBefore = new[] { 10, 2 }, onEndDay = true, quotaUsedUp = true, contractEndPasswords = true }));
        var cafe = await ClientIdAsync("کافه نمونه");
        var pkg = await Db(db => db.ClientPackages.Where(p => p.ClientId == cafe && p.Status == ClientPackageStatus.Active).Select(p => new { p.Id, p.EndsAt }).FirstAsync());

        async Task<int> RunAsync()
        {
            using var scope = _f.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PackageExpiryJob>().RunAsync(CancellationToken.None);
        }
        Task<int> Count(string kind) => Db(db => db.Notifications.CountAsync(n => n.EventKey.StartsWith($"pkg:{pkg.Id:N}") && n.Kind == kind));
        Task<int> PortalCount(string kind) => Db(db => (from n in db.Notifications join u in db.Users on n.UserId equals u.Id
                                                         where u.Phone == "09000000004" && n.Kind == kind select n).CountAsync());

        // The clock can't jump days (tokens would not be valid yet), so the package's end moves instead.
        Task MoveEnd(TimeSpan fromNow) => Db(db => db.ClientPackages.Where(p => p.Id == pkg.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.EndsAt, _f.Clock.GetUtcNow() + fromNow)));

        // 9 days before the end: the "10 days" reminder, to the client and the managers — and only once.
        await MoveEnd(TimeSpan.FromDays(9));
        await RunAsync();
        await RunAsync();
        (await PortalCount("package.expiring")).Should().Be(1);
        (await Count("package.expiring")).Should().BeGreaterThan(1, "managers get it too");
        var afterFirst = await Count("package.expiring");

        // 5 days left: nothing new (next threshold is 2).
        await MoveEnd(TimeSpan.FromDays(5));
        await RunAsync();
        (await Count("package.expiring")).Should().Be(afterFirst);

        // 1.5 days left: the "2 days" reminder.
        await MoveEnd(TimeSpan.FromHours(36));
        await RunAsync();
        (await PortalCount("package.expiring")).Should().Be(2);

        // End passed: "package ended" + contract end → delete passwords (managers) / change password (client), once.
        await MoveEnd(TimeSpan.FromHours(-1));
        await RunAsync();
        await RunAsync();
        (await PortalCount("package.ended")).Should().Be(1);
        (await PortalCount("credentials.changePassword")).Should().Be(1);
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "credentials.contractEnded"))).Should().BeGreaterThan(0);
        (await Db(db => db.ClientPackages.Where(p => p.Id == pkg.Id).Select(p => p.Status).FirstAsync())).Should().Be(ClientPackageStatus.Ended);
    }

    [Fact]
    public async Task Client_profile_shows_package_and_history_and_renewal_is_requested_once_a_day()
    {
        var client = await As("client");
        var profile = await Ok(await client.GetAsync("/api/v1/portal/profile"));
        profile.GetProperty("businessName").GetString().Should().Be("کافه نمونه");
        profile.GetProperty("history").GetArrayLength().Should().BeGreaterThan(0);
        profile.GetRawText().Should().NotContain("یادداشت داخلی", "internal notes never reach the client");

        var first = await Ok(await client.PostAsJsonAsync("/api/v1/portal/renew", new { note = "همین پکیج را تمدید کنید" }));
        first.GetProperty("alreadyRequested").GetBoolean().Should().BeFalse();
        var second = await Ok(await client.PostAsJsonAsync("/api/v1/portal/renew", new { note = (string?)null }));
        second.GetProperty("alreadyRequested").GetBoolean().Should().BeTrue();
        (await Ok(await client.GetAsync("/api/v1/portal/profile"))).GetProperty("renewRequestedToday").GetBoolean().Should().BeTrue();
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "package.renewRequested" && n.Important))).Should().BeGreaterThan(0);

        // Staff can't use portal endpoints.
        (await (await As("admin")).PostAsJsonAsync("/api/v1/portal/renew", new { note = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
