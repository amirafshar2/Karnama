using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Infrastructure.Auditing;
using Karnama.Infrastructure.Persistence;
using Karnama.Infrastructure.Security;
using Karnama.Infrastructure.Seeding;
using Karnama.Infrastructure.Sms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Karnama.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        StartupGuard.Validate(config, env);

        services.AddSingleton(TimeProvider.System);
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<OtpOptions>(config.GetSection(OtpOptions.Section));
        services.Configure<SetupOptions>(config.GetSection(SetupOptions.Section));
        services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));
        services.Configure<DemoLoginOptions>(config.GetSection(DemoLoginOptions.Section));
        services.Configure<AppVersionOptions>(config.GetSection(AppVersionOptions.Section));
        services.Configure<SmsOptions>(config.GetSection(SmsOptions.Section));

        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Default"), npg => npg.EnableRetryOnFailure(3))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddDataProtection().SetApplicationName("Karnama").PersistKeysToDbContext<AppDbContext>();

        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "karnama:"; });
        services.AddHybridCache();

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IOtpCodeService, OtpCodeService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<DataSeeder>();
        services.AddSingleton<IMediaStore, Storage.LocalMediaStore>();
        services.AddSingleton<IFileStore, Storage.LocalFileStore>();
        services.Configure<Application.Files.FileOptions>(config.GetSection(Application.Files.FileOptions.Section));
        services.AddSingleton<IContentRenderer, Content.ContentRenderer>();
        services.AddSingleton<IExcelExporter, Content.ExcelExporter>();
        services.AddSingleton<OtpDispatcher>();
        services.AddSingleton<IOtpDispatcher>(sp => sp.GetRequiredService<OtpDispatcher>());
        services.AddHostedService<OtpDispatchService>();

        var provider = config["Sms:Provider"] ?? "Console";
        switch (provider)
        {
            case "Kavenegar":
                services.AddHttpClient<ISmsSender, KavenegarSmsSender>(c => { c.BaseAddress = new Uri("https://api.kavenegar.com/"); c.Timeout = TimeSpan.FromSeconds(10); });
                break;
            case "SmsIr":
                services.AddHttpClient<ISmsSender, SmsIrSmsSender>(c => { c.BaseAddress = new Uri("https://api.sms.ir/"); c.Timeout = TimeSpan.FromSeconds(10); });
                break;
            default:
                services.AddSingleton<ISmsSender, ConsoleSmsSender>();
                break;
        }
        return services;
    }
}
