namespace ApiGateway.Abstractions.Routing;

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
