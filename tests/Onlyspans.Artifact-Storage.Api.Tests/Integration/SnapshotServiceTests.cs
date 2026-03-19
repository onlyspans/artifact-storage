using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Features.Snapshots;
using Onlyspans.Artifact_Storage.Api.Tests.Fixtures;
using static Onlyspans.Artifact_Storage.Api.Tests.Fixtures.AppFixture;

namespace Onlyspans.Artifact_Storage.Api.Tests.Integration;

public sealed class SnapshotServiceTests(AppFixture appFixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task StoreAsync_NewSnapshot_PersistsMetadataAndContent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        var data = "snapshot payload"u8.ToArray();
        using var stream = new MemoryStream(data);

        // Act
        var entity = await snapshotService.StoreAsync(
            "myapp/release", "v1.0.0", "application/gzip",
            [], new Dictionary<string, string> { ["release"] = "v1.0.0" },
            stream, ct);

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.Key.Should().Be("myapp/release");
        entity.Version.Should().Be("v1.0.0");
        entity.ContentType.Should().Be("application/gzip");
        entity.SizeBytes.Should().Be(data.Length);
        entity.ChecksumSha256.Should().NotBeNullOrEmpty();
        entity.Labels.Should().ContainKey("release");
    }

    [Fact]
    public async Task StoreAsync_DuplicateKeyAndVersion_ThrowsInvalidOperationException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var s1 = new MemoryStream([0x01]);
        await snapshotService.StoreAsync("dup/snap", "v1", "application/gzip", [], [], s1, ct);

        using var s2 = new MemoryStream([0x02]);

        // Act
        var act = () => snapshotService.StoreAsync("dup/snap", "v1", "application/gzip", [], [], s2, ct);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task StoreAsync_WithArtifactIds_LinksArtifactsToSnapshot()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var artifactService = scope.ServiceProvider.GetRequiredService<IArtifactService>();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var as1 = new MemoryStream([0x01]);
        var a1 = await artifactService.StoreAsync("link/a.txt", "v1", "text/plain", [], as1, ct);
        using var as2 = new MemoryStream([0x02]);
        var a2 = await artifactService.StoreAsync("link/b.txt", "v1", "text/plain", [], as2, ct);

        using var ss = new MemoryStream("snapshot bundle"u8.ToArray());

        // Act
        var snapshot = await snapshotService.StoreAsync(
            "link/release", "v1", "application/gzip",
            [a1.Id, a2.Id], [], ss, ct);

        // Assert
        snapshot.SnapshotArtifacts.Should().HaveCount(2);

        var info = await snapshotService.GetInfoAsync("link/release", "v1", ct);
        info!.SnapshotArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public async Task StoreAsync_WithNonExistentArtifactId_ThrowsInvalidOperationException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var stream = new MemoryStream([0x01]);

        // Act
        var act = () => snapshotService.StoreAsync(
            "bad-ref/release", "v1", "application/gzip",
            [Guid.NewGuid()], [], stream, ct);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*artifact IDs do not exist*");
    }

    [Fact]
    public async Task LoadAsync_ExistingSnapshot_ReturnsMetadataAndContent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        var data = "snapshot load test"u8.ToArray();
        using var writeStream = new MemoryStream(data);
        await snapshotService.StoreAsync("load/snap", "v1", "application/gzip", [], [], writeStream, ct);

        // Act
        var (meta, content) = await snapshotService.LoadAsync("load/snap", "v1", ct);

        // Assert
        await using (content)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);

            meta.Key.Should().Be("load/snap");
            ms.ToArray().Should().Equal(data);
        }
    }

    [Fact]
    public async Task LoadAsync_NonExistent_ThrowsKeyNotFoundException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        // Act
        var act = () => snapshotService.LoadAsync("no/snap", "v1", ct);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetInfoAsync_ExistingSnapshot_ReturnsMetadata()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var stream = new MemoryStream([0xFF]);
        await snapshotService.StoreAsync("info/snap", "v2", "application/gzip", [], [], stream, ct);

        // Act
        var info = await snapshotService.GetInfoAsync("info/snap", "v2", ct);

        // Assert
        info.Should().NotBeNull();
        info!.Key.Should().Be("info/snap");
        info.Version.Should().Be("v2");
    }

    [Fact]
    public async Task GetInfoAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        // Act
        var info = await snapshotService.GetInfoAsync("missing/snap", "v1", ct);

        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingSnapshot_ReturnsMetadata()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var stream = new MemoryStream([0xDE, 0xAD]);
        var stored = await snapshotService.StoreAsync(
            "byid/snap", "v1", "application/gzip", [], [], stream, ct);

        // Act
        var found = await snapshotService.GetByIdAsync(stored.Id, ct);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(stored.Id);
        found.Key.Should().Be("byid/snap");
        found.Version.Should().Be("v1");
        found.SizeBytes.Should().Be(2);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        // Act
        var found = await snapshotService.GetByIdAsync(Guid.NewGuid(), ct);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithArtifacts_IncludesSnapshotArtifacts()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var artifactService = scope.ServiceProvider.GetRequiredService<IArtifactService>();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var as1 = new MemoryStream([0x01]);
        var a1 = await artifactService.StoreAsync("byid-link/a.txt", "v1", "text/plain", [], as1, ct);

        using var ss = new MemoryStream([0xFF]);
        var stored = await snapshotService.StoreAsync(
            "byid-link/snap", "v1", "application/gzip", [a1.Id], [], ss, ct);

        // Act
        var found = await snapshotService.GetByIdAsync(stored.Id, ct);

        // Assert
        found.Should().NotBeNull();
        found!.SnapshotArtifacts.Should().HaveCount(1);
        found.SnapshotArtifacts.First().ArtifactId.Should().Be(a1.Id);
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAll()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var s1 = new MemoryStream([0x01]);
        await snapshotService.StoreAsync("slist/a", "v1", "application/gzip", [], [], s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await snapshotService.StoreAsync("slist/b", "v1", "application/gzip", [], [], s2, ct);

        // Act
        var (items, totalCount, _) = await snapshotService.ListAsync(null, null, 50, null, ct);

        // Assert
        items.Should().HaveCount(2);
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_KeyPrefixFilter_FiltersCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var s1 = new MemoryStream([0x01]);
        await snapshotService.StoreAsync("alpha/release", "v1", "application/gzip", [], [], s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await snapshotService.StoreAsync("beta/release", "v1", "application/gzip", [], [], s2, ct);

        // Act
        var (items, _, _) = await snapshotService.ListAsync("alpha", null, 50, null, ct);

        // Assert
        items.Should().HaveCount(1);
        items[0].Key.Should().StartWith("alpha");
    }

    [Fact]
    public async Task ListAsync_Pagination_WorksCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        for (var i = 0; i < 5; i++)
        {
            using var s = new MemoryStream([(byte)i]);
            await snapshotService.StoreAsync($"spage/s{i}", "v1", "application/gzip", [], [], s, ct);
        }

        // Act
        var (page1, total, nextToken) = await snapshotService.ListAsync("spage/", null, 3, null, ct);

        // Assert
        page1.Should().HaveCount(3);
        total.Should().Be(5);
        nextToken.Should().NotBeNullOrEmpty();

        // Act
        var (page2, _, _) = await snapshotService.ListAsync("spage/", null, 3, nextToken, ct);

        // Assert
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_LabelFilter_MatchesCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var s1 = new MemoryStream([0x01]);
        await snapshotService.StoreAsync("slabel/a", "v1", "application/gzip", [],
            new() { ["env"] = "prod" }, s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await snapshotService.StoreAsync("slabel/b", "v1", "application/gzip", [],
            new() { ["env"] = "dev" }, s2, ct);

        // Act
        var (items, _, _) = await snapshotService.ListAsync(
            null, new() { ["env"] = "prod" }, 50, null, ct);

        // Assert
        items.Should().HaveCount(1);
        items[0].Key.Should().Be("slabel/a");
    }

    [Fact]
    public async Task StoreAsync_SameKeyDifferentVersion_BothPersisted()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();

        using var s1 = new MemoryStream([0x01]);
        using var s2 = new MemoryStream([0x02]);

        // Act
        var v1 = await snapshotService.StoreAsync("multiversion/snap", "v1", "application/gzip", [], [], s1, ct);
        var v2 = await snapshotService.StoreAsync("multiversion/snap", "v2", "application/gzip", [], [], s2, ct);

        // Assert
        v1.Id.Should().NotBe(v2.Id);
        v1.Version.Should().Be("v1");
        v2.Version.Should().Be("v2");
    }

    private Task<WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: postconfigure);
}
