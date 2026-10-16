namespace ApiGateway.Proxy.Normalization;

/// <summary>Canonical form of a (decoded) request path. Pure function.</summary>
public static class PathCanonicalizer
{
    /// <summary>
    /// Removes <c>.</c> and <c>..</c> segments (RFC 3986 §5.2.4, never above the root) and empty segments
    /// (duplicate slashes), optionally the trailing slash, and optionally lower-cases. The result always starts with
    /// exactly one <c>/</c>.
    /// </summary>
    public static string Canonicalize(string path, bool removeTrailingSlash, bool lowercase)
    {
        if (path.Length == 0)
        {
            return "/";
        }

        var segments = new List<string>();
        var raw = path.Split('/');
        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case "":
                case ".":
                    break;
                case "..":
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    break;
                default:
                    segments.Add(raw[i]);
                    break;
            }
        }

        // A trailing slash is kept only when asked to, and only if the original had one (or ended in a dot segment).
        var last = raw[^1];
        var trailingSlash = !removeTrailingSlash && segments.Count > 0 && last is "" or "." or "..";

        var canonical = "/" + string.Join('/', segments) + (trailingSlash ? "/" : "");
        return lowercase ? canonical.ToLowerInvariant() : canonical;
    }
}
