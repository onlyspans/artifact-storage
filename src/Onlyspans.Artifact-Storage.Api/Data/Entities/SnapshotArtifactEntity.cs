namespace Onlyspans.Artifact_Storage.Api.Data.Entities;

public sealed class SnapshotArtifactEntity
{
    public Guid SnapshotId { get; set; }
    public SnapshotEntity Snapshot { get; set; } = null!;

    public Guid ArtifactId { get; set; }
    public ArtifactEntity Artifact { get; set; } = null!;
}
