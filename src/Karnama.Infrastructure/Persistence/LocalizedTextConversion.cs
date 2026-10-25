using System.Text.Json;
using Karnama.Domain.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Karnama.Infrastructure.Persistence;

internal static class LocalizedTextConversion
{
    public static readonly ValueConverter<LocalizedText, string> Converter = new(
        v => JsonSerializer.Serialize(v.Values, (JsonSerializerOptions?)null),
        v => new LocalizedText(JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)));

    public static readonly ValueComparer<LocalizedText> Comparer = new(
        (a, b) => a == null ? b == null : a.Equals(b),
        v => v.GetHashCode(),
        v => new LocalizedText(v.Values));
}
