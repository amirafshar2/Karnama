using Ganss.Xss;
using Karnama.Application.Abstractions;
using Markdig;

namespace Karnama.Infrastructure.Content;

/// <summary>Markdown → HTML with a strict whitelist (no scripts, no event handlers, no style, links http(s)/mailto/tel only).</summary>
internal sealed class ContentRenderer : IContentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml() // raw HTML inside markdown is shown as text, never executed
        .UseAutoLinks()
        .UsePipeTables()
        .UseEmphasisExtras()
        .Build();

    private readonly HtmlSanitizer _sanitizer;

    public ContentRenderer()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p", "br", "h2", "h3", "h4", "strong", "em", "b", "i", "u", "s", "del", "ul", "ol", "li", "blockquote",
                "a", "hr", "code", "pre", "table", "thead", "tbody", "tr", "th", "td", "img", "figure", "figcaption",
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "title", "src", "alt", "width", "height", "dir" },
            AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" },
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src" },
        });
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { TagName: "A" } a)
            {
                a.SetAttribute("rel", "nofollow noopener noreferrer");
                a.SetAttribute("target", "_blank");
            }
        };
    }

    public string MarkdownToSafeHtml(string markdown) => _sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));

    public string SanitizeHtml(string html) => _sanitizer.Sanitize(html);
}
