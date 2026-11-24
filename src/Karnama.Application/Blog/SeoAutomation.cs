using System.Text;
using System.Text.RegularExpressions;
using Karnama.Domain.Content;

namespace Karnama.Application.Blog;

/// <summary>
/// Automatic SEO for every post (docs/PROMPT2.md §18): title, meta description, slug, focus keyword, image alt text,
/// table of contents, reading time, FAQ, and a traffic-light checklist. Pure functions — no database.
/// </summary>
public static partial class SeoAutomation
{
    public const int TitleMax = 60;
    public const int MetaMin = 120;
    public const int MetaMax = 160;

    /// <summary>Target phrases from docs/SEO.md; a post that talks about one of them gets it as its keyword.</summary>
    public static readonly string[] TargetKeywords =
    [
        "Instagram-Betreuung Stuttgart", "Instagram-Betreuung", "Content-Produktion Stuttgart", "Content-Produktion", "Instagram-Admin Stuttgart", "Instagram-Admin",
        "Instagram-Werbung Stuttgart", "Instagram-Werbung", "Digitalagentur Stuttgart", "Digitales Marketing", "Reel-Produktion", "Produktfotografie",
        "SEO", "Instagram-Story", "Captions schreiben", "Branding",
    ];

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "eines", "einem", "einen", "und", "oder", "aber", "auch", "als", "am", "an", "auf", "aus", "bei", "bis", "da", "dass", "dein", "deine", "denn", "dich", "dir", "du", "durch", "er", "es", "euch", "für", "hat", "hatte", "haben", "habe", "hier", "ich", "ihr", "ihm", "ihn", "im", "in", "ist", "ja", "jede", "jeder", "jedes", "kann", "kein", "keine", "man", "mehr", "mich", "mir", "mit", "muss", "nach", "nicht", "noch", "nur", "ob", "ohne", "sehr", "sein", "seine", "sich", "sie", "sind", "so", "soll", "über", "um", "uns", "unser", "unter", "vom", "von", "vor", "war", "was", "weil", "wenn", "wer", "wie", "wir", "wird", "wo", "zu", "zum", "zur", "zwischen", "wurde", "werden", "sowie", "dieser", "diese", "dieses", "welche", "welcher", "schon", "the", "a", "and", "or", "of", "to", "on", "for", "is", "are", "with", "how", "what", "why",
    };

    [GeneratedRegex("[\\p{L}\\p{N}\\u200c]+")]
    private static partial Regex WordRx();

    public static string Normalize(string s) => s.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('ة', 'ه');

    public static IReadOnlyList<string> Words(string text) =>
        WordRx().Matches(Normalize(text)).Select(m => m.Value.Trim('‌').ToLowerInvariant()).Where(w => w.Length > 0).ToList();

    /// <summary>Cuts at the last whole word that fits; adds "…" when something was cut.</summary>
    public static string CutAtWord(string s, int max)
    {
        s = Regex.Replace(s.Trim(), "\\s+", " ");
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        var sp = cut.LastIndexOf(' ');
        if (sp > max / 2) cut = cut[..sp];
        return cut.TrimEnd(' ', ',', ',', '.', ';', ':', '-') + "…";
    }

    public static string Title(string title, string? brand)
    {
        title = Regex.Replace(title.Trim(), "\\s+", " ");
        if (!string.IsNullOrWhiteSpace(brand) && title.Length + 3 + brand.Length <= TitleMax) return $"{title} | {brand}";
        return CutAtWord(title, TitleMax);
    }

    public static string Meta(IReadOnlyList<string> paragraphs, string title)
    {
        var sb = new StringBuilder();
        foreach (var p in paragraphs)
        {
            var clean = Regex.Replace(p, "\\s+", " ").Trim();
            if (Words(clean).Count < 6 && sb.Length == 0) continue;   // skip one-line greetings
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(clean);
            if (sb.Length >= MetaMin) break;
        }
        if (sb.Length == 0) sb.Append(title);
        return CutAtWord(sb.ToString(), MetaMax);
    }

    /// <summary>Readable slug from the title, at most 60 characters, cut at a word boundary.</summary>
    public static string SlugFor(string title)
    {
        var s = Domain.Common.Slug.From(title, 200);
        if (s.Length <= 60) return s;
        var cut = s[..60];
        var dash = cut.LastIndexOf('-');
        return (dash > 20 ? cut[..dash] : cut).Trim('-');
    }

    public static string Keyword(string title, IReadOnlyList<TocItem> headings, string body)
    {
        var norm = Normalize(title + " " + body);
        var target = TargetKeywords.Where(k => norm.Contains(k, StringComparison.Ordinal)).OrderByDescending(k => k.Length).FirstOrDefault();
        if (target is not null) return target;

        var score = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(IReadOnlyList<string> words, double weight)
        {
            for (var i = 0; i < words.Count; i++)
            {
                if (Stop.Contains(words[i]) || words[i].Length < 2 || words[i].All(char.IsDigit)) continue;
                score[words[i]] = score.GetValueOrDefault(words[i]) + weight;
                if (i + 1 < words.Count && !Stop.Contains(words[i + 1]) && words[i + 1].Length >= 2)
                {
                    var bi = words[i] + " " + words[i + 1];
                    score[bi] = score.GetValueOrDefault(bi) + weight * 1.6;   // two-word phrases are better keywords
                }
            }
        }
        Add(Words(title), 4);
        foreach (var h in headings) Add(Words(h.Text), 2);
        Add(Words(body), 1);
        var titleWords = Words(title).ToHashSet();
        // The keyword should be in the title (that's where it counts most).
        var best = score.Where(kv => kv.Key.Split(' ').All(titleWords.Contains)).OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key.Length).FirstOrDefault();
        return best.Key ?? score.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
    }

    public static IReadOnlyList<FaqItem> Faq(RenderOutput r)
    {
        var faq = new List<FaqItem>();
        var inFaq = false;
        foreach (var s in r.Sections)
        {
            if (s.Level == 2) { inFaq = Regex.IsMatch(Normalize(s.Text), "Frage|Fragen|FAQ", RegexOptions.IgnoreCase); continue; }
            if (inFaq && s.Level == 3 && s.After.Count > 0) faq.Add(new FaqItem(s.Text, string.Join(" ", s.After)));
        }
        return faq.Take(20).ToList();
    }

    /// <param name="seoTitleOverride">The author's locked values, if any: the checklist scores what will really be published.</param>
    public static SeoResult Run(string title, string contentJson, string? brand, string? seoTitleOverride = null, string? metaOverride = null, string? keywordOverride = null)
    {
        string? AltFor(string near) => string.IsNullOrWhiteSpace(near) ? title : near;
        var r = TiptapRenderer.Render(contentJson, AltFor);
        var plain = r.Plain.ToString();
        var words = Words(plain).Count;
        var keyword = Keyword(title, r.Headings, plain);
        var seoTitle = Title(title, brand);
        var meta = Meta(r.Paragraphs, title);
        var faq = Faq(r);
        var checks = Checks(title, seoTitleOverride ?? seoTitle, metaOverride ?? meta, keywordOverride ?? keyword, r, words);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(checks.Sum(c => c.Level switch { "good" => 1.0, "warn" => 0.5, _ => 0 }) * 100 / checks.Count);
        return new SeoResult(seoTitle, meta, SlugFor(title), keyword, r.Headings, words, Math.Max(1, (int)Math.Ceiling(words / 200.0)), score, checks,
            faq, r.Images.Count(i => i.AltWasEmpty), r.Html.ToString(), r.Markdown.ToString().Trim(), plain, r.Images.FirstOrDefault()?.Src);
    }

    /// <summary>Re-scores with the fields that will actually be used (the author's overrides win).</summary>
    public static IReadOnlyList<SeoCheck> Checks(string title, string seoTitle, string meta, string keyword, RenderOutput r, int words)
    {
        var c = new List<SeoCheck>();
        var kw = Normalize(keyword);
        bool Has(string s) => kw.Length > 0 && Normalize(s).Contains(kw, StringComparison.OrdinalIgnoreCase);
        c.Add(new("keywordInTitle", kw.Length == 0 ? "bad" : Has(title) ? "good" : "warn", keyword));
        c.Add(new("keywordInIntro", kw.Length == 0 ? "bad" : r.Paragraphs.Count > 0 && Has(r.Paragraphs[0]) ? "good" : "warn", null));
        c.Add(new("length", words >= 600 ? "good" : words >= 300 ? "warn" : "bad", words.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var internalLinks = r.Links.Count(l => l.Internal && !l.Href.StartsWith('#'));
        c.Add(new("internalLinks", internalLinks >= 2 ? "good" : internalLinks == 1 ? "warn" : "bad", internalLinks.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var autoAlt = r.Images.Count(i => i.AltWasEmpty);
        c.Add(new("imageAlt", r.Images.Count == 0 ? "warn" : autoAlt == 0 ? "good" : "warn", autoAlt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var h2 = r.Headings.Count(h => h.Level == 2);
        c.Add(new("headings", h2 >= 2 ? "good" : h2 == 1 ? "warn" : "bad", h2.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        c.Add(new("metaLength", meta.Length is >= MetaMin and <= MetaMax ? "good" : meta.Length >= 70 ? "warn" : "bad", meta.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        c.Add(new("titleLength", seoTitle.Length is >= 25 and <= TitleMax ? "good" : seoTitle.Length <= 70 ? "warn" : "bad", seoTitle.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return c;
    }
}
