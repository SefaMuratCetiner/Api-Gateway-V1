using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;

namespace ApiGateway.Proxy.Compression;

/// <summary>
/// The framework's compression provider (Brotli, gzip) with the per-route opt-out applied. Responses a backend already
/// encoded, and range responses, are never re-compressed (framework behavior).
/// </summary>
public sealed class RouteAwareResponseCompressionProvider(
    ResponseCompressionProvider inner,
    RoutePolicyResolver<CompressionPolicy> policies) : IResponseCompressionProvider
{
    public ICompressionProvider? GetCompressionProvider(HttpContext context) => inner.GetCompressionProvider(context);

    public bool ShouldCompressResponse(HttpContext context) =>
        policies.Resolve(context).Enabled && inner.ShouldCompressResponse(context);

    public bool CheckRequestAcceptsCompression(HttpContext context) => inner.CheckRequestAcceptsCompression(context);
}
