namespace ApiGateway.Observability.Redaction;

/// <summary>Creates the anonymizer configured for this environment.</summary>
public static class ClientIpAnonymizerFactory
{
    public static IClientIpAnonymizer Create(ClientIpOptions options) => options.Mode switch
    {
        ClientIpMode.None => new PassThroughClientIpAnonymizer(),
        ClientIpMode.Truncate => new TruncatingClientIpAnonymizer(options.Ipv4PrefixLength, options.Ipv6PrefixLength),
        ClientIpMode.Hash => new HashingClientIpAnonymizer(options.HashKey ?? throw new InvalidOperationException("Observability:ClientIp:HashKey is required for Hash mode.")),
        ClientIpMode.Erase => new ErasingClientIpAnonymizer(),
        _ => throw new InvalidOperationException($"Unknown client IP mode '{options.Mode}'."),
    };
}
