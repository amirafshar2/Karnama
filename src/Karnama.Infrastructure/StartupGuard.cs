using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Karnama.Infrastructure;

/// <summary>Fail fast with a clear Persian message when a required setting is missing or unsafe.</summary>
public static class StartupGuard
{
    /// <summary>The environment name of a public showcase install (sample data only).</summary>
    public const string DemoEnvironment = "Demo";

    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
            errors.Add("ConnectionStrings__Default خالی است (اتصال PostgreSQL).");
        RequireSecret(config, "Jwt:SigningKey", 32, errors);
        RequireSecret(config, "Otp:HashKey", 32, errors);
        RequireSecret(config, "Setup:Token", 16, errors);

        // "Demo" is a public showcase install (e.g. for a client meeting): sample data and one-click role login, nothing real.
        var demo = config.GetValue<bool>("DemoLogin:Enabled");
        if (demo && !env.IsDevelopment() && !env.IsEnvironment(DemoEnvironment))
            errors.Add($"DemoLogin__Enabled در محیط {env.EnvironmentName} روشن است. ورود سریع دمو فقط در Development یا Demo مجاز است (در Production ممنوع).");
        if (env.IsEnvironment(DemoEnvironment) && !string.Equals(config["Seed:Mode"], "Demo", StringComparison.OrdinalIgnoreCase))
            errors.Add("محیط Demo فقط با داده‌ی نمونه اجرا می‌شود: Seed__Mode=Demo.");

        if (config.GetValue<bool>("Qa:Enabled") && !env.IsDevelopment())
            errors.Add($"Qa__Enabled در محیط {env.EnvironmentName} روشن است. حالت QA (ساعت جابه‌جاشونده، ورود بدون کد) فقط در Development مجاز است.");

        if (env.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(config.GetConnectionString("Redis")))
                errors.Add("ConnectionStrings__Redis در Production الزامی است (کش و SignalR backplane).");
            var sms = config["Sms:Provider"] ?? "Console";
            if (sms == "Console")
                errors.Add("Sms__Provider در Production نمی‌تواند Console باشد (Kavenegar یا SmsIr).");
            var proxies = config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            var networks = config.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
            if (proxies.Length == 0 && networks.Length == 0)
                errors.Add("ReverseProxy__KnownNetworks__0 (مثل 172.16.0.0/12) یا ReverseProxy__KnownProxies__0 خالی است؛ بدون آن محدودیت درخواست‌ها پشت Nginx درست کار نمی‌کند.");
            var vaultKey = config["Vault:MasterKey"];
            bool validKey;
            try { validKey = !string.IsNullOrWhiteSpace(vaultKey) && Convert.FromBase64String(vaultKey.Trim()).Length == 32; }
            catch (FormatException) { validKey = false; }
            if (!validKey)
                errors.Add("Vault__MasterKey در Production الزامی است (۳۲ بایت تصادفی به‌صورت base64؛ با tools/new-env.py --prod بسازید). بدون آن رمز حساب مشتری‌ها ذخیره نمی‌شود. این کلید را جدا از بکاپ دیتابیس نگه دارید.");
            if (config.GetValue<bool>("Database:AutoMigrate"))
                errors.Add("Database__AutoMigrate در Production مجاز نیست؛ اسکریپت Migration بازبینی‌شده اجرا کنید.");
        }

        var smsProvider = config["Sms:Provider"] ?? "Console";
        if (smsProvider is "Kavenegar" or "SmsIr")
        {
            if (string.IsNullOrWhiteSpace(config["Sms:ApiKey"])) errors.Add("Sms__ApiKey خالی است.");
            if (string.IsNullOrWhiteSpace(config["Sms:OtpTemplate"])) errors.Add("Sms__OtpTemplate خالی است.");
        }
        else if (smsProvider != "Console")
        {
            errors.Add($"Sms__Provider نامعتبر است: {smsProvider} (Console | Kavenegar | SmsIr).");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("تنظیمات کارنما ناقص است:\n - " + string.Join("\n - ", errors) +
                                                "\nفایل .env.example را ببینید.");
    }

    private static void RequireSecret(IConfiguration config, string key, int minLength, List<string> errors)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value) || value.Length < minLength)
            errors.Add($"{key.Replace(":", "__", StringComparison.Ordinal)} باید حداقل {minLength} کاراکتر تصادفی باشد.");
    }
}
