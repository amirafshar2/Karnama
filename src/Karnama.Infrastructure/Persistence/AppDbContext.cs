using System.Linq.Expressions;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Auditing;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Localization;
using Karnama.Domain.Settings;
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
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

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
