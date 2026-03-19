using ArtifactStorage.Communication;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Onlyspans.Artifact_Storage.Api.Configuration;
using Onlyspans.Artifact_Storage.Api.Data.Entities;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Features.Snapshots;

namespace Onlyspans.Artifact_Storage.Api.Grpc.Services;

public sealed class ArtifactStorageGrpcService(
    IArtifactService artifactService,
    ISnapshotService snapshotService,
    IOptions<StorageOptions> storageOptions,
    ILogger<ArtifactStorageGrpcService> logger)
    : ArtifactStorageService.ArtifactStorageServiceBase
{
    private int ChunkSize => storageOptions.Value.ChunkSizeBytes;

    public override async Task<UploadArtifactResponse> UploadArtifact(
        IAsyncStreamReader<UploadArtifactRequest> requestStream,
        ServerCallContext context)
    {
        try
        {
            if (!await requestStream.MoveNext(context.CancellationToken))
                return ErrorUploadArtifact("INVALID_ARGUMENT", "Empty request stream");

            if (requestStream.Current.PayloadCase != UploadArtifactRequest.PayloadOneofCase.Header)
                return ErrorUploadArtifact("INVALID_ARGUMENT", "First message must be header");

            var header = requestStream.Current.Header;

            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Version))
                return ErrorUploadArtifact("INVALID_ARGUMENT", "key and version are required");

            await using var buffer = new MemoryStream();
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                if (requestStream.Current.PayloadCase == UploadArtifactRequest.PayloadOneofCase.Chunk)
                    requestStream.Current.Chunk.WriteTo(buffer);
            }

            buffer.Position = 0;

            var labels = new Dictionary<string, string>(header.Labels);
            var entity = await artifactService.StoreAsync(
                header.Key, header.Version, header.ContentType,
                labels, buffer, context.CancellationToken);

            return new UploadArtifactResponse { Success = ToArtifactInfo(entity) };
        }
        catch (InvalidOperationException ex)
        {
            return ErrorUploadArtifact("ALREADY_EXISTS", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UploadArtifact failed");
            return ErrorUploadArtifact("INTERNAL", ex.Message);
        }
    }

    public override async Task DownloadArtifact(
        DownloadArtifactRequest request,
        IServerStreamWriter<DownloadArtifactResponse> responseStream,
        ServerCallContext context)
    {
        try
        {
            var (meta, content) = await artifactService.LoadAsync(
                request.Key, request.Version, context.CancellationToken);

            await using (content)
            {
                await responseStream.WriteAsync(
                    new DownloadArtifactResponse { Header = ToArtifactInfo(meta) },
                    context.CancellationToken);

                var buf = new byte[ChunkSize];
                int read;
                while ((read = await content.ReadAsync(buf, context.CancellationToken)) > 0)
                {
                    await responseStream.WriteAsync(
                        new DownloadArtifactResponse
                        {
                            Chunk = ByteString.CopyFrom(buf, 0, read),
                        },
                        context.CancellationToken);
                }
            }
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Artifact '{request.Key}' version '{request.Version}' not found"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DownloadArtifact failed for {Key}@{Version}",
                request.Key, request.Version);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<GetArtifactInfoResponse> GetArtifactInfo(
        GetArtifactInfoRequest request,
        ServerCallContext context)
    {
        try
        {
            var entity = await artifactService.GetInfoAsync(
                request.Key, request.Version, context.CancellationToken);

            if (entity is null)
                return new GetArtifactInfoResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "NOT_FOUND",
                        Message = $"Artifact '{request.Key}' version '{request.Version}' not found",
                    },
                };

            return new GetArtifactInfoResponse { Success = ToArtifactInfo(entity) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetArtifactInfo failed");
            return new GetArtifactInfoResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    public override async Task<GetArtifactByIdResponse> GetArtifactById(
        GetArtifactByIdRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.Id, out var id))
                return new GetArtifactByIdResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "INVALID_ARGUMENT",
                        Message = $"'{request.Id}' is not a valid GUID",
                    },
                };

            var entity = await artifactService.GetByIdAsync(id, context.CancellationToken);

            if (entity is null)
                return new GetArtifactByIdResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "NOT_FOUND",
                        Message = $"Artifact with id '{request.Id}' not found",
                    },
                };

            return new GetArtifactByIdResponse { Success = ToArtifactInfo(entity) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetArtifactById failed");
            return new GetArtifactByIdResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    public override async Task<ListArtifactsResponse> ListArtifacts(
        ListArtifactsRequest request,
        ServerCallContext context)
    {
        try
        {
            var labelFilter = request.LabelFilter.Count > 0
                ? new Dictionary<string, string>(request.LabelFilter)
                : null;

            var (items, totalCount, nextToken) = await artifactService.ListAsync(
                request.HasKeyPrefix ? request.KeyPrefix : null,
                labelFilter,
                request.PageSize,
                request.PageToken,
                context.CancellationToken);

            var list = new ArtifactList { TotalCount = totalCount };
            list.Items.AddRange(items.Select(ToArtifactInfo));
            if (nextToken is not null)
                list.NextPageToken = nextToken;

            return new ListArtifactsResponse { Success = list };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ListArtifacts failed");
            return new ListArtifactsResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    public override async Task<UploadSnapshotResponse> UploadSnapshot(
        IAsyncStreamReader<UploadSnapshotRequest> requestStream,
        ServerCallContext context)
    {
        try
        {
            if (!await requestStream.MoveNext(context.CancellationToken))
                return ErrorUploadSnapshot("INVALID_ARGUMENT", "Empty request stream");

            if (requestStream.Current.PayloadCase != UploadSnapshotRequest.PayloadOneofCase.Header)
                return ErrorUploadSnapshot("INVALID_ARGUMENT", "First message must be header");

            var header = requestStream.Current.Header;

            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Version))
                return ErrorUploadSnapshot("INVALID_ARGUMENT", "key and version are required");

            await using var buffer = new MemoryStream();
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                if (requestStream.Current.PayloadCase == UploadSnapshotRequest.PayloadOneofCase.Chunk)
                    requestStream.Current.Chunk.WriteTo(buffer);
            }

            buffer.Position = 0;

            var artifactIds = header.ArtifactIds
                .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();

            var labels = new Dictionary<string, string>(header.Labels);

            var entity = await snapshotService.StoreAsync(
                header.Key, header.Version, header.ContentType,
                artifactIds, labels, buffer, context.CancellationToken);

            return new UploadSnapshotResponse { Success = ToSnapshotInfo(entity) };
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("already exists") ? "ALREADY_EXISTS" : "INVALID_ARGUMENT";
            return ErrorUploadSnapshot(code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UploadSnapshot failed");
            return ErrorUploadSnapshot("INTERNAL", ex.Message);
        }
    }

    public override async Task DownloadSnapshot(
        DownloadSnapshotRequest request,
        IServerStreamWriter<DownloadSnapshotResponse> responseStream,
        ServerCallContext context)
    {
        try
        {
            var (meta, content) = await snapshotService.LoadAsync(
                request.Key, request.Version, context.CancellationToken);

            await using (content)
            {
                await responseStream.WriteAsync(
                    new DownloadSnapshotResponse { Header = ToSnapshotInfo(meta) },
                    context.CancellationToken);

                var buf = new byte[ChunkSize];
                int read;
                while ((read = await content.ReadAsync(buf, context.CancellationToken)) > 0)
                {
                    await responseStream.WriteAsync(
                        new DownloadSnapshotResponse
                        {
                            Chunk = ByteString.CopyFrom(buf, 0, read),
                        },
                        context.CancellationToken);
                }
            }
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Snapshot '{request.Key}' version '{request.Version}' not found"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DownloadSnapshot failed for {Key}@{Version}",
                request.Key, request.Version);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<GetSnapshotInfoResponse> GetSnapshotInfo(
        GetSnapshotInfoRequest request,
        ServerCallContext context)
    {
        try
        {
            var entity = await snapshotService.GetInfoAsync(
                request.Key, request.Version, context.CancellationToken);

            if (entity is null)
                return new GetSnapshotInfoResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "NOT_FOUND",
                        Message = $"Snapshot '{request.Key}' version '{request.Version}' not found",
                    },
                };

            return new GetSnapshotInfoResponse { Success = ToSnapshotInfo(entity) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetSnapshotInfo failed");
            return new GetSnapshotInfoResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    public override async Task<GetSnapshotByIdResponse> GetSnapshotById(
        GetSnapshotByIdRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.Id, out var id))
                return new GetSnapshotByIdResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "INVALID_ARGUMENT",
                        Message = $"'{request.Id}' is not a valid GUID",
                    },
                };

            var entity = await snapshotService.GetByIdAsync(id, context.CancellationToken);

            if (entity is null)
                return new GetSnapshotByIdResponse
                {
                    Error = new ErrorResult
                    {
                        Code = "NOT_FOUND",
                        Message = $"Snapshot with id '{request.Id}' not found",
                    },
                };

            return new GetSnapshotByIdResponse { Success = ToSnapshotInfo(entity) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetSnapshotById failed");
            return new GetSnapshotByIdResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    public override async Task<ListSnapshotsResponse> ListSnapshots(
        ListSnapshotsRequest request,
        ServerCallContext context)
    {
        try
        {
            var labelFilter = request.LabelFilter.Count > 0
                ? new Dictionary<string, string>(request.LabelFilter)
                : null;

            var (items, totalCount, nextToken) = await snapshotService.ListAsync(
                request.HasKeyPrefix ? request.KeyPrefix : null,
                labelFilter,
                request.PageSize,
                request.PageToken,
                context.CancellationToken);

            var list = new SnapshotList { TotalCount = totalCount };
            list.Items.AddRange(items.Select(ToSnapshotInfo));
            if (nextToken is not null)
                list.NextPageToken = nextToken;

            return new ListSnapshotsResponse { Success = list };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ListSnapshots failed");
            return new ListSnapshotsResponse
            {
                Error = new ErrorResult { Code = "INTERNAL", Message = ex.Message },
            };
        }
    }

    private static ArtifactInfo ToArtifactInfo(ArtifactEntity e)
    {
        var info = new ArtifactInfo
        {
            Id = e.Id.ToString(),
            Key = e.Key,
            Version = e.Version,
            ContentType = e.ContentType,
            SizeBytes = e.SizeBytes,
            ChecksumSha256 = e.ChecksumSha256,
            CreatedAt = Timestamp.FromDateTimeOffset(e.CreatedAt),
        };
        info.Labels.Add(e.Labels);
        return info;
    }

    private static SnapshotInfo ToSnapshotInfo(SnapshotEntity e)
    {
        var info = new SnapshotInfo
        {
            Id = e.Id.ToString(),
            Key = e.Key,
            Version = e.Version,
            ContentType = e.ContentType,
            SizeBytes = e.SizeBytes,
            ChecksumSha256 = e.ChecksumSha256,
            CreatedAt = Timestamp.FromDateTimeOffset(e.CreatedAt),
        };
        info.ArtifactIds.AddRange(
            e.SnapshotArtifacts.Select(sa => sa.ArtifactId.ToString()));
        info.Labels.Add(e.Labels);
        return info;
    }

    private static UploadArtifactResponse ErrorUploadArtifact(string code, string message)
        => new() { Error = new ErrorResult { Code = code, Message = message } };

    private static UploadSnapshotResponse ErrorUploadSnapshot(string code, string message)
        => new() { Error = new ErrorResult { Code = code, Message = message } };
}
