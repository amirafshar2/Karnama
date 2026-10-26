using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Settings;

public sealed partial class UpdateBrandValidator : AbstractValidator<UpdateBrandRequest>
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    public UpdateBrandValidator()
    {
        RuleFor(x => x.Name).MaximumLength(80).WithMessage("نام برند حداکثر ۸۰ کاراکتر است.");
        RuleFor(x => x.Tagline).MaximumLength(160).WithMessage("شعار حداکثر ۱۶۰ کاراکتر است.");
        RuleFor(x => x.Colors).NotNull().WithMessage("رنگ‌ها را انتخاب کنید.");
        RuleFor(x => x.Colors.Primary).Must(c => c is not null && Hex().IsMatch(c)).When(x => x.Colors is not null)
            .WithMessage("رنگ اصلی باید به شکل #RRGGBB باشد (مثلاً #3B6CFF).");
        RuleFor(x => x.Colors.Dark).Must(c => c is not null && Hex().IsMatch(c)).When(x => x.Colors is not null)
            .WithMessage("رنگ تیره باید به شکل #RRGGBB باشد (مثلاً #12141A).");
        RuleFor(x => x.Colors.Emphasis).Must(c => c is null || Hex().IsMatch(c)).When(x => x.Colors is not null)
            .WithMessage("رنگ تأکید باید به شکل #RRGGBB باشد.");
    }
}

/// <summary>Brand section of the white-label settings (name, tagline, logo, colours). Public read, admin write.</summary>
public sealed class BrandService(IAppDbContext db, IAuditWriter audit, ICurrentUser me, TimeProvider clock, IMediaStore media, IOptions<Common.DemoLoginOptions> demo)
{
    public const string Key = "brand";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Stored(string? Name, string? Tagline, string? Logo, BrandColors? Colors);

    public async Task<BrandDto> GetAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var stored = Parse(row?.Json);
        return new BrandDto(
            Blank(stored.Name), Blank(stored.Tagline),
            stored.Logo is null ? null : media.PublicUrl(stored.Logo),
            stored.Colors ?? BrandDefaults.Colors, row?.Version ?? 0, demo.Value.Enabled);
    }

    public async Task<Result<BrandDto>> UpdateAsync(UpdateBrandRequest request, CancellationToken ct)
    {
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        var before = Parse(row?.Json);
        var colors = new BrandColors(request.Colors.Primary.ToUpperInvariant(), request.Colors.Dark.ToUpperInvariant(), request.Colors.Emphasis?.ToUpperInvariant());
        var next = before with { Name = Blank(request.Name), Tagline = Blank(request.Tagline), Colors = colors };
        await SaveAsync(row, next, request.Version, ct, new { before, after = next });
        return await GetAsync(ct);
    }

    public async Task<Result<BrandDto>> SetLogoAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var saved = await media.SaveImageAsync(content, length, "brand", ct);
        if (saved.IsFailure) return saved.Error!;

        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        var before = Parse(row?.Json);
        await SaveAsync(row, before with { Logo = saved.Value }, null, ct, new { logo = saved.Value, previous = before.Logo, fileName });
        return await GetAsync(ct);
    }

    public async Task<Result<BrandDto>> RemoveLogoAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        var before = Parse(row?.Json);
        await SaveAsync(row, before with { Logo = null }, null, ct, new { removed = before.Logo });
        return await GetAsync(ct);
    }

    private async Task SaveAsync(SiteSetting? row, Stored value, uint? version, CancellationToken ct, object auditData)
    {
        var json = JsonSerializer.Serialize(value, Json);
        if (row is null)
        {
            db.SiteSettings.Add(row = new SiteSetting(Key, json));
            row.Replace(json, me.UserId, clock.GetUtcNow());
        }
        else
        {
            if (version is { } v) db.SetOriginalVersion(row, v);
            row.Replace(json, me.UserId, clock.GetUtcNow());
        }
        audit.Add("settings.brand.updated", nameof(SiteSetting), Key, auditData);
        await db.SaveChangesAsync(ct);
    }

    private static Stored Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Stored(null, null, null, null);
        try
        {
            return JsonSerializer.Deserialize<Stored>(json, Json) ?? new Stored(null, null, null, null);
        }
        catch (JsonException)
        {
            return new Stored(null, null, null, null);
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
