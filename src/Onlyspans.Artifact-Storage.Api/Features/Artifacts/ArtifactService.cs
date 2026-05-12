using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Onlyspans.Artifact_Storage.Api.Data.Contexts;
using Onlyspans.Artifact_Storage.Api.Data.Entities;
using Onlyspans.Artifact_Storage.Api.Features.Storage;

namespace Onlyspans.Artifact_Storage.Api.Features.Artifacts;

public sealed class ArtifactService(
    ArtifactStorageDbContext db,
    IStorageBackend storage,
    ILogger<ArtifactService> logger) : IArtifactService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public async Task<ArtifactEntity> StoreAsync(
        string key, string version, string contentType,
        Dictionary<string, string> labels, Stream content,
        CancellationToken ct)
    {
        var exists = await db.Artifacts
            .AnyAsync(a => a.Key == key && a.Version == version, ct);

        if (exists)
            throw new InvalidOperationException(
                $"Artifact '{key}' version '{version}' already exists");

        var id = Guid.NewGuid();
        var storagePath = BuildStoragePath("artifacts", id);

        using var hashStream = new HashingStream(content);
        await storage.WriteAsync(storagePath, hashStream, ct);

        var entity = new ArtifactEntity
        {
            Id = id,
            Key = key,
            Version = version,
            ContentType = contentType,
            SizeBytes = hashStream.BytesWritten,
            ChecksumSha256 = hashStream.GetHashHex(),
            StoragePath = storagePath,
            Labels = labels,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Artifacts.Add(entity);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stored artifact {Key}@{Version} ({SizeBytes} bytes, id={Id})",
            key, version, entity.SizeBytes, id);

        return entity;
    }

    public async Task<(ArtifactEntity Meta, Stream Content)> LoadAsync(
        string key, string version, CancellationToken ct)
    {
        var entity = await db.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == key && a.Version == version, ct)
            ?? throw new KeyNotFoundException(
                $"Artifact '{key}' version '{version}' not found");

        var stream = await storage.ReadAsync(entity.StoragePath, ct);
        return (entity, stream);
    }

    public async Task<ArtifactEntity?> GetInfoAsync(
        string key, string version, CancellationToken ct)
    {
        return await db.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == key && a.Version == version, ct);
    }

    public async Task<ArtifactEntity?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(List<ArtifactEntity> Items, int TotalCount, string? NextPageToken)> ListAsync(
        string? keyPrefix, Dictionary<string, string>? labelFilter,
        int pageSize, string? pageToken, CancellationToken ct)
    {
        var query = db.Artifacts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(keyPrefix))
            query = query.Where(a => a.Key.StartsWith(keyPrefix));

        if (labelFilter is { Count: > 0 })
        {
            foreach (var (lk, lv) in labelFilter)
            {
                var filter = new Dictionary<string, string> { { lk, lv } };
                query = query.Where(a => EF.Functions.JsonContains(a.Labels, filter));
            }
        }

        var totalCount = await query.CountAsync(ct);

        query = query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id);

        var offset = 0;
        if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var parsedOffset))
            offset = parsedOffset;

        var effectivePageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var items = await query.Skip(offset).Take(effectivePageSize + 1).ToListAsync(ct);

        string? nextToken = null;
        if (items.Count > effectivePageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextToken = (offset + effectivePageSize).ToString();
        }

        return (items, totalCount, nextToken);
    }

    internal static string BuildStoragePath(string prefix, Guid id)
    {
        var hex = id.ToString("N");
        return $"{prefix}/{hex[..2]}/{hex[2..4]}/{hex}";
    }
}

internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;
    private long _bytesWritten;

    public HashingStream(Stream inner)
    {
        _inner = inner;
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public long BytesWritten => _bytesWritten;

    public string GetHashHex()
        => Convert.ToHexString(_hash.GetCurrentHash()).ToLowerInvariant();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _hash.AppendData(buffer, offset, read);
            _bytesWritten += read;
        }
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct);
        if (read > 0)
        {
            _hash.AppendData(buffer, offset, read);
            _bytesWritten += read;
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct);
        if (read > 0)
        {
            _hash.AppendData(buffer[..read].Span);
            _bytesWritten += read;
        }
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
