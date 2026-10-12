using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Errors.Handling;

/// <summary>
/// Response of the request-timeouts middleware when a route's overall timeout fires before the forwarder
/// produced anything. Plugged into <c>RequestTimeoutPolicy.WriteTimeoutResponse</c> by the composition root.
/// </summary>
public sealed class RouteTimeoutProblemWriter(IProblemResponseWriter writer)
{
    public Task WriteAsync(HttpContext context) =>
        writer.WriteAsync(context, GatewayErrors.RouteTimeout).AsTask();
}
