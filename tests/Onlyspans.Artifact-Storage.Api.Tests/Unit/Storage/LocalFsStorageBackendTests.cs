using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Onlyspans.Artifact_Storage.Api.Configuration;
using Onlyspans.Artifact_Storage.Api.Features.Storage;

namespace Onlyspans.Artifact_Storage.Api.Tests.Unit.Storage;

public sealed class LocalFsStorageBackendTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFsStorageBackend _backend;

    public LocalFsStorageBackendTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fs-backend-tests", Guid.NewGuid().ToString());
        var options = Options.Create(new StorageOptions
        {
            Fs = new FsOptions { BasePath = _tempDir },
        });
        _backend = new LocalFsStorageBackend(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteAsync_CreatesFileOnDisk()
    {
        // Arrange
        var data = "Hello, world!"u8.ToArray();
        using var stream = new MemoryStream(data);

        // Act
        await _backend.WriteAsync("test/file.bin", stream, CancellationToken.None);

        // Assert
        var fullPath = Path.Combine(_tempDir, "test", "file.bin");
        File.Exists(fullPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(fullPath, TestContext.Current.CancellationToken)).Should().Equal(data);
    }

    [Fact]
    public async Task WriteAsync_CreatesNestedDirectories()
    {
        // Arrange
        using var stream = new MemoryStream([0x01]);

        // Act
        await _backend.WriteAsync("a/b/c/deep.bin", stream, CancellationToken.None);

        // Assert
        var fullPath = Path.Combine(_tempDir, "a", "b", "c", "deep.bin");
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_DuplicatePath_ThrowsIOException()
    {
        // Arrange
        using var stream1 = new MemoryStream([0x01]);
        await _backend.WriteAsync("dup.bin", stream1, CancellationToken.None);

        using var stream2 = new MemoryStream([0x02]);

        // Act
        var act = () => _backend.WriteAsync("dup.bin", stream2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ReadAsync_ExistingFile_ReturnsContent()
    {
        // Arrange
        var data = "read me"u8.ToArray();
        using var writeStream = new MemoryStream(data);
        await _backend.WriteAsync("readable.bin", writeStream, CancellationToken.None);

        // Act
        var ct = TestContext.Current.CancellationToken;
        await using var readStream = await _backend.ReadAsync("readable.bin", CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms, ct);

        // Assert
        ms.ToArray().Should().Equal(data);
    }

    [Fact]
    public async Task ReadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange & Act
        var act = () => _backend.ReadAsync("no-such-file.bin", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        using var stream = new MemoryStream([0x01]);
        await _backend.WriteAsync("exists.bin", stream, CancellationToken.None);

        // Act
        var exists = await _backend.ExistsAsync("exists.bin", CancellationToken.None);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        // Act
        var exists = await _backend.ExistsAsync("nope.bin", CancellationToken.None);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAndRead_LargeFile_RoundTrips()
    {
        // Arrange
        var data = new byte[1_000_000];
        Random.Shared.NextBytes(data);

        using var writeStream = new MemoryStream(data);

        // Act
        await _backend.WriteAsync("large.bin", writeStream, CancellationToken.None);

        var ct = TestContext.Current.CancellationToken;
        await using var readStream = await _backend.ReadAsync("large.bin", CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms, ct);

        // Assert
        ms.ToArray().Should().Equal(data);
    }
}
