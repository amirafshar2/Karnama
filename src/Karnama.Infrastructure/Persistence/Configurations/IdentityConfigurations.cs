using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Karnama.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.Property(x => x.Phone).HasMaxLength(11).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.TwoFactorSecret).HasMaxLength(1000);
        b.HasIndex(x => x.Phone).IsUnique().HasFilter("\"IsDeleted\" = false").HasDatabaseName("UX_Users_Phone");
        b.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles");
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasIndex(x => x.RoleId);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.Property(x => x.Name).HasColumnType("jsonb").IsRequired()
            .HasConversion(Persistence.LocalizedTextConversion.Converter, Persistence.LocalizedTextConversion.Comparer);
        b.Property(x => x.Description).HasMaxLength(Role.MaxDescriptionLength);
        b.Property(x => x.SystemKey).HasMaxLength(30);
        b.HasIndex(x => x.SystemKey).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL").HasDatabaseName("UX_Roles_SystemKey");
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsSystem);
        b.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions");
        b.HasKey(x => new { x.RoleId, x.Code });
        b.Property(x => x.Code).HasMaxLength(60);
    }
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.ToTable("otp_challenges");
        b.Property(x => x.Phone).HasMaxLength(11).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.Phone, x.CreatedAt });
        b.HasIndex(x => x.ExpiresAt);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
