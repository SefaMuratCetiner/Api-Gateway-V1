using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ApiGateway.Proxy.Listeners;

/// <summary>
/// Configuration section <c>Listeners</c>. The public listener carries only proxied API traffic; metrics,
/// health, the error catalogue and other operational endpoints live on the internal listener, which is
/// never exposed outside the cluster network.
/// </summary>
public sealed class ListenerOptions
{
    public const string SectionName = "Listeners";

    /// <summary>Ports that serve proxied API traffic.</summary>
    [Required]
    [MinLength(1)]
    public int[] PublicPorts { get; set; } = [8080];

    /// <summary>Port for operational endpoints.</summary>
    [Range(1, 65535)]
    public int InternalPort { get; set; } = 8081;

    /// <summary>Host patterns for <c>RequireHost</c> on public endpoints.</summary>
    public string[] PublicHostPatterns() => [.. PublicPorts.Select(static port => $"*:{port}")];

    /// <summary>Host pattern for <c>RequireHost</c> on internal endpoints.</summary>
    public string InternalHostPattern() => $"*:{InternalPort}";

    /// <summary>True when the request arrived on the internal listener.</summary>
    public bool IsInternal(HttpContext context) => context.Connection.LocalPort == InternalPort;
}

[OptionsValidator]
public sealed partial class ListenerOptionsValidator : IValidateOptions<ListenerOptions>;

/// <summary>Cross-field checks the attribute validator cannot express.</summary>
public sealed class ListenerOptionsConsistencyValidator : IValidateOptions<ListenerOptions>
{
    public ValidateOptionsResult Validate(string? name, ListenerOptions options)
    {
        if (options.PublicPorts.Any(static port => port is < 1 or > 65535))
        {
            return ValidateOptionsResult.Fail("Listeners:PublicPorts must contain ports between 1 and 65535.");
        }

        return options.PublicPorts.Contains(options.InternalPort)
            ? ValidateOptionsResult.Fail("Listeners:InternalPort must differ from every public port.")
            : ValidateOptionsResult.Success;
    }
}
