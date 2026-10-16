using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Serilog;

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
