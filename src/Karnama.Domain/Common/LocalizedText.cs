using System.Collections.ObjectModel;

namespace Karnama.Domain.Common;

/// <summary>
/// Translatable content stored as JSON (<c>{"fa":"...","en":"..."}</c>).
/// Reads fall back to the default language, then to any available value.
/// </summary>
public sealed class LocalizedText : IEquatable<LocalizedText>
{
    public const int MaxLength = 400;
    private readonly Dictionary<string, string> _values;

    public LocalizedText(IReadOnlyDictionary<string, string>? values = null)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is null) return;
        foreach (var (lang, text) in values)
        {
            if (!string.IsNullOrWhiteSpace(lang) && !string.IsNullOrWhiteSpace(text))
                _values[lang.Trim().ToLowerInvariant()] = text.Trim();
        }
    }

    public static LocalizedText Of(string lang, string text) => new(new Dictionary<string, string> { [lang] = text });

    public IReadOnlyDictionary<string, string> Values => new ReadOnlyDictionary<string, string>(_values);

    public bool IsEmpty => _values.Count == 0;

    public string Get(string? lang, string defaultLang = "fa")
    {
        if (lang is not null && _values.TryGetValue(lang, out var v)) return v;
        if (_values.TryGetValue(defaultLang, out var d)) return d;
        return _values.Values.FirstOrDefault() ?? string.Empty;
    }

    public bool Equals(LocalizedText? other) =>
        other is not null && _values.Count == other._values.Count &&
        _values.All(kv => other._values.TryGetValue(kv.Key, out var o) && o == kv.Value);

    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var kv in _values) hash ^= HashCode.Combine(kv.Key, kv.Value);
        return hash;
    }
}
