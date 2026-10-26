namespace Karnama.Application.Common;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "karnama";
    public string Audience { get; set; } = "karnama";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class OtpOptions
{
    public const string Section = "Otp";
    public string HashKey { get; set; } = string.Empty;
    public int MaxSendsPer10Minutes { get; set; } = 3;
    public int ResendSeconds { get; set; } = 60;
}

public sealed class SetupOptions
{
    public const string Section = "Setup";
    public string Token { get; set; } = string.Empty;
}

public enum SeedMode { Demo, Empty }

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public SeedMode Mode { get; set; } = SeedMode.Empty;
}

/// <summary>QA mode (docs/PROMPT3.md Q0): a movable clock, sign-in without OTP and job triggers. Development only.</summary>
public sealed class QaOptions
{
    public const string Section = "Qa";
    public bool Enabled { get; set; }
}

public sealed class DemoLoginOptions
{
    public const string Section = "DemoLogin";
    public bool Enabled { get; set; }
}

public sealed class AppVersionOptions
{
    public const string Section = "App";
    public string MinSupportedVersion { get; set; } = "1.0.0";
    public string LatestVersion { get; set; } = "1.0.0";
    public string? DownloadUrl { get; set; }
}
