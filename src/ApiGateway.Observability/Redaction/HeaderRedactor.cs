using Microsoft.AspNetCore.Http;

namespace ApiGateway.Observability.Redaction;

/// <summary>
/// Selects headers for logging. Only allow-listed headers are returned at all; values of sensitive headers are
/// masked even when allow-listed, other values are scrubbed and capped.
/// </summary>
public sealed class HeaderRedactor(SensitiveDataClassifier classifier)
{
    public const int MaxValueLength = 256;

    public IReadOnlyDictionary<string, string> Select(IHeaderDictionary headers, IReadOnlyCollection<string> allowList)
    {
        if (allowList.Count == 0)
        {
            return new Dictionary<string, string>(0, StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(allowList.Count, StringComparer.Ordinal);
        foreach (var name in allowList)
        {
            if (!headers.TryGetValue(name, out var values) || values.Count == 0)
            {
                continue;
            }

            result[name] = classifier.IsSensitiveName(name) ? classifier.Mask : Cap(classifier.Scrub(values.ToString()));
        }

        return result;
    }

    private static string Cap(string value) => value.Length <= MaxValueLength ? value : value[..MaxValueLength] + "...";
}
