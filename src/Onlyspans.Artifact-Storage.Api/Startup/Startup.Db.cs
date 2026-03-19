using Microsoft.EntityFrameworkCore;
using Npgsql;
using Onlyspans.Artifact_Storage.Api.Data.Contexts;

namespace Onlyspans.Artifact_Storage.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ArtifactStorage")
            ?? throw new InvalidOperationException(
                "Connection string 'ArtifactStorage' is not configured");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ArtifactStorageDbContext>(options =>
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations", "public");
                npgsql.CommandTimeout(30);
            }));

        return services;
    }

    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArtifactStorageDbContext>();
        await db.Database.MigrateAsync();
    }
}
