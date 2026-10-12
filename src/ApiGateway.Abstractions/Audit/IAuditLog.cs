namespace ApiGateway.Abstractions.Audit;

/// <summary>
/// A change to configuration or state made through the control plane: who did what to which target, and
/// fingerprints of the before/after state.
/// </summary>
/// <param name="Actor">Who made the change, e.g. <c>user:alice</c>, <c>client:ops-bot</c>, <c>system:configuration</c>.</param>
/// <param name="Action">What was done, e.g. <c>proxy-config.applied</c>, <c>route.disabled</c>.</param>
/// <param name="Target">What it was done to, e.g. <c>route:orders</c> or <c>proxy-config</c>.</param>
/// <param name="BeforeHash">SHA-256 of the previous state, if any.</param>
/// <param name="AfterHash">SHA-256 of the new state, if any.</param>
/// <param name="Details">Short, non-secret description of the change.</param>
public sealed record AuditEvent(
    string Actor,
    string Action,
    string Target,
    string? BeforeHash = null,
    string? AfterHash = null,
    string? Details = null);

/// <summary>Append-only audit log of configuration and admin changes.</summary>
public interface IAuditLog
{
    void Write(AuditEvent auditEvent);
}
