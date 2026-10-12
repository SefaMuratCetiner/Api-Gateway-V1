using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Errors;

/// <summary>Set when the gateway wrote an error response itself. Read by logging and metrics.</summary>
public interface IGatewayErrorFeature
{
    GatewayError Error { get; }
}

public sealed record GatewayErrorFeature(GatewayError Error) : IGatewayErrorFeature;

/// <summary>
/// Marks a response that came from a backend. Gateway components that turn empty error responses into
/// problem details must leave these untouched: backend errors pass through unchanged.
/// </summary>
public interface IUpstreamResponseFeature
{
    int StatusCode { get; }
}

public sealed record UpstreamResponseFeature(int StatusCode) : IUpstreamResponseFeature;

public static class ErrorFeatureHttpContextExtensions
{
    /// <summary>True when the current response was produced by a backend rather than by the gateway.</summary>
    public static bool IsUpstreamResponse(this HttpContext context) =>
        context.Features.Get<IUpstreamResponseFeature>() is not null;

    /// <summary>The gateway error written for this request, if any.</summary>
    public static GatewayError? GetGatewayError(this HttpContext context) =>
        context.Features.Get<IGatewayErrorFeature>()?.Error;
}
