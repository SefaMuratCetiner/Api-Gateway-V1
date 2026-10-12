using ApiGateway.Abstractions.Errors;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Transforms;

/// <summary>
/// Adds a response transform to every route that marks responses coming from a backend with
/// <see cref="IUpstreamResponseFeature"/>. YARP also runs response transforms when the destination did not
/// answer (<c>ProxyResponse == null</c>); those stay unmarked, so the gateway can still write its own error.
/// </summary>
public sealed class UpstreamResponseMarkerTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context) =>
        context.AddResponseTransform(static transformContext =>
        {
            if (transformContext.ProxyResponse is { } response)
            {
                transformContext.HttpContext.Features.Set<IUpstreamResponseFeature>(
                    new UpstreamResponseFeature((int)response.StatusCode));
            }

            return ValueTask.CompletedTask;
        });
}
