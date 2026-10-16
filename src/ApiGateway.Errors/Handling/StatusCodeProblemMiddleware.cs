using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.Errors.Handling;

/// <summary>
/// Turns empty error responses the gateway produced itself (routing's 404/405, an authentication challenge, a
/// timeout) into problem details. Headers the component set, such as <c>Allow</c> and <c>WWW-Authenticate</c>,
/// are kept. Responses from a backend are never touched; a backend's empty 404 reaches the client as a backend
/// 404. This replaces <c>UseStatusCodePages</c>, which cannot tell the two apart.
/// </summary>
public sealed class StatusCodeProblemMiddleware(RequestDelegate next, IProblemResponseWriter writer)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (!ShouldWrite(context))
        {
            return;
        }

        await writer.WriteAsync(context, Classify(context));
    }

    internal static GatewayError Classify(HttpContext context)
    {
        var status = context.Response.StatusCode;
        return status switch
        {
            // Routing found no endpoint at all.
            StatusCodes.Status404NotFound when context.GetEndpoint() is null => GatewayErrors.RouteNotFound,

            // The request-timeouts middleware fired (route Timeout / TimeoutPolicy).
            StatusCodes.Status504GatewayTimeout when context.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true
                => GatewayErrors.RouteTimeout,

            _ => GatewayErrors.ForStatus(status),
        };
    }

    internal static bool ShouldWrite(HttpContext context)
    {
        var response = context.Response;
        return response.StatusCode >= StatusCodes.Status400BadRequest
            && !response.HasStarted
            && !context.RequestAborted.IsCancellationRequested
            && !context.IsUpstreamResponse()
            && context.GetGatewayError() is null
            && response.ContentLength is null or 0
            && string.IsNullOrEmpty(response.ContentType)
            && !response.Headers.ContainsKey(HeaderNames.ContentEncoding);
    }
}
