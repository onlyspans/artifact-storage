namespace Onlyspans.Artifact_Storage.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Artifact Storage API",
                Version = "v1",
                Description = "Thin storage service with pluggable backend (FS / S3) for immutable artifacts and snapshots.",
            });
        });

        return services;
    }

    public static WebApplication UseSwaggerDocs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        return app;
    }
}
