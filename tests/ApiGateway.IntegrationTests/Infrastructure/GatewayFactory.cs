using System.Collections.Concurrent;
using ApiGateway.Proxy.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real composition root in memory. YARP's outbound client is replaced by <see cref="FakeUpstream"/>,
/// so tests decide what "the backend" answers; Serilog events are captured by <see cref="CapturingSink"/>
/// (behind the same redaction wrapper as every real sink).
/// </summary>
public class GatewayFactory : WebApplicationFactory<Program>
{
    public const int PublicPort = 80;
    public const int InternalPort = 5081;

    public FakeUpstream Upstream { get; } = new();

    public CapturingSink Logs { get; } = new();

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Ports as seen through the in-memory server (http://localhost → port 80).
        builder.UseSetting("Listeners:PublicPorts:0", PublicPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Listeners:InternalPort", InternalPort.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Deterministic telemetry, no files.
        builder.UseSetting("Observability:Tracing:TrustMode", "Always");
        builder.UseSetting("Observability:Tracing:SamplingRatio", "1");
        builder.UseSetting("Observability:ClientIp:Mode", "Truncate");
        builder.UseSetting("Observability:Logging:Console:Enabled", "false");
        builder.UseSetting("Observability:Logging:Audit:Enabled", "false");
        builder.UseSetting("Observability:Logging:Usage:Enabled", "false");
        builder.UseSetting("Observability:Usage:Enabled", "false");

        // Explicit hosts and methods (outside Development every route needs them), no DNS in tests.
        builder.UseSetting("Routing:AllowedHosts:0", "localhost");
        builder.UseSetting("Routing:ExternalRedirectHosts:0", "login.example.net");
        builder.UseSetting("Routing:DnsDiscovery:Enabled", "false");
        builder.UseSetting("ReverseProxy:Routes:orders:Match:Hosts:0", "localhost");
        builder.UseSetting("ReverseProxy:Routes:catalog-read:Match:Hosts:0", "localhost");

        // A route whose cluster has no destinations (→ 503) and a short route timeout (→ 504).
        AddRoute(builder, "empty", "empty", "/api/empty/{**rest}", "GET");
        builder.UseSetting("ReverseProxy:Clusters:empty:LoadBalancingPolicy", "RoundRobin");
        builder.UseSetting("ReverseProxy:Routes:catalog-read:Timeout", "00:00:00.300");

        foreach (var (key, value) in TestRoutes.Settings)
        {
            builder.UseSetting(key, value);
        }

        ConfigureSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new FakeUpstreamClientFactory(Upstream));
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddSingleton<IStartupFilter, LocalPortFromHostStartupFilter>();
            services.AddHttpClient(ContractRefreshService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Contracts);
        });
    }

    /// <summary>Lets a derived factory change settings after the defaults above.</summary>
    protected virtual void ConfigureSettings(IWebHostBuilder builder)
    {
    }

    /// <summary>Serves the backends' published contracts.</summary>
    public FakeUpstream Contracts { get; } = new();

    internal static void AddRoute(IWebHostBuilder builder, string routeId, string clusterId, string path, params string[] methods)
    {
        builder.UseSetting($"ReverseProxy:Routes:{routeId}:ClusterId", clusterId);
        builder.UseSetting($"ReverseProxy:Routes:{routeId}:Match:Path", path);
        builder.UseSetting($"ReverseProxy:Routes:{routeId}:Match:Hosts:0", "localhost");
        for (var i = 0; i < methods.Length; i++)
        {
            builder.UseSetting($"ReverseProxy:Routes:{routeId}:Match:Methods:{i}", methods[i]);
        }
    }

    public HttpClient CreatePublicClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://localhost:{PublicPort}"), AllowAutoRedirect = false, HandleCookies = false });

    public HttpClient CreateInternalClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://localhost:{InternalPort}"), AllowAutoRedirect = false, HandleCookies = false });
}

/// <summary>
/// The in-memory server has no sockets, so <c>Connection.LocalPort</c> is 0 and there is no remote address. Kestrel
/// sets both; this filter sets the port from the request and a loopback client, so listener separation and
/// <c>X-Forwarded-For</c> behave as deployed.
/// </summary>
internal sealed class LocalPortFromHostStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = context.Request.Host.Port ?? 80;
            context.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>The backend, as a programmable message handler.</summary>
public sealed class FakeUpstream : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; } =
        static (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

    public ConcurrentQueue<HttpRequestMessage> Received { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Received.Enqueue(request);
        return Handler(request, cancellationToken);
    }
}

internal sealed class FakeUpstreamClientFactory(FakeUpstream upstream) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(upstream, disposeHandler: false);
}

/// <summary>Collects log events written through the gateway's Serilog pipeline.</summary>
public sealed class CapturingSink : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
}
