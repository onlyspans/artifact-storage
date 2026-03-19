using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Onlyspans.Artifact_Storage.Api.Data.Contexts;
using Onlyspans.Artifact_Storage.Api.Startup;

namespace Onlyspans.Artifact_Storage.Api.Tests.Fixtures;

public sealed class AppFixture : IAsyncLifetime
{
    public delegate void ConfigureServices(WebApplicationBuilder builder);

    private readonly DbFixture _dbFixture = new();

    public string ConnectionString => _dbFixture.ConnectionString;

    public async Task<WebApplication> BuildApplicationAsync(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        var storagePath = Path.Combine(
            Path.GetTempPath(), "artifact-storage-tests", Guid.NewGuid().ToString());

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection([
            new("ConnectionStrings:ArtifactStorage", _dbFixture.ConnectionString),
            new("Storage:Backend", "fs"),
            new("Storage:ChunkSizeBytes", "4096"),
            new("Storage:Fs:BasePath", storagePath),
        ]);

        preconfigure?.Invoke(builder);

        builder.Services.AddApplication(builder.Configuration);

        builder.Logging.ClearProviders();

        postconfigure?.Invoke(builder);

        var app = builder.Build();

        app.UseApplication();

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ArtifactStorageDbContext>();
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
            await context.SnapshotArtifacts.ExecuteDeleteAsync();
            await context.Snapshots.ExecuteDeleteAsync();
            await context.Artifacts.ExecuteDeleteAsync();
        }

        return app;
    }

    public ValueTask InitializeAsync()
        => _dbFixture.InitializeAsync();

    public ValueTask DisposeAsync()
        => _dbFixture.DisposeAsync();
}
