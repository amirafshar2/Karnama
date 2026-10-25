using Karnama.Domain.Common;

namespace Karnama.Domain.Identity;

/// <summary>
/// One OTP send. The code itself is never stored, only an HMAC of it.
/// Rules: valid 2 minutes, 5 attempts, single use.
/// </summary>
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

    public static OtpChallenge Create(Guid id, string phone, string codeHash, DateTimeOffset now) => new()
    {
        Id = id,
        Phone = phone,
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = now.Add(Lifetime),
    };

    /// <summary>Checks a code. <paramref name="matches"/> must be a constant-time comparison of hashes.</summary>
    public Result Verify(bool matches, DateTimeOffset now)
    {
        if (ConsumedAt is not null)
            return Error.Validation("auth.otpUsed", "این کد قبلاً استفاده شده است. کد جدید بگیرید.");
        if (now >= ExpiresAt)
            return Error.Validation("auth.otpExpired", "کد منقضی شده است. کد جدید بگیرید.");
        if (Attempts >= MaxAttempts)
            return Error.TooMany("auth.otpTooManyAttempts", "تعداد تلاش‌ها زیاد بود. کد جدید بگیرید.");

        Attempts++;
        if (!matches)
            return Error.Validation("auth.otpWrong", "کد واردشده درست نیست.");

        ConsumedAt = now;
        return Result.Success();
    }
}
