using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Serilog.Events;

namespace ApiGateway.Observability.Serilog;

/// <summary>
/// Configuration section <c>Observability:Logging</c>. Sinks are typed options rather than free-form
/// <c>Serilog:WriteTo</c> entries on purpose: every sink is wrapped by the redaction sink in code, which a
/// configuration-declared sink would bypass.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Observability:Logging";

    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>Per-namespace minimum levels, e.g. <c>Microsoft.AspNetCore: Warning</c>.</summary>
    public Dictionary<string, LogEventLevel> Overrides { get; set; } = new(StringComparer.Ordinal)
    {
        ["Microsoft.AspNetCore"] = LogEventLevel.Warning,
        ["Microsoft.Hosting.Lifetime"] = LogEventLevel.Information,
        ["System.Net.Http.HttpClient"] = LogEventLevel.Warning,
        ["Yarp"] = LogEventLevel.Warning,
    };

    /// <summary>Events buffered per sink. When a sink stalls the buffer fills and new events are dropped; logging never blocks requests.</summary>
    [Range(100, 1_000_000)]
    public int AsyncBufferSize { get; set; } = 10_000;

    public ConsoleSinkOptions Console { get; set; } = new();

    [ValidateObjectMembers]
    public FileSinkOptions File { get; set; } = new();

    [ValidateObjectMembers]
    public OtlpSinkOptions Otlp { get; set; } = new();

    /// <summary>Audit events (<c>ApiGateway.Audit</c>): own file, long retention.</summary>
    [ValidateObjectMembers]
    public FileSinkOptions Audit { get; set; } = new() { Enabled = true, Path = "logs/audit/audit-.json", RetainedDays = 400, RetainedFileCountLimit = 400 };

    /// <summary>Usage records (<c>ApiGateway.Usage</c>): own file and retention.</summary>
    [ValidateObjectMembers]
    public FileSinkOptions Usage { get; set; } = new() { Enabled = true, Path = "logs/usage/usage-.json", RetainedDays = 90, RetainedFileCountLimit = 90 };
}

public enum ConsoleFormat
{
    /// <summary>Human-readable single line (development).</summary>
    Text,

    /// <summary>Compact JSON, one event per line (containers, log shippers).</summary>
    Json,
}

public sealed class ConsoleSinkOptions
{
    public bool Enabled { get; set; } = true;

    public ConsoleFormat Format { get; set; } = ConsoleFormat.Json;
}

public sealed class FileSinkOptions
{
    public bool Enabled { get; set; }

    /// <summary>Rolling file path; the date is inserted before the extension.</summary>
    [Required]
    public string Path { get; set; } = "logs/gateway-.json";

    /// <summary>Files older than this are deleted (log retention).</summary>
    [Range(1, 3650)]
    public int RetainedDays { get; set; } = 14;

    /// <summary>At most this many files are kept, whatever their age.</summary>
    [Range(1, 10_000)]
    public int RetainedFileCountLimit { get; set; } = 31;

    /// <summary>A file rolls over early when it reaches this size.</summary>
    [Range(1_048_576, 10_737_418_240)]
    public long FileSizeLimitBytes { get; set; } = 256L * 1024 * 1024;
}

public sealed class OtlpSinkOptions
{
    /// <summary>Ship logs over OTLP to <c>Observability:Export:OtlpEndpoint</c>.</summary>
    public bool Enabled { get; set; }

    [Range(100, 1_000_000)]
    public int QueueLimit { get; set; } = 10_000;
}

[OptionsValidator]
public sealed partial class LoggingOptionsValidator : IValidateOptions<LoggingOptions>;
