using Karnama.Domain.Common;

namespace Karnama.Domain.Identity;

public enum UserKind
{
    Staff = 1,
    Client = 2,
}

public sealed class User : AuditableEntity
{
    public const int MaxFailedLogins = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    private User() { Phone = string.Empty; DisplayName = string.Empty; LanguageCode = "fa"; }

    public string Phone { get; private set; }
    public string DisplayName { get; private set; }
    public UserKind Kind { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Demo accounts exist only in demo installs and can only be reached through demo login.</summary>
    public bool IsDemo { get; private set; }
    public string LanguageCode { get; private set; }

    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntil { get; private set; }

    /// <summary>Base32 TOTP secret, protected at rest by the Data Protection API (see Infrastructure).</summary>
    public string? TwoFactorSecret { get; private set; }
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>Last accepted TOTP time step; a code can never be used twice.</summary>
    public long? LastTwoFactorStep { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    public static Result<User> Create(string phone, string displayName, UserKind kind, bool isDemo = false, string languageCode = "fa")
    {
        if (!PhoneNumber.TryNormalize(phone, out var normalized))
            return Error.Validation("users.invalidPhone", "شماره‌ی موبایل معتبر نیست.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            return Error.Validation("users.invalidName", "نام باید بین ۱ تا ۱۰۰ کاراکتر باشد.");

        return new User
        {
            Phone = normalized,
            DisplayName = displayName.Trim(),
            Kind = kind,
            IsDemo = isDemo,
            LanguageCode = languageCode,
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && until > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockoutUntil = now.Add(LockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
    }

    public void AssignRole(Guid roleId)
    {
        if (_roles.All(r => r.RoleId != roleId)) _roles.Add(new UserRole(Id, roleId));
    }

    public void RemoveRole(Guid roleId) => _roles.RemoveAll(r => r.RoleId == roleId);

    public Result ChangeLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 10)
            return Error.Validation("users.invalidLanguage", "زبان معتبر نیست.");
        LanguageCode = code.ToLowerInvariant();
        return Result.Success();
    }

    public Result Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            return Error.Validation("users.invalidName", "نام باید بین ۱ تا ۱۰۰ کاراکتر باشد.");
        DisplayName = displayName.Trim();
        return Result.Success();
    }

    public void BeginTwoFactorEnrollment(string protectedSecret)
    {
        TwoFactorSecret = protectedSecret;
        TwoFactorEnabled = false;
    }

    public Result AcceptTwoFactorStep(long step)
    {
        if (LastTwoFactorStep is { } last && step <= last)
            return Error.Validation("auth.totpReused", "این کد قبلاً استفاده شده است. منتظر کد بعدی بمانید.");
        LastTwoFactorStep = step;
        return Result.Success();
    }

    public Result ConfirmTwoFactor()
    {
        if (TwoFactorSecret is null) return Error.Validation("auth.twoFactorNotStarted", "اول کد QR را بسازید.");
        TwoFactorEnabled = true;
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

public sealed class UserRole
{
    private UserRole() { }

    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
