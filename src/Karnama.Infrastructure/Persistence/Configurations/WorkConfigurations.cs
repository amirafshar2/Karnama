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
        b.Property(x => x.Payload).HasMaxLength(8000);
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

internal sealed class BlogPostConfiguration : IEntityTypeConfiguration<Karnama.Domain.Content.BlogPost>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Content.BlogPost> b)
    {
        b.ToTable("blog_posts");
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.Property(x => x.ContentJson).HasColumnType("text").IsRequired();
        b.Property(x => x.ContentHtml).HasColumnType("text").IsRequired();
        b.Property(x => x.ContentMarkdown).HasColumnType("text").IsRequired();
        b.Property(x => x.PlainText).HasColumnType("text").IsRequired();
        b.Property(x => x.CoverImage).HasMaxLength(100);
        b.Property(x => x.CoverAlt).HasMaxLength(200);
        b.Property(x => x.SeoTitle).HasMaxLength(80).IsRequired();
        b.Property(x => x.MetaDescription).HasMaxLength(200).IsRequired();
        b.Property(x => x.FocusKeyword).HasMaxLength(80).IsRequired();
        b.Property(x => x.Tags).AsJson();
        b.Property(x => x.Toc).AsJson();
        b.Property(x => x.SeoChecks).AsJson();
        b.Property(x => x.Faq).AsJson();
        b.Property(x => x.Version).IsRowVersion();
        // Slugs are unique among everything that isn't archived (an archived post frees its URL).
        b.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasIndex(x => new { x.Status, x.PublishedAt });
        b.HasIndex(x => x.AuthorId);
    }
}

internal sealed class BlogCategoryConfiguration : IEntityTypeConfiguration<Karnama.Domain.Content.BlogCategory>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Content.BlogCategory> b)
    {
        b.ToTable("blog_categories");
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(70).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

internal sealed class BlogSlugRedirectConfiguration : IEntityTypeConfiguration<Karnama.Domain.Content.BlogSlugRedirect>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Content.BlogSlugRedirect> b)
    {
        b.ToTable("blog_slug_redirects");
        b.HasKey(x => x.OldSlug);
        b.Property(x => x.OldSlug).HasMaxLength(100);
        b.HasIndex(x => x.PostId);
    }
}

internal sealed class CaseStudyConfiguration : IEntityTypeConfiguration<Karnama.Domain.Content.CaseStudy>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Content.CaseStudy> b)
    {
        b.ToTable("case_studies");
        b.Property(x => x.BusinessName).HasMaxLength(80).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        b.Property(x => x.Services).AsJson();
        b.Property(x => x.Metrics).AsJson();
        b.Property(x => x.Shots).AsJson();
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.FollowerGrowth);
        b.Ignore(x => x.RevenueGrowth);
        b.Ignore(x => x.CanPublish);
        b.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Karnama.Domain.Crm.Lead>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Crm.Lead> b)
    {
        b.ToTable("leads");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Business).HasMaxLength(80);
        b.Property(x => x.Industry).HasMaxLength(60);
        b.Property(x => x.City).HasMaxLength(40);
        b.Property(x => x.Instagram).HasMaxLength(60);
        b.Property(x => x.Message).HasMaxLength(1000);
        // Kept the N10 column name (website page the request came from).
        b.Property(x => x.Origin).HasColumnName("Source").HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.LostReason).HasMaxLength(300);
        b.Property(x => x.Tags).AsJson();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.Phone);
        b.HasIndex(x => new { x.OwnerId, x.StageId });
        b.HasIndex(x => x.NextFollowUpAt);
        b.HasIndex(x => x.MessengerChatId);
        b.HasOne<Karnama.Domain.Crm.LeadStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Karnama.Domain.Crm.LeadSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class LeadStageConfiguration : IEntityTypeConfiguration<Karnama.Domain.Crm.LeadStage>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Crm.LeadStage> b)
    {
        b.ToTable("lead_stages");
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
    }
}

internal sealed class LeadSourceConfiguration : IEntityTypeConfiguration<Karnama.Domain.Crm.LeadSource>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Crm.LeadSource> b)
    {
        b.ToTable("lead_sources");
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class LeadActivityConfiguration : IEntityTypeConfiguration<Karnama.Domain.Crm.LeadActivity>
{
    public void Configure(EntityTypeBuilder<Karnama.Domain.Crm.LeadActivity> b)
    {
        b.ToTable("lead_activities");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.LeadId, x.Id });
        b.HasIndex(x => new { x.Kind, x.At });
        b.HasOne<Karnama.Domain.Crm.Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}
