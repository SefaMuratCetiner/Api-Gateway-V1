using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Headers;

/// <summary>
/// Removes <see cref="HeaderOptions.StrippedRequestHeaders"/> from every request on arrival. YARP's default
/// <c>X-Forwarded-For/Proto/Host/Prefix</c> transforms then set the gateway's own values on the way out.
/// Hop-by-hop headers are left to YARP.
/// </summary>
public sealed class RequestHeaderStrippingMiddleware(RequestDelegate next, IOptions<HeaderOptions> options)
{
    private readonly HeaderNameSet _stripped = new(options.Value.StrippedRequestHeaders);

    public Task InvokeAsync(HttpContext context)
    {
        _stripped.RemoveFrom(context.Request.Headers);
        return next(context);
    }
}
