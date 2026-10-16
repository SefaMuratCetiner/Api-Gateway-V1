using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Headers;

/// <summary>
/// Configuration section <c>Headers</c>. Names are matched case-insensitively; a trailing <c>*</c> matches a prefix.
/// </summary>
public sealed class HeaderOptions
{
    public const string SectionName = "Headers";

    /// <summary>
    /// Client-supplied headers removed on arrival, before anything in the gateway reads them: forwarding headers
    /// (the gateway sets its own <c>X-Forwarded-*</c>) and identity headers only the gateway may set.
    /// </summary>
    public string[] StrippedRequestHeaders { get; set; } =
        ["X-Forwarded-*", "Forwarded", "X-Real-IP", "X-Tenant-Id", "X-User-Id", "X-Internal-*"];

    /// <summary>Stack-revealing headers removed from backend responses (Kestrel's own <c>Server</c> header is off).</summary>
    public string[] StrippedResponseHeaders { get; set; } =
        ["Server", "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version", "X-SourceFiles"];
}

/// <summary>Exact and prefix (<c>X-Internal-*</c>) header-name matching.</summary>
public sealed class HeaderNameSet
{
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _prefixes;

    public HeaderNameSet(IEnumerable<string> patterns)
    {
        var prefixes = new List<string>();
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith('*'))
            {
                prefixes.Add(pattern[..^1]);
            }
            else
            {
                _exact.Add(pattern);
            }
        }

        _prefixes = [.. prefixes];
    }

    public bool Contains(string name)
    {
        if (_exact.Contains(name))
        {
            return true;
        }

        foreach (var prefix in _prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes every matching header.</summary>
    public void RemoveFrom(IHeaderDictionary headers)
    {
        List<string>? matches = null;
        foreach (var name in headers.Keys)
        {
            if (Contains(name))
            {
                (matches ??= []).Add(name);
            }
        }

        foreach (var name in matches ?? [])
        {
            headers.Remove(name);
        }
    }
}

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
