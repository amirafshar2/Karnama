namespace Karnama.Application.Auth;

/// <summary>Whitelist of demo accounts reachable through one-click demo login (only in demo installs).</summary>
public static class DemoAccounts
{
    public sealed record Account(string Key, string Phone, string NameFa, string DescriptionFa, string Icon);

    public static IReadOnlyList<Account> All { get; } =
    [
        new("admin", "09000000001", "ادمین", "مدیریت مشتری‌ها، کارها، تیم و تنظیمات", "shield"),
        new("editor", "09000000002", "تدوینگر", "کارهای من، تایمر و تحویل ویدیو", "clapperboard"),
        new("writer", "09000000003", "متن‌نویس", "کپشن، عکس و متن‌های سپرده‌شده", "pen-line"),
        new("client", "09000000004", "مشتری", "پیگیری سفارش‌ها و تأیید محتوا", "store"),
    ];

    public static Account? Find(string key) => All.FirstOrDefault(a => a.Key == key);
}
