using FluentValidation;
using Karnama.Domain.Common;

namespace Karnama.Application.Auth;

public sealed class RequestOtpValidator : AbstractValidator<RequestOtpRequest>
{
    public RequestOtpValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Geben Sie die Handynummer ein.")
            .Must(p => PhoneNumber.TryNormalize(p, out _)).WithMessage("Die Handynummer ist ungültig (z. B. 01512 3456789).")
            .WithErrorCode("auth.invalidPhone");
    }
}

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty().WithMessage("Die Code-Anfrage ist ungültig. Fordern Sie erneut einen Code an.");
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Geben Sie den Bestätigungscode ein.")
            .Matches("^[0-9۰-۹]{6}$").WithMessage("Der Bestätigungscode muss 6-stellig sein.")
            .WithErrorCode("auth.invalidCode");
    }
}

public sealed class TwoFactorVerifyValidator : AbstractValidator<TwoFactorVerifyRequest>
{
    public TwoFactorVerifyValidator()
    {
        RuleFor(x => x.TwoFactorToken).NotEmpty().WithMessage("Der Anmeldeschritt ist abgelaufen. Melden Sie sich erneut an.");
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9۰-۹]{6}$").WithMessage("Der Code der Authenticator-App muss 6-stellig sein.");
    }
}

public sealed class DemoLoginValidator : AbstractValidator<DemoLoginRequest>
{
    public DemoLoginValidator()
    {
        RuleFor(x => x.Role).NotEmpty().Must(r => DemoAccounts.Find(r) is not null).WithMessage("Die Demo-Rolle ist ungültig.");
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
