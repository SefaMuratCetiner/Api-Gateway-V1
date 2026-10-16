using System.Net;

namespace ApiGateway.Observability.Redaction;

/// <summary>Never logs the address.</summary>
public sealed class ErasingClientIpAnonymizer : IClientIpAnonymizer
{
    public string? Anonymize(IPAddress? address) => null;
}
