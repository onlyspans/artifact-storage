namespace Onlyspans.Artifact_Storage.Api.Data.Entities;

public sealed class ArtifactEntity
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Version { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string ChecksumSha256 { get; set; }
    public required string StoragePath { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SnapshotArtifactEntity> SnapshotArtifacts { get; set; } = [];
}
