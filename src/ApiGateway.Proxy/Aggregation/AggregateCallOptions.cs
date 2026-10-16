namespace ApiGateway.Proxy.Aggregation;

public sealed class AggregateCallOptions
{
    /// <summary>YARP cluster the call goes to (its destinations, health and load-balancing policy).</summary>
    public string ClusterId { get; set; } = "";

    /// <summary>Backend path; <c>{name}</c> is replaced with the route value of that name.</summary>
    public string Path { get; set; } = "";

    /// <summary>Forward the client's query string.</summary>
    public bool ForwardQuery { get; set; }

    /// <summary>Timeout of this call alone (the route timeout still bounds the whole request).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Largest response body accepted from this call.</summary>
    public int MaxResponseBytes { get; set; } = 1024 * 1024;
}
