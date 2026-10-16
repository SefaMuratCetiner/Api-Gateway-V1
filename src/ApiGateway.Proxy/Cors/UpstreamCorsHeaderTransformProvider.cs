using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Cors;

/// <summary>
/// On routes with a gateway CORS policy, removes <c>Access-Control-*</c> headers a backend sent, so clients see only
/// the gateway's (set when the response starts) instead of conflicting duplicates.
/// </summary>
public sealed class UpstreamCorsHeaderTransformProvider : ITransformProvider
{
    private const string Prefix = "Access-Control-";

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var policy = context.Route.CorsPolicy;
        if (string.IsNullOrEmpty(policy) || string.Equals(policy, "disable", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.AddResponseTransform(static transformContext =>
        {
            if (transformContext.ProxyResponse is not null)
            {
                var headers = transformContext.HttpContext.Response.Headers;
                foreach (var name in headers.Keys.Where(static key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    headers.Remove(name);
                }
            }

            return ValueTask.CompletedTask;
        });
    }
}
