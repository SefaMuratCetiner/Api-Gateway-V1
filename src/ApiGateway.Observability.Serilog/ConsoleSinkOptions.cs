namespace ApiGateway.Observability.Serilog;

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
