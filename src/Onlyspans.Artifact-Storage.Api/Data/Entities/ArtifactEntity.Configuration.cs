using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Artifact_Storage.Api.Data.Entities;

public sealed class ArtifactEntityConfiguration : IEntityTypeConfiguration<ArtifactEntity>
{
    public void Configure(EntityTypeBuilder<ArtifactEntity> builder)
    {
        builder.ToTable("artifacts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(a => a.Key).HasColumnName("key").IsRequired();
        builder.Property(a => a.Version).HasColumnName("version").IsRequired();
        builder.Property(a => a.ContentType).HasColumnName("content_type").IsRequired();
        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes");
        builder.Property(a => a.ChecksumSha256).HasColumnName("checksum_sha256").IsRequired();
        builder.Property(a => a.StoragePath).HasColumnName("storage_path").IsRequired();
        builder.Property(a => a.Labels).HasColumnName("labels").HasColumnType("jsonb");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.Key, a.Version }).IsUnique();
        builder.HasIndex(a => a.Key)
            .HasDatabaseName("ix_artifacts_key_prefix");
        builder.HasIndex(a => a.Labels)
            .HasDatabaseName("ix_artifacts_labels")
            .HasMethod("gin");
    }
}
