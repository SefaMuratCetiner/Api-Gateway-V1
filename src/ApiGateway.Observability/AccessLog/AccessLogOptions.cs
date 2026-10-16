namespace ApiGateway.Observability.AccessLog;

public sealed class AccessLogOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Log the query string (with sensitive parameters redacted). Off by default: queries often carry personal data.</summary>
    public bool IncludeQueryString { get; set; }

    /// <summary>Request headers logged by name (values of sensitive headers are always redacted).</summary>
    public string[] LoggedRequestHeaders { get; set; } = ["User-Agent", "Content-Type", "Accept"];

    /// <summary>Also log requests to the internal listener (metrics scrapes, health checks).</summary>
    public bool IncludeInternalListener { get; set; }
}
