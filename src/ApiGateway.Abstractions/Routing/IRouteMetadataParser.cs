namespace ApiGateway.Abstractions.Routing;

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
