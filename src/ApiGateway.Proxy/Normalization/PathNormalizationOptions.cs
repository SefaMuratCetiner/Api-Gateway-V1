namespace ApiGateway.Proxy.Normalization;

/// <summary>How a non-canonical path is handled once it has been canonicalized.</summary>
public enum NonCanonicalPathHandling
{
    /// <summary>The canonical path replaces the request path; routing, authorization and the backend see only it.</summary>
    Rewrite,

    /// <summary>The client is redirected (308) to the canonical path.</summary>
    Redirect,
}

public sealed class PathNormalizationOptions
{
    public NonCanonicalPathHandling Handling { get; set; } = NonCanonicalPathHandling.Rewrite;

    /// <summary>Remove a trailing slash (except on <c>/</c>), so <c>/api/orders/</c> and <c>/api/orders</c> are one resource.</summary>
    public bool RemoveTrailingSlash { get; set; } = true;

    /// <summary>
    /// Lower-case the path. Off by default: route matching is already case-insensitive, and backends may treat
    /// path segments (IDs) case-sensitively.
    /// </summary>
    public bool LowercasePath { get; set; }

    /// <summary>
    /// Accept <c>%2F</c> and <c>%5C</c> inside path segments. Off by default: a backend that decodes them sees a
    /// different path than the one the gateway routed and authorized.
    /// </summary>
    public bool AllowEncodedSlashes { get; set; }
}
