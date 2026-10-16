using System.Diagnostics;
using System.Globalization;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Proxy.Lifecycle;

/// <summary>
/// Lifecycle of the API version a route serves. Versions are selected by routing (one route per version, matched
/// by path such as <c>/api/v2/**</c> or by a header such as <c>Api-Version</c>); this policy adds the lifecycle.
/// </summary>
/// <param name="Version">Version label (span attribute <c>gateway.api_version</c>).</param>
/// <param name="Deprecation">When the version was or will be deprecated (<c>Deprecation</c>, RFC 9745).</param>
/// <param name="Sunset">When it stops responding (<c>Sunset</c>, RFC 8594). From then on it is retired.</param>
/// <param name="Link">Deprecation and migration notes (<c>Link</c> with <c>rel="deprecation"</c> / <c>rel="sunset"</c>).</param>
/// <param name="Retired">Retired now, regardless of <paramref name="Sunset"/>.</param>
public sealed record ApiLifecyclePolicy(string? Version, DateTimeOffset? Deprecation, DateTimeOffset? Sunset, Uri? Link, bool Retired)
{
    public static readonly ApiLifecyclePolicy None = new(null, null, null, null, Retired: false);

    public bool IsRetired(DateTimeOffset now) => Retired || Sunset <= now;

    public bool HasHeaders => Deprecation is not null || Sunset is not null;
}

/// <summary>
/// Route metadata keys: <c>ApiLifecycle.Version</c>, <c>ApiLifecycle.Deprecation</c> and <c>ApiLifecycle.Sunset</c>
/// (ISO 8601 date/time, UTC if no offset), <c>ApiLifecycle.Link</c> (absolute URL, required with Sunset or Retired)
/// and <c>ApiLifecycle.Retired</c> (true/false).
/// </summary>
public sealed class ApiLifecycleMetadataParser : IRouteMetadataParser<ApiLifecyclePolicy>
{
    public const string VersionKey = "ApiLifecycle.Version";
    public const string DeprecationKey = "ApiLifecycle.Deprecation";
    public const string SunsetKey = "ApiLifecycle.Sunset";
    public const string LinkKey = "ApiLifecycle.Link";
    public const string RetiredKey = "ApiLifecycle.Retired";

    private static readonly IReadOnlySet<string> KnownKeys =
        new HashSet<string>(StringComparer.Ordinal) { VersionKey, DeprecationKey, SunsetKey, LinkKey, RetiredKey };

    public string Prefix => "ApiLifecycle.";

    public ApiLifecyclePolicy Parse(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.Keys.Any(key => key.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            return ApiLifecyclePolicy.None;
        }

        return new ApiLifecyclePolicy(
            RouteMetadata.Get(metadata, VersionKey),
            ParseDate(RouteMetadata.Get(metadata, DeprecationKey)),
            ParseDate(RouteMetadata.Get(metadata, SunsetKey)),
            ParseLink(RouteMetadata.Get(metadata, LinkKey)),
            RouteMetadata.GetBool(metadata, RetiredKey, fallback: false));
    }

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        RouteMetadata.ValidateBool(metadata, RetiredKey, problems);

        var deprecation = ValidateDate(metadata, DeprecationKey, problems);
        var sunset = ValidateDate(metadata, SunsetKey, problems);
        if (deprecation > sunset)
        {
            problems.Add($"'{DeprecationKey}' must not be later than '{SunsetKey}'");
        }

        var link = RouteMetadata.Get(metadata, LinkKey);
        if (link is not null && ParseLink(link) is null)
        {
            problems.Add($"'{LinkKey}' must be an absolute http(s) URL, got '{link}'");
        }

        if (link is null && (sunset is not null || RouteMetadata.GetBool(metadata, RetiredKey, fallback: false)))
        {
            problems.Add($"'{LinkKey}' (migration notes) is required when '{SunsetKey}' or '{RetiredKey}' is set");
        }

        return problems;
    }

    private static DateTimeOffset? ValidateDate(IReadOnlyDictionary<string, string>? metadata, string key, List<string> problems)
    {
        var raw = RouteMetadata.Get(metadata, key);
        var value = ParseDate(raw);
        if (raw is not null && value is null)
        {
            problems.Add($"'{key}' must be an ISO 8601 date/time, got '{raw}'");
        }

        return value;
    }

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    private static Uri? ParseLink(string? raw) => HttpUrl.Parse(raw);
}

/// <summary>
/// Proxy-pipeline middleware applying <see cref="ApiLifecyclePolicy"/>: a retired version is answered at the
/// gateway with 410 <c>gateway.api_version_retired</c> and a link to the migration notes (header and
/// <c>migration</c> member); a deprecated one gets <c>Deprecation</c>, <c>Sunset</c> and <c>Link</c> on every
/// response, the backend's and the gateway's own.
/// </summary>
public sealed class ApiLifecycleMiddleware(
    RequestDelegate next,
    RoutePolicyResolver<ApiLifecyclePolicy> policies,
    IProblemResponseWriter writer,
    TimeProvider time)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var policy = policies.Resolve(context);
        if (ReferenceEquals(policy, ApiLifecyclePolicy.None))
        {
            await next(context);
            return;
        }

        if (policy.Version is not null)
        {
            Activity.Current?.SetTag(GatewayTelemetry.Tags.ApiVersion, policy.Version);
        }

        if (policy.IsRetired(time.GetUtcNow()))
        {
            AddLink(context.Response.Headers, policy);
            await writer.WriteAsync(context, GatewayErrors.ApiVersionRetired, new ProblemWriteOptions
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["migration"] = policy.Link?.AbsoluteUri },
            });
            return;
        }

        if (policy.HasHeaders)
        {
            context.Response.OnStarting(static state =>
            {
                var (ctx, lifecycle) = ((HttpContext, ApiLifecyclePolicy))state;
                Apply(ctx.Response.Headers, lifecycle);
                return Task.CompletedTask;
            }, (context, policy));
        }

        await next(context);
    }

    internal static void Apply(IHeaderDictionary headers, ApiLifecyclePolicy policy)
    {
        if (policy.Deprecation is { } deprecation)
        {
            // RFC 9745: structured-field date, seconds since the epoch.
            headers[GatewayHeaders.Deprecation] = "@" + deprecation.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (policy.Sunset is { } sunset)
        {
            headers[GatewayHeaders.Sunset] = sunset.ToString("R", CultureInfo.InvariantCulture);
        }

        AddLink(headers, policy);
    }

    private static void AddLink(IHeaderDictionary headers, ApiLifecyclePolicy policy)
    {
        if (policy.Link is not { } link)
        {
            return;
        }

        var rel = policy.Sunset is not null ? "\"deprecation sunset\"" : "\"deprecation\"";
        headers.Append(GatewayHeaders.Link, $"<{link.AbsoluteUri}>; rel={rel}");
    }
}
