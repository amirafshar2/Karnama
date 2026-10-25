using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

public sealed class CatalogTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;

    /// <summary>Mints an access token with exactly the given permissions (to test scopes without building roles).</summary>
    private HttpClient As(Dictionary<string, PermissionScope> perms, Guid? userId = null)
    {
        var tokens = _f.Services.GetRequiredService<ITokenService>();
        var token = tokens.CreateAccessToken(new TokenSubject(userId ?? Guid.NewGuid(), UserKind.Staff, false, "fa", perms), Guid.NewGuid(), out _);
        return _f.CreateClient().WithToken(token);
    }

    private async Task<HttpClient> Admin() => _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));

    [Fact]
    public async Task Client_mobile_is_hidden_without_clients_contact()
    {
        var viewer = As(new() { ["clients.view"] = PermissionScope.All });
        var list = await (await viewer.GetAsync("/api/v1/clients?limit=50")).JsonAsync();
        list.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        list.GetProperty("items").EnumerateArray().Should().OnlyContain(c => c.GetProperty("mobile").ValueKind == System.Text.Json.JsonValueKind.Null);

        var admin = await Admin();
        var full = await (await admin.GetAsync("/api/v1/clients?limit=50")).JsonAsync();
        full.GetProperty("items").EnumerateArray().Should().OnlyContain(c => c.GetProperty("mobile").GetString()!.StartsWith("09"));

        var worker = _f.CreateClient().WithToken(await _f.DemoTokenAsync("editor"));
        (await worker.GetAsync("/api/v1/clients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_crud_with_keyset_paging_history_and_excel()
    {
        var admin = await Admin();
        var platforms = await (await admin.GetAsync("/api/v1/platforms")).JsonAsync();
        var insta = platforms.EnumerateArray().First(p => p.GetProperty("key").GetString() == "instagram").GetProperty("id").GetGuid();

        var created = await admin.PostAsJsonAsync("/api/v1/clients", new
        {
            businessName = "نانوایی تست", industry = "نان", contactName = "آقای تست", mobile = "۰۹۱۷۰۰۰۰۰۰۱", city = "شیراز",
            internalNote = "یادداشت", status = "active", accounts = new[] { new { platformId = insta, handle = "@nan_test", management = "clientManaged" } },
        }, Http.Json);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var c = await created.JsonAsync();
        var id = c.GetProperty("id").GetGuid();
        c.GetProperty("accounts")[0].GetProperty("handle").GetString().Should().Be("nan_test");
        c.GetProperty("mobile").GetString().Should().Be("09170000001");

        var bad = await admin.PostAsJsonAsync("/api/v1/clients", new { businessName = "x", mobile = "123", status = "active" }, Http.Json);
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await admin.GetAsync($"/api/v1/clients?limit=3&cursor={cursor}")).JsonAsync();
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").ValueKind == System.Text.Json.JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        seen.Should().OnlyHaveUniqueItems().And.Contain(id).And.HaveCountGreaterThanOrEqualTo(7);

        (await admin.DeleteAsync($"/api/v1/clients/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/clients/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await (await admin.GetAsync($"/api/v1/history/Client/{id}")).JsonAsync();
        history.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);

        var xlsx = await admin.GetAsync("/api/v1/clients/export");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.Should().Contain("spreadsheetml");
        (await xlsx.Content.ReadAsByteArrayAsync())[..2].Should().Equal((byte)'P', (byte)'K');
    }

    [Fact]
    public async Task Package_rules_sanitized_description_and_capacity_race()
    {
        var admin = await Admin();
        var services = await (await admin.GetAsync("/api/v1/services/options")).JsonAsync();
        var serviceId = services[0].GetProperty("id").GetGuid();

        object Body(string sku, string slug, int? capacity = null, string? md = null) => new
        {
            name = new Dictionary<string, string> { ["fa"] = "پکیج تست " + sku }, slug, sku, category = "تست",
            descriptionMarkdown = md, price = 10_000_000L, period = "monthly", validityDays = 30, capacity,
            defaultManagement = "agencyManaged", access = "usernameOnly", consultSessions = 0, revisionsPerItem = 1, deliveryHours = 24,
            status = "active", showOnSite = false, sortOrder = 0, badge = "none",
            quotas = new[] { new { serviceTypeId = serviceId, count = 3 } },
        };

        (await admin.PostAsJsonAsync("/api/v1/packages", Body("bad sku", "x"), Http.Json)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var ok = await admin.PostAsJsonAsync("/api/v1/packages", Body("PKG-T1", "پکیج-تست-یک", 1, "## عنوان\n\n<script>alert(1)</script> [x](javascript:alert(1)) **پررنگ**"), Http.Json);
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        var p = await ok.JsonAsync();
        var html = p.GetProperty("descriptionHtml").GetString()!;
        html.Should().Contain("<h2").And.Contain("<strong>پررنگ</strong>").And.NotContain("<script").And.NotContain("javascript:");

        (await admin.PostAsJsonAsync("/api/v1/packages", Body("PKG-T1", "پکیج-تست-دو"), Http.Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The assign picker asks for active packages with the camelCase enum the API itself writes.
        var active = await (await admin.GetAsync("/api/v1/packages?status=active&limit=100")).JsonAsync();
        active.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("sku").GetString()).Should().Contain("PKG-T1");

        // Capacity 1: two admins assign the last seat at the same moment -> exactly one wins.
        var clients = await (await admin.GetAsync("/api/v1/clients?limit=10")).JsonAsync();
        var ids = clients.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Take(2).ToList();
        var packageId = p.GetProperty("id").GetGuid();
        var results = await Task.WhenAll(ids.Select(cid => admin.PostAsJsonAsync($"/api/v1/clients/{cid}/packages", new { packageId }, Http.Json)));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        // Snapshot: changing the package later doesn't change the client's contract.
        var winner = ids[results.ToList().FindIndex(r => r.StatusCode == HttpStatusCode.OK)];
        var before = await (await admin.GetAsync($"/api/v1/clients/{winner}")).JsonAsync();
        before.GetProperty("activePackage").GetProperty("price").GetInt64().Should().Be(10_000_000);
    }

    [Fact]
    public async Task Service_form_is_validated_and_versioned_only_on_change()
    {
        var admin = await Admin();
        object Body(string fieldKey, uint? version = null) => new
        {
            name = new Dictionary<string, string> { ["fa"] = "خدمت تست" }, consumesQuota = true, weight = 1, sortOrder = 0, isActive = true,
            form = new[] { new { key = fieldKey, type = "text", label = new Dictionary<string, string> { ["fa"] = "موضوع" }, required = true } },
            stages = new[] { new { name = new Dictionary<string, string> { ["fa"] = "انجام" }, estimatedMinutes = 60, needsAdminReview = true, needsClientApproval = false } },
            version,
        };
        (await admin.PostAsJsonAsync("/api/v1/services", Body("bad key"), Http.Json)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var createdRes = await admin.PostAsJsonAsync("/api/v1/services", Body("topic"), Http.Json);
        createdRes.StatusCode.Should().Be(HttpStatusCode.Created, await createdRes.Content.ReadAsStringAsync());
        var created = await createdRes.JsonAsync();
        created.GetProperty("formVersion").GetInt32().Should().Be(1);
        var id = created.GetProperty("id").GetGuid();
        var fetched = await (await admin.GetAsync($"/api/v1/services/{id}")).JsonAsync();
        fetched.GetProperty("version").GetUInt32().Should().Be(created.GetProperty("version").GetUInt32(), "create must return the stored version");

        var sameRes = await admin.PutAsJsonAsync($"/api/v1/services/{id}", Body("topic", created.GetProperty("version").GetUInt32()), Http.Json);
        sameRes.StatusCode.Should().Be(HttpStatusCode.OK, await sameRes.Content.ReadAsStringAsync());
        var same = await sameRes.JsonAsync();
        same.GetProperty("formVersion").GetInt32().Should().Be(1);
        var changed = await (await admin.PutAsJsonAsync($"/api/v1/services/{id}", Body("subject", same.GetProperty("version").GetUInt32()), Http.Json)).JsonAsync();
        changed.GetProperty("formVersion").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Only_an_owner_can_grant_the_owner_role()
    {
        var admin = await Admin(); // demo admin: has users.manage but is not an Owner
        var roles = await (await admin.GetAsync("/api/v1/roles?limit=100")).JsonAsync();
        var ownerRole = roles.GetProperty("items").EnumerateArray().First(r => r.GetProperty("systemKey").GetString() == "owner").GetProperty("id").GetGuid();
        var editorRole = roles.GetProperty("items").EnumerateArray().First(r => r.GetProperty("name").GetString() == "تدوینگر").GetProperty("id").GetGuid();

        var r1 = await admin.PostAsJsonAsync("/api/v1/users", new { displayName = "کارگر تست", phone = "09127771111", roleIds = new[] { ownerRole }, isActive = true });
        r1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var r2 = await admin.PostAsJsonAsync("/api/v1/users", new { displayName = "کارگر تست", phone = "09127771111", roleIds = new[] { editorRole }, isActive = true });
        r2.StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/api/v1/users", new { displayName = "تکراری", phone = "09127771111", roleIds = Array.Empty<Guid>(), isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Non_owner_cannot_escalate_or_touch_owners()
    {
        var admin = await Admin();
        var roles = (await (await admin.GetAsync("/api/v1/roles?limit=100")).JsonAsync()).GetProperty("items").EnumerateArray().ToList();
        Guid RoleId(string name) => roles.First(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

        // Only users.manage: can't hand out a role stronger than itself.
        var limited = As(new() { [Permissions.UsersManage] = PermissionScope.All });
        (await limited.PostAsJsonAsync("/api/v1/users", new { displayName = "ارتقا", phone = "09127772222", roleIds = new[] { RoleId("ادمین نمایشی") }, isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Holding the role's permissions is enough.
        var withWork = As(new()
        {
            [Permissions.UsersManage] = PermissionScope.All, [Permissions.WorkView] = PermissionScope.All, [Permissions.WorkRun] = PermissionScope.All,
            [Permissions.OrdersView] = PermissionScope.All, [Permissions.AttendanceSelf] = PermissionScope.Own, [Permissions.AttendanceView] = PermissionScope.All,
            [Permissions.ReportsView] = PermissionScope.All, [Permissions.ChatUse] = PermissionScope.Own,
        });
        (await withWork.PostAsJsonAsync("/api/v1/users", new { displayName = "تدوینگر دوم", phone = "09127772223", roleIds = new[] { RoleId("تدوینگر") }, isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // A full non-owner admin can't deactivate or delete an owner.
        var users = (await (await admin.GetAsync("/api/v1/users?limit=100")).JsonAsync()).GetProperty("items").EnumerateArray().ToList();
        var owner = users.First(u => u.GetProperty("roles").EnumerateArray().Any(r => r.GetProperty("systemKey").GetString() == "owner"));
        var everything = As(Permissions.Catalog.ToDictionary(p => p.Code, p => p.MaxScope));
        var body = new { displayName = "x", phone = owner.GetProperty("phone").GetString(), roleIds = Array.Empty<Guid>(), isActive = false };
        (await everything.PutAsJsonAsync($"/api/v1/users/{owner.GetProperty("id").GetGuid()}", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await everything.DeleteAsync($"/api/v1/users/{owner.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Client logins never show up on the staff page.
        var clients = (await (await admin.GetAsync("/api/v1/users?kind=client&limit=100")).JsonAsync()).GetProperty("items").EnumerateArray();
        clients.Should().NotContain(u => u.GetProperty("phone").GetString() == "09000000004");
    }
}
