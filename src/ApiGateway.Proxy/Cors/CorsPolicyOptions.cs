namespace ApiGateway.Proxy.Cors;

public sealed class CorsPolicyOptions
{
    /// <summary>
    /// Explicit origins (<c>https://app.example.com</c>), wildcard subdomains (<c>https://*.example.com</c>), or
    /// <c>*</c> (any origin; not allowed together with credentials).
    /// </summary>
    public string[] Origins { get; set; } = [];

    public string[] Methods { get; set; } = [];

    /// <summary>Request headers allowed beyond the CORS-safelisted ones.</summary>
    public string[] Headers { get; set; } = [];

    /// <summary>Response headers exposed to scripts, in addition to the gateway's own (correlation, trace, lifecycle, Retry-After).</summary>
    public string[] ExposedHeaders { get; set; } = [];

    public bool AllowCredentials { get; set; }

    /// <summary>How long browsers may cache a preflight answer (<c>Access-Control-Max-Age</c>).</summary>
    public TimeSpan PreflightMaxAge { get; set; } = TimeSpan.FromMinutes(10);
}
