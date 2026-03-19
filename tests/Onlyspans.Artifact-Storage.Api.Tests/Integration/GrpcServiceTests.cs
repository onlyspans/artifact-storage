using ArtifactStorage.Communication;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Onlyspans.Artifact_Storage.Api.Configuration;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Features.Snapshots;
using Onlyspans.Artifact_Storage.Api.Grpc.Services;
using Onlyspans.Artifact_Storage.Api.Tests.Fixtures;
using static Onlyspans.Artifact_Storage.Api.Tests.Fixtures.AppFixture;

namespace Onlyspans.Artifact_Storage.Api.Tests.Integration;

public sealed class GrpcServiceTests(AppFixture appFixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task UploadArtifact_ValidStream_ReturnsSuccessWithMetadata()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var data = "grpc artifact content"u8.ToArray();
        var messages = BuildUploadArtifactMessages(
            "grpc/artifact.txt", "v1", "text/plain", data);

        var reader = new FakeAsyncStreamReader<UploadArtifactRequest>(messages);
        var context = CreateServerCallContext(ct);

        // Act
        var response = await grpcService.UploadArtifact(reader, context);

        // Assert
        response.ResultCase.Should().Be(UploadArtifactResponse.ResultOneofCase.Success);
        response.Success.Key.Should().Be("grpc/artifact.txt");
        response.Success.Version.Should().Be("v1");
        response.Success.SizeBytes.Should().Be(data.Length);
        response.Success.ChecksumSha256.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadArtifact_EmptyStream_ReturnsError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var reader = new FakeAsyncStreamReader<UploadArtifactRequest>([]);
        var context = CreateServerCallContext(ct);

        // Act
        var response = await grpcService.UploadArtifact(reader, context);

        // Assert
        response.ResultCase.Should().Be(UploadArtifactResponse.ResultOneofCase.Error);
        response.Error.Code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task UploadArtifact_MissingHeader_ReturnsError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var messages = new List<UploadArtifactRequest>
        {
            new() { Chunk = ByteString.CopyFrom([0x01]) },
        };

        var reader = new FakeAsyncStreamReader<UploadArtifactRequest>(messages);
        var context = CreateServerCallContext(ct);

        // Act
        var response = await grpcService.UploadArtifact(reader, context);

        // Assert
        response.ResultCase.Should().Be(UploadArtifactResponse.ResultOneofCase.Error);
        response.Error.Code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task UploadArtifact_Duplicate_ReturnsAlreadyExists()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var messages = BuildUploadArtifactMessages(
            "grpc-dup/file.txt", "v1", "text/plain", [0x01]);

        var reader1 = new FakeAsyncStreamReader<UploadArtifactRequest>(messages);
        await grpcService.UploadArtifact(reader1, CreateServerCallContext(ct));

        var messages2 = BuildUploadArtifactMessages(
            "grpc-dup/file.txt", "v1", "text/plain", [0x02]);
        var reader2 = new FakeAsyncStreamReader<UploadArtifactRequest>(messages2);

        // Act
        var response = await grpcService.UploadArtifact(reader2, CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(UploadArtifactResponse.ResultOneofCase.Error);
        response.Error.Code.Should().Be("ALREADY_EXISTS");
    }

    [Fact]
    public async Task DownloadArtifact_ExistingArtifact_StreamsHeaderThenChunks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var data = new byte[10_000];
        Random.Shared.NextBytes(data);

        var uploadMessages = BuildUploadArtifactMessages(
            "grpc-dl/file.bin", "v1", "application/octet-stream", data);
        var uploadReader = new FakeAsyncStreamReader<UploadArtifactRequest>(uploadMessages);
        await grpcService.UploadArtifact(uploadReader, CreateServerCallContext(ct));

        var responseWriter = new FakeServerStreamWriter<DownloadArtifactResponse>();

        // Act
        await grpcService.DownloadArtifact(
            new DownloadArtifactRequest { Key = "grpc-dl/file.bin", Version = "v1" },
            responseWriter,
            CreateServerCallContext(ct));

        // Assert
        responseWriter.Written.Should().HaveCountGreaterThanOrEqualTo(2);
        responseWriter.Written[0].PayloadCase.Should()
            .Be(DownloadArtifactResponse.PayloadOneofCase.Header);
        responseWriter.Written[0].Header.Key.Should().Be("grpc-dl/file.bin");
        responseWriter.Written[0].Header.SizeBytes.Should().Be(data.Length);

        var reassembled = responseWriter.Written
            .Where(m => m.PayloadCase == DownloadArtifactResponse.PayloadOneofCase.Chunk)
            .SelectMany(m => m.Chunk.ToByteArray())
            .ToArray();

        reassembled.Should().Equal(data);
    }

    [Fact]
    public async Task DownloadArtifact_NonExistent_ThrowsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var responseWriter = new FakeServerStreamWriter<DownloadArtifactResponse>();

        // Act
        var act = () => grpcService.DownloadArtifact(
            new DownloadArtifactRequest { Key = "no/file", Version = "v1" },
            responseWriter,
            CreateServerCallContext(ct));

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.NotFound);
    }

    [Fact]
    public async Task GetArtifactInfo_Existing_ReturnsSuccess()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var messages = BuildUploadArtifactMessages(
            "grpc-info/artifact.txt", "v1", "text/plain", [0x01]);
        var reader = new FakeAsyncStreamReader<UploadArtifactRequest>(messages);
        await grpcService.UploadArtifact(reader, CreateServerCallContext(ct));

        // Act
        var response = await grpcService.GetArtifactInfo(
            new GetArtifactInfoRequest { Key = "grpc-info/artifact.txt", Version = "v1" },
            CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(GetArtifactInfoResponse.ResultOneofCase.Success);
        response.Success.Key.Should().Be("grpc-info/artifact.txt");
    }

    [Fact]
    public async Task GetArtifactInfo_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        // Act
        var response = await grpcService.GetArtifactInfo(
            new GetArtifactInfoRequest { Key = "nope", Version = "v1" },
            CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(GetArtifactInfoResponse.ResultOneofCase.Error);
        response.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ListArtifacts_ReturnsUploadedArtifacts()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        for (var i = 0; i < 3; i++)
        {
            var msgs = BuildUploadArtifactMessages(
                $"grpc-list/f{i}.txt", "v1", "text/plain", [(byte)i]);
            var r = new FakeAsyncStreamReader<UploadArtifactRequest>(msgs);
            await grpcService.UploadArtifact(r, CreateServerCallContext(ct));
        }

        // Act
        var response = await grpcService.ListArtifacts(
            new ListArtifactsRequest { KeyPrefix = "grpc-list/", PageSize = 10 },
            CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(ListArtifactsResponse.ResultOneofCase.Success);
        response.Success.Items.Should().HaveCount(3);
        response.Success.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task UploadSnapshot_ValidStream_ReturnsSuccess()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var data = "snapshot data"u8.ToArray();
        var messages = BuildUploadSnapshotMessages(
            "grpc-snap/release", "v1", "application/gzip", data);

        var reader = new FakeAsyncStreamReader<UploadSnapshotRequest>(messages);

        // Act
        var response = await grpcService.UploadSnapshot(reader, CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(UploadSnapshotResponse.ResultOneofCase.Success);
        response.Success.Key.Should().Be("grpc-snap/release");
        response.Success.Version.Should().Be("v1");
    }

    [Fact]
    public async Task UploadSnapshot_WithArtifactIds_LinksCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var artMessages = BuildUploadArtifactMessages(
            "grpc-snap-link/art.txt", "v1", "text/plain", [0x01]);
        var artReader = new FakeAsyncStreamReader<UploadArtifactRequest>(artMessages);
        var artResponse = await grpcService.UploadArtifact(artReader, CreateServerCallContext(ct));
        var artifactId = artResponse.Success.Id;

        var snapData = "snapshot with artifact"u8.ToArray();
        var snapMessages = BuildUploadSnapshotMessages(
            "grpc-snap-link/release", "v1", "application/gzip", snapData,
            artifactIds: [artifactId]);

        var snapReader = new FakeAsyncStreamReader<UploadSnapshotRequest>(snapMessages);

        // Act
        var snapResponse = await grpcService.UploadSnapshot(snapReader, CreateServerCallContext(ct));

        // Assert
        snapResponse.ResultCase.Should().Be(UploadSnapshotResponse.ResultOneofCase.Success);
        snapResponse.Success.ArtifactIds.Should().Contain(artifactId);
    }

    [Fact]
    public async Task DownloadSnapshot_ExistingSnapshot_StreamsCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var data = new byte[8_000];
        Random.Shared.NextBytes(data);

        var uploadMessages = BuildUploadSnapshotMessages(
            "grpc-snapdl/release", "v1", "application/gzip", data);
        var uploadReader = new FakeAsyncStreamReader<UploadSnapshotRequest>(uploadMessages);
        await grpcService.UploadSnapshot(uploadReader, CreateServerCallContext(ct));

        var responseWriter = new FakeServerStreamWriter<DownloadSnapshotResponse>();

        // Act
        await grpcService.DownloadSnapshot(
            new DownloadSnapshotRequest { Key = "grpc-snapdl/release", Version = "v1" },
            responseWriter,
            CreateServerCallContext(ct));

        // Assert
        responseWriter.Written.Should().HaveCountGreaterThanOrEqualTo(2);
        responseWriter.Written[0].PayloadCase.Should()
            .Be(DownloadSnapshotResponse.PayloadOneofCase.Header);

        var reassembled = responseWriter.Written
            .Where(m => m.PayloadCase == DownloadSnapshotResponse.PayloadOneofCase.Chunk)
            .SelectMany(m => m.Chunk.ToByteArray())
            .ToArray();

        reassembled.Should().Equal(data);
    }

    [Fact]
    public async Task DownloadSnapshot_NonExistent_ThrowsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var responseWriter = new FakeServerStreamWriter<DownloadSnapshotResponse>();

        // Act
        var act = () => grpcService.DownloadSnapshot(
            new DownloadSnapshotRequest { Key = "no/snap", Version = "v1" },
            responseWriter,
            CreateServerCallContext(ct));

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.NotFound);
    }

    [Fact]
    public async Task GetSnapshotInfo_Existing_ReturnsSuccess()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        var messages = BuildUploadSnapshotMessages(
            "grpc-snapinfo/release", "v1", "application/gzip", [0x01]);
        var reader = new FakeAsyncStreamReader<UploadSnapshotRequest>(messages);
        await grpcService.UploadSnapshot(reader, CreateServerCallContext(ct));

        // Act
        var response = await grpcService.GetSnapshotInfo(
            new GetSnapshotInfoRequest { Key = "grpc-snapinfo/release", Version = "v1" },
            CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(GetSnapshotInfoResponse.ResultOneofCase.Success);
        response.Success.Key.Should().Be("grpc-snapinfo/release");
    }

    [Fact]
    public async Task ListSnapshots_ReturnsUploadedSnapshots()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        var grpcService = ResolveGrpc(app);

        for (var i = 0; i < 2; i++)
        {
            var msgs = BuildUploadSnapshotMessages(
                $"grpc-snaplist/s{i}", "v1", "application/gzip", [(byte)i]);
            var r = new FakeAsyncStreamReader<UploadSnapshotRequest>(msgs);
            await grpcService.UploadSnapshot(r, CreateServerCallContext(ct));
        }

        // Act
        var response = await grpcService.ListSnapshots(
            new ListSnapshotsRequest { KeyPrefix = "grpc-snaplist/", PageSize = 10 },
            CreateServerCallContext(ct));

        // Assert
        response.ResultCase.Should().Be(ListSnapshotsResponse.ResultOneofCase.Success);
        response.Success.Items.Should().HaveCount(2);
    }

    private static ArtifactStorageGrpcService ResolveGrpc(WebApplication app)
    {
        var scope = app.Services.CreateScope();
        return new ArtifactStorageGrpcService(
            scope.ServiceProvider.GetRequiredService<IArtifactService>(),
            scope.ServiceProvider.GetRequiredService<ISnapshotService>(),
            scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>(),
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<ArtifactStorageGrpcService>());
    }

    private static List<UploadArtifactRequest> BuildUploadArtifactMessages(
        string key, string version, string contentType, byte[] data, int chunkSize = 4096)
    {
        var messages = new List<UploadArtifactRequest>
        {
            new()
            {
                Header = new ArtifactUploadHeader
                {
                    Key = key,
                    Version = version,
                    ContentType = contentType,
                },
            },
        };

        for (var i = 0; i < data.Length; i += chunkSize)
        {
            var size = Math.Min(chunkSize, data.Length - i);
            messages.Add(new UploadArtifactRequest
            {
                Chunk = ByteString.CopyFrom(data, i, size),
            });
        }

        return messages;
    }

    private static List<UploadSnapshotRequest> BuildUploadSnapshotMessages(
        string key, string version, string contentType, byte[] data,
        IEnumerable<string>? artifactIds = null, int chunkSize = 4096)
    {
        var header = new SnapshotUploadHeader
        {
            Key = key,
            Version = version,
            ContentType = contentType,
        };

        if (artifactIds is not null)
            header.ArtifactIds.AddRange(artifactIds);

        var messages = new List<UploadSnapshotRequest>
        {
            new() { Header = header },
        };

        for (var i = 0; i < data.Length; i += chunkSize)
        {
            var size = Math.Min(chunkSize, data.Length - i);
            messages.Add(new UploadSnapshotRequest
            {
                Chunk = ByteString.CopyFrom(data, i, size),
            });
        }

        return messages;
    }

    private static ServerCallContext CreateServerCallContext(CancellationToken ct)
    {
        var context = Substitute.For<ServerCallContext>();
        context.CancellationToken.Returns(ct);
        return context;
    }

    private Task<WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: postconfigure);
}
