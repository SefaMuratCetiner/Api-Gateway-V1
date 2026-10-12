using ApiGateway.Host.CompositionRoot;
using ApiGateway.Observability.Serilog;
using Serilog;

// Startup failures before configuration is loaded still reach the console, redacted.
Log.Logger = GatewayLoggerConfigurator.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var root = new GatewayCompositionRoot(builder);
    root.ComposeServices();

    var app = builder.Build();
    root.ComposePipeline(app);
    root.ComposeEndpoints(app);

    await app.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "The gateway terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; public so integration tests can host the real composition root.</summary>
public partial class Program;
