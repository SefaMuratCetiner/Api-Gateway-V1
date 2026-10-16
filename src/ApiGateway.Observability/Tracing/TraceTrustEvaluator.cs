using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Tracing;

/// <summary>
/// Decides whether a request's inbound trace context (<c>traceparent</c>, <c>tracestate</c>, <c>baggage</c>) may be
/// used. Decided from the TCP peer address only; headers such as <c>X-Forwarded-For</c> are never consulted.
/// </summary>
public sealed class TraceTrustEvaluator
{
    private readonly TraceTrustMode _mode;
    private readonly IPNetwork[] _trusted;

    public TraceTrustEvaluator(IOptions<ObservabilityOptions> options)
    {
        var tracing = options.Value.Tracing;
        _mode = tracing.TrustMode;
        _trusted = [.. tracing.TrustedNetworks.Select(static cidr => IPNetwork.Parse(cidr))];
    }

    public TraceTrustMode Mode => _mode;

    public bool IsTrusted(HttpContext? context) => _mode switch
    {
        TraceTrustMode.Always => true,
        TraceTrustMode.Never => false,
        _ => context?.Connection.RemoteIpAddress is { } address && IsTrusted(address),
    };

    public bool IsTrusted(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (var network in _trusted)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }
}
