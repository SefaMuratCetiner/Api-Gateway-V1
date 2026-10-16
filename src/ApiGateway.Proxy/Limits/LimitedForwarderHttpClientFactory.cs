using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// YARP's client factory with the backend response-header limit applied
/// (<see cref="RequestLimitsOptions.MaxResponseHeadersSizeKb"/>). A backend exceeding it fails the exchange, which the
/// gateway answers with 502 <c>gateway.bad_upstream_response</c>.
/// </summary>
public sealed class LimitedForwarderHttpClientFactory(
    IOptions<RequestLimitsOptions> options,
    ILogger<ForwarderHttpClientFactory> logger) : ForwarderHttpClientFactory(logger)
{
    private readonly int _maxResponseHeadersKb = options.Value.MaxResponseHeadersSizeKb;

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);
        handler.MaxResponseHeadersLength = _maxResponseHeadersKb;
    }
}
