using Microsoft.EntityFrameworkCore;
using Onlyspans.Artifact_Storage.Api.Data.Entities;

namespace Onlyspans.Artifact_Storage.Api.Data.Contexts;

public sealed class ArtifactStorageDbContext(DbContextOptions<ArtifactStorageDbContext> options)
    : DbContext(options)
{
    public DbSet<ArtifactEntity> Artifacts => Set<ArtifactEntity>();
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();
    public DbSet<SnapshotArtifactEntity> SnapshotArtifacts => Set<SnapshotArtifactEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArtifactStorageDbContext).Assembly);
    }
}
