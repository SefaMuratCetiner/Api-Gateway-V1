using System.ComponentModel.DataAnnotations;
using ApiGateway.Observability.AccessLog;
using ApiGateway.Observability.BodyLogging;
using ApiGateway.Observability.Correlation;
using ApiGateway.Observability.Export;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Synthetic;
using ApiGateway.Observability.Tracing;
using ApiGateway.Observability.Usage;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability;

/// <summary>
/// Configuration section <c>Observability</c>. Each concern has its own nested section so environments can
/// override one area (e.g. <c>Observability:ClientIp:Mode</c>) without restating the rest.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary><c>service.name</c> resource attribute.</summary>
    [Required]
    public string ServiceName { get; set; } = "api-gateway";

    /// <summary><c>service.namespace</c> resource attribute.</summary>
    public string? ServiceNamespace { get; set; }

    [ValidateObjectMembers]
    public CorrelationOptions Correlation { get; set; } = new();

    [ValidateObjectMembers]
    public TracingOptions Tracing { get; set; } = new();

    [ValidateObjectMembers]
    public MetricsOptions Metrics { get; set; } = new();

    public AccessLogOptions AccessLog { get; set; } = new();

    [ValidateObjectMembers]
    public BodyLoggingOptions BodyLogging { get; set; } = new();

    [ValidateObjectMembers]
    public RedactionOptions Redaction { get; set; } = new();

    [ValidateObjectMembers]
    public ClientIpOptions ClientIp { get; set; } = new();

    [ValidateObjectMembers]
    public UsageOptions Usage { get; set; } = new();

    [ValidateObjectMembers]
    public SyntheticOptions Synthetic { get; set; } = new();

    [ValidateObjectMembers]
    public ExportOptions Export { get; set; } = new();
}

[OptionsValidator]
public sealed partial class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>;
