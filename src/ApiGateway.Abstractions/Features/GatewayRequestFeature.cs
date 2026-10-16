using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Features;

/// <summary>
/// What the gateway knows about the current request: which route and cluster served it and who the caller is.
/// Routing fills the route/cluster/destination; authentication and traffic management fill the caller;
/// observability reads all of it for logs, metrics and traces.
/// </summary>
public interface IGatewayRequestFeature
{
    string? RouteId { get; set; }

    string? ClusterId { get; set; }

    string? DestinationId { get; set; }

    /// <summary>The authenticated client (API key owner or OAuth client), when known.</summary>
    string? ClientId { get; set; }

    string? TenantId { get; set; }

    /// <summary>A small, bounded classification of the client (e.g. <c>partner</c>, <c>internal</c>, <c>public</c>). Safe as a metric label.</summary>
    string? ClientType { get; set; }

    /// <summary>True for verified synthetic-check traffic, which is excluded from usage analytics.</summary>
    bool IsSynthetic { get; set; }
}

/// <summary>Default mutable implementation of <see cref="IGatewayRequestFeature"/>.</summary>
public sealed class GatewayRequestFeature : IGatewayRequestFeature
{
    public string? RouteId { get; set; }

    public string? ClusterId { get; set; }

    public string? DestinationId { get; set; }

    public string? ClientId { get; set; }

    public string? TenantId { get; set; }

    public string? ClientType { get; set; }

    public bool IsSynthetic { get; set; }
}

public static class GatewayRequestHttpContextExtensions
{
    /// <summary>Returns the request's gateway feature, creating it on first use.</summary>
    public static IGatewayRequestFeature GetGatewayRequest(this HttpContext context)
    {
        var feature = context.Features.Get<IGatewayRequestFeature>();
        if (feature is null)
        {
            feature = new GatewayRequestFeature();
            context.Features.Set(feature);
        }

        return feature;
    }

    /// <summary>Returns the request's gateway feature, or <c>null</c> if nothing has set it yet.</summary>
    public static IGatewayRequestFeature? FindGatewayRequest(this HttpContext context) =>
        context.Features.Get<IGatewayRequestFeature>();
}
