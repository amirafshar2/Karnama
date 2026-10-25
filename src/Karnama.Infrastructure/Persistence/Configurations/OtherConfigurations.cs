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

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<Karnama.Domain.Media.MediaAsset>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Media.MediaAsset> b)
    {
        b.ToTable("media_assets");
        b.Property(x => x.FileId).HasMaxLength(200).IsRequired();
        b.Property(x => x.FileUniqueId).HasMaxLength(100).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.MimeType).HasMaxLength(100);
        b.Property(x => x.Caption).HasMaxLength(1000);
        b.Property(x => x.ThumbFileId).HasMaxLength(200);
        b.Property(x => x.ThumbPath).HasMaxLength(100);
        b.Property(x => x.Error).HasMaxLength(300);
        b.Property(x => x.Version).IsRowVersion();
        // One record per file per messenger: the same file sent twice is recognised (§8.3).
        b.HasIndex(x => new { x.Messenger, x.FileUniqueId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.Id });
        b.HasIndex(x => x.SenderId);
        b.HasIndex(x => x.WorkItemId);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Karnama.Domain.Chat.Conversation>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Chat.Conversation> b)
    {
        b.ToTable("conversations");
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.DirectKey).HasMaxLength(70);
        b.Property(x => x.LastPreview).HasMaxLength(200);
        b.HasIndex(x => x.DirectKey).IsUnique().HasFilter("\"DirectKey\" IS NOT NULL");
        b.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"OrderId\" IS NOT NULL");
        b.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<Karnama.Domain.Chat.ConversationMember>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Chat.ConversationMember> b)
    {
        b.ToTable("conversation_members");
        b.HasKey(x => new { x.ConversationId, x.UserId });
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<Karnama.Domain.Chat.ChatMessage>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Chat.ChatMessage> b)
    {
        b.ToTable("chat_messages");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Body).HasMaxLength(4000);
        b.Property(x => x.FileKey).HasMaxLength(200);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.HasIndex(x => new { x.ConversationId, x.Id });
        // A message re-sent after a dropped connection is stored once.
        b.HasIndex(x => new { x.ConversationId, x.ClientKey }).IsUnique();
    }
}
