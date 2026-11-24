using System.Text.Json.Nodes;
using FluentAssertions;
using Karnama.Application.Blog;

namespace Karnama.UnitTests;

public sealed class BlogSeoTests
{
    private static string Doc(params JsonNode[] blocks) => new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(blocks) }.ToJsonString();
    private static JsonObject P(string t, JsonArray? marks = null)
    {
        var text = new JsonObject { ["type"] = "text", ["text"] = t };
        if (marks is not null) text["marks"] = marks;
        return new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(text) };
    }
    private static JsonObject H(int l, string t) => new() { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = l }, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = t }) };
    private static JsonArray LinkMark(string href) => new(new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } });

    [Theory]
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Unsafe_links_are_dropped(string href) =>
        Karnama.Application.Blog.TiptapRenderer.SafeHref(href).Should().BeNull();

    [Fact]
    public void Renderer_encodes_text_and_drops_dangerous_links_images_and_unknown_nodes()
    {
        var json = Doc(
            P("<script>alert(1)</script>"),
            P("bad", LinkMark("javascript:alert(1)")),
            P("proto", LinkMark("//evil.com/x")),
            P("ok", LinkMark("/blog/x")),
            new JsonObject { ["type"] = "image", ["attrs"] = new JsonObject { ["src"] = "https://evil.com/a.png\" onerror=\"x", ["alt"] = "a" } },
            new JsonObject { ["type"] = "iframe", ["attrs"] = new JsonObject { ["src"] = "https://evil.com" } },
            new JsonObject { ["type"] = "image", ["attrs"] = new JsonObject { ["src"] = "/media/public/blog/0123456789abcdef0123456789abcdef.webp", ["alt"] = "\"><img onerror=1>", ["width"] = 800, ["height"] = 600 } });
        var html = TiptapRenderer.Render(json).Html.ToString();
        html.Should().Contain("&lt;script&gt;").And.NotContain("<script");
        html.Should().NotContain("javascript:").And.NotContain("evil.com").And.NotContain("<iframe").And.NotContain("onerror=\"");
        html.Should().Contain("<a href=\"/blog/x\">ok</a>");
        html.Should().Contain("width=\"800\" height=\"600\" loading=\"lazy\"");
    }

    [Fact]
    public void Seo_is_generated_without_any_author_input()
    {
        var json = Doc(
            P("Hallo!"),
            P("Instagram-Betreuung für Unternehmen in Stuttgart beginnt mit einem festen Veröffentlichungsplan und einheitlicher Tonalität und bindet Kunden."),
            H(2, "Veröffentlichungsplan"), P("Drei Posts pro Woche reichen."),
            H(1, "Häufige Fragen"),
            H(3, "Wie oft sollten wir posten?"), P("Dreimal pro Woche."),
            new JsonObject { ["type"] = "image", ["attrs"] = new JsonObject { ["src"] = "/media/public/blog/0123456789abcdef0123456789abcdef.webp" } });
        var title = "Der komplette Leitfaden zur Instagram-Betreuung für Cafés und Restaurants in Stuttgart 2026";
        var r = SeoAutomation.Run(title, json, "Karnama");
        r.SeoTitle.Length.Should().BeLessThanOrEqualTo(60);
        r.SeoTitle.Should().EndWith("…");
        r.MetaDescription.Should().StartWith("Instagram-Betreuung").And.NotContain("Hallo");
        r.MetaDescription.Length.Should().BeLessThanOrEqualTo(160);
        r.Slug.Should().StartWith("der-komplette-leitfaden").And.NotContain("--");
        r.Slug.Length.Should().BeLessThanOrEqualTo(60);
        r.FocusKeyword.Should().Be("Instagram-Betreuung");   // a target phrase from docs/SEO.md
        r.Toc.Should().HaveCount(3);
        r.Toc[1].Level.Should().Be(2);   // h1 inside the body becomes h2 (the title is the page's h1)
        r.Faq.Should().ContainSingle(f => f.Question.StartsWith("Wie oft") && f.Answer == "Dreimal pro Woche.");
        r.AltFilled.Should().Be(1);
        r.Html.Should().Contain("alt=\"Wie oft sollten wir posten?\"");   // nearest heading above the image
        r.Checks.Should().Contain(c => c.Key == "internalLinks" && c.Level == "bad");
        r.ReadingMinutes.Should().Be(1);
        r.Score.Should().BeInRange(1, 99);
    }

    [Fact]
    public void Short_titles_get_the_brand_and_slugs_normalize_arabic_letters()
    {
        SeoAutomation.Title("Reels oder Posts?", "Karnama").Should().Be("Reels oder Posts? | Karnama");
        SeoAutomation.SlugFor("Geburtstagstorte 2026").Should().Be("geburtstagstorte-2026");
    }
}
