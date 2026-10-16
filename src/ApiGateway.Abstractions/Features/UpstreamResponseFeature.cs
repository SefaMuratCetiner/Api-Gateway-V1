using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Features;

/// <summary>
/// Marks a response that came from a backend. Gateway components that turn empty error responses into
/// problem details must leave these untouched: backend errors pass through unchanged.
/// </summary>
public interface IUpstreamResponseFeature
{
    int StatusCode { get; }
}

public sealed record UpstreamResponseFeature(int StatusCode) : IUpstreamResponseFeature;

public static class UpstreamResponseHttpContextExtensions
{
    /// <summary>True when the current response was produced by a backend rather than by the gateway.</summary>
    public static bool IsUpstreamResponse(this HttpContext context) =>
        context.Features.Get<IUpstreamResponseFeature>() is not null;
}
