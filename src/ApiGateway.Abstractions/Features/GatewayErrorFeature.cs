using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Features;

/// <summary>Set when the gateway wrote an error response itself. Read by logging and metrics.</summary>
public interface IGatewayErrorFeature
{
    GatewayError Error { get; }
}

public sealed record GatewayErrorFeature(GatewayError Error) : IGatewayErrorFeature;

public static class GatewayErrorHttpContextExtensions
{
    /// <summary>The gateway error written for this request, if any.</summary>
    public static GatewayError? GetGatewayError(this HttpContext context) =>
        context.Features.Get<IGatewayErrorFeature>()?.Error;
}
