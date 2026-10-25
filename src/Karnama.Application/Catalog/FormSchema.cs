using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Karnama.Domain.Common;

namespace Karnama.Application.Catalog;

/// <summary>One field of a service's order form (form builder). Stored as a JSON array, versioned.</summary>
public sealed record FormField(
    string Key, string Type, Dictionary<string, string> Label, bool Required, Dictionary<string, string>? Help,
    decimal? Min, decimal? Max, List<FormOption>? Options);

public sealed record FormOption(string Value, Dictionary<string, string> Label);

public static partial class FormSchema
{
    public static readonly string[] Types = ["text", "textarea", "number", "date", "datetime", "select", "multiselect", "file", "url", "platform", "color"];
    public const int MaxFields = 40;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [GeneratedRegex("^[a-z][a-zA-Z0-9_]{0,39}$")]
    private static partial Regex KeyPattern();

    /// <summary>Validates and returns canonical JSON (so an unchanged form doesn't create a new version).</summary>
    public static Result<string> Normalize(IReadOnlyList<FormField>? fields)
    {
        fields ??= [];
        if (fields.Count > MaxFields) return Error.Validation("forms.tooMany", "حداکثر ۴۰ فیلد.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var clean = new List<FormField>();
        foreach (var f in fields)
        {
            if (!KeyPattern().IsMatch(f.Key ?? string.Empty)) return Error.Validation("forms.invalidKey", $"شناسه‌ی فیلد «{f.Key}» باید انگلیسی و بدون فاصله باشد (مثل topic).");
            if (!keys.Add(f.Key!)) return Error.Validation("forms.duplicateKey", $"شناسه‌ی «{f.Key}» تکراری است.");
            if (!Types.Contains(f.Type)) return Error.Validation("forms.invalidType", $"نوع فیلد «{f.Type}» معتبر نیست.");
            var label = new LocalizedText(f.Label);
            if (label.IsEmpty) return Error.Validation("forms.labelRequired", "برچسب هر فیلد الزامی است.");
            if (f.Min is { } min && f.Max is { } max && max < min) return Error.Validation("forms.minMax", "حداکثر باید از حداقل بزرگ‌تر باشد.");
            List<FormOption>? options = null;
            if (f.Type is "select" or "multiselect")
            {
                if (f.Options is null || f.Options.Count is 0 or > 50) return Error.Validation("forms.options", $"فیلد «{f.Key}» باید ۱ تا ۵۰ گزینه داشته باشد.");
                options = f.Options.Select(o => new FormOption(o.Value.Trim(), new LocalizedText(o.Label).Values.ToDictionary())).ToList();
                if (options.Any(o => o.Value.Length is 0 or > 60)) return Error.Validation("forms.optionValue", "مقدار هر گزینه الزامی است.");
                if (options.Select(o => o.Value).Distinct().Count() != options.Count) return Error.Validation("forms.optionDuplicate", "گزینه‌ی تکراری.");
            }
            clean.Add(new FormField(f.Key!, f.Type, label.Values.ToDictionary(), f.Required,
                f.Help is null ? null : new LocalizedText(f.Help).Values.ToDictionary(), f.Min, f.Max, options));
        }
        return JsonSerializer.Serialize(clean, Json);
    }

    public static IReadOnlyList<FormField> Parse(string json) =>
        JsonSerializer.Deserialize<List<FormField>>(json, Json) ?? [];
}
