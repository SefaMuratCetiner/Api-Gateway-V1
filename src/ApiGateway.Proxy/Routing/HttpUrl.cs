namespace ApiGateway.Proxy.Routing;

/// <summary>Absolute http(s) URLs in configuration and headers.</summary>
public static class HttpUrl
{
    public static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal);

    /// <summary>The absolute http(s) URL in <paramref name="raw"/>, or <c>null</c>.</summary>
    public static Uri? Parse(string? raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out var uri) && IsHttp(uri) ? uri : null;
}
