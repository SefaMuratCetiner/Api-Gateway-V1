using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Transforms;

/// <summary>How a route's path transforms change the path, so backend paths can be mapped back to public ones.</summary>
public enum PathMappingKind
{
    /// <summary>Path forwarded unchanged (the default).</summary>
    None,

    /// <summary><c>PathRemovePrefix</c>: the public path is the prefix plus the backend path.</summary>
    RemovePrefix,

    /// <summary><c>PathPrefix</c>: the backend path is the prefix plus the public path.</summary>
    AddPrefix,

    /// <summary><c>PathSet</c>, <c>PathPattern</c> or several path transforms: only the forwarded path itself maps back.</summary>
    Opaque,
}

/// <summary>A route's path rewriting, read from its YARP transforms.</summary>
public sealed record PathMapping(PathMappingKind Kind, PathString Prefix)
{
    public static readonly PathMapping Unchanged = new(PathMappingKind.None, PathString.Empty);

    private static readonly string[] PathTransformKeys = ["PathRemovePrefix", "PathPrefix", "PathSet", "PathPattern"];

    public bool RewritesPath => Kind != PathMappingKind.None;

    public static PathMapping From(IReadOnlyList<IReadOnlyDictionary<string, string>>? transforms)
    {
        var pathTransforms = (transforms ?? [])
            .SelectMany(static transform => transform)
            .Where(static entry => PathTransformKeys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (pathTransforms.Count == 0)
        {
            return Unchanged;
        }

        if (pathTransforms.Count > 1)
        {
            return new PathMapping(PathMappingKind.Opaque, PathString.Empty);
        }

        var (key, value) = pathTransforms[0];
        var prefix = new PathString(value.TrimEnd('/'));
        return key.ToUpperInvariant() switch
        {
            "PATHREMOVEPREFIX" when prefix.HasValue => new PathMapping(PathMappingKind.RemovePrefix, prefix),
            "PATHPREFIX" when prefix.HasValue => new PathMapping(PathMappingKind.AddPrefix, prefix),
            _ => new PathMapping(PathMappingKind.Opaque, PathString.Empty),
        };
    }

    /// <summary>
    /// The public path for <paramref name="backendPath"/>, or <c>null</c> when it cannot be mapped back (it lies
    /// outside the route's backend space). <paramref name="forwardedPath"/> and <paramref name="publicPath"/> are the
    /// current request's paths on both sides, used for opaque rewrites.
    /// </summary>
    public string? ToPublic(string backendPath, string forwardedPath, PathString publicPath) => Kind switch
    {
        PathMappingKind.None => backendPath,
        PathMappingKind.RemovePrefix => Prefix.Value + (string.Equals(backendPath, "/", StringComparison.Ordinal) ? "" : backendPath),
        PathMappingKind.AddPrefix => StripPrefix(backendPath, Prefix.Value!),
        _ => string.Equals(backendPath, forwardedPath, StringComparison.Ordinal) ? publicPath.ToUriComponent() : null,
    };

    /// <summary>The path the backend receives for <paramref name="publicPath"/>, or <c>null</c> for opaque rewrites.</summary>
    public string? ToBackend(string publicPath) => Kind switch
    {
        PathMappingKind.None => publicPath,
        PathMappingKind.RemovePrefix => StripPrefix(publicPath, Prefix.Value!) ?? publicPath,
        PathMappingKind.AddPrefix => Prefix.Value + publicPath,
        _ => null,
    };

    private static string? StripPrefix(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (path.Length == prefix.Length)
        {
            return "/";
        }

        return path[prefix.Length] == '/' ? path[prefix.Length..] : null;
    }
}
