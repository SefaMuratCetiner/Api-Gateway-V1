using ApiGateway.Errors.Handling;
using ApiGateway.Host.CompositionRoot;
using Microsoft.AspNetCore.Authorization;

namespace ApiGateway.Host.Features;

/// <summary>Authentication and authorization, with authorization failures written as problem details.</summary>
internal sealed class AuthorizationFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.Services.AddAuthentication();
        gateway.Services.AddAuthorization();
        gateway.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
    }
}
