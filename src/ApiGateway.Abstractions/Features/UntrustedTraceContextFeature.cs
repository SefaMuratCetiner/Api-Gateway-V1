namespace ApiGateway.Abstractions.Features;

/// <summary>An inbound trace context that was not trusted; kept so the new trace can link to it.</summary>
public interface IUntrustedTraceContextFeature
{
    string TraceParent { get; }

    string? TraceState { get; }
}

public sealed record UntrustedTraceContextFeature(string TraceParent, string? TraceState) : IUntrustedTraceContextFeature;
