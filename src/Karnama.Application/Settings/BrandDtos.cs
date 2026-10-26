namespace Karnama.Application.Settings;

/// <summary>The two colours the admin picks; every other UI colour is derived from them with guaranteed contrast.</summary>
public sealed record BrandColors(string Primary, string Dark, string? Emphasis);

public sealed record BrandDto(string? Name, string? Tagline, string? LogoUrl, BrandColors Colors, uint Version, bool Demo);

public sealed record UpdateBrandRequest(string? Name, string? Tagline, BrandColors Colors, uint? Version);

public static class BrandDefaults
{
    public static readonly BrandColors Colors = new("#3B6CFF", "#0B1B34", null);
}
