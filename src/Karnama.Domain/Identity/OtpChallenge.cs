using Karnama.Domain.Common;

namespace Karnama.Domain.Identity;

/// <summary>
/// One OTP send. The code itself is never stored, only an HMAC of it.
/// Rules: valid 2 minutes, 5 attempts, single use.
/// </summary>
public enum OtpPurpose { Login = 0, Reveal = 1 }

public sealed class OtpChallenge : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxAttempts = 5;

    private OtpChallenge() { Phone = string.Empty; CodeHash = string.Empty; }

    public string Phone { get; private set; }
    public string CodeHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    /// <summary>A code sent to re-confirm a sensitive action (e.g. seeing a password) can never log anyone in, and vice versa.</summary>
    public OtpPurpose Purpose { get; private set; }

    public static OtpChallenge Create(Guid id, string phone, string codeHash, DateTimeOffset now, OtpPurpose purpose = OtpPurpose.Login) => new()
    {
        Id = id,
        Purpose = purpose,
        Phone = phone,
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = now.Add(Lifetime),
    };

    /// <summary>
    /// Why this challenge can't take another attempt, or success if it can.
    /// The attempt itself is claimed atomically in the database (see AuthService) so parallel guesses
    /// can never exceed <see cref="MaxAttempts"/>; this method only explains the state to the user.
    /// </summary>
    public Result CheckUsable(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
            return Error.Validation("auth.otpUsed", "این کد قبلاً استفاده شده است. کد جدید بگیرید.");
        if (now >= ExpiresAt)
            return Error.Validation("auth.otpExpired", "کد منقضی شده است. کد جدید بگیرید.");
        if (Attempts >= MaxAttempts)
            return Error.TooMany("auth.otpTooManyAttempts", "تعداد تلاش‌ها زیاد بود. کد جدید بگیرید.");
        return Result.Success();
    }

    public static Error WrongCode => Error.Validation("auth.otpWrong", "کد واردشده درست نیست.");
}
