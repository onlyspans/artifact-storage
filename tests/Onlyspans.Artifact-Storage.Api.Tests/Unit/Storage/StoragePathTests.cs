using FluentAssertions;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;

namespace Onlyspans.Artifact_Storage.Api.Tests.Unit.Storage;

public sealed class StoragePathTests
{
    [Fact]
    public void BuildStoragePath_ProducesExpectedFormat()
    {
        // Arrange
        var id = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

        // Act
        var path = ArtifactService.BuildStoragePath("artifacts", id);

        // Assert
        path.Should().Be("artifacts/ab/cd/abcdef0123456789abcdef0123456789");
    }

    [Fact]
    public void BuildStoragePath_DifferentPrefix_UsesPrefix()
    {
        // Arrange
        var id = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");

        // Act
        var path = ArtifactService.BuildStoragePath("snapshots", id);

        // Assert
        path.Should().StartWith("snapshots/");
    }

    [Fact]
    public void BuildStoragePath_DifferentIds_ProduceDifferentPaths()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Act
        var path1 = ArtifactService.BuildStoragePath("artifacts", id1);
        var path2 = ArtifactService.BuildStoragePath("artifacts", id2);

        // Assert
        path1.Should().NotBe(path2);
    }

    [Fact]
    public void BuildStoragePath_ContainsSubdirectoryBuckets()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var path = ArtifactService.BuildStoragePath("artifacts", id);

        // Assert
        var parts = path.Split('/');
        parts.Should().HaveCount(4);
        parts[0].Should().Be("artifacts");
        parts[1].Should().HaveLength(2);
        parts[2].Should().HaveLength(2);
        parts[3].Should().HaveLength(32);
    }
}
