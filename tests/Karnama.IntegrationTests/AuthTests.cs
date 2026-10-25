using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Karnama.IntegrationTests;

public sealed class AuthTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private const string OwnerPhone = AppFixture.OwnerPhone;

    private HttpClient Https() => _f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private async Task<(Guid Id, string Code)> RequestOtpAsync(HttpClient c, string phone)
    {
        var r = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await r.JsonAsync()).GetProperty("challengeId").GetGuid();
        _f.Sms.LastCode.TryGetValue(phone, out var code);
        return (id, code ?? string.Empty);
    }

    [Fact]
    public async Task Unknown_phone_gets_the_same_response_but_no_sms()
    {
        var before = _f.Sms.Sent;
        var r = await Https().PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "09359998877" });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
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
    public async Task Demo_accounts_cannot_log_in_with_otp()
    {
        var c = Https();
        var before = _f.Sms.Sent;
        var r = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "09000000001" });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
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
