using System.Text.Json;
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
    ValueTask<bool> WriteAsync(HttpContext context, GatewayError error, ProblemWriteArgs? args = null);

    /// <summary>
    /// Writes <paramref name="error"/> as a problem object (same members, order and localization as a problem
    /// response) into a larger JSON document, e.g. the <c>errors</c> of a partial aggregated response. The HTTP
    /// response is not touched and no error metric is recorded.
    /// </summary>
    void WriteEmbedded(Utf8JsonWriter writer, HttpContext context, GatewayError error);
}

/// <summary>Per-call additions to a problem response.</summary>
public sealed class ProblemWriteArgs
{
    /// <summary>Sets <c>Retry-After</c> (whole seconds, rounded up). Used with 429 and 503.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Extra members added to the problem body. Values must be safe for clients: never exception text,
    /// host names or destination addresses.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
