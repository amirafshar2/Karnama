using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

public sealed class BrandAndTextsTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Brand_colours_and_name_are_admin_only_validated_and_public()
    {
        var editor = _f.CreateClient().WithToken(await _f.DemoTokenAsync("editor"));
        (await editor.PutAsJsonAsync("/api/v1/settings/brand", new { name = "x", colors = new { primary = "#FFFFFF", dark = "#000000" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var bad = await admin.PutAsJsonAsync("/api/v1/settings/brand", new { name = "x", colors = new { primary = "yellow", dark = "#000000" } });
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.JsonAsync()).GetProperty("title").GetString().Should().Contain("#RRGGBB");

        var ok = await admin.PutAsJsonAsync("/api/v1/settings/brand",
            new { name = "آژانس M", tagline = "شعار", colors = new { primary = "#feff00", dark = "#0a0a0a" } });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var pub = await (await _f.CreateClient().GetAsync("/api/v1/public/brand")).JsonAsync();
        pub.GetProperty("name").GetString().Should().Be("آژانس M");
        pub.GetProperty("colors").GetProperty("primary").GetString().Should().Be("#FEFF00");
    }

    [Fact]
    public async Task Logo_upload_accepts_real_images_only_and_serves_them()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));

        var svg = new MultipartFormDataContent { { new ByteArrayContent("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray()), "file", "logo.png" } };
        (await admin.PostAsync("/api/v1/settings/brand/logo", svg)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var png = new ByteArrayContent(TinyPng);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var r = await admin.PostAsync("/api/v1/settings/brand/logo", new MultipartFormDataContent { { png, "file", "logo.png" } });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = (await r.JsonAsync()).GetProperty("logoUrl").GetString()!;
        url.Should().MatchRegex("^/media/public/brand/[0-9a-f]{32}\\.png$");

        var img = await _f.CreateClient().GetAsync(url);
        img.StatusCode.Should().Be(HttpStatusCode.OK);
        img.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        (await _f.CreateClient().GetAsync("/media/public/brand/..%2F..%2Fappsettings.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Texts_override_the_bundle_and_reset_restores_default()
    {
        var editor = _f.CreateClient().WithToken(await _f.DemoTokenAsync("editor"));
        (await editor.PutAsJsonAsync("/api/v1/translations/fa/login.title", new { value = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        (await admin.PutAsJsonAsync("/api/v1/translations/fa/login.title", new { value = "ورود همکاران" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var bundle = await (await _f.CreateClient().GetAsync("/api/v1/i18n/fa")).JsonAsync();
        bundle.GetProperty("texts").GetProperty("login.title").GetString().Should().Be("ورود همکاران");

        (await admin.PutAsJsonAsync("/api/v1/translations/fa/bad%20key!", new { value = "x" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await admin.DeleteAsync("/api/v1/translations/fa/login.title")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await (await _f.CreateClient().GetAsync("/api/v1/i18n/fa")).JsonAsync();
        after.GetProperty("texts").TryGetProperty("login.title", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_shared_cache_fill_survives_the_first_caller_going_away()
    {
        // Two pages of one person ask for the same texts at once; the first one is closed mid-request.
        // The cache shares one database read between them: the second must still get its answer (it used to get a 500).
        var cache = _f.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        for (var round = 0; round < 5; round++)
        {
            await cache.RemoveByTagAsync(Karnama.Application.Localization.LocalizationService.CacheTag);
            var first = _f.Services.CreateAsyncScope();
            using var gone = new CancellationTokenSource();
            var a = first.ServiceProvider.GetRequiredService<Karnama.Application.Localization.LocalizationService>().GetBundleAsync("fa", gone.Token);
            await using var second = _f.Services.CreateAsyncScope();
            var b = second.ServiceProvider.GetRequiredService<Karnama.Application.Localization.LocalizationService>().GetBundleAsync("fa", CancellationToken.None);
            gone.Cancel();
            await first.DisposeAsync();
            try { await a; } catch (OperationCanceledException) { /* the closed page */ }
            (await b).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task A_cached_public_answer_is_not_lost_when_the_first_asker_leaves()
    {
        // Output caching used to run one request for everyone waiting on the same URL; when that first page was closed,
        // all the others got its "cancelled" as a 500.
        var store = _f.Services.GetRequiredService<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore>();
        var cache = _f.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        var failures = 0;
        for (var round = 0; round < 20; round++)
        {
            await store.EvictByTagAsync("public", default);
            await cache.RemoveByTagAsync(Karnama.Application.Localization.LocalizationService.CacheTag);
            using var gone = new CancellationTokenSource();
            var first = _f.CreateClient().GetAsync("/api/v1/i18n/languages", gone.Token);
            var others = Enumerable.Range(0, 4).Select(_ => _f.CreateClient().GetAsync("/api/v1/i18n/languages")).ToList();
            await Task.Delay(round % 3);
            gone.Cancel();
            try { await first; } catch (OperationCanceledException) { /* the closed page */ }
            foreach (var r in await Task.WhenAll(others)) if (r.StatusCode != HttpStatusCode.OK) failures++;
        }
        failures.Should().Be(0);
    }
}
