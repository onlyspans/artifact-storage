using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Artifact_Storage.Api.Data.Entities;

public sealed class SnapshotEntityConfiguration : IEntityTypeConfiguration<SnapshotEntity>
{
    public void Configure(EntityTypeBuilder<SnapshotEntity> builder)
    {
        builder.ToTable("snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(s => s.Key).HasColumnName("key").IsRequired();
        builder.Property(s => s.Version).HasColumnName("version").IsRequired();
        builder.Property(s => s.ContentType).HasColumnName("content_type").IsRequired();
        builder.Property(s => s.SizeBytes).HasColumnName("size_bytes");
        builder.Property(s => s.ChecksumSha256).HasColumnName("checksum_sha256").IsRequired();
        builder.Property(s => s.StoragePath).HasColumnName("storage_path").IsRequired();
        builder.Property(s => s.Labels).HasColumnName("labels").HasColumnType("jsonb");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(s => new { s.Key, s.Version }).IsUnique();
        builder.HasIndex(s => s.Key)
            .HasDatabaseName("ix_snapshots_key_prefix");
        builder.HasIndex(s => s.Labels)
            .HasDatabaseName("ix_snapshots_labels")
            .HasMethod("gin");
    }
}
