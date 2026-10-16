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
