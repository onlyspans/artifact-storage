using Onlyspans.Artifact_Storage.Api.Data.Entities;

namespace Onlyspans.Artifact_Storage.Api.Features.Artifacts;

public interface IArtifactService
{
    Task<ArtifactEntity> StoreAsync(
        string key, string version, string contentType,
        Dictionary<string, string> labels, Stream content,
        CancellationToken ct);

    Task<(ArtifactEntity Meta, Stream Content)> LoadAsync(
        string key, string version, CancellationToken ct);

    Task<ArtifactEntity?> GetInfoAsync(string key, string version, CancellationToken ct);

    Task<ArtifactEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<(List<ArtifactEntity> Items, int TotalCount, string? NextPageToken)> ListAsync(
        string? keyPrefix, Dictionary<string, string>? labelFilter,
        int pageSize, string? pageToken, CancellationToken ct);
}
