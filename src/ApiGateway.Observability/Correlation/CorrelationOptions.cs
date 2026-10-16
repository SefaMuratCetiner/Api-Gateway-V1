using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Correlation;

public sealed class CorrelationOptions
{
    /// <summary>Longest inbound <c>X-Correlation-Id</c> that is accepted; longer values are replaced.</summary>
    [Range(8, 256)]
    public int MaxLength { get; set; } = 64;

    /// <summary>Accept a well-formed inbound ID at all. When false the gateway always creates its own.</summary>
    public bool AcceptInbound { get; set; } = true;
}
