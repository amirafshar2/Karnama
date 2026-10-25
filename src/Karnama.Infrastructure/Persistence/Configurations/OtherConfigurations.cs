using Karnama.Domain.Auditing;
using Karnama.Domain.Localization;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Karnama.Infrastructure.Persistence.Configurations;

internal sealed class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> b)
    {
        b.ToTable("languages");
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = true").HasDatabaseName("UX_Languages_OneDefault");
    }
}

internal sealed class TranslationConfiguration : IEntityTypeConfiguration<Translation>
{
    public void Configure(EntityTypeBuilder<Translation> b)
    {
        b.ToTable("translations");
        b.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Key).HasMaxLength(Translation.MaxKeyLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(Translation.MaxValueLength).IsRequired();
        b.HasIndex(x => new { x.LanguageCode, x.Key }).IsUnique();
        b.HasOne<Language>().WithMany().HasForeignKey(x => x.LanguageCode).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> b)
    {
        b.ToTable("site_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(40);
        b.Property(x => x.Json).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Action).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.DataJson).HasColumnType("jsonb");
        b.Property(x => x.TraceId).HasMaxLength(64);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
