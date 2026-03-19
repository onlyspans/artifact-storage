using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Tests.Fixtures;
using static Onlyspans.Artifact_Storage.Api.Tests.Fixtures.AppFixture;

namespace Onlyspans.Artifact_Storage.Api.Tests.Integration;

public sealed class ArtifactServiceTests(AppFixture appFixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task StoreAsync_NewArtifact_PersistsMetadataAndContent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        var data = "manifest: true\nversion: v1"u8.ToArray();
        using var stream = new MemoryStream(data);

        // Act
        var entity = await service.StoreAsync(
            "myapp/deploy.yaml", "v1.0.0", "text/yaml",
            new Dictionary<string, string> { ["project"] = "myapp" },
            stream, ct);

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.Key.Should().Be("myapp/deploy.yaml");
        entity.Version.Should().Be("v1.0.0");
        entity.ContentType.Should().Be("text/yaml");
        entity.SizeBytes.Should().Be(data.Length);
        entity.ChecksumSha256.Should().NotBeNullOrEmpty();
        entity.Labels.Should().ContainKey("project").WhoseValue.Should().Be("myapp");
        entity.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StoreAsync_DuplicateKeyAndVersion_ThrowsInvalidOperationException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var s1 = new MemoryStream([0x01]);
        await service.StoreAsync("dup/file.txt", "v1", "text/plain", [], s1, ct);

        using var s2 = new MemoryStream([0x02]);

        // Act
        var act = () => service.StoreAsync("dup/file.txt", "v1", "text/plain", [], s2, ct);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task StoreAsync_SameKeyDifferentVersion_BothPersisted()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var s1 = new MemoryStream([0x01]);
        using var s2 = new MemoryStream([0x02]);

        // Act
        var v1 = await service.StoreAsync("ver/file.txt", "v1", "text/plain", [], s1, ct);
        var v2 = await service.StoreAsync("ver/file.txt", "v2", "text/plain", [], s2, ct);

        // Assert
        v1.Id.Should().NotBe(v2.Id);
        v1.Version.Should().Be("v1");
        v2.Version.Should().Be("v2");
    }

    [Fact]
    public async Task LoadAsync_ExistingArtifact_ReturnsMetadataAndContent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        var data = "load test content"u8.ToArray();
        using var writeStream = new MemoryStream(data);
        await service.StoreAsync("load/test.txt", "v1", "text/plain", [], writeStream, ct);

        // Act
        var (meta, content) = await service.LoadAsync("load/test.txt", "v1", ct);

        // Assert
        await using (content)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);

            meta.Key.Should().Be("load/test.txt");
            meta.SizeBytes.Should().Be(data.Length);
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
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        // Act
        var act = () => service.LoadAsync("no/such.txt", "v1", ct);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetInfoAsync_ExistingArtifact_ReturnsMetadata()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var stream = new MemoryStream([0xCA, 0xFE]);
        await service.StoreAsync("info/test.bin", "v3", "application/octet-stream", [], stream, ct);

        // Act
        var info = await service.GetInfoAsync("info/test.bin", "v3", ct);

        // Assert
        info.Should().NotBeNull();
        info!.Key.Should().Be("info/test.bin");
        info.Version.Should().Be("v3");
        info.SizeBytes.Should().Be(2);
    }

    [Fact]
    public async Task GetInfoAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        // Act
        var info = await service.GetInfoAsync("missing/key", "v1", ct);

        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllArtifacts()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var s1 = new MemoryStream([0x01]);
        await service.StoreAsync("list/a.txt", "v1", "text/plain", [], s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await service.StoreAsync("list/b.txt", "v1", "text/plain", [], s2, ct);
        using var s3 = new MemoryStream([0x03]);
        await service.StoreAsync("list/c.txt", "v1", "text/plain", [], s3, ct);

        // Act
        var (items, totalCount, _) = await service.ListAsync(null, null, 50, null, ct);

        // Assert
        items.Should().HaveCount(3);
        totalCount.Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_KeyPrefix_FiltersCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var s1 = new MemoryStream([0x01]);
        await service.StoreAsync("proj-a/file.txt", "v1", "text/plain", [], s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await service.StoreAsync("proj-b/file.txt", "v1", "text/plain", [], s2, ct);

        // Act
        var (items, totalCount, _) = await service.ListAsync("proj-a", null, 50, null, ct);

        // Assert
        items.Should().HaveCount(1);
        items[0].Key.Should().StartWith("proj-a");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_Pagination_ReturnsPageWithToken()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        for (var i = 0; i < 5; i++)
        {
            using var s = new MemoryStream([(byte)i]);
            await service.StoreAsync($"page/f{i}.txt", "v1", "text/plain", [], s, ct);
        }

        // Act
        var (page1, total, nextToken) = await service.ListAsync("page/", null, 2, null, ct);

        // Assert
        page1.Should().HaveCount(2);
        total.Should().Be(5);
        nextToken.Should().NotBeNullOrEmpty();

        // Act
        var (page2, _, nextToken2) = await service.ListAsync("page/", null, 2, nextToken, ct);

        // Assert
        page2.Should().HaveCount(2);
        page2[0].Id.Should().NotBe(page1[0].Id);
        page2[0].Id.Should().NotBe(page1[1].Id);
    }

    [Fact]
    public async Task ListAsync_LabelFilter_MatchesCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var s1 = new MemoryStream([0x01]);
        await service.StoreAsync("label/a.txt", "v1", "text/plain",
            new() { ["env"] = "prod" }, s1, ct);
        using var s2 = new MemoryStream([0x02]);
        await service.StoreAsync("label/b.txt", "v1", "text/plain",
            new() { ["env"] = "staging" }, s2, ct);

        // Act
        var (items, _, _) = await service.ListAsync(
            null, new() { ["env"] = "prod" }, 50, null, ct);

        // Assert
        items.Should().HaveCount(1);
        items[0].Key.Should().Be("label/a.txt");
    }

    [Fact]
    public async Task GetByIdAsync_ExistingArtifact_ReturnsMetadata()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        using var stream = new MemoryStream([0xAB, 0xCD]);
        var stored = await service.StoreAsync("byid/file.bin", "v1", "application/octet-stream", [], stream, ct);

        // Act
        var found = await service.GetByIdAsync(stored.Id, ct);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(stored.Id);
        found.Key.Should().Be("byid/file.bin");
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
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        // Act
        var found = await service.GetByIdAsync(Guid.NewGuid(), ct);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ComputesCorrectChecksum()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtifactService>();

        var data = "checksum test"u8.ToArray();
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

        using var stream = new MemoryStream(data);

        // Act
        var entity = await service.StoreAsync("cs/test.txt", "v1", "text/plain", [], stream, ct);

        // Assert
        entity.ChecksumSha256.Should().Be(expected);
    }

    private Task<WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: postconfigure);
}
