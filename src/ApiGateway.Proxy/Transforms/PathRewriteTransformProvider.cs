using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Http;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Transforms;

/// <summary>
/// Keeps backend-generated URLs valid for clients when a route rewrites paths (YARP path transforms) or hosts (the
/// default: the destination's host is sent upstream):
/// <list type="bullet">
/// <item>routes that strip a prefix send it as <c>X-Forwarded-Prefix</c>, so backends can build public links;</item>
/// <item><c>Location</c> pointing at the destination (or relative) is mapped back to the public host and path;</item>
/// <item><c>Set-Cookie</c> <c>Domain</c> naming the destination is dropped and <c>Path</c> mapped back.</item>
/// </list>
/// Redirects (3xx) are then checked: a <c>Location</c> that is protocol-relative, contains backslashes or control
/// characters, uses a non-http(s) scheme, or points to a host outside <see cref="RoutingOptions.AllowedHosts"/> and
/// <see cref="RoutingOptions.ExternalRedirectHosts"/> is blocked with 502 <c>gateway.bad_upstream_response</c>
/// (open-redirect protection). Paths are forwarded unchanged by default, so on most routes only the host changes.
/// </summary>
public sealed partial class PathRewriteTransformProvider(
    IOptions<RoutingOptions> routing,
    IProblemResponseWriter writer,
    ILogger<PathRewriteTransformProvider> logger) : ITransformProvider
{
    private readonly HostPatterns _allowedRedirectHosts = new([.. routing.Value.AllowedHosts, .. routing.Value.ExternalRedirectHosts]);

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var mapping = PathMapping.From(context.Route.Transforms);

        // The outbound request is completed (URI, Host) by YARP after the request transforms; keep a reference.
        context.AddRequestTransform(static transformContext =>
        {
            transformContext.HttpContext.Features.Set(new OutboundRequestFeature(transformContext.ProxyRequest));
            return ValueTask.CompletedTask;
        });

        if (mapping.Kind == PathMappingKind.RemovePrefix && context.UseDefaultForwarders != false)
        {
            // Take over the default X-Forwarded-* transforms so the prefix transform below runs after them.
            context.AddXForwarded(ForwardedTransformActions.Set);
            context.UseDefaultForwarders = false;
            var prefix = mapping.Prefix;
            context.AddRequestTransform(transformContext =>
            {
                var headers = transformContext.ProxyRequest.Headers;
                headers.Remove(GatewayHeaders.XForwardedPrefix);
                headers.TryAddWithoutValidation(GatewayHeaders.XForwardedPrefix, transformContext.HttpContext.Request.PathBase.Add(prefix).ToUriComponent());
                return ValueTask.CompletedTask;
            });
        }

        context.AddResponseTransform(transformContext => RewriteAsync(transformContext, mapping));
    }

    private async ValueTask RewriteAsync(ResponseTransformContext context, PathMapping mapping)
    {
        var http = context.HttpContext;
        var outboundRequest = http.Features.Get<OutboundRequestFeature>()?.Request ?? context.ProxyResponse?.RequestMessage;
        if (context.ProxyResponse is null || outboundRequest?.RequestUri is not { } outbound)
        {
            return;
        }

        var view = new ExchangeView(http.Request, outbound, outboundRequest.Headers.Host ?? outbound.Authority, DestinationBasePath(http), mapping);
        var headers = http.Response.Headers;

        if (headers.Location is { Count: 1 } location && location[0] is { Length: > 0 } value)
        {
            var rewritten = RewriteLocation(value, view);
            if (IsRedirect(http.Response.StatusCode) && !IsSafeRedirect(rewritten, http.Request.Host, _allowedRedirectHosts))
            {
                LogUnsafeRedirect(logger, http.Response.StatusCode);
                await UpstreamResponseBlocker.BlockAsync(context, writer, GatewayErrors.BadUpstreamResponse);
                return;
            }

            headers.Location = rewritten;
        }

        if ((view.HostRewritten || mapping.RewritesPath) && headers.SetCookie.Count > 0)
        {
            headers.SetCookie = RewriteCookies(headers.SetCookie, view);
        }
    }

    /// <summary>Maps a backend <c>Location</c> to the public URL; anything not pointing at the backend is returned unchanged.</summary>
    internal static string RewriteLocation(string location, ExchangeView view)
    {
        if (location.StartsWith('/'))
        {
            if (location.StartsWith("//", StringComparison.Ordinal) || location.Contains('\\', StringComparison.Ordinal))
            {
                return location;
            }

            var (path, suffix) = SplitPath(location);
            return view.ToPublicPath(path) is { } mapped ? mapped + suffix : location;
        }

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !HttpUrl.IsHttp(uri) || !view.IsBackendAuthority(uri))
        {
            return location;
        }

        var backendPath = uri.GetComponents(UriComponents.Path | UriComponents.KeepDelimiter, UriFormat.UriEscaped);
        var publicPath = view.ToPublicPath(backendPath.Length == 0 ? "/" : backendPath);
        if (publicPath is null)
        {
            return location;
        }

        var tail = uri.GetComponents(UriComponents.Query | UriComponents.Fragment | UriComponents.KeepDelimiter, UriFormat.UriEscaped);
        return $"{view.Request.Scheme}://{view.Request.Host.ToUriComponent()}{publicPath}{tail}";
    }

    internal static StringValues RewriteCookies(StringValues cookies, ExchangeView view)
    {
        var result = new string?[cookies.Count];
        for (var i = 0; i < cookies.Count; i++)
        {
            var raw = cookies[i];
            result[i] = raw is not null && SetCookieHeaderValue.TryParse(raw, out var cookie) && RewriteCookie(cookie, view)
                ? cookie.ToString()
                : raw;
        }

        return new StringValues(result);
    }

    private static bool RewriteCookie(SetCookieHeaderValue cookie, ExchangeView view)
    {
        var changed = false;
        if (view.HostRewritten && cookie.Domain.HasValue && view.IsBackendHost(cookie.Domain.Value!.TrimStart('.')))
        {
            // Host-only cookie for the public host.
            cookie.Domain = null;
            changed = true;
        }

        if (view.Mapping.RewritesPath && cookie.Path.HasValue && view.ToPublicPath(cookie.Path.Value!, clampToDestination: true) is { } path
            && !string.Equals(path, cookie.Path.Value, StringComparison.Ordinal))
        {
            cookie.Path = path;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// True when a redirect target is safe to send: a relative reference (not protocol-relative), or an absolute
    /// http(s) URL whose host is the request's own host or an allowed one. Backslashes and control characters are
    /// never safe: browsers normalize them into other URLs.
    /// </summary>
    internal static bool IsSafeRedirect(string location, HostString requestHost, HostPatterns allowedHosts)
    {
        foreach (var c in location)
        {
            if (c < 0x20 || c == 0x7F || c == '\\')
            {
                return false;
            }
        }

        if (location.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (location.StartsWith('/') || !HasScheme(location))
        {
            return true;
        }

        return Uri.TryCreate(location, UriKind.Absolute, out var uri)
            && HttpUrl.IsHttp(uri)
            && (string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase) || allowedHosts.Matches(uri.Host));
    }

    private static bool HasScheme(string location)
    {
        var colon = location.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !char.IsAsciiLetter(location[0]))
        {
            return false;
        }

        for (var i = 1; i < colon; i++)
        {
            var c = location[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not '+' and not '-' and not '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRedirect(int status) => status is >= 300 and < 400;

    private static (string Path, string Tail) SplitPath(string relative)
    {
        var end = relative.IndexOfAny(['?', '#']);
        return end < 0 ? (relative, "") : (relative[..end], relative[end..]);
    }

    /// <summary>Path of the destination address (e.g. <c>/svc</c> for <c>http://backend/svc/</c>), without trailing slash.</summary>
    private static string DestinationBasePath(HttpContext context)
    {
        var address = context.GetReverseProxyFeature().ProxiedDestination?.Model.Config.Address;
        return address is not null && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.AbsolutePath.TrimEnd('/') : "";
    }

    /// <summary>The request sent upstream; its <c>Host</c> is set explicitly by a transform or destination, else the URI's authority.</summary>
    private sealed record OutboundRequestFeature(HttpRequestMessage Request);

    [LoggerMessage(EventId = 4100, Level = LogLevel.Warning, Message = "Blocked a {StatusCode} redirect from the backend: Location is not an allowed target")]
    private static partial void LogUnsafeRedirect(ILogger logger, int statusCode);
}

/// <summary>Both sides of one proxied exchange, for mapping backend URLs to public ones.</summary>
internal sealed record ExchangeView(HttpRequest Request, Uri Outbound, string OutboundHost, string DestinationBasePath, PathMapping Mapping)
{
    /// <summary>True when the backend saw a different host than the client used.</summary>
    public bool HostRewritten => !string.Equals(OutboundHost, Request.Host.Value, StringComparison.OrdinalIgnoreCase);

    public bool IsBackendAuthority(Uri uri) =>
        string.Equals(uri.Authority, Outbound.Authority, StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Authority, OutboundHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);

    public bool IsBackendHost(string host) =>
        string.Equals(host, Outbound.Host, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, HostString.FromUriComponent(OutboundHost).Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Public path (with the request's path base) for a backend path, or <c>null</c> when it does not map. With
    /// <paramref name="clampToDestination"/> (cookie paths), a path above the destination's base path, such as
    /// <c>/</c>, means the whole destination and maps to the route's public space.
    /// </summary>
    public string? ToPublicPath(string backendPath, bool clampToDestination = false)
    {
        var path = backendPath;
        if (DestinationBasePath.Length > 0)
        {
            var under = path.StartsWith(DestinationBasePath, StringComparison.OrdinalIgnoreCase)
                && (path.Length == DestinationBasePath.Length || path[DestinationBasePath.Length] == '/');
            if (under)
            {
                path = path.Length == DestinationBasePath.Length ? "/" : path[DestinationBasePath.Length..];
            }
            else if (clampToDestination && DestinationBasePath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                path = "/";
            }
            else
            {
                return null;
            }
        }

        var forwarded = Outbound.AbsolutePath;
        if (DestinationBasePath.Length > 0 && forwarded.StartsWith(DestinationBasePath, StringComparison.OrdinalIgnoreCase))
        {
            forwarded = forwarded[DestinationBasePath.Length..];
        }

        var mapped = Mapping.ToPublic(path, forwarded, Request.Path);
        return mapped is null ? null : Request.PathBase.Add(new PathString(mapped)).Value;
    }
}
