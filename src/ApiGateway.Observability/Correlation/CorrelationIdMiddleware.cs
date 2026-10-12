using System.Diagnostics;
using System.Text.RegularExpressions;
using ApiGateway.Abstractions.Context;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Correlation;

/// <summary>
/// Establishes the request's correlation ID. An inbound <c>X-Correlation-Id</c> (or <c>X-Request-ID</c>) is
/// accepted only if short and well-formed; otherwise the gateway creates one. The ID is written back to the
/// request so the backend receives it, put on the current span and into the log scope of everything downstream.
/// </summary>
public sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    IOptions<ObservabilityOptions> options,
    ILogger<CorrelationIdMiddleware> logger)
{
    private readonly CorrelationOptions _options = options.Value.Correlation;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context.Request.Headers, _options);

        context.Features.Set<ICorrelationIdFeature>(new CorrelationIdFeature(correlationId));
        context.Request.Headers[GatewayHeaders.CorrelationId] = correlationId;
        context.Request.Headers.Remove(GatewayHeaders.RequestId);
        Activity.Current?.SetTag(GatewayTelemetry.Tags.CorrelationId, correlationId);

        using (logger.BeginScope(new CorrelationScope(correlationId)))
        {
            await next(context);
        }
    }

    internal static string Resolve(IHeaderDictionary headers, CorrelationOptions options)
    {
        if (options.AcceptInbound)
        {
            var inbound = headers[GatewayHeaders.CorrelationId].ToString();
            if (string.IsNullOrEmpty(inbound))
            {
                inbound = headers[GatewayHeaders.RequestId].ToString();
            }

            if (IsAcceptable(inbound, options.MaxLength))
            {
                return inbound;
            }
        }

        return Guid.CreateVersion7().ToString("N");
    }

    internal static bool IsAcceptable(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && AllowedFormat().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    private static partial Regex AllowedFormat();

    /// <summary>Log scope with a single, stable property name.</summary>
    private sealed class CorrelationScope(string correlationId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 1;

        public KeyValuePair<string, object?> this[int index] =>
            index == 0 ? new("CorrelationId", correlationId) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => "CorrelationId:" + correlationId;
    }
}
