using System.Linq.Expressions;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Attendance;
using Karnama.Domain.Auditing;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Localization;
using Karnama.Domain.Files;
using Karnama.Domain.Realtime;
using Karnama.Domain.Settings;
using Karnama.Domain.Work;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null, TimeProvider? clock = null)
    : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<WorkflowStage> WorkflowStages => Set<WorkflowStage>();
    public DbSet<ServiceFormVersion> ServiceFormVersions => Set<ServiceFormVersion>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageQuota> PackageQuotas => Set<PackageQuota>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientPackage> ClientPackages => Set<ClientPackage>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkComment> WorkComments => Set<WorkComment>();
    public DbSet<FileAsset> Files => Set<FileAsset>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<WorkTimer> WorkTimers => Set<WorkTimer>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();
    public DbSet<AttendancePlace> AttendancePlaces => Set<AttendancePlace>();
    public DbSet<KioskCodeUse> KioskCodeUses => Set<KioskCodeUse>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<Karnama.Domain.Notify.Notification> Notifications => Set<Karnama.Domain.Notify.Notification>();
    public DbSet<Karnama.Domain.Notify.OutboxMessage> Outbox => Set<Karnama.Domain.Notify.OutboxMessage>();
    public DbSet<Karnama.Domain.Notify.NotificationPrefs> NotificationPrefs => Set<Karnama.Domain.Notify.NotificationPrefs>();
    public DbSet<Karnama.Domain.Notify.MessengerLink> MessengerLinks => Set<Karnama.Domain.Notify.MessengerLink>();
    public DbSet<Karnama.Domain.Notify.MessengerLinkCode> MessengerLinkCodes => Set<Karnama.Domain.Notify.MessengerLinkCode>();
    public DbSet<Karnama.Domain.Notify.BotUpdate> BotUpdates => Set<Karnama.Domain.Notify.BotUpdate>();
    public DbSet<Karnama.Domain.Publishing.PublishJob> PublishJobs => Set<Karnama.Domain.Publishing.PublishJob>();
    public DbSet<Karnama.Domain.Content.BlogPost> BlogPosts => Set<Karnama.Domain.Content.BlogPost>();
    public DbSet<Karnama.Domain.Content.BlogCategory> BlogCategories => Set<Karnama.Domain.Content.BlogCategory>();
    public DbSet<Karnama.Domain.Content.BlogSlugRedirect> BlogSlugRedirects => Set<Karnama.Domain.Content.BlogSlugRedirect>();
    public DbSet<Karnama.Domain.Content.CaseStudy> CaseStudies => Set<Karnama.Domain.Content.CaseStudy>();
    public DbSet<Karnama.Domain.Crm.Lead> Leads => Set<Karnama.Domain.Crm.Lead>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// PostgreSQL timestamptz stores UTC only. Clients send times with an offset (e.g. "+03:30" from a Persian date
    /// picker); normalise every DateTimeOffset to UTC on the way in so nothing fails or shifts.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    private sealed class UtcDateTimeOffsetConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(), v => v.ToUniversalTime());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.HasSequence<long>("order_numbers").StartsAt(1001);

        // Ids are Guid v7 created in the domain (never by the database). Telling EF so makes new child entities
        // added through a navigation (e.g. a new workflow stage) INSERTs instead of mistaken UPDATEs.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }

        // Soft delete: every ISoftDeletable is hidden unless IgnoreQueryFilters() is used.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;
            var p = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Not(Expression.Property(p, nameof(ISoftDeletable.IsDeleted)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, p));
        }
    }

    public void SetOriginalVersion<TEntity>(TEntity entity, uint version) where TEntity : class =>
        Entry(entity).Property("Version").OriginalValue = version;

    public async Task<long> NextOrderNumberAsync(CancellationToken ct) =>
        await Database.SqlQueryRaw<long>("SELECT nextval('order_numbers') AS \"Value\"").SingleAsync(ct);

    public void MarkModified<TEntity>(TEntity entity) where TEntity : class
    {
        var entry = Entry(entity);
        if (entry.State == EntityState.Unchanged) entry.State = EntityState.Modified;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var actor = currentUser?.UserId;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy ??= actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var strategy = Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async token =>
        {
            ChangeTracker.Clear();
            await using var tx = await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var result = await work(token);
            await tx.CommitAsync(token);
            return result;
        }, ct);
    }
}
