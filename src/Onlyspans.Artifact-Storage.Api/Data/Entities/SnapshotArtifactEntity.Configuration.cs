using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Artifact_Storage.Api.Data.Entities;

public sealed class SnapshotArtifactEntityConfiguration : IEntityTypeConfiguration<SnapshotArtifactEntity>
{
    public void Configure(EntityTypeBuilder<SnapshotArtifactEntity> builder)
    {
        builder.ToTable("snapshot_artifacts");

        builder.HasKey(sa => new { sa.SnapshotId, sa.ArtifactId });
        builder.Property(sa => sa.SnapshotId).HasColumnName("snapshot_id");
        builder.Property(sa => sa.ArtifactId).HasColumnName("artifact_id");

        builder.HasOne(sa => sa.Snapshot)
            .WithMany(s => s.SnapshotArtifacts)
            .HasForeignKey(sa => sa.SnapshotId);

        builder.HasOne(sa => sa.Artifact)
            .WithMany(a => a.SnapshotArtifacts)
            .HasForeignKey(sa => sa.ArtifactId);
    }
}
