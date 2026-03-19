using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;

namespace Onlyspans.Artifact_Storage.Api.Tests.Unit.Storage;

public sealed class HashingStreamTests
{
    [Fact]
    public async Task ReadAsync_ComputesCorrectSha256()
    {
        // Arrange
        var data = "Hello, Artifact Storage!"u8.ToArray();
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var inner = new MemoryStream(data);
        using var hashing = new HashingStream(inner);

        var buf = new byte[1024];
        var ct = TestContext.Current.CancellationToken;

        // Act
        while (await hashing.ReadAsync(buf, ct) > 0) { }

        // Assert
        hashing.GetHashHex().Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_TracksCorrectByteCount()
    {
        // Arrange
        var data = new byte[12345];
        Random.Shared.NextBytes(data);

        using var inner = new MemoryStream(data);
        using var hashing = new HashingStream(inner);

        var buf = new byte[100];
        var ct = TestContext.Current.CancellationToken;

        // Act
        while (await hashing.ReadAsync(buf, ct) > 0) { }

        // Assert
        hashing.BytesWritten.Should().Be(12345);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsZeroBytesAndValidHash()
    {
        // Arrange
        var expected = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();

        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        var buf = new byte[64];
        var ct = TestContext.Current.CancellationToken;

        // Act
        var read = await hashing.ReadAsync(buf, ct);

        // Assert
        read.Should().Be(0);
        hashing.BytesWritten.Should().Be(0);
        hashing.GetHashHex().Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_SmallChunks_HashMatchesFullRead()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("chunk1chunk2chunk3chunk4");
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var inner = new MemoryStream(data);
        using var hashing = new HashingStream(inner);

        var buf = new byte[5];
        var ct = TestContext.Current.CancellationToken;

        // Act
        while (await hashing.ReadAsync(buf, ct) > 0) { }

        // Assert
        hashing.GetHashHex().Should().Be(expected);
        hashing.BytesWritten.Should().Be(data.Length);
    }

    [Fact]
    public void SynchronousRead_ComputesCorrectHash()
    {
        // Arrange
        var data = "sync test data"u8.ToArray();
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var inner = new MemoryStream(data);
        using var hashing = new HashingStream(inner);

        var buf = new byte[1024];

        // Act
        while (hashing.Read(buf, 0, buf.Length) > 0) { }

        // Assert
        hashing.GetHashHex().Should().Be(expected);
        hashing.BytesWritten.Should().Be(data.Length);
    }

    [Fact]
    public void CanRead_ReturnsTrue()
    {
        // Arrange
        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        // Act
        var result = hashing.CanRead;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanSeek_ReturnsFalse()
    {
        // Arrange
        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        // Act
        var result = hashing.CanSeek;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanWrite_ReturnsFalse()
    {
        // Arrange
        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        // Act
        var result = hashing.CanWrite;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Write_ThrowsNotSupportedException()
    {
        // Arrange
        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        // Act
        var act = () => hashing.Write([], 0, 0);

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        // Arrange
        using var inner = new MemoryStream([]);
        using var hashing = new HashingStream(inner);

        // Act
        var act = () => hashing.Seek(0, SeekOrigin.Begin);

        // Assert
        act.Should().Throw<NotSupportedException>();
    }
}
