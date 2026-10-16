using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ApiGateway.Abstractions.Errors;

namespace ApiGateway.Errors.Problems;

/// <summary>
/// Every gateway error registered by every contributor. Construction fails on duplicate codes, codes without the
/// <c>gateway.</c> prefix, malformed codes, or a code reused with a different status.
/// </summary>
public sealed partial class GatewayErrorCatalog : IGatewayErrorCatalog
{
    private readonly FrozenDictionary<string, GatewayError> _errors;

    public GatewayErrorCatalog(IEnumerable<IErrorCatalogContributor> contributors)
    {
        var errors = new Dictionary<string, GatewayError>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var error in contributors.SelectMany(static contributor => contributor.GetErrors()))
        {
            if (!CodeFormat().IsMatch(error.Code))
            {
                problems.Add($"'{error.Code}' does not match '{GatewayErrors.Prefix}<snake_case>'.");
            }

            if (error.Status is < 400 or > 599)
            {
                problems.Add($"'{error.Code}' has non-error status {error.Status}.");
            }

            if (errors.TryGetValue(error.Code, out var existing))
            {
                if (existing != error)
                {
                    problems.Add($"'{error.Code}' is registered twice with different definitions.");
                }

                continue;
            }

            errors.Add(error.Code, error);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid gateway error catalog: " + string.Join(" ", problems));
        }

        _errors = errors.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyCollection<GatewayError> Errors => _errors.Values;

    public bool TryGet(string code, out GatewayError error)
    {
        if (_errors.TryGetValue(code, out var found))
        {
            error = found;
            return true;
        }

        error = GatewayErrors.InternalError;
        return false;
    }

    [GeneratedRegex(@"^gateway\.[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodeFormat();
}
