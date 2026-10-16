using NJsonSchema;
using NSwag;

namespace ApiGateway.Proxy.Validation;

/// <summary>Finds the OpenAPI operation for a backend path and method.</summary>
public static class OpenApiOperations
{
    /// <summary>
    /// The operation whose path template matches <paramref name="path"/> (after removing the base path of the first
    /// server URL, if any), preferring templates with more literal segments, as OpenAPI requires.
    /// </summary>
    public static OpenApiOperation? Find(OpenApiDocument document, string path, string method)
    {
        var basePath = document.Servers.FirstOrDefault() is { } first && Uri.TryCreate(new Uri("http://base/"), first.Url, out var server)
            ? server.AbsolutePath.TrimEnd('/')
            : "";
        if (basePath.Length > 0 && path.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[basePath.Length..];
        }

        var segments = path.Trim('/').Split('/');
        OpenApiOperation? best = null;
        var bestLiterals = -1;
        foreach (var (template, item) in document.Paths)
        {
            var literals = Match(template.Trim('/').Split('/'), segments);
            if (literals > bestLiterals && item.TryGetValue(method.ToLowerInvariant(), out var operation))
            {
                best = operation;
                bestLiterals = literals;
            }
        }

        return best;
    }

    /// <summary>Number of literal segments matched, or -1 when the template does not match.</summary>
    private static int Match(string[] template, string[] segments)
    {
        if (template.Length != segments.Length)
        {
            return -1;
        }

        var literals = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var part = template[i];
            if (part.StartsWith('{') && part.EndsWith('}'))
            {
                if (segments[i].Length == 0)
                {
                    return -1;
                }

                continue;
            }

            if (!string.Equals(part, segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            literals++;
        }

        return literals;
    }
}
