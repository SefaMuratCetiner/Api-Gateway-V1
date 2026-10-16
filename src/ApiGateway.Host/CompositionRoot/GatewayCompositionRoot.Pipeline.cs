using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Handling;
using ApiGateway.Observability.Correlation;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Serilog.AccessLog;
using ApiGateway.Observability.Synthetic;
using ApiGateway.Observability.Tracing;
using ApiGateway.Proxy.Headers;
using ApiGateway.Proxy.Limits;
using ApiGateway.Proxy.Listeners;
using ApiGateway.Proxy.Normalization;
using Serilog;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    /// <summary>
    /// The middleware order, outermost first. Read top to bottom: every request passes these in order, and
    /// responses pass them in reverse.
    /// </summary>
    public void ComposePipeline(WebApplication app)
    {
        // 1. Identity of the request: correlation ID, trace headers on every response, trace trust, synthetic marker.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<TraceResponseHeadersMiddleware>();
        app.UseMiddleware<InboundTraceLinkMiddleware>();
        app.UseMiddleware<SyntheticRequestDetector>();

        // 2. Access log and metrics see the final status of everything inside, including gateway errors.
        var accessLog = app.Services.GetRequiredService<AccessLogEnricher>();
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = AccessLogEnricher.MessageTemplate;
            options.EnrichDiagnosticContext = accessLog.Enrich;
            options.GetLevel = accessLog.GetLevel;
            options.IncludeQueryInRequestPath = false;
        });
        app.UseMiddleware<RequestMetricsMiddleware>();
        if (Settings.Compression.Enabled)
        {
            app.UseResponseCompression();
        }

        // 3. Errors: empty gateway-made error responses become problem details; exceptions become 4xx/5xx problems.
        app.UseMiddleware<StatusCodeProblemMiddleware>();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Only reached when no IExceptionHandler handled the exception.
            ExceptionHandler = static async context => await context.RequestServices.GetRequiredService<IProblemResponseWriter>()
                .WriteAsync(context, GatewayErrors.InternalError),
        });
        app.UseMiddleware<ProtocolLimitsMiddleware>();

        // 4. Normalization, before routing and authorization: allowed hosts (Host and X-Forwarded-Host, checked
        //    before client forwarding headers are stripped), client-supplied forwarding/identity headers removed,
        //    request target checked and the path canonicalized.
        app.UseMiddleware<HostValidationMiddleware>();
        app.UseMiddleware<RequestHeaderStrippingMiddleware>();
        app.UseMiddleware<RequestNormalizationMiddleware>();

        // 5. Routing, listener separation, CORS (preflights answered here, before authentication), timeouts, authorization.
        app.UseRouting();
        app.UseMiddleware<InternalListenerGuardMiddleware>();
        app.UseCors();
        app.UseRequestTimeouts();
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
