using Karnama.Domain.Files;
using Karnama.Domain.Realtime;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Karnama.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.ServiceName).AsLocalized().IsRequired();
        b.Property(x => x.FormDataJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Title).HasMaxLength(Order.MaxTitle).IsRequired();
        b.Property(x => x.Note).HasMaxLength(Order.MaxNote);
        b.Property(x => x.RejectReason).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.ClientId, x.Status });
        b.HasIndex(x => new { x.ClientPackageId, x.ServiceTypeId });
        b.HasIndex(x => x.Status);
    }
}

internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> b)
    {
        b.ToTable("work_items");
        b.Property(x => x.StageName).AsLocalized().IsRequired();
        b.Property(x => x.Title).HasMaxLength(Order.MaxTitle).IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => new { x.AssigneeId, x.Status });
        b.HasIndex(x => new { x.Status, x.Rank });
        b.HasIndex(x => x.ClientId);
    }
}

internal sealed class WorkCommentConfiguration : IEntityTypeConfiguration<WorkComment>
{
    public void Configure(EntityTypeBuilder<WorkComment> b)
    {
        b.ToTable("work_comments");
        b.Property(x => x.Body).HasMaxLength(WorkComment.MaxBody).IsRequired();
        b.Property(x => x.AuthorName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.WorkItemId, x.CreatedAt });
    }
}

internal sealed class FileAssetConfiguration : IEntityTypeConfiguration<FileAsset>
{
    public void Configure(EntityTypeBuilder<FileAsset> b)
    {
        b.ToTable("files");
        b.Property(x => x.FileName).HasMaxLength(FileAsset.MaxName).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.WorkItemId);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class ChangeEventConfiguration : IEntityTypeConfiguration<ChangeEvent>
{
    public void Configure(EntityTypeBuilder<ChangeEvent> b)
    {
        b.ToTable("change_events");
        b.HasKey(x => x.Seq);
        b.Property(x => x.Seq).UseIdentityAlwaysColumn();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Fields).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.At);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.Notification>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.Notification> b)
    {
        b.ToTable("notifications");
        b.Property(x => x.Kind).HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.Link).HasMaxLength(300);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.UserId, x.ReadAt });
        b.Property(x => x.EventKey).HasMaxLength(160).IsRequired();
        b.HasIndex(x => new { x.UserId, x.EventKey }).IsUnique();
    }
}

internal sealed class OutboxConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.OutboxMessage>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.OutboxMessage> b)
    {
        b.ToTable("outbox");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Text).HasMaxLength(1500).IsRequired();
        b.Property(x => x.Link).HasMaxLength(300);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.DueAt });
    }
}

internal sealed class NotificationPrefsConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.NotificationPrefs>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.NotificationPrefs> b)
    {
        b.ToTable("notification_prefs");
        b.HasKey(x => x.UserId);
        b.Property(x => x.Channels).AsJson();
        b.Property(x => x.Muted).AsJson();
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class MessengerLinkConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.MessengerLink>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.MessengerLink> b)
    {
        b.ToTable("messenger_links");
        b.HasKey(x => new { x.UserId, x.Messenger });
        b.HasIndex(x => new { x.Messenger, x.ChatId }).IsUnique();
    }
}

internal sealed class MessengerLinkCodeConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.MessengerLinkCode>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.MessengerLinkCode> b)
    {
        b.ToTable("messenger_link_codes");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.CodeHash).IsUnique();
    }
}

internal sealed class BotUpdateConfiguration : IEntityTypeConfiguration<Karnama.Domain.Notify.BotUpdate>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Notify.BotUpdate> b)
    {
        b.ToTable("bot_updates");
        b.HasKey(x => new { x.Messenger, x.UpdateId });
    }
}

internal sealed class PublishJobConfiguration : IEntityTypeConfiguration<Karnama.Domain.Publishing.PublishJob>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Publishing.PublishJob> b)
    {
        b.ToTable("publish_jobs");
        b.Property(x => x.Handle).HasMaxLength(100).IsRequired();
        b.Property(x => x.Caption).HasMaxLength(Karnama.Domain.Publishing.PublishJob.MaxCaption).IsRequired();
        b.Property(x => x.Hashtags).AsJson();
        b.Property(x => x.FileIds).AsJson();
        b.Property(x => x.PostUrl).HasMaxLength(500);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.Status, x.PublishAt });
        b.HasIndex(x => new { x.ClientId, x.PublishAt });
    }
}
