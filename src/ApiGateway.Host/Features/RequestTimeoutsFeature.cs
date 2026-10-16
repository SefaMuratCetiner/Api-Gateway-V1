using ApiGateway.Errors.Handling;
using ApiGateway.Host.CompositionRoot;
using Microsoft.AspNetCore.Http.Timeouts;

namespace ApiGateway.Host.Features;

/// <summary>Route timeouts, answered with a 504 problem.</summary>
internal sealed class RequestTimeoutsFeature : IGatewayFeature
{
    /// <summary>Request-timeout policy routes reference with <c>"TimeoutPolicy": "gateway-default"</c>.</summary>
    public const string DefaultRouteTimeoutPolicy = "gateway-default";

    public void Register(GatewayBuilder gateway)
    {
        gateway.Services.AddSingleton<RouteTimeoutProblemWriter>();
        gateway.Services.AddRequestTimeouts(options => options.AddPolicy(DefaultRouteTimeoutPolicy, new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            WriteTimeoutResponse = static context => context.RequestServices.GetRequiredService<RouteTimeoutProblemWriter>().WriteAsync(context),
        }));
    }
}
