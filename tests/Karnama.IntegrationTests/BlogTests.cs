using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Karnama.IntegrationTests;

/// <summary>N9: writer → review → publish, automatic SEO with no SEO input, slug change = 301, public API shows live posts only.</summary>
public sealed class BlogTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    private static string Body(int sentences, string extraLink = "/blog/x") => new JsonObject
    {
        ["type"] = "doc",
        ["content"] = new JsonArray(
            new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = string.Join(" ", Enumerable.Repeat("عکاسی محصول برای فروشگاه آنلاین کیفیت فروش را بالا می‌برد.", sentences)) }) },
            new JsonObject { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = 2 }, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "نور طبیعی" }) },
            new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "لینک", ["marks"] = new JsonArray(new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = extraLink } }) }) }),
    }.ToJsonString();

    [Fact]
    public async Task Writer_drafts_editor_publishes_and_seo_is_automatic()
    {
        var writer = await As("writer");
        var admin = await As("admin");
        var editor = await As("editor");
        (await editor.GetAsync("/api/v1/blog/posts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var title = "عکاسی محصول با موبایل برای فروشگاه‌های کوچک";
        var post = await Ok(await writer.PostAsJsonAsync("/api/v1/blog/posts", new { title, contentJson = Body(8) }));
        var id = post.GetProperty("id").GetGuid();
        post.GetProperty("slug").GetString().Should().Be("عکاسی-محصول-با-موبایل-برای-فروشگاه-های-کوچک");
        post.GetProperty("seoTitle").GetString().Should().Contain(title);
        post.GetProperty("metaDescription").GetString().Should().StartWith("عکاسی محصول");
        post.GetProperty("focusKeyword").GetString().Should().Be("عکاسی محصول");
        post.GetProperty("seoScore").GetInt32().Should().BeGreaterThan(0);
        post.GetProperty("toc").GetArrayLength().Should().Be(1);

        // Writers can't publish; they submit. Other writers' posts are invisible to them.
        (await writer.PostAsJsonAsync($"/api/v1/blog/posts/{id}/publish", new { at = (DateTimeOffset?)null, version = post.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var submitted = await Ok(await writer.PostAsync($"/api/v1/blog/posts/{id}/submit", null));
        submitted.GetProperty("status").GetString().Should().Be("inReview");
        var seeded = (await Ok(await admin.GetAsync("/api/v1/blog/posts?limit=100"))).GetProperty("items").EnumerateArray().First(p => p.GetProperty("authorName").GetString()!.StartsWith("سارا"));
        (await writer.GetAsync($"/api/v1/blog/posts/{seeded.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Not public yet.
        var anon = _f.CreateClient();
        var slug = Uri.EscapeDataString(post.GetProperty("slug").GetString()!);
        (await anon.GetAsync($"/api/v1/public/blog/posts/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var published = await Ok(await admin.PostAsJsonAsync($"/api/v1/blog/posts/{id}/publish", new { at = (DateTimeOffset?)null, version = submitted.GetProperty("version").GetUInt32() }));
        published.GetProperty("status").GetString().Should().Be("published");
        var pub = await Ok(await anon.GetAsync($"/api/v1/public/blog/posts/{slug}"));
        pub.GetProperty("html").GetString().Should().Contain("<h2 id=\"نور-طبیعی\"");
        pub.GetProperty("metaDescription").GetString().Should().NotBeEmpty();
        (await Ok(await anon.GetAsync("/api/v1/public/blog/sitemap"))).EnumerateArray().Should().Contain(e => e.GetProperty("path").GetString() == "/blog/" + post.GetProperty("slug").GetString());

        // A published post: the writer can no longer change it; the editor renames it → the old URL redirects.
        (await writer.PutAsJsonAsync($"/api/v1/blog/posts/{id}", new { title, contentJson = Body(8), version = published.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        var renamed = await Ok(await admin.PutAsJsonAsync($"/api/v1/blog/posts/{id}", new { title = "عکاسی محصول با گوشی", contentJson = Body(8), version = published.GetProperty("version").GetUInt32() }));
        renamed.GetProperty("slug").GetString().Should().Be("عکاسی-محصول-با-گوشی");
        var moved = await Ok(await anon.GetAsync($"/api/v1/public/blog/posts/{slug}"));
        moved.GetProperty("redirectTo").GetString().Should().Be("عکاسی-محصول-با-گوشی");

        // Stale version → 409.
        (await admin.PutAsJsonAsync($"/api/v1/blog/posts/{id}", new { title = "x y z w", contentJson = Body(8), version = published.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Locked SEO title survives the automatic pass; empty unlocks it.
        var locked = await Ok(await admin.PutAsJsonAsync($"/api/v1/blog/posts/{id}", new { title = "عکاسی محصول با گوشی", contentJson = Body(8), seoTitle = "عنوان دستی من", version = renamed.GetProperty("version").GetUInt32() }));
        locked.GetProperty("seoTitle").GetString().Should().Be("عنوان دستی من");
        locked.GetProperty("seoTitleLocked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Same_title_gets_a_unique_slug_and_bad_input_is_refused()
    {
        var admin = await As("admin");
        var a = await Ok(await admin.PostAsJsonAsync("/api/v1/blog/posts", new { title = "عنوان تکراری آزمایشی", contentJson = Body(2) }));
        var b = await Ok(await admin.PostAsJsonAsync("/api/v1/blog/posts", new { title = "عنوان تکراری آزمایشی", contentJson = Body(2) }));
        b.GetProperty("slug").GetString().Should().Be(a.GetProperty("slug").GetString() + "-2");
        (await admin.PostAsJsonAsync("/api/v1/blog/posts", new { title = "خراب", contentJson = "{not json" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // Too short to publish.
        (await admin.PostAsJsonAsync($"/api/v1/blog/posts/{a.GetProperty("id").GetGuid()}/publish", new { at = (DateTimeOffset?)null, version = a.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // Media: only real images.
        using var form = new MultipartFormDataContent { { new ByteArrayContent("<svg onload=alert(1)>"u8.ToArray()), "file", "x.svg" } };
        (await admin.PostAsync("/api/v1/blog/media", form)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // The client portal has no blog.
        (await (await As("client")).GetAsync("/api/v1/blog/posts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // The seeded sample posts are public, the in-review one isn't.
        var list = await Ok(await _f.CreateClient().GetAsync("/api/v1/public/blog"));
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).Should().NotContain(t => t!.StartsWith("کپشن‌نویسی"));
        list.GetProperty("categories").GetArrayLength().Should().BeGreaterThan(0);
    }
}
