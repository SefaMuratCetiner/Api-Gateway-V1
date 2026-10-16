using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Routing;

/// <summary>
/// Resolves a feature's typed per-route policy for the current request, so libraries can read per-route settings
/// without depending on the proxy.
/// </summary>
/// <typeparam name="TPolicy">The feature's per-route policy.</typeparam>
public interface IRoutePolicyResolver<out TPolicy>
    where TPolicy : class
{
    /// <summary>The policy that applies when no route matched (gateway endpoints, 404s).</summary>
    TPolicy Default { get; }

    /// <summary>The policy of the route serving <paramref name="context"/>, or <see cref="Default"/>.</summary>
    TPolicy Resolve(HttpContext context);
}
