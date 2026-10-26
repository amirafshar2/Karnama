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
        services.AddScoped<Catalog.PlatformService>();
        services.AddScoped<Catalog.ServiceTypeService>();
        services.AddScoped<Catalog.PackageService>();
        services.AddScoped<Clients.ClientService>();
        services.AddScoped<Users.UserService>();
        services.AddScoped<Onboarding.OnboardingService>();
        services.AddScoped<History.HistoryService>();
        services.AddScoped<Work.WorkAccess>();
        services.AddScoped<Work.OrderService>();
        services.AddScoped<Work.WorkService>();
        services.AddScoped<Files.FileService>();
        services.AddScoped<Realtime.ChangesService>();
        services.AddScoped<Abstractions.IChangeFeed, Realtime.ChangeFeed>();
        services.AddScoped<Portal.PortalService>();
        services.AddScoped<Attendance.AttendanceSettingsStore>();
        services.AddScoped<Attendance.AttendanceService>();
        services.AddScoped<Attendance.AttendanceAdminService>();
        services.AddScoped<Attendance.LeaveService>();
        services.AddScoped<Notify.Notifier>();
        services.AddScoped<Notify.InboxService>();
        services.AddScoped<Notify.BotUpdateService>();
        services.AddScoped<Media.MediaService>();
        services.AddScoped<Media.BotRouter>();
        services.AddScoped<Chat.ChatService>();
        services.AddScoped<Crm.CrmService>();
        services.AddScoped<Assets.AssetService>();
        services.AddScoped<Help.HelpService>();
        services.AddScoped<Attendance.DeviceService>();
        services.AddScoped<Cctv.CctvService>();
        services.AddSingleton<Cctv.SnapshotCache>();
        services.AddScoped<Reports.PerformanceService>();
        services.AddScoped<Notify.PackageExpiryJob>();
        services.AddScoped<Publishing.PublishService>();
        services.AddScoped<Blog.BlogService>();
        services.AddScoped<Site.SiteContentStore>();
        services.AddScoped<Site.PublicSiteService>();
        services.AddScoped<Site.CaseStudyService>();
        services.AddScoped<Clients.CredentialService>();
        services.AddScoped<Clients.PackageNoticeSettingsStore>();
        return services;
    }
}
