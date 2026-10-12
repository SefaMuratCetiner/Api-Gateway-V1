using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Errors;

/// <summary>
/// Writes gateway-made errors as RFC 9457 problem details. The only way any gateway component should produce
/// an error body, so every error has the same shape, headers, telemetry and localization.
/// </summary>
public interface IProblemResponseWriter
{
    /// <summary>
    /// Writes <paramref name="error"/> to the response. Returns <c>false</c> (and writes nothing) when the
    /// response has already started.
    /// </summary>
    ValueTask<bool> WriteAsync(HttpContext context, GatewayError error, ProblemWriteOptions? options = null);
}

/// <summary>Per-call additions to a problem response.</summary>
public sealed class ProblemWriteOptions
{
    /// <summary>Sets <c>Retry-After</c> (whole seconds, rounded up). Used with 429 and 503.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Extra members added to the problem body. Values must be safe for clients: never exception text,
    /// host names or destination addresses.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
