using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Telemetry;

namespace ApiGateway.Errors.Telemetry;

/// <summary>
/// <c>gateway.errors</c>: gateway-made error responses by code and status. Both labels are bounded (codes come
/// from the validated catalog).
/// </summary>
public sealed class ErrorMetrics
{
    private readonly Counter<long> _errors;

    public ErrorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(GatewayTelemetry.ErrorsMeter);
        _errors = meter.CreateCounter<long>(
            "gateway.errors",
            unit: "{error}",
            description: "Error responses produced by the gateway itself (backend errors are not counted).");
    }

    public void RecordError(GatewayError error)
    {
        var tags = new TagList
        {
            { GatewayTelemetry.Tags.ErrorCode, error.Code },
            { GatewayTelemetry.Tags.StatusCode, error.Status },
        };
        _errors.Add(1, tags);
    }
}
