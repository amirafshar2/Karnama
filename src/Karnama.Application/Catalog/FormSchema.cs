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

    /// <summary>
    /// Checks an order's answers against the form it was placed with and returns canonical JSON with only known keys.
    /// File fields are answered by uploading files to the order afterwards, so they are skipped here.
    /// </summary>
    public static Result<string> ValidateData(IReadOnlyList<FormField> fields, JsonElement? data)
    {
        var input = data is { ValueKind: JsonValueKind.Object } d ? d : default;
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (f.Type == "file") continue;
            var label = new LocalizedText(f.Label).Get("fa");
            JsonElement v = default;
            var has = input.ValueKind == JsonValueKind.Object && input.TryGetProperty(f.Key, out v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                      && !(v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()))
                      && !(v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 0);
            if (!has)
            {
                if (f.Required) return Error.Validation("forms.required", $"«{label}» را پر کنید.");
                continue;
            }
            switch (f.Type)
            {
                case "number":
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var n)) return Error.Validation("forms.number", $"«{label}» باید عدد باشد.");
                    if (f.Min is { } min && n < min || f.Max is { } max && n > max) return Error.Validation("forms.range", $"«{label}» خارج از محدوده است.");
                    output[f.Key] = n;
                    break;
                case "multiselect":
                    if (v.ValueKind != JsonValueKind.Array) return Error.Validation("forms.multi", $"«{label}» معتبر نیست.");
                    var picked = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToList();
                    if (picked.Any(p => f.Options?.All(o => o.Value != p) ?? true)) return Error.Validation("forms.option", $"گزینه‌ی «{label}» معتبر نیست.");
                    output[f.Key] = picked.Distinct().ToList();
                    break;
                default:
                    if (v.ValueKind != JsonValueKind.String) return Error.Validation("forms.text", $"«{label}» معتبر نیست.");
                    var text = v.GetString()!.Trim();
                    if (text.Length > (f.Type == "textarea" ? 4000 : 500)) return Error.Validation("forms.long", $"«{label}» خیلی طولانی است.");
                    if (f.Type == "select" && (f.Options?.All(o => o.Value != text) ?? true)) return Error.Validation("forms.option", $"گزینه‌ی «{label}» معتبر نیست.");
                    if (f.Type == "url" && !(Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme is "https" or "http"))
                        return Error.Validation("forms.url", $"«{label}» باید یک لینک کامل باشد (https://…).");
                    if (f.Type is "date" or "datetime" && !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                        return Error.Validation("forms.date", $"تاریخ «{label}» معتبر نیست.");
                    if (f.Type == "color" && !System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
                        return Error.Validation("forms.color", $"رنگ «{label}» معتبر نیست.");
                    output[f.Key] = text;
                    break;
            }
        }
        return JsonSerializer.Serialize(output, Json);
    }
}
