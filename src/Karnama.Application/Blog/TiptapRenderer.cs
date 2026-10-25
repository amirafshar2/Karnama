using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karnama.Domain.Content;

namespace Karnama.Application.Blog;

/// <summary>A heading, image, link or paragraph the renderer met — the SEO pass works from these, not from HTML.</summary>
public sealed record RenderedImage(string Src, string? Alt, string? Caption, string? NearestHeading, bool AltWasEmpty);
public sealed record RenderedLink(string Href, bool Internal);

public sealed class RenderOutput
{
    public StringBuilder Html { get; } = new();
    public StringBuilder Markdown { get; } = new();
    public List<string> Paragraphs { get; } = [];
    public List<TocItem> Headings { get; } = [];
    public List<RenderedImage> Images { get; } = [];
    public List<RenderedLink> Links { get; } = [];
    public List<(int Level, string Text, List<string> After)> Sections { get; } = [];
    public StringBuilder Plain { get; } = new();
}

/// <summary>
/// Editor JSON (Tiptap/ProseMirror) → HTML, Markdown and plain text, on the server. Only known node and mark types are
/// rendered and every text/attribute is encoded, so the HTML is safe by construction (an unknown node is dropped, a
/// "javascript:" link loses its link, an image from outside our own media is dropped).
/// </summary>
public static partial class TiptapRenderer
{
    public const int MaxDepth = 40;

    [GeneratedRegex("^/media/public/blog/[0-9a-f]{32}\\.(png|jpg|webp)$")]
    private static partial Regex OwnImage();

    public static RenderOutput Render(string json, Func<string, string?>? altFor = null)
    {
        var o = new RenderOutput();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? lastHeading = null;
        if (doc.RootElement.TryGetProperty("content", out var top) && top.ValueKind == JsonValueKind.Array)
            foreach (var n in top.EnumerateArray()) Block(n, 0, o, ids, ref lastHeading, altFor);
        return o;
    }

    private static string Type(JsonElement n) => n.ValueKind == JsonValueKind.Object && n.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
    private static JsonElement? Attrs(JsonElement n) => n.TryGetProperty("attrs", out var a) && a.ValueKind == JsonValueKind.Object ? a : null;
    private static string? Attr(JsonElement n, string name) => Attrs(n) is { } a && a.TryGetProperty(name, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        _ => null,
    } : null;
    private static IEnumerable<JsonElement> Children(JsonElement n) =>
        n.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray() : [];
    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static void Block(JsonElement n, int depth, RenderOutput o, HashSet<string> ids, ref string? lastHeading, Func<string, string?>? altFor)
    {
        if (depth > MaxDepth) return;
        var h = o.Html; var md = o.Markdown;
        switch (Type(n))
        {
            case "paragraph":
            {
                var (html, mdText, plain) = Inline(n, o);
                if (plain.Trim().Length == 0) return;
                h.Append("<p dir=\"auto\">").Append(html).Append("</p>");
                md.Append(mdText).Append("\n\n");
                o.Paragraphs.Add(plain.Trim());
                o.Plain.Append(plain).Append('\n');
                if (o.Sections.Count > 0) o.Sections[^1].After.Add(plain.Trim());
                return;
            }
            case "heading":
            {
                var level = int.TryParse(Attr(n, "level"), out var l) ? Math.Clamp(l, 2, 3) : 2;   // the page's h1 is the title
                var (html, _, plain) = Inline(n, o);
                plain = plain.Trim();
                if (plain.Length == 0) return;
                var id = Domain.Common.Slug.From(plain, 50);
                if (id.Length == 0) id = "h";
                var unique = id; var i = 2;
                while (!ids.Add(unique)) unique = $"{id}-{i++}";
                h.Append($"<h{level} id=\"{E(unique)}\" dir=\"auto\">").Append(html).Append($"</h{level}>");
                md.Append(new string('#', level)).Append(' ').Append(plain).Append("\n\n");
                o.Headings.Add(new TocItem(level, unique, plain));
                o.Sections.Add((level, plain, []));
                o.Plain.Append(plain).Append('\n');
                lastHeading = plain;
                return;
            }
            case "bulletList" or "orderedList":
            {
                var ordered = Type(n) == "orderedList";
                var start = ordered && int.TryParse(Attr(n, "start"), out var s) && s > 1 ? $" start=\"{s}\"" : "";
                h.Append(ordered ? $"<ol{start}>" : "<ul>");
                var k = 1;
                foreach (var li in Children(n).Where(c => Type(c) == "listItem"))
                {
                    h.Append("<li dir=\"auto\">");
                    var before = md.Length;
                    var inner = new RenderOutput();
                    string? lh = lastHeading;
                    foreach (var c in Children(li)) Block(c, depth + 1, inner, ids, ref lh, altFor);
                    // Inside a list item a single paragraph renders without <p>.
                    var innerHtml = inner.Html.ToString();
                    if (innerHtml.StartsWith("<p dir=\"auto\">", StringComparison.Ordinal) && innerHtml.IndexOf("<p", 1, StringComparison.Ordinal) < 0)
                        innerHtml = innerHtml["<p dir=\"auto\">".Length..^"</p>".Length];
                    h.Append(innerHtml).Append("</li>");
                    md.Append(ordered ? $"{k++}. " : "- ").Append(inner.Markdown.ToString().Trim().Replace("\n\n", "\n  ")).Append('\n');
                    o.Plain.Append(inner.Plain);
                    o.Links.AddRange(inner.Links); o.Images.AddRange(inner.Images);
                }
                h.Append(ordered ? "</ol>" : "</ul>");
                md.Append('\n');
                return;
            }
            case "blockquote":
            {
                var inner = new RenderOutput();
                string? lh = lastHeading;
                foreach (var c in Children(n)) Block(c, depth + 1, inner, ids, ref lh, altFor);
                if (inner.Html.Length == 0) return;
                h.Append("<blockquote dir=\"auto\">").Append(inner.Html).Append("</blockquote>");
                md.Append(string.Join('\n', inner.Markdown.ToString().Trim().Split('\n').Select(x => "> " + x))).Append("\n\n");
                o.Plain.Append(inner.Plain); o.Links.AddRange(inner.Links);
                return;
            }
            case "callout":
            {
                var tone = Attr(n, "tone") is "warning" ? "warning" : "tip";
                var inner = new RenderOutput();
                string? lh = lastHeading;
                foreach (var c in Children(n)) Block(c, depth + 1, inner, ids, ref lh, altFor);
                if (inner.Html.Length == 0) return;
                h.Append($"<aside class=\"callout callout-{tone}\" dir=\"auto\">").Append(inner.Html).Append("</aside>");
                md.Append(string.Join('\n', inner.Markdown.ToString().Trim().Split('\n').Select(x => "> " + x))).Append("\n\n");
                o.Plain.Append(inner.Plain); o.Links.AddRange(inner.Links);
                return;
            }
            case "codeBlock":
            {
                var text = string.Concat(Children(n).Where(c => Type(c) == "text").Select(c => c.TryGetProperty("text", out var t) ? t.GetString() : ""));
                h.Append("<pre dir=\"ltr\"><code>").Append(E(text)).Append("</code></pre>");
                md.Append("```\n").Append(text).Append("\n```\n\n");
                return;
            }
            case "horizontalRule":
                h.Append("<hr>");
                md.Append("---\n\n");
                return;
            case "image":
            {
                var src = Attr(n, "src") ?? "";
                if (!OwnImage().IsMatch(src)) return;
                var caption = Attr(n, "title")?.Trim();
                var alt = Attr(n, "alt")?.Trim();
                var altEmpty = string.IsNullOrEmpty(alt);
                if (altEmpty) alt = altFor?.Invoke(caption ?? lastHeading ?? "") ?? caption ?? lastHeading ?? "";
                var w = int.TryParse(Attr(n, "width"), out var ww) && ww is > 0 and < 10000 ? $" width=\"{ww}\"" : "";
                var hh = int.TryParse(Attr(n, "height"), out var hv) && hv is > 0 and < 10000 ? $" height=\"{hv}\"" : "";
                h.Append("<figure><img src=\"").Append(E(src)).Append("\" alt=\"").Append(E(alt ?? "")).Append('"').Append(w).Append(hh)
                    .Append(" loading=\"lazy\" decoding=\"async\">");
                if (!string.IsNullOrEmpty(caption)) h.Append("<figcaption dir=\"auto\">").Append(E(caption)).Append("</figcaption>");
                h.Append("</figure>");
                md.Append("![").Append((alt ?? "").Replace("]", "")).Append("](").Append(src).Append(")\n\n");
                o.Images.Add(new RenderedImage(src, alt, caption, lastHeading, altEmpty));
                return;
            }
            case "table":
            {
                h.Append("<div class=\"table-wrap\"><table><tbody>");
                var first = true;
                foreach (var row in Children(n).Where(r => Type(r) == "tableRow"))
                {
                    h.Append("<tr>");
                    var cells = new List<string>();
                    foreach (var cell in Children(row).Where(c => Type(c) is "tableCell" or "tableHeader"))
                    {
                        var tag = Type(cell) == "tableHeader" ? "th" : "td";
                        var cs = int.TryParse(Attr(cell, "colspan"), out var c1) && c1 is > 1 and < 20 ? $" colspan=\"{c1}\"" : "";
                        var rs = int.TryParse(Attr(cell, "rowspan"), out var r1) && r1 is > 1 and < 50 ? $" rowspan=\"{r1}\"" : "";
                        var inner = new RenderOutput();
                        string? lh = lastHeading;
                        foreach (var c in Children(cell)) Block(c, depth + 1, inner, ids, ref lh, altFor);
                        var innerHtml = inner.Html.ToString().Replace("<p dir=\"auto\">", "").Replace("</p>", "<br>");
                        if (innerHtml.EndsWith("<br>", StringComparison.Ordinal)) innerHtml = innerHtml[..^4];
                        h.Append($"<{tag}{cs}{rs} dir=\"auto\">").Append(innerHtml).Append($"</{tag}>");
                        cells.Add(inner.Markdown.ToString().Trim().Replace("\n", " ").Replace("|", "\\|"));
                        o.Plain.Append(inner.Plain); o.Links.AddRange(inner.Links);
                    }
                    h.Append("</tr>");
                    md.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                    if (first) { md.Append('|').Append(string.Concat(cells.Select(_ => " --- |"))).Append('\n'); first = false; }
                }
                h.Append("</tbody></table></div>");
                md.Append('\n');
                return;
            }
            case "ctaButton":
            {
                var href = SafeHref(Attr(n, "href"));
                var label = Attr(n, "label")?.Trim();
                if (href is null || string.IsNullOrEmpty(label)) return;
                h.Append("<p class=\"cta\"><a class=\"cta-button\" href=\"").Append(E(href.Value.Href)).Append('"')
                    .Append(href.Value.Internal ? "" : " rel=\"noopener noreferrer\" target=\"_blank\"").Append('>').Append(E(label)).Append("</a></p>");
                md.Append('[').Append(label).Append("](").Append(href.Value.Href).Append(")\n\n");
                o.Links.Add(new RenderedLink(href.Value.Href, href.Value.Internal));
                return;
            }
            default:
                return;   // unknown node: dropped
        }
    }

    /// <summary>http(s), mailto, tel, or a site-relative path. Everything else (javascript:, data:, //host) is refused.</summary>
    public static (string Href, bool Internal)? SafeHref(string? href)
    {
        href = href?.Trim();
        if (string.IsNullOrEmpty(href) || href.Length > 1000) return null;
        // Browsers drop tabs/newlines inside URLs, so "/\t/evil.example" would become "//evil.example".
        if (href.Any(char.IsControl)) return null;
        if (href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal) && !href.Contains('\\')) return (href, true);
        if (href.StartsWith('#')) return (href, true);
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u)) return null;
        return u.Scheme is "https" or "http" or "mailto" or "tel" ? (u.ToString(), false) : null;
    }

    private static (string Html, string Md, string Plain) Inline(JsonElement n, RenderOutput o)
    {
        var h = new StringBuilder(); var md = new StringBuilder(); var plain = new StringBuilder();
        foreach (var c in Children(n))
        {
            var type = Type(c);
            if (type == "hardBreak") { h.Append("<br>"); md.Append("  \n"); plain.Append(' '); continue; }
            if (type != "text" || !c.TryGetProperty("text", out var tv) || tv.ValueKind != JsonValueKind.String) continue;
            var text = tv.GetString()!;
            var open = new StringBuilder(); var close = new List<string>();
            var mdOpen = new StringBuilder(); var mdClose = new List<string>();
            string? linkHref = null;
            if (c.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
                foreach (var m in marks.EnumerateArray())
                {
                    switch (Type(m))
                    {
                        case "bold": open.Append("<strong>"); close.Insert(0, "</strong>"); mdOpen.Append("**"); mdClose.Insert(0, "**"); break;
                        case "italic": open.Append("<em>"); close.Insert(0, "</em>"); mdOpen.Append('_'); mdClose.Insert(0, "_"); break;
                        case "underline": open.Append("<u>"); close.Insert(0, "</u>"); break;
                        case "strike": open.Append("<s>"); close.Insert(0, "</s>"); mdOpen.Append("~~"); mdClose.Insert(0, "~~"); break;
                        case "code": open.Append("<code>"); close.Insert(0, "</code>"); mdOpen.Append('`'); mdClose.Insert(0, "`"); break;
                        case "link":
                            if (SafeHref(Attr(m, "href")) is { } l)
                            {
                                open.Append("<a href=\"").Append(E(l.Href)).Append('"').Append(l.Internal ? "" : " rel=\"noopener noreferrer\" target=\"_blank\"").Append('>');
                                close.Insert(0, "</a>");
                                linkHref = l.Href;
                                o.Links.Add(new RenderedLink(l.Href, l.Internal));
                            }
                            break;
                    }
                }
            h.Append(open).Append(E(text)).Append(string.Concat(close));
            var mdText = linkHref is null ? text : $"[{text}]({linkHref})";
            md.Append(mdOpen).Append(mdText).Append(string.Concat(mdClose));
            plain.Append(text);
        }
        return (h.ToString(), md.ToString(), plain.ToString());
    }
}
