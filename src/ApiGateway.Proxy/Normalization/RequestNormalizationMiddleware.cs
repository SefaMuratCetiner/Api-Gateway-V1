using System.Diagnostics;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.Proxy.Normalization;

/// <summary>
/// Runs before routing and authorization. Rejects (400 <c>gateway.bad_request</c>) request targets that are
/// double-encoded, overlong or invalid UTF-8, contain NUL or control characters or hide slashes in escapes, and
/// header values with control characters. Then canonicalizes the path (dot segments, duplicate and trailing
/// slashes, optional lower-casing) and either rewrites the request to it or redirects (308) to it, so routing,
/// authorization and the backend all see one form of every path.
/// </summary>
public sealed partial class RequestNormalizationMiddleware(
    RequestDelegate next,
    IOptions<RoutingOptions> options,
    IProblemResponseWriter writer,
    ILogger<RequestNormalizationMiddleware> logger)
{
    public const string ProblemTag = "gateway.normalization.problem";

    private readonly PathNormalizationOptions _options = options.Value.Normalization;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (string.IsNullOrEmpty(rawTarget))
        {
            rawTarget = request.PathBase.ToUriComponent() + request.Path.ToUriComponent() + request.QueryString.ToUriComponent();
        }

        var problem = RequestTargetInspector.Inspect(rawTarget, _options.AllowEncodedSlashes);
        if (problem == RequestTargetProblem.None)
        {
            problem = RequestTargetInspector.InspectDecodedPath(request.Path.Value ?? "", _options.AllowEncodedSlashes);
        }

        if (problem == RequestTargetProblem.None && HasControlCharacterInHeaders(request.Headers))
        {
            problem = RequestTargetProblem.ControlCharacter;
        }

        if (problem != RequestTargetProblem.None)
        {
            LogRejected(logger, problem);
            Activity.Current?.SetTag(ProblemTag, problem.ToString());
            await writer.WriteAsync(context, GatewayErrors.BadRequest);
            return;
        }

        var path = request.Path.Value ?? "";
        var canonical = PathCanonicalizer.Canonicalize(path, _options.RemoveTrailingSlash, _options.LowercasePath);
        if (!string.Equals(canonical, path, StringComparison.Ordinal) && path.Length > 0)
        {
            if (_options.Handling == NonCanonicalPathHandling.Redirect)
            {
                // Path-only, starts with exactly one '/': never readable as a protocol-relative URL (open redirect).
                context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
                context.Response.Headers[HeaderNames.Location] = request.PathBase.Add(new PathString(canonical)).ToUriComponent() + request.QueryString.ToUriComponent();
                return;
            }

            request.Path = new PathString(canonical);
        }

        await next(context);
    }

    private static bool HasControlCharacterInHeaders(IHeaderDictionary headers)
    {
        foreach (var (_, values) in headers)
        {
            foreach (var value in values)
            {
                if (value is not null && RequestTargetInspector.HasControlCharacter(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Request rejected before routing: {Problem}")]
    private static partial void LogRejected(ILogger logger, RequestTargetProblem problem);
}

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
