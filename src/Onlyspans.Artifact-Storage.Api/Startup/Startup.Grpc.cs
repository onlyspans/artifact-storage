using Onlyspans.Artifact_Storage.Api.Grpc.Services;

namespace Onlyspans.Artifact_Storage.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddGrpcServices(this IServiceCollection services)
    {
        services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = null;
            options.MaxSendMessageSize = null;
        });

        return services;
    }

    public static WebApplication MapGrpcEndpoints(this WebApplication app)
    {
        app.MapGrpcService<ArtifactStorageGrpcService>();
        return app;
    }
}
