using Karnama.Domain.Auditing;
using Karnama.Domain.Identity;
using Karnama.Domain.Localization;
using Karnama.Domain.Settings;
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

    /// <summary>Sets the concurrency token the client last saw, so a stale update fails with a conflict.</summary>
    void SetOriginalVersion<TEntity>(TEntity entity, uint version) where TEntity : class;

    /// <summary>Forces an UPDATE of the row (audit fields + concurrency token) even if only children changed.</summary>
    void MarkModified<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs <paramref name="work"/> in a serializable transaction (with the provider's retry strategy).</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
