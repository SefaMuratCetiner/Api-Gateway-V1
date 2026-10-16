namespace ApiGateway.Proxy.Aggregation;

/// <summary>What a composite endpoint returns when some of its calls fail.</summary>
public enum PartialFailureMode
{
    /// <summary>Any failed call fails the whole request with that call's problem.</summary>
    FailWhole,

    /// <summary>200 with the successful parts in <c>data</c>, failed ones <c>null</c> and described in <c>errors</c>.</summary>
    Partial,
}

public sealed class AggregateRouteOptions
{
    /// <summary>Route template under the public prefix, e.g. <c>/api/bff/orders/{id}</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>GET and/or HEAD.</summary>
    public string[] Methods { get; set; } = [];

    public string[] Hosts { get; set; } = [];

    public PartialFailureMode FailureMode { get; set; } = PartialFailureMode.FailWhole;

    /// <summary>The sub-calls, by name (the member name in <c>data</c> / <c>errors</c>).</summary>
    public Dictionary<string, AggregateCallOptions> Calls { get; set; } = new(StringComparer.Ordinal);
}
