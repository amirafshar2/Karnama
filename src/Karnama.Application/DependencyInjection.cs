using FluentValidation;
using Karnama.Application.Auth;
using Karnama.Application.Localization;
using Karnama.Application.Me;
using Karnama.Application.Roles;
using Karnama.Application.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("fa");
        services.AddValidatorsFromAssemblyContaining<RequestOtpValidator>(includeInternalTypes: true);
        services.AddScoped<AuthService>();
        services.AddScoped<RoleService>();
        services.AddScoped<MeService>();
        services.AddScoped<LocalizationService>();
        services.AddScoped<SetupService>();
        services.AddScoped<Settings.BrandService>();
        services.AddScoped<TranslationAdminService>();
        return services;
    }
}
