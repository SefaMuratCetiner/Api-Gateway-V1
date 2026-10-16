using System.Net;

namespace ApiGateway.Observability.Redaction;

/// <summary>Turns a client address into what may be written to logs in this environment.</summary>
public interface IClientIpAnonymizer
{
    /// <summary>The loggable form, or <c>null</c> when the address must not be logged at all.</summary>
    string? Anonymize(IPAddress? address);
}
