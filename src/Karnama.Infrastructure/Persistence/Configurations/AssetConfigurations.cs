using Karnama.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Karnama.Infrastructure.Persistence.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.ToTable("assets");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Brand).HasMaxLength(40);
        b.Property(x => x.Model).HasMaxLength(60);
        b.Property(x => x.Serial).HasMaxLength(60);
        b.Property(x => x.Tag).HasMaxLength(30).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.Tag).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasIndex(x => x.Serial);
        b.HasIndex(x => x.HolderId);
        b.HasIndex(x => x.Status);
        b.HasOne<AssetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssetCategoryConfiguration : IEntityTypeConfiguration<AssetCategory>
{
    public void Configure(EntityTypeBuilder<AssetCategory> b)
    {
        b.ToTable("asset_categories");
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> b)
    {
        b.ToTable("asset_assignments");
        b.Property(x => x.ConditionOut).HasMaxLength(500);
        b.Property(x => x.ConditionIn).HasMaxLength(500);
        b.Property(x => x.Damage).HasMaxLength(1000);
        b.Property(x => x.Accessories).AsJson();
        b.Property(x => x.Version).IsRowVersion();
        // PROMPT2 §3: one open hand-over per asset, enforced by the database (pending, active or return submitted).
        b.HasIndex(x => x.AssetId).IsUnique().HasFilter("\"State\" IN (1, 2, 3)").HasDatabaseName("ux_asset_assignments_open");
        b.HasIndex(x => new { x.UserId, x.State });
        b.HasIndex(x => x.TransferToId);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssetEventConfiguration : IEntityTypeConfiguration<AssetEvent>
{
    public void Configure(EntityTypeBuilder<AssetEvent> b)
    {
        b.ToTable("asset_events");
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.AssetId, x.Id });
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssetPhotoConfiguration : IEntityTypeConfiguration<AssetPhoto>
{
    public void Configure(EntityTypeBuilder<AssetPhoto> b)
    {
        b.ToTable("asset_photos");
        b.Property(x => x.FileKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.AssetId, x.At });
        b.HasIndex(x => x.AssignmentId);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
