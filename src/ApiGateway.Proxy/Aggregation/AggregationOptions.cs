using System.Text.RegularExpressions;
using ApiGateway.Proxy.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Aggregation;

/// <summary>
/// Configuration section <c>Aggregation</c>: backend-for-frontend endpoints that call several backends and return one
/// JSON document. Read at startup (endpoints are mapped once); changes need a restart.
/// </summary>
public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    public Dictionary<string, AggregateRouteOptions> Routes { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>What a composite endpoint returns when some of its calls fail.</summary>
public enum PartialFailureMode
{
    /// <summary>Any failed call fails the whole request with that call's problem.</summary>
    FailWhole,

    /// <summary>200 with the successful parts in <c>data</c>, failed ones <c>null</c> and described in <c>errors</c>.</summary>
    Partial,
}

public sealed class AggregateRouteOptions
{
    /// <summary>Route template under the public prefix, e.g. <c>/api/bff/orders/{id}</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>GET and/or HEAD.</summary>
    public string[] Methods { get; set; } = [];

    public string[] Hosts { get; set; } = [];

    public PartialFailureMode FailureMode { get; set; } = PartialFailureMode.FailWhole;

    /// <summary>The sub-calls, by name (the member name in <c>data</c> / <c>errors</c>).</summary>
    public Dictionary<string, AggregateCallOptions> Calls { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AggregateCallOptions
{
    /// <summary>YARP cluster the call goes to (its destinations, health and load-balancing policy).</summary>
    public string ClusterId { get; set; } = "";

    /// <summary>Backend path; <c>{name}</c> is replaced with the route value of that name.</summary>
    public string Path { get; set; } = "";

    /// <summary>Forward the client's query string.</summary>
    public bool ForwardQuery { get; set; }

    /// <summary>Timeout of this call alone (the route timeout still bounds the whole request).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Largest response body accepted from this call.</summary>
    public int MaxResponseBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// Composite endpoints follow the routing conventions (public prefix, explicit methods and hosts) and use only
/// placeholders their route template defines. Cluster IDs are resolved per request (clusters reload independently).
/// </summary>
public sealed partial class AggregationOptionsValidator(string publicPathPrefix, bool allowWildcardHosts) : IValidateOptions<AggregationOptions>
{
    public ValidateOptionsResult Validate(string? name, AggregationOptions options)
    {
        var failures = new List<string>();
        foreach (var (id, route) in options.Routes)
        {
            var prefix = $"Aggregation:Routes:{id}";
            failures.AddRange(ProxyConventionsFilter.Validate(route.Path, route.Methods, route.Hosts, publicPathPrefix, allowWildcardHosts)
                .Select(problem => $"{prefix}: {problem}"));

            if (route.Methods.Any(static method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)))
            {
                failures.Add($"{prefix}:Methods: composite endpoints answer GET and HEAD only.");
            }

            var parameters = TryParameters(route.Path);
            if (parameters is null)
            {
                failures.Add($"{prefix}:Path '{route.Path}' is not a valid route template.");
                parameters = [];
            }

            if (route.Calls.Count == 0)
            {
                failures.Add($"{prefix}:Calls must define at least one call.");
            }

            foreach (var (callName, call) in route.Calls)
            {
                ValidateCall($"{prefix}:Calls:{callName}", callName, call, route.Path, parameters, failures);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateCall(string callPrefix, string callName, AggregateCallOptions call, string routePath, string[] parameters, List<string> failures)
    {
        if (!CallName().IsMatch(callName))
        {
            failures.Add($"{callPrefix}: call names must match {CallName()}.");
        }

        if (string.IsNullOrWhiteSpace(call.ClusterId))
        {
            failures.Add($"{callPrefix}:ClusterId is required.");
        }

        if (!call.Path.StartsWith('/') || call.Path.StartsWith("//", StringComparison.Ordinal))
        {
            failures.Add($"{callPrefix}:Path must be an absolute path.");
        }

        failures.AddRange(Placeholder().Matches(call.Path)
            .Select(static match => match.Groups["name"].Value)
            .Where(placeholder => !parameters.Contains(placeholder, StringComparer.OrdinalIgnoreCase))
            .Select(placeholder => $"{callPrefix}:Path uses '{{{placeholder}}}', which '{routePath}' does not define."));

        if (call.Timeout <= TimeSpan.Zero || call.Timeout > TimeSpan.FromMinutes(5))
        {
            failures.Add($"{callPrefix}:Timeout must be between 0 and 5 minutes.");
        }

        if (call.MaxResponseBytes is < 1 or > 64 * 1024 * 1024)
        {
            failures.Add($"{callPrefix}:MaxResponseBytes must be between 1 byte and 64 MiB.");
        }
    }

    private static string[]? TryParameters(string template)
    {
        try
        {
            return [.. RoutePatternFactory.Parse(template).Parameters.Select(static parameter => parameter.Name)];
        }
        catch (RoutePatternException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,40}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    private static partial Regex CallName();

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 50)]
    internal static partial Regex Placeholder();
}
