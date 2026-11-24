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
            errors.Add("ConnectionStrings__Default ist leer (PostgreSQL-Verbindung).");
        RequireSecret(config, "Jwt:SigningKey", 32, errors);
        RequireSecret(config, "Otp:HashKey", 32, errors);
        RequireSecret(config, "Setup:Token", 16, errors);

        // "Demo" is a public showcase install (e.g. for a client meeting): sample data and one-click role login, nothing real.
        var demo = config.GetValue<bool>("DemoLogin:Enabled");
        if (demo && !env.IsDevelopment() && !env.IsEnvironment(DemoEnvironment))
            errors.Add($"DemoLogin__Enabled ist in der Umgebung {env.EnvironmentName} eingeschaltet. Die schnelle Demo-Anmeldung ist nur in Development oder Demo erlaubt (in Production verboten).");
        if (env.IsEnvironment(DemoEnvironment) && !string.Equals(config["Seed:Mode"], "Demo", StringComparison.OrdinalIgnoreCase))
            errors.Add("Die Demo-Umgebung läuft nur mit Beispieldaten: Seed__Mode=Demo.");

        if (config.GetValue<bool>("Qa:Enabled") && !env.IsDevelopment())
            errors.Add($"Qa__Enabled ist in der Umgebung {env.EnvironmentName} eingeschaltet. Der QA-Modus (verschiebbare Uhr, Anmeldung ohne Code) ist nur in Development erlaubt.");

        if (env.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(config.GetConnectionString("Redis")))
                errors.Add("ConnectionStrings__Redis ist in Production Pflicht (Cache und SignalR-Backplane).");
            var sms = config["Sms:Provider"] ?? "Console";
            if (sms == "Console")
                errors.Add("Sms__Provider darf in Production nicht Console sein (Kavenegar oder SmsIr).");
            var proxies = config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            var networks = config.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
            if (proxies.Length == 0 && networks.Length == 0)
                errors.Add("ReverseProxy__KnownNetworks__0 (z. B. 172.16.0.0/12) oder ReverseProxy__KnownProxies__0 ist leer; ohne diese Angabe funktionieren die Anfragelimits hinter Nginx nicht richtig.");
            var vaultKey = config["Vault:MasterKey"];
            bool validKey;
            try { validKey = !string.IsNullOrWhiteSpace(vaultKey) && Convert.FromBase64String(vaultKey.Trim()).Length == 32; }
            catch (FormatException) { validKey = false; }
            if (!validKey)
                errors.Add("Vault__MasterKey ist in Production Pflicht (32 zufällige Bytes als Base64; mit tools/new-env.py --prod erzeugen). Ohne ihn werden die Zugangsdaten der Kunden nicht gespeichert. Bewahren Sie diesen Schlüssel getrennt vom Datenbank-Backup auf.");
            if (config.GetValue<bool>("Database:AutoMigrate"))
                errors.Add("Database__AutoMigrate ist in Production nicht erlaubt; führen Sie ein geprüftes Migrationsskript aus.");
        }

        var smsProvider = config["Sms:Provider"] ?? "Console";
        if (smsProvider is "Kavenegar" or "SmsIr")
        {
            if (string.IsNullOrWhiteSpace(config["Sms:ApiKey"])) errors.Add("Sms__ApiKey ist leer.");
            if (string.IsNullOrWhiteSpace(config["Sms:OtpTemplate"])) errors.Add("Sms__OtpTemplate ist leer.");
        }
        else if (smsProvider != "Console")
        {
            errors.Add($"Sms__Provider ist ungültig: {smsProvider} (Console | Kavenegar | SmsIr).");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Die Karnama-Konfiguration ist unvollständig:\n - " + string.Join("\n - ", errors) +
                                                "\nSiehe Datei .env.example.");
    }

    private static void RequireSecret(IConfiguration config, string key, int minLength, List<string> errors)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value) || value.Length < minLength)
            errors.Add($"{key.Replace(":", "__", StringComparison.Ordinal)} muss mindestens {minLength} zufällige Zeichen sein.");
    }
}
