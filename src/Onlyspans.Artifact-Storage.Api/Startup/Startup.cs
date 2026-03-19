namespace Onlyspans.Artifact_Storage.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddDatabase(configuration)
            .AddStorageServices(configuration)
            .AddGrpcServices()
            .AddSwaggerDocs();

        return services;
    }

    public static WebApplication UseApplication(this WebApplication app)
    {
        app.UseSwaggerDocs();
        app.MapGrpcEndpoints();
        return app;
    }
}
