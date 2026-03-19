using Microsoft.EntityFrameworkCore;
using Onlyspans.Artifact_Storage.Api.Data.Contexts;
using Onlyspans.Artifact_Storage.Api.Data.Entities;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Features.Storage;

namespace Onlyspans.Artifact_Storage.Api.Features.Snapshots;

public sealed class SnapshotService(
    ArtifactStorageDbContext db,
    IStorageBackend storage,
    ILogger<SnapshotService> logger) : ISnapshotService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public async Task<SnapshotEntity> StoreAsync(
        string key, string version, string contentType,
        IReadOnlyList<Guid> artifactIds, Dictionary<string, string> labels,
        Stream content, CancellationToken ct)
    {
        var exists = await db.Snapshots
            .AnyAsync(s => s.Key == key && s.Version == version, ct);

        if (exists)
            throw new InvalidOperationException(
                $"Snapshot '{key}' version '{version}' already exists");

        if (artifactIds.Count > 0)
        {
            var foundCount = await db.Artifacts
                .Where(a => artifactIds.Contains(a.Id))
                .CountAsync(ct);

            if (foundCount != artifactIds.Count)
                throw new InvalidOperationException(
                    $"Some artifact IDs do not exist ({foundCount}/{artifactIds.Count} found)");
        }

        var id = Guid.NewGuid();
        var storagePath = ArtifactService.BuildStoragePath("snapshots", id);

        using var hashStream = new HashingStream(content);
        await storage.WriteAsync(storagePath, hashStream, ct);

        var entity = new SnapshotEntity
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

        db.Snapshots.Add(entity);

        foreach (var artifactId in artifactIds)
        {
            db.SnapshotArtifacts.Add(new SnapshotArtifactEntity
            {
                SnapshotId = id,
                ArtifactId = artifactId,
            });
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stored snapshot {Key}@{Version} ({SizeBytes} bytes, id={Id}, artifacts={ArtifactCount})",
            key, version, entity.SizeBytes, id, artifactIds.Count);

        return entity;
    }

    public async Task<(SnapshotEntity Meta, Stream Content)> LoadAsync(
        string key, string version, CancellationToken ct)
    {
        var entity = await db.Snapshots
            .AsNoTracking()
            .Include(s => s.SnapshotArtifacts)
            .FirstOrDefaultAsync(s => s.Key == key && s.Version == version, ct)
            ?? throw new KeyNotFoundException(
                $"Snapshot '{key}' version '{version}' not found");

        var stream = await storage.ReadAsync(entity.StoragePath, ct);
        return (entity, stream);
    }

    public async Task<SnapshotEntity?> GetInfoAsync(
        string key, string version, CancellationToken ct)
    {
        return await db.Snapshots
            .AsNoTracking()
            .Include(s => s.SnapshotArtifacts)
            .FirstOrDefaultAsync(s => s.Key == key && s.Version == version, ct);
    }

    public async Task<SnapshotEntity?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Snapshots
            .AsNoTracking()
            .Include(s => s.SnapshotArtifacts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<(List<SnapshotEntity> Items, int TotalCount, string? NextPageToken)> ListAsync(
        string? keyPrefix, Dictionary<string, string>? labelFilter,
        int pageSize, string? pageToken, CancellationToken ct)
    {
        var query = db.Snapshots
            .AsNoTracking()
            .Include(s => s.SnapshotArtifacts)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyPrefix))
            query = query.Where(s => s.Key.StartsWith(keyPrefix));

        if (labelFilter is { Count: > 0 })
        {
            foreach (var (lk, lv) in labelFilter)
            {
                var filter = new Dictionary<string, string> { { lk, lv } };
                query = query.Where(s => EF.Functions.JsonContains(s.Labels, filter));
            }
        }

        var totalCount = await query.CountAsync(ct);

        query = query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id);

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
}
