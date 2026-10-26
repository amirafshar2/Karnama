using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

public sealed class AuthTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private const string OwnerPhone = AppFixture.OwnerPhone;

    private HttpClient Https() => _f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private async Task<(Guid Id, string Code)> RequestOtpAsync(HttpClient c, string phone, string? previous = null)
    {
        var r = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await r.JsonAsync()).GetProperty("challengeId").GetGuid();
        var code = await _f.Sms.WaitForCodeAsync(phone, previous);
        return (id, code);
    }

    [Fact]
    public async Task Unknown_phone_gets_the_same_response_but_no_sms()
    {
        var before = _f.Sms.Sent;
        var r = await Https().PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "09359998877" });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Task.Delay(300);
        _f.Sms.Sent.Should().Be(before);
    }

    [Fact]
    public async Task Resend_needs_60_seconds_and_max_3_per_10_minutes()
    {
        var c = Https();
        const string phone = "09125550001";
        (await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        _f.Clock.Advance(TimeSpan.FromSeconds(61));
        (await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        _f.Clock.Advance(TimeSpan.FromSeconds(61));
        (await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        _f.Clock.Advance(TimeSpan.FromSeconds(61));
        var fourth = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone });
        fourth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await fourth.JsonAsync()).GetProperty("code").GetString().Should().Be("auth.otpLimit");
    }

    [Fact]
    public async Task Owner_must_enroll_totp_and_a_totp_code_cannot_be_replayed()
    {
        var c = Https();
        var (id, code) = await RequestOtpAsync(c, OwnerPhone);

        var wrong = await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code = code == "000000" ? "111111" : "000000" });
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var verify = await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code });
        var body = await verify.JsonAsync();
        body.GetProperty("step").GetString().Should().Be("twoFactorSetupRequired");
        var twoFactorToken = body.GetProperty("twoFactorToken").GetString();

        // OTP is single use
        (await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var setup = await (await c.PostAsJsonAsync("/api/v1/auth/2fa/setup", new { twoFactorToken })).JsonAsync();
        var secret = setup.GetProperty("secret").GetString()!;
        var totp = Karnama.Infrastructure.Security.TotpService.Compute(
            Karnama.Infrastructure.Security.TotpService.Base32Decode(secret), _f.Clock.GetUtcNow().ToUnixTimeSeconds() / 30);

        var done = await c.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { twoFactorToken, code = totp });
        done.StatusCode.Should().Be(HttpStatusCode.OK);
        (await done.JsonAsync()).GetProperty("step").GetString().Should().Be("completed");
        done.Headers.GetValues("Set-Cookie").Should().Contain(v => v.Contains("karnama_rt=") && v.Contains("httponly") && v.Contains("samesite=strict") && v.Contains("secure"));

        var replay = await c.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { twoFactorToken, code = totp });
        replay.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Parallel_guesses_cannot_exceed_five_attempts_and_do_not_lock_the_account()
    {
        // Uses a separate staff user so the owner flow in other tests is untouched.
        const string phone = "09127770000";
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Karnama.Infrastructure.Persistence.AppDbContext>();
            db.Users.Add(Karnama.Domain.Identity.User.Create(phone, "کارگر تست", Karnama.Domain.Identity.UserKind.Staff).Value);
            await db.SaveChangesAsync();
        }

        var (id, code) = await RequestOtpAsync(Https(), phone);
        var wrong = code == "000000" ? "111111" : "000000";
        var guesses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            Https().PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code = wrong })));
        guesses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.UnprocessableEntity || r.StatusCode == HttpStatusCode.TooManyRequests);

        await using (var conn = new Npgsql.NpgsqlConnection(app.Pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT \"Attempts\" FROM otp_challenges WHERE \"Id\" = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            ((int)(await cmd.ExecuteScalarAsync())!).Should().Be(5);
        }

        // Even the right code is refused now…
        (await Https().PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // …but the account is not locked: a fresh code works (no lockout DoS through OTP guessing).
        _f.Clock.Advance(TimeSpan.FromSeconds(61));
        var (id2, code2) = await RequestOtpAsync(Https(), phone, code);
        var ok = await Https().PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id2, code = code2 });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.JsonAsync()).GetProperty("step").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task One_code_gives_one_session_even_when_raced()
    {
        const string phone = "09127770001";
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Karnama.Infrastructure.Persistence.AppDbContext>();
            db.Users.Add(Karnama.Domain.Identity.User.Create(phone, "کارگر دوم", Karnama.Domain.Identity.UserKind.Staff).Value);
            await db.SaveChangesAsync();
        }
        var (id, code) = await RequestOtpAsync(Https(), phone);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Https().PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = id, code })));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
    }

    [Fact]
    public async Task Setup_is_refused_in_a_demo_install()
    {
        var r = await _f.CreateClient().PostAsJsonAsync("/api/v1/setup",
            new { token = KarnamaFactory.SetupToken, phone = "09121234500", displayName = "x" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await (await _f.CreateClient().GetAsync("/api/v1/setup/status")).JsonAsync()).GetProperty("needsSetup").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Demo_accounts_cannot_log_in_with_otp()
    {
        var c = Https();
        var before = _f.Sms.Sent;
        var r = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "09000000001" });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Task.Delay(300);
        _f.Sms.Sent.Should().Be(before, "demo users never receive an SMS");
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_whole_family()
    {
        var c = _f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        var login = await c.PostAsJsonAsync("/api/v1/auth/demo", new { role = "editor" });
        login.EnsureSuccessStatusCode();
        var firstCookie = CookieOf(login);

        var r1 = await PostWithCookie(c, firstCookie);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCookie = CookieOf(r1);
        secondCookie.Should().NotBe(firstCookie);

        // Old token replayed -> 401 and the new token dies too.
        (await PostWithCookie(c, firstCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostWithCookie(c, secondCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_parallel_refreshes_with_the_same_token_only_one_wins()
    {
        var c = Https();
        var login = await c.PostAsJsonAsync("/api/v1/auth/demo", new { role = "writer" });
        var cookie = CookieOf(login);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostWithCookie(_f.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false }), cookie)));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Android_client_gets_refresh_token_in_body_and_refreshes_by_header()
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Client", "android");
        var login = await (await c.PostAsJsonAsync("/api/v1/auth/demo", new { role = "client" })).JsonAsync();
        var refresh = login.GetProperty("refreshToken").GetString()!;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("X-Refresh-Token", refresh);
        var r = await c.SendAsync(req);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.JsonAsync()).GetProperty("refreshToken").GetString().Should().NotBe(refresh);
        r.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Me_requires_token_and_returns_permissions()
    {
        (await _f.CreateClient().GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var token = await _f.DemoTokenAsync("editor");
        var me = await (await _f.CreateClient().WithToken(token).GetAsync("/api/v1/me")).JsonAsync();
        me.GetProperty("isDemo").GetBoolean().Should().BeTrue();
        me.GetProperty("phoneMasked").GetString().Should().Contain("***");
        me.GetProperty("permissions").GetProperty("work.run").GetString().Should().Be("assigned");
        me.GetProperty("permissions").TryGetProperty("roles.manage", out _).Should().BeFalse();
    }

    private static string CookieOf(HttpResponseMessage r) =>
        r.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("karnama_rt=", StringComparison.Ordinal)).Split(';')[0];

    private static Task<HttpResponseMessage> PostWithCookie(HttpClient c, string cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", cookie);
        return c.SendAsync(req);
    }
}

/// <summary>
/// Removed access takes effect at once (QA finding D B2). Own class (own app and clock): AuthTests moves its fake
/// clock forward, which would make freshly issued tokens "not valid yet" against the real-time JWT check.
/// </summary>
public sealed class SessionStampTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;

    [Fact]
    public async Task Role_grant_change_stops_the_old_token_at_once_and_refresh_brings_the_new_rights()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var leads = await admin.CreateRoleAsync("سرنخ تست", ("crm.leads", "own"));
        var (_, access, refresh) = await _f.StaffLoginAsync("09127772001", leads);
        var old = _f.CreateClient().WithToken(access);
        (await old.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await old.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.OK, "a valid token keeps working (cached check)");

        var role = await (await admin.GetAsync($"/api/v1/roles/{leads}")).JsonAsync();
        (await admin.PutAsJsonAsync($"/api/v1/roles/{leads}", new
        {
            name = new Dictionary<string, string> { ["fa"] = "سرنخ تست" },
            permissions = new[] { new { code = "chat.use", scope = "own" } },
            version = role.GetProperty("version").GetUInt32(),
        }, Http.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await old.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await old.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var fresh = await _f.RefreshAsync(refresh);
        fresh.Should().NotBeNull("the refresh flow issues a token with the new stamp");
        var c = _f.CreateClient().WithToken(fresh!.Value.Access);
        (await c.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var me = await (await c.GetAsync("/api/v1/me")).JsonAsync();
        me.GetProperty("permissions").TryGetProperty("crm.leads", out _).Should().BeFalse();
        me.GetProperty("permissions").GetProperty("chat.use").GetString().Should().Be("own");

        // Renaming a role without touching its grants doesn't end anybody's session.
        role = await (await admin.GetAsync($"/api/v1/roles/{leads}")).JsonAsync();
        (await admin.PutAsJsonAsync($"/api/v1/roles/{leads}", new
        {
            name = new Dictionary<string, string> { ["fa"] = "سرنخ تست ۲" },
            permissions = new[] { new { code = "chat.use", scope = "own" } },
            version = role.GetProperty("version").GetUInt32(),
        }, Http.Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Removing_a_role_or_deactivating_a_user_stops_their_tokens_at_once()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var leads = await admin.CreateRoleAsync("سرنخ تست دو", ("crm.leads", "own"));
        var managers = await admin.CreateRoleAsync("مدیر کاربران تست", ("users.manage", "all"), ("crm.leads", "all"));
        var (_, managerToken, _) = await _f.StaffLoginAsync("09127772002", managers);
        var manager = _f.CreateClient().WithToken(managerToken);
        var (userId, access, refresh) = await _f.StaffLoginAsync("09127772003", leads);
        var old = _f.CreateClient().WithToken(access);
        (await old.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Role taken away.
        (await manager.PutAsJsonAsync($"/api/v1/users/{userId}", new { displayName = "کاربر ۲۰۰۳", phone = "09127772003", roleIds = Array.Empty<Guid>(), isActive = true }, Http.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await old.GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var next = await _f.RefreshAsync(refresh);
        next.Should().NotBeNull();
        (await _f.CreateClient().WithToken(next!.Value.Access).GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Deactivated: the current token dies and so does the refresh.
        var current = _f.CreateClient().WithToken(next.Value.Access);
        (await current.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PutAsJsonAsync($"/api/v1/users/{userId}", new { displayName = "کاربر ۲۰۰۳", phone = "09127772003", roleIds = Array.Empty<Guid>(), isActive = false }, Http.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await current.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _f.RefreshAsync(next.Value.Refresh)).Should().BeNull();

        // The manager's own session was never touched.
        (await manager.GetAsync("/api/v1/users?limit=1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
