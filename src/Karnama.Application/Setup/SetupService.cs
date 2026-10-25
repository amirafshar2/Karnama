using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Auditing;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Setup;

public sealed record SetupStatusDto(bool NeedsSetup);
public sealed record CompleteSetupRequest(string Token, string Phone, string DisplayName, string? BrandName);

public sealed class CompleteSetupValidator : AbstractValidator<CompleteSetupRequest>
{
    public CompleteSetupValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("کد نصب را وارد کنید.");
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalize(p, out _)).WithMessage("شماره‌ی موبایل معتبر نیست.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100).WithMessage("نام باید بین ۱ تا ۱۰۰ کاراکتر باشد.");
        RuleFor(x => x.BrandName).MaximumLength(80).WithMessage("نام برند حداکثر ۸۰ کاراکتر است.");
    }
}

/// <summary>First-run: creates the first real Owner. Allowed only while no real (non-demo) owner exists.</summary>
public sealed class SetupService(IAppDbContext db, IAuditWriter audit, IOptions<SetupOptions> options)
{
    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken ct) => new(!await HasRealOwnerAsync(ct));

    public async Task<Result> CompleteAsync(CompleteSetupRequest request, CancellationToken ct)
    {
        var expected = Encoding.UTF8.GetBytes(options.Value.Token);
        var given = Encoding.UTF8.GetBytes(request.Token ?? string.Empty);
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(given)))
            return Error.Forbidden("setup.invalidToken", "کد نصب درست نیست.");

        return await db.InTransactionAsync<Result>(async token =>
        {
            if (await HasRealOwnerAsync(token))
                return Error.Conflict("setup.done", "راه‌اندازی قبلاً انجام شده است.");

            var ownerRole = await db.Roles.FirstOrDefaultAsync(r => r.SystemKey == SystemRoles.Owner, token);
            if (ownerRole is null) return Error.Conflict("setup.noOwnerRole", "نقش مالک پیدا نشد؛ برنامه را دوباره اجرا کنید.");

            PhoneNumber.TryNormalize(request.Phone, out var phone);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone && !u.IsDemo, token);
            if (user is null)
            {
                var created = User.Create(phone, request.DisplayName, UserKind.Staff);
                if (created.IsFailure) return created.Error!;
                user = created.Value;
                db.Users.Add(user);
            }
            user.AssignRole(ownerRole.Id);

            if (!string.IsNullOrWhiteSpace(request.BrandName))
            {
                var brand = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == "brand", token);
                var json = System.Text.Json.JsonSerializer.Serialize(new { name = request.BrandName.Trim() });
                if (brand is null) db.SiteSettings.Add(new Domain.Settings.SiteSetting("brand", json));
                else brand.Replace(json, user.Id, DateTimeOffset.UtcNow);
            }

            audit.Add(AuditActions.Setup, nameof(User), user.Id.ToString(), new { phone = PhoneNumber.Mask(phone) }, actorOverride: user.Id, isDemoOverride: false);
            await db.SaveChangesAsync(token);
            return Result.Success();
        }, ct);
    }

    private Task<bool> HasRealOwnerAsync(CancellationToken ct) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join r in db.Roles on ur.RoleId equals r.Id
         where r.SystemKey == SystemRoles.Owner && !u.IsDemo && u.IsActive
         select u.Id).AnyAsync(ct);
}
