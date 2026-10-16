using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Errors;

/// <summary>
/// The errors the gateway produces itself. Every code carries the <see cref="Prefix"/> so clients can tell a
/// gateway-made error from one the backend returned (which is passed through unchanged).
/// Resource keys match the code with dots and underscores turned into PascalCase (e.g. <c>RouteNotFound_Title</c>).
/// </summary>
public static class GatewayErrors
{
    public const string Prefix = "gateway.";

    // 4xx
    public static readonly GatewayError BadRequest = Create("bad_request", StatusCodes.Status400BadRequest);
    public static readonly GatewayError RequestValidationFailed = Create("request_validation_failed", StatusCodes.Status400BadRequest);
    public static readonly GatewayError AuthenticationRequired = Create("authentication_required", StatusCodes.Status401Unauthorized);
    public static readonly GatewayError InvalidToken = Create("invalid_token", StatusCodes.Status401Unauthorized);
    public static readonly GatewayError Forbidden = Create("forbidden", StatusCodes.Status403Forbidden);
    public static readonly GatewayError RouteNotFound = Create("route_not_found", StatusCodes.Status404NotFound);
    public static readonly GatewayError MethodNotAllowed = Create("method_not_allowed", StatusCodes.Status405MethodNotAllowed);
    public static readonly GatewayError ApiVersionRetired = Create("api_version_retired", StatusCodes.Status410Gone);
    public static readonly GatewayError PayloadTooLarge = Create("payload_too_large", StatusCodes.Status413PayloadTooLarge);
    public static readonly GatewayError UriTooLong = Create("uri_too_long", StatusCodes.Status414UriTooLong);
    public static readonly GatewayError HeadersTooLarge = Create("headers_too_large", StatusCodes.Status431RequestHeaderFieldsTooLarge);
    public static readonly GatewayError RateLimited = Create("rate_limited", StatusCodes.Status429TooManyRequests);
    public static readonly GatewayError QuotaExceeded = Create("quota_exceeded", StatusCodes.Status429TooManyRequests);

    // 5xx
    public static readonly GatewayError InternalError = Create("internal_error", StatusCodes.Status500InternalServerError);
    public static readonly GatewayError BadUpstreamResponse = Create("bad_upstream_response", StatusCodes.Status502BadGateway);
    public static readonly GatewayError UpstreamUnreachable = Create("upstream_unreachable", StatusCodes.Status502BadGateway);
    public static readonly GatewayError NoHealthyDestination = Create("no_healthy_destination", StatusCodes.Status503ServiceUnavailable);
    public static readonly GatewayError Overloaded = Create("overloaded", StatusCodes.Status503ServiceUnavailable);
    public static readonly GatewayError Maintenance = Create("maintenance", StatusCodes.Status503ServiceUnavailable);
    public static readonly GatewayError RouteDisabled = Create("route_disabled", StatusCodes.Status503ServiceUnavailable);
    public static readonly GatewayError UpstreamTimeout = Create("upstream_timeout", StatusCodes.Status504GatewayTimeout);
    public static readonly GatewayError RouteTimeout = Create("route_timeout", StatusCodes.Status504GatewayTimeout);

    /// <summary>Every built-in gateway error, for the catalog, documentation and startup validation.</summary>
    public static IReadOnlyList<GatewayError> All { get; } =
    [
        BadRequest, RequestValidationFailed, AuthenticationRequired, InvalidToken, Forbidden, RouteNotFound, MethodNotAllowed,
        ApiVersionRetired, PayloadTooLarge, UriTooLong, HeadersTooLarge, RateLimited, QuotaExceeded,
        InternalError, BadUpstreamResponse, UpstreamUnreachable, NoHealthyDestination, Overloaded, Maintenance,
        RouteDisabled, UpstreamTimeout, RouteTimeout,
    ];

    /// <summary>
    /// The generic gateway error for a status code, used when a component produced an empty error response
    /// (for example routing's 405) and it has to be turned into problem details.
    /// </summary>
    public static GatewayError ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => AuthenticationRequired,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => RouteNotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status414UriTooLong => UriTooLong,
        StatusCodes.Status429TooManyRequests => RateLimited,
        StatusCodes.Status431RequestHeaderFieldsTooLarge => HeadersTooLarge,
        StatusCodes.Status502BadGateway => BadUpstreamResponse,
        StatusCodes.Status503ServiceUnavailable => NoHealthyDestination,
        StatusCodes.Status504GatewayTimeout => UpstreamTimeout,
        >= 400 and < 500 => BadRequest,
        _ => InternalError,
    };

    /// <summary>Builds a gateway error whose resource keys follow the naming convention.</summary>
    public static GatewayError Create(string name, int status)
    {
        var key = ResourceKeyOf(name);
        return new GatewayError(Prefix + name, status, key + "_Title", key + "_Detail");
    }

    /// <summary><c>route_not_found</c> → <c>RouteNotFound</c>.</summary>
    public static string ResourceKeyOf(string name) =>
        string.Concat(name.Split('_', '.', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
}
