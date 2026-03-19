using Onlyspans.Artifact_Storage.Api.Startup;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddSerilog();
    builder.Services.AddApplication(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogLogging();
    app.UseApplication();

    await app.MigrateDatabaseAsync();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
