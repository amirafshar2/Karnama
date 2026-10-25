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
