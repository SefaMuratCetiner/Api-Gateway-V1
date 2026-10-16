using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Configuration section <c>Limits</c>: body and response limits, URL and request-header limits (414/431), and
/// Kestrel server limits.
/// </summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "Limits";

    /// <summary>
    /// Global request body cap in bytes (Kestrel <c>MaxRequestBodySize</c>); <c>null</c> = unlimited. Routes override
    /// it with YARP's <c>MaxRequestBodySize</c> (<c>-1</c> = unlimited, e.g. uploads and streaming).
    /// </summary>
    [Range(1L, long.MaxValue)]
    public long? MaxRequestBodySize { get; set; } = 30L * 1024 * 1024;

    /// <summary>
    /// Largest backend response body forwarded, in bytes; <c>null</c> = unlimited. Routes override it with the
    /// <c>Limits.MaxResponseBodySize</c> metadata (<c>-1</c> = unlimited).
    /// </summary>
    [Range(1L, long.MaxValue)]
    public long? MaxResponseBodySize { get; set; }

    /// <summary>Largest backend response header block, in KiB (<c>SocketsHttpHandler.MaxResponseHeadersLength</c>).</summary>
    [Range(1, 1024)]
    public int MaxResponseHeadersSizeKb { get; set; } = 64;

    [ValidateObjectMembers]
    public ProtocolLimitsOptions Protocol { get; set; } = new();

    [ValidateObjectMembers]
    public ServerLimitsOptions Server { get; set; } = new();
}

[OptionsValidator]
public sealed partial class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>;
