using Onlyspans.Artifact_Storage.Api.Configuration;
using Onlyspans.Artifact_Storage.Api.Features.Artifacts;
using Onlyspans.Artifact_Storage.Api.Features.Snapshots;
using Onlyspans.Artifact_Storage.Api.Features.Storage;

namespace Onlyspans.Artifact_Storage.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddStorageServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        var options = configuration
            .GetSection(StorageOptions.SectionName)
            .Get<StorageOptions>() ?? new StorageOptions();

        if (string.Equals(options.Backend, "s3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IStorageBackend, S3StorageBackend>();
        else
            services.AddSingleton<IStorageBackend, LocalFsStorageBackend>();

        services.AddScoped<IArtifactService, ArtifactService>();
        services.AddScoped<ISnapshotService, SnapshotService>();

        return services;
    }
}
