using FluentValidation;
using Karnama.Domain.Common;

namespace Karnama.Application.Auth;

public sealed class RequestOtpValidator : AbstractValidator<RequestOtpRequest>
{
    public RequestOtpValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("شماره‌ی موبایل را وارد کنید.")
            .Must(p => PhoneNumber.TryNormalize(p, out _)).WithMessage("شماره‌ی موبایل معتبر نیست (مثل ۰۹۱۲۱۲۳۴۵۶۷).")
            .WithErrorCode("auth.invalidPhone");
    }
}

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty().WithMessage("درخواست کد معتبر نیست. دوباره کد بگیرید.");
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("کد تأیید را وارد کنید.")
            .Matches("^[0-9۰-۹]{6}$").WithMessage("کد تأیید باید ۶ رقم باشد.")
            .WithErrorCode("auth.invalidCode");
    }
}

public sealed class TwoFactorVerifyValidator : AbstractValidator<TwoFactorVerifyRequest>
{
    public TwoFactorVerifyValidator()
    {
        RuleFor(x => x.TwoFactorToken).NotEmpty().WithMessage("مرحله‌ی ورود منقضی شده است. دوباره وارد شوید.");
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9۰-۹]{6}$").WithMessage("کد برنامه‌ی تأیید باید ۶ رقم باشد.");
    }
}

public sealed class DemoLoginValidator : AbstractValidator<DemoLoginRequest>
{
    public DemoLoginValidator()
    {
        RuleFor(x => x.Role).NotEmpty().Must(r => DemoAccounts.Find(r) is not null).WithMessage("نقش نمایشی معتبر نیست.");
    }
}

public static class Digits
{
    public static string ToLatin(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            buf[i] = c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            };
        }
        return new string(buf);
    }
}
