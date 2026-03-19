namespace Onlyspans.Artifact_Storage.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Backend { get; set; } = "fs";
    public int ChunkSizeBytes { get; set; } = 65_536;
    public FsOptions Fs { get; set; } = new();
    public S3Options S3 { get; set; } = new();
}

public sealed class FsOptions
{
    public string BasePath { get; set; } = "./data/artifact-storage";
}

public sealed class S3Options
{
    public string Endpoint { get; set; } = string.Empty;
    public string Bucket { get; set; } = "artifact-storage";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public string Region { get; set; } = "us-east-1";
}
