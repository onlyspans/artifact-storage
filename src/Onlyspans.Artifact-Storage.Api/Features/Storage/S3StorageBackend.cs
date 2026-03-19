using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Onlyspans.Artifact_Storage.Api.Configuration;

namespace Onlyspans.Artifact_Storage.Api.Features.Storage;

public sealed class S3StorageBackend : IStorageBackend, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3StorageBackend(IOptions<StorageOptions> options)
    {
        var s3 = options.Value.S3;
        var config = new AmazonS3Config
        {
            ServiceURL = s3.Endpoint,
            ForcePathStyle = s3.ForcePathStyle,
        };
        _client = new AmazonS3Client(s3.AccessKey, s3.SecretKey, config);
        _bucket = s3.Bucket;
    }

    public async Task WriteAsync(string storagePath, Stream content, CancellationToken ct)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = storagePath,
            InputStream = content,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(request, ct);
    }

    public async Task<Stream> ReadAsync(string storagePath, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(_bucket, storagePath, ct);
        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, storagePath, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();
}
