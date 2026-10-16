using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Features;

/// <summary>The correlation ID of the current request (validated inbound value, or one the gateway created).</summary>
public interface ICorrelationIdFeature
{
    string CorrelationId { get; }
}

public sealed record CorrelationIdFeature(string CorrelationId) : ICorrelationIdFeature;

public static class CorrelationIdHttpContextExtensions
{
    /// <summary>The request's correlation ID, or <c>null</c> before the correlation middleware has run.</summary>
    public static string? GetCorrelationId(this HttpContext context) =>
        context.Features.Get<ICorrelationIdFeature>()?.CorrelationId;
}
