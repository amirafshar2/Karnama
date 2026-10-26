using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Karnama.IntegrationTests;

/// <summary>N10: home content (SEO required), portfolio needs written consent, real stats only, consultation form → lead + notification.</summary>
public sealed class SiteTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    [Fact]
    public async Task Home_shows_real_data_and_editable_content_with_required_seo()
    {
        var anon = _f.CreateClient();
        var home = await Ok(await anon.GetAsync("/api/v1/public/site/home"));
        home.GetProperty("content").GetProperty("seoTitle").GetString().Should().NotBeEmpty();
        home.GetProperty("stats").GetProperty("clients").GetInt32().Should().BeGreaterThan(0);
        home.GetProperty("cases").EnumerateArray().Should().Contain(c => c.GetProperty("isSample").GetBoolean());
        home.GetProperty("packages").GetArrayLength().Should().BeGreaterThan(0);
        home.GetProperty("services").GetArrayLength().Should().BeGreaterThan(0);

        var admin = await As("admin");
        var content = await Ok(await admin.GetAsync("/api/v1/site/home"));
        var edited = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content.GetRawText())!;
        edited["heroTitle"] = JsonSerializer.SerializeToElement("تیتر تازه‌ی آزمایشی");
        edited["seoDescription"] = JsonSerializer.SerializeToElement("کوتاه");
        (await admin.PutAsJsonAsync("/api/v1/site/home", edited)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        edited["seoDescription"] = content.GetProperty("seoDescription");
        await Ok(await admin.PutAsJsonAsync("/api/v1/site/home", edited));
        (await Ok(await anon.GetAsync("/api/v1/public/site/home"))).GetProperty("content").GetProperty("heroTitle").GetString().Should().Be("تیتر تازه‌ی آزمایشی");
        (await (await As("editor")).PutAsJsonAsync("/api/v1/site/home", edited)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hero_video_and_photos_are_uploaded_checked_and_served_with_ranges()
    {
        var admin = await As("admin");
        // A minimal MP4 header ("ftyp" at offset 4) is accepted; a renamed text file is not.
        var mp4 = new byte[4096];
        new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' }.CopyTo(mp4, 0);
        using (var bad = new MultipartFormDataContent { { new ByteArrayContent("not a video at all"u8.ToArray()), "file", "x.mp4" } })
            (await admin.PostAsync("/api/v1/site/media?video=true", bad)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var vf = new MultipartFormDataContent { { new ByteArrayContent(mp4), "file", "hero.mp4" } };
        var video = await Ok(await admin.PostAsync("/api/v1/site/media?video=true", vf));
        var videoId = video.GetProperty("id").GetString()!;
        videoId.Should().MatchRegex("^site/[0-9a-f]{32}\\.mp4$");
        using var pf = new MultipartFormDataContent { { new ByteArrayContent(Png()), "file", "p.png" } };
        var photoId = (await Ok(await admin.PostAsync("/api/v1/site/media", pf))).GetProperty("id").GetString()!;
        using (var ef = new MultipartFormDataContent { { new ByteArrayContent(Png()), "file", "p.png" } })
            (await (await As("editor")).PostAsync("/api/v1/site/media", ef)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Range requests work, so phones can start the clip without downloading all of it.
        var req = new HttpRequestMessage(HttpMethod.Get, video.GetProperty("url").GetString());
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
        var part = await _f.CreateClient().SendAsync(req);
        part.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        part.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");

        var content = await Ok(await admin.GetAsync("/api/v1/site/home"));
        var edited = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content.GetRawText())!;
        // Only files stored by us: a free link is refused.
        edited["heroVideo"] = JsonSerializer.SerializeToElement("https://evil.example/x.mp4");
        (await admin.PutAsJsonAsync("/api/v1/site/home", edited)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        edited["heroVideo"] = JsonSerializer.SerializeToElement(videoId);
        edited["heroImages"] = JsonSerializer.SerializeToElement(new[] { photoId });
        await Ok(await admin.PutAsJsonAsync("/api/v1/site/home", edited));

        var hero = (await Ok(await _f.CreateClient().GetAsync("/api/v1/public/site/home"))).GetProperty("hero");
        hero.GetProperty("videoUrl").GetString().Should().Be("/media/public/" + videoId);
        hero.GetProperty("images").EnumerateArray().Select(x => x.GetString()).Should().Equal("/media/public/" + photoId);
    }

    [Fact]
    public async Task Case_is_published_only_with_written_consent()
    {
        var admin = await As("admin");
        var body = new Dictionary<string, object?>
        {
            ["businessName"] = "نانوایی واقعی", ["industry"] = "نانوایی", ["city"] = "شیراز", ["services"] = new[] { "ریلز" },
            ["followersBefore"] = 1000, ["followersAfter"] = 5000, ["revenueBefore"] = 100_000_000, ["revenueAfter"] = 150_000_000, ["revenueAsPercent"] = true,
            ["consentObtained"] = false, ["featured"] = false, ["sortOrder"] = 9,
        };
        // Sample cases never count as results.
        var before = (await Ok(await _f.CreateClient().GetAsync("/api/v1/public/site/home"))).GetProperty("stats").GetProperty("medianFollowerGrowth");
        before.ValueKind.Should().Be(JsonValueKind.Null);
        var c = await Ok(await admin.PostAsJsonAsync("/api/v1/cases", body));
        var id = c.GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/api/v1/cases/{id}/publish", new { published = true })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Consent photo (private), then publish.
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Png()), "file", "consent.png" } };
        var up = await Ok(await admin.PostAsync("/api/v1/cases/media?consent=true", form));
        var consentId = up.GetProperty("id").GetString()!;
        consentId.Should().StartWith("consent/");
        (await _f.CreateClient().GetAsync("/media/public/" + consentId)).StatusCode.Should().Be(HttpStatusCode.NotFound);   // never public
        (await admin.GetAsync(up.GetProperty("url").GetString())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await As("editor")).GetAsync(up.GetProperty("url").GetString())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        body["consentObtained"] = true; body["consentFile"] = consentId; body["version"] = c.GetProperty("version").GetUInt32();
        await Ok(await admin.PutAsJsonAsync($"/api/v1/cases/{id}", body));
        await Ok(await admin.PostAsJsonAsync($"/api/v1/cases/{id}/publish", new { published = true }));

        var pub = await Ok(await _f.CreateClient().GetAsync("/api/v1/public/site/cases/" + Uri.EscapeDataString(c.GetProperty("slug").GetString()!)));
        pub.GetProperty("card").GetProperty("followerGrowth").GetInt32().Should().Be(400);
        pub.GetProperty("card").GetProperty("revenueGrowth").GetInt32().Should().Be(50);
        pub.GetProperty("revenueBefore").ValueKind.Should().Be(JsonValueKind.Null);   // the client agreed to the percentage only
        // A real case with measured growth now counts in the stats.
        (await Ok(await _f.CreateClient().GetAsync("/api/v1/public/site/home"))).GetProperty("stats").GetProperty("medianFollowerGrowth").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task Consultation_form_creates_one_lead_and_notifies_managers()
    {
        var anon = _f.CreateClient();
        (await anon.PostAsJsonAsync("/api/v1/public/site/leads", new { name = "مهمان", phone = "123" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await anon.PostAsJsonAsync("/api/v1/public/site/leads", new { name = "مهمان آزمایشی", phone = "09121112233", business = "شیرینی", message = "سلام", source = "/" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await anon.PostAsJsonAsync("/api/v1/public/site/leads", new { name = "مهمان آزمایشی", phone = "09121112233" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        // Honeypot: accepted but dropped.
        (await anon.PostAsJsonAsync("/api/v1/public/site/leads", new { name = "ربات", phone = "09121119999", website = "http://spam" })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var admin = await As("admin");
        var leads = await Ok(await admin.GetAsync("/api/v1/crm/leads?limit=100"));
        leads.GetProperty("items").EnumerateArray().Count(l => l.GetProperty("phone").GetString() == "09121112233").Should().Be(1);
        leads.GetProperty("items").EnumerateArray().Should().NotContain(l => l.GetProperty("phone").GetString() == "09121119999");
        (await Ok(await admin.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray().Should().Contain(n => n.GetProperty("kind").GetString() == "crm.newLead");
        (await (await As("editor")).GetAsync("/api/v1/crm/leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
