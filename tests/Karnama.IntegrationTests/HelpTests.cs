using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N12: help center (PROMPT2 §13).</summary>
public sealed class HelpTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    private static string Doc(params object[] blocks) => JsonSerializer.Serialize(new { type = "doc", content = blocks });
    private static object P(string text) => new { type = "paragraph", content = new[] { new { type = "text", text } } };

    [Fact]
    public async Task Staff_and_clients_each_read_their_own_articles_drafts_stay_with_editors()
    {
        var editor = await As("editor");
        var client = await As("client");
        var staffList = (await Ok(await editor.GetAsync("/api/v1/help"))).EnumerateArray().ToList();
        staffList.Should().Contain(a => a.GetProperty("slug").GetString() == "ورود-و-خروج");
        staffList.Should().NotContain(a => a.GetProperty("audience").GetString() == "clients");
        var clientList = (await Ok(await client.GetAsync("/api/v1/help"))).EnumerateArray().ToList();
        clientList.Should().Contain(a => a.GetProperty("slug").GetString() == "تأیید-محتوا");
        clientList.Should().NotContain(a => a.GetProperty("audience").GetString() == "staff");
        (await client.GetAsync($"/api/v1/help/{Uri.EscapeDataString("ورود-و-خروج")}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Search finds words inside the body, not only titles.
        (await Ok(await editor.GetAsync($"/api/v1/help?q={Uri.EscapeDataString("وای‌فای دفتر")}"))).EnumerateArray()
            .Should().Contain(a => a.GetProperty("slug").GetString() == "ورود-و-خروج");

        var admin = await As("admin");
        var draft = await Ok(await admin.PostAsJsonAsync("/api/v1/help", new { title = "پیش‌نویس راهنما", category = "تست", audience = "everyone", contentJson = Doc(P("متن")), sortOrder = 1, isPublished = false }));
        (await editor.GetAsync($"/api/v1/help/{Uri.EscapeDataString(draft.GetProperty("slug").GetString()!)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await editor.PostAsJsonAsync("/api/v1/help", new { title = "x", category = "y", audience = "everyone", contentJson = Doc(P("z")), sortOrder = 1, isPublished = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/v1/help", new { title = "پیش‌نویس راهنما", category = "تست", audience = "everyone", contentJson = Doc(P("دوباره")), sortOrder = 1, isPublished = true }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Article_html_is_rendered_safely_on_the_server()
    {
        var admin = await As("admin");
        var json = Doc(
            new { type = "paragraph", content = new object[] { new { type = "text", text = "<script>alert(1)</script>" },
                new { type = "text", text = "لینک", marks = new[] { new { type = "link", attrs = new { href = "javascript:alert(1)" } } } } } },
            new { type = "iframe", attrs = new { src = "https://evil.example" } });
        var a = await Ok(await admin.PostAsJsonAsync("/api/v1/help", new { title = "امنیت متن راهنما", category = "تست", audience = "staff", contentJson = json, sortOrder = 1, isPublished = true }));
        var html = a.GetProperty("contentHtml").GetString()!;
        html.Should().NotContain("<script").And.NotContain("javascript:").And.NotContain("iframe").And.Contain("&lt;script&gt;");
        (await admin.PostAsJsonAsync("/api/v1/help", new { title = "خراب", category = "تست", audience = "staff", contentJson = "{not json", sortOrder = 1, isPublished = true }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Page_button_finds_the_most_specific_article()
    {
        var editor = await As("editor");
        (await Ok(await editor.GetAsync("/api/v1/help/page?key=attendance/calendar"))).GetProperty("slug").GetString().Should().Be("مرخصی-گرفتن");
        (await Ok(await editor.GetAsync("/api/v1/help/page?key=attendance/live"))).GetProperty("slug").GetString().Should().Be("ورود-و-خروج");
        (await editor.GetAsync("/api/v1/help/page?key=nothing-here")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
