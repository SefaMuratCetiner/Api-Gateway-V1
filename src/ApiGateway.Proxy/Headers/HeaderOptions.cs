namespace ApiGateway.Proxy.Headers;

/// <summary>
/// Configuration section <c>Headers</c>. Names are matched case-insensitively; a trailing <c>*</c> matches a prefix.
/// </summary>
public sealed class HeaderOptions
{
    public const string SectionName = "Headers";

    /// <summary>
    /// Client-supplied headers removed on arrival, before anything in the gateway reads them: forwarding headers
    /// (the gateway sets its own <c>X-Forwarded-*</c>) and identity headers only the gateway may set.
    /// </summary>
    public string[] StrippedRequestHeaders { get; set; } =
        ["X-Forwarded-*", "Forwarded", "X-Real-IP", "X-Tenant-Id", "X-User-Id", "X-Internal-*"];

    /// <summary>Stack-revealing headers removed from backend responses (Kestrel's own <c>Server</c> header is off).</summary>
    public string[] StrippedResponseHeaders { get; set; } =
        ["Server", "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version", "X-SourceFiles"];
}
