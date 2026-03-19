using Microsoft.Extensions.Options;
using Onlyspans.Artifact_Storage.Api.Configuration;

namespace Onlyspans.Artifact_Storage.Api.Features.Storage;

public sealed class LocalFsStorageBackend(IOptions<StorageOptions> options) : IStorageBackend
{
    private readonly string _basePath = Path.GetFullPath(options.Value.Fs.BasePath);

    public async Task WriteAsync(string storagePath, Stream content, CancellationToken ct)
    {
        var fullPath = GetFullPath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81_920, useAsync: true);

        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> ReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = GetFullPath(storagePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Blob not found at path: {storagePath}");

        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81_920, useAsync: true);

        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = GetFullPath(storagePath);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string GetFullPath(string storagePath)
        => Path.Combine(_basePath, storagePath.Replace('/', Path.DirectorySeparatorChar));
}
