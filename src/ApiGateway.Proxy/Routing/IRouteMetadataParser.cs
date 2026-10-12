namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Validates the route-metadata keys one feature owns. All validators run when YARP loads configuration;
/// any error rejects the whole configuration, so a bad config never replaces a good one.
/// </summary>
public interface IRouteMetadataValidator
{
    /// <summary>Key prefix this validator owns, e.g. <c>Observability.</c>.</summary>
    string Prefix { get; }

    /// <summary>Returns human-readable problems with the metadata; empty when it is valid.</summary>
    IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata);
}

/// <summary>Turns a route's metadata into a typed, immutable policy for one feature.</summary>
/// <typeparam name="TPolicy">The feature's per-route policy.</typeparam>
public interface IRouteMetadataParser<TPolicy> : IRouteMetadataValidator
    where TPolicy : class
{
    /// <summary>
    /// Parses the policy. Invalid values never reach this point at runtime (the validator rejected the
    /// config), so implementations fall back to defaults instead of throwing.
    /// </summary>
    TPolicy Parse(IReadOnlyDictionary<string, string>? metadata);
}
