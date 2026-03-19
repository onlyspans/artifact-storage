using Onlyspans.Artifact_Storage.Api.Data.Entities;

namespace Onlyspans.Artifact_Storage.Api.Features.Snapshots;

public interface ISnapshotService
{
    Task<SnapshotEntity> StoreAsync(
        string key, string version, string contentType,
        IReadOnlyList<Guid> artifactIds, Dictionary<string, string> labels,
        Stream content, CancellationToken ct);

    Task<(SnapshotEntity Meta, Stream Content)> LoadAsync(
        string key, string version, CancellationToken ct);

    Task<SnapshotEntity?> GetInfoAsync(string key, string version, CancellationToken ct);

    Task<SnapshotEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<(List<SnapshotEntity> Items, int TotalCount, string? NextPageToken)> ListAsync(
        string? keyPrefix, Dictionary<string, string>? labelFilter,
        int pageSize, string? pageToken, CancellationToken ct);
}
