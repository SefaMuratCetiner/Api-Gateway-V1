using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Soft limits checked in middleware so the client gets a 414/431 problem body. Keep them below Kestrel's hard
/// limits (<c>MaxRequestLineSize</c>, <c>MaxRequestHeadersTotalSize</c>, <c>MaxRequestHeaderCount</c>):
/// Kestrel rejects beyond those before any middleware runs, with an empty body.
/// </summary>
public sealed class ProtocolLimitsOptions
{
    /// <summary>Maximum length of the request target (path + query) in characters.</summary>
    [Range(256, 65536)]
    public int MaxUrlLength { get; set; } = 8000;

    /// <summary>Maximum total size of request header names and values in bytes (approximate).</summary>
    [Range(1024, 1048576)]
    public int MaxRequestHeadersTotalSize { get; set; } = 30 * 1024;

    /// <summary>Maximum number of request header fields.</summary>
    [Range(8, 1000)]
    public int MaxRequestHeaderCount { get; set; } = 90;
}
