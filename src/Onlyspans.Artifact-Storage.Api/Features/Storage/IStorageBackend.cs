namespace Onlyspans.Artifact_Storage.Api.Features.Storage;

public interface IStorageBackend
{
    Task WriteAsync(string storagePath, Stream content, CancellationToken ct);
    Task<Stream> ReadAsync(string storagePath, CancellationToken ct);
    Task<bool> ExistsAsync(string storagePath, CancellationToken ct);
}
