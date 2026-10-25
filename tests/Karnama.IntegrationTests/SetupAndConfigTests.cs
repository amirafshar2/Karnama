using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Karnama.IntegrationTests;

public sealed class EmptyInstallTests(EmptyAppFixture app) : IClassFixture<EmptyAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;

    [Fact]
    public async Task Empty_install_has_no_demo_and_setup_runs_exactly_once()
    {
        var c = _f.CreateClient();
        (await c.GetAsync("/api/v1/auth/demo/options")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/v1/auth/demo", new { role = "admin" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await c.GetAsync("/api/v1/setup/status")).JsonAsync()).GetProperty("needsSetup").GetBoolean().Should().BeTrue();

        (await c.PostAsJsonAsync("/api/v1/setup", new { token = "wrong-token", phone = "09121234567", displayName = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Parallel first-run attempts: exactly one wins.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => _f.CreateClient().PostAsJsonAsync("/api/v1/setup",
            new { token = KarnamaFactory.SetupToken, phone = $"0912123456{i}", displayName = "مالک", brandName = "آژانس تست" })));
        attempts.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        attempts.Where(r => r.StatusCode != HttpStatusCode.NoContent).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);

        (await (await c.GetAsync("/api/v1/setup/status")).JsonAsync()).GetProperty("needsSetup").GetBoolean().Should().BeFalse();
        (await (await c.GetAsync("/api/v1/public/brand")).JsonAsync()).GetProperty("name").GetString().Should().Be("آژانس تست");
    }

    [Fact]
    public async Task Translation_bundle_supports_etag()
    {
        var c = _f.CreateClient();
        var first = await c.GetAsync("/api/v1/i18n/en");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.Tag;

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/i18n/en");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        (await c.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await c.GetAsync("/api/v1/i18n/xx")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_and_security_headers()
    {
        var c = _f.CreateClient();
        (await c.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await c.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        ready.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
    }
}

public sealed class StartupGuardTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void Staging_with_demo_login_refuses_to_start()
    {
        using var f = new KarnamaFactory(pg.ConnectionString, demoLogin: true, environment: "Staging");
        var act = () => f.CreateClient();
        act.Should().Throw<InvalidOperationException>().WithMessage("*DemoLogin__Enabled*");
    }

    [Fact]
    public void Production_with_demo_login_refuses_to_start()
    {
        using var f = new KarnamaFactory(pg.ConnectionString, demoLogin: true, environment: "Production");
        var act = () => f.CreateClient();
        act.Should().Throw<InvalidOperationException>().WithMessage("*DemoLogin__Enabled*");
    }
}
