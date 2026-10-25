using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Karnama.Infrastructure;

/// <summary>Fail fast with a clear Persian message when a required setting is missing or unsafe.</summary>
public static class StartupGuard
{
    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
            errors.Add("ConnectionStrings__Default خالی است (اتصال PostgreSQL).");
        RequireSecret(config, "Jwt:SigningKey", 32, errors);
        RequireSecret(config, "Otp:HashKey", 32, errors);
        RequireSecret(config, "Setup:Token", 16, errors);

        var demo = config.GetValue<bool>("DemoLogin:Enabled");
        if (demo && env.IsProduction())
            errors.Add("DemoLogin__Enabled در Production روشن است. ورود سریع دمو در Production ممنوع است.");

        if (env.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(config.GetConnectionString("Redis")))
                errors.Add("ConnectionStrings__Redis در Production الزامی است (کش و SignalR backplane).");
            var sms = config["Sms:Provider"] ?? "Console";
            if (sms == "Console")
                errors.Add("Sms__Provider در Production نمی‌تواند Console باشد (Kavenegar یا SmsIr).");
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
