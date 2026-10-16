using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Validation;

/// <summary>Configuration section <c>Validation</c>: how backend contracts are fetched and request bodies validated.</summary>
public sealed class ContractValidationOptions
{
    public const string SectionName = "Validation";

    /// <summary>How often published contracts are re-fetched (conditional GET with the last ETag).</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest request body buffered for validation, unless a route sets <c>Validation.MaxBodyBytes</c>.</summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int DefaultMaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>Validation errors listed in the problem's <c>errors</c> member.</summary>
    [Range(1, 100)]
    public int MaxReportedErrors { get; set; } = 20;
}

[OptionsValidator]
public sealed partial class ContractValidationOptionsValidator : IValidateOptions<ContractValidationOptions>;
