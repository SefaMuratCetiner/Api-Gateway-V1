using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Headers;

/// <summary>Response transform removing <see cref="HeaderOptions.StrippedResponseHeaders"/> from backend responses.</summary>
public sealed class ResponseHeaderStrippingTransformProvider(IOptions<HeaderOptions> options) : ITransformProvider
{
    private readonly HeaderNameSet _stripped = new(options.Value.StrippedResponseHeaders);

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context) =>
        context.AddResponseTransform(transformContext =>
        {
            if (transformContext.ProxyResponse is not null)
            {
                _stripped.RemoveFrom(transformContext.HttpContext.Response.Headers);
            }

            return ValueTask.CompletedTask;
        });
}
