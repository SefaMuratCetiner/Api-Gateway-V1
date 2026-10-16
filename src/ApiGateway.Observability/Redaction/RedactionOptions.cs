using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Observability.Redaction;

public sealed class RedactionOptions
{
    /// <summary>Extra field/header/parameter names treated as sensitive, in addition to the built-in list.</summary>
    public string[] AdditionalSensitiveNames { get; set; } = [];

    /// <summary>Replacement text for redacted values.</summary>
    [Required]
    public string Mask { get; set; } = "***";
}
