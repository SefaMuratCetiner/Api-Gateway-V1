using ApiGateway.Abstractions.Errors;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Enforces the response body limit. A backend response that declares a larger <c>Content-Length</c> is replaced with
/// 502 <c>gateway.bad_upstream_response</c> before anything is sent; one that streams past the limit without declaring
/// it is cut off (the connection is aborted, as nothing can be written once the response has started).
/// </summary>
public sealed partial class ResponseLimitsTransformProvider(
    RoutePolicyResolver<ResponseLimitsPolicy> policies,
    IProblemResponseWriter writer,
    ILogger<ResponseLimitsTransformProvider> logger) : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var policy = policies.Resolve(context.Route);
        if (policy.MaxResponseBodySize is not { } max)
        {
            return;
        }

        context.AddResponseTransform(async transformContext =>
        {
            if (transformContext.ProxyResponse is not { } response)
            {
                return;
            }

            var http = transformContext.HttpContext;
            if (HttpMethods.IsHead(http.Request.Method))
            {
                return;
            }

            if (response.Content.Headers.ContentLength > max)
            {
                LogTooLarge(logger, max);
                await UpstreamResponseBlocker.BlockAsync(transformContext, writer, GatewayErrors.BadUpstreamResponse);
                return;
            }

            http.Response.Body = new ResponseSizeLimitStream(http.Response.Body, max, () =>
            {
                LogTooLarge(logger, max);
                http.Abort();
            });
        });
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Backend response exceeded the {MaxBytes}-byte response limit")]
    private static partial void LogTooLarge(ILogger logger, long maxBytes);
}
