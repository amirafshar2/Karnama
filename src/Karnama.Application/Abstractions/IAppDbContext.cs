using Karnama.Domain.Attendance;
using Karnama.Domain.Auditing;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Identity;
using Karnama.Domain.Localization;
using Karnama.Domain.Files;
using Karnama.Domain.Realtime;
using Karnama.Domain.Settings;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Language> Languages { get; }
    DbSet<Translation> Translations { get; }
    DbSet<SiteSetting> SiteSettings { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<ServiceType> ServiceTypes { get; }
    DbSet<WorkflowStage> WorkflowStages { get; }
    DbSet<ServiceFormVersion> ServiceFormVersions { get; }
    DbSet<Package> Packages { get; }
    DbSet<PackageQuota> PackageQuotas { get; }
    DbSet<Client> Clients { get; }
    DbSet<ClientPackage> ClientPackages { get; }
    DbSet<Order> Orders { get; }
    DbSet<WorkItem> WorkItems { get; }
    DbSet<WorkComment> WorkComments { get; }
    DbSet<FileAsset> Files { get; }
    DbSet<ChangeEvent> ChangeEvents { get; }
    DbSet<WorkSchedule> WorkSchedules { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<Shift> Shifts { get; }
    DbSet<AttendanceEvent> AttendanceEvents { get; }
    DbSet<WorkTimer> WorkTimers { get; }
    DbSet<AttendanceCorrection> AttendanceCorrections { get; }
    DbSet<AttendancePolicy> AttendancePolicies { get; }
    DbSet<AttendancePlace> AttendancePlaces { get; }
    DbSet<KioskCodeUse> KioskCodeUses { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<Absence> Absences { get; }
    DbSet<Karnama.Domain.Notify.Notification> Notifications { get; }
    DbSet<Karnama.Domain.Notify.OutboxMessage> Outbox { get; }
    DbSet<Karnama.Domain.Notify.NotificationPrefs> NotificationPrefs { get; }
    DbSet<Karnama.Domain.Notify.MessengerLink> MessengerLinks { get; }
    DbSet<Karnama.Domain.Notify.MessengerLinkCode> MessengerLinkCodes { get; }
    DbSet<Karnama.Domain.Publishing.PublishJob> PublishJobs { get; }
    DbSet<Karnama.Domain.Content.BlogPost> BlogPosts { get; }
    DbSet<Karnama.Domain.Content.BlogCategory> BlogCategories { get; }
    DbSet<Karnama.Domain.Content.BlogSlugRedirect> BlogSlugRedirects { get; }
    DbSet<Karnama.Domain.Content.CaseStudy> CaseStudies { get; }
    DbSet<Karnama.Domain.Crm.Lead> Leads { get; }

    /// <summary>For the few statements EF can't express (e.g. INSERT … ON CONFLICT DO NOTHING).</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    /// <summary>Next human order number ("سفارش ۱۰۴۲") from a database sequence.</summary>
    Task<long> NextOrderNumberAsync(CancellationToken ct);

    /// <summary>Sets the concurrency token the client last saw, so a stale update fails with a conflict.</summary>
    void SetOriginalVersion<TEntity>(TEntity entity, uint version) where TEntity : class;

    /// <summary>Forces an UPDATE of the row (audit fields + concurrency token) even if only children changed.</summary>
    void MarkModified<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs <paramref name="work"/> in a serializable transaction (with the provider's retry strategy).</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
