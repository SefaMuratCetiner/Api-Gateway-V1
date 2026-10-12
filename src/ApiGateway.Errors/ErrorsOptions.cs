using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace ApiGateway.Errors;

/// <summary>Configuration section <c>Errors</c>.</summary>
public sealed class ErrorsOptions
{
    public const string SectionName = "Errors";

    /// <summary>
    /// Prefix of the problem <c>type</c> member; the error code is appended. A URN by default because codes are
    /// stable identifiers; set an https base URL to make <c>type</c> link to the published error catalogue.
    /// </summary>
    [Required]
    [MinLength(4)]
    public string TypeBaseUri { get; set; } = "urn:apigateway:error:";

    /// <summary>Culture used when <c>Accept-Language</c> names nothing supported. Must be in <see cref="SupportedCultures"/>.</summary>
    [Required]
    public string DefaultCulture { get; set; } = "en";

    /// <summary>Cultures error messages are available in. Every one is checked against the resources at startup.</summary>
    [Required]
    [MinLength(1)]
    public string[] SupportedCultures { get; set; } = ["en", "tr"];

    /// <summary><c>Retry-After</c> sent with 503 when a cluster has no healthy destination.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan NoHealthyDestinationRetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Longest <c>instance</c> (request path) echoed in a problem body; longer paths are cut.</summary>
    [Range(16, 2048)]
    public int MaxInstanceLength { get; set; } = 256;

    [ValidateObjectMembers]
    public ProtocolLimitsOptions ProtocolLimits { get; set; } = new();
}

/// <summary>
/// Soft limits checked in middleware so the client gets a 414/431 problem body. Keep them below Kestrel's hard
/// limits (<c>MaxRequestLineSize</c>, <c>MaxRequestHeadersTotalSize</c>, <c>MaxRequestHeaderCount</c>):
/// Kestrel rejects beyond those before any middleware runs, with an empty body.
/// </summary>
public sealed class ProtocolLimitsOptions
{
    /// <summary>Maximum length of the request target (path + query) in characters.</summary>
    [Range(256, 65536)]
    public int MaxUrlLength { get; set; } = 8000;

    /// <summary>Maximum total size of request header names and values in bytes (approximate).</summary>
    [Range(1024, 1048576)]
    public int MaxRequestHeadersTotalSize { get; set; } = 30 * 1024;

    /// <summary>Maximum number of request header fields.</summary>
    [Range(8, 1000)]
    public int MaxRequestHeaderCount { get; set; } = 90;
}

[OptionsValidator]
public sealed partial class ErrorsOptionsValidator : IValidateOptions<ErrorsOptions>;

/// <summary>Checks that need code: cultures must parse and the default must be supported.</summary>
public sealed class ErrorsOptionsCultureValidator : IValidateOptions<ErrorsOptions>
{
    public ValidateOptionsResult Validate(string? name, ErrorsOptions options)
    {
        var failures = new List<string>();
        foreach (var culture in options.SupportedCultures.Append(options.DefaultCulture))
        {
            try
            {
                _ = CultureInfo.GetCultureInfo(culture, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                failures.Add($"Errors: '{culture}' is not a known culture.");
            }
        }

        if (!options.SupportedCultures.Contains(options.DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("Errors:DefaultCulture must be one of Errors:SupportedCultures.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
