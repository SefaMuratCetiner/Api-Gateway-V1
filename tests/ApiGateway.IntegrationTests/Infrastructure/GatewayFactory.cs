using System.Collections.Concurrent;
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
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const int PublicPort = 80;
    public const int InternalPort = 5081;

    public FakeUpstream Upstream { get; } = new();

    public CapturingSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
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

        // A route whose cluster has no destinations (→ 503) and a short route timeout (→ 504).
        builder.UseSetting("ReverseProxy:Routes:empty:ClusterId", "empty");
        builder.UseSetting("ReverseProxy:Routes:empty:Match:Path", "/api/empty/{**rest}");
        builder.UseSetting("ReverseProxy:Clusters:empty:LoadBalancingPolicy", "RoundRobin");
        builder.UseSetting("ReverseProxy:Routes:catalog-read:Timeout", "00:00:00.300");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new FakeUpstreamClientFactory(Upstream));
            services.AddSingleton<ILogEventSink>(Logs);
        });
    }

    public HttpClient CreatePublicClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://localhost:{PublicPort}"), AllowAutoRedirect = false });

    public HttpClient CreateInternalClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://localhost:{InternalPort}"), AllowAutoRedirect = false });
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
