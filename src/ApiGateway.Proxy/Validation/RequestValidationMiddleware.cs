using System.Text;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NJsonSchema;
using NSwag;

namespace ApiGateway.Proxy.Validation;

/// <summary>
/// Proxy-pipeline middleware validating JSON request bodies against the backend's published contract, for routes
/// that set <c>Validation.Contract</c>. The body is buffered up to the route's limit (413 beyond it), validated, and
/// forwarded unchanged; violations are answered with 400 <c>gateway.request_validation_failed</c> listing the failed
/// JSON paths. Requests without a JSON body, operations without a JSON request schema, and routes whose contract has
/// not been loaded yet are forwarded unvalidated (the backend stays authoritative).
/// </summary>
public sealed partial class RequestValidationMiddleware(
    RequestDelegate next,
    RoutePolicyResolver<ContractValidationPolicy> policies,
    ContractStore store,
    IProblemResponseWriter writer,
    IOptions<ContractValidationOptions> options,
    ILogger<RequestValidationMiddleware> logger)
{
    private readonly int _maxErrors = options.Value.MaxReportedErrors;

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = policies.Resolve(context);
        if (!policy.Enabled || !HasJsonBody(context.Request))
        {
            await next(context);
            return;
        }

        if (store.Find(policy.ContractUrl!, policy.Kind) is not { } contract)
        {
            LogNoContract(logger, policy.ContractUrl!);
            await next(context);
            return;
        }

        var schema = FindSchema(context, contract);
        if (schema is null)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength > policy.MaxBodyBytes || await BufferAsync(context, policy.MaxBodyBytes) is not { } body)
        {
            await writer.WriteAsync(context, GatewayErrors.PayloadTooLarge);
            return;
        }

        var errors = Validate(schema, body, _maxErrors);
        if (errors.Count > 0)
        {
            await writer.WriteAsync(context, GatewayErrors.RequestValidationFailed, new ProblemWriteArgs
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["errors"] = errors },
            });
            return;
        }

        await next(context);
    }

    /// <summary>Validation errors as <c>path: kind</c> (client input only, never contract internals).</summary>
    internal static IReadOnlyList<string> Validate(JsonSchema schema, string body, int maxErrors)
    {
        try
        {
            return [.. schema.Validate(body).Take(maxErrors).Select(static error => $"{error.Path ?? "#"}: {error.Kind}")];
        }
        catch (JsonException)
        {
            return ["#: InvalidJson"];
        }
    }

    /// <summary>The JSON Schema the request body must satisfy, or <c>null</c> when the contract has none for it.</summary>
    internal static JsonSchema? FindSchema(HttpContext context, LoadedContract contract)
    {
        if (contract.Schema is { } schema)
        {
            return schema;
        }

        if (contract.OpenApi is not { } document)
        {
            return null;
        }

        var route = RouteContext.FindRoute(context);
        var backendPath = PathMapping.From(route?.Config.Transforms).ToBackend(context.Request.Path.Value ?? "/");
        if (backendPath is null || OpenApiOperations.Find(document, backendPath, context.Request.Method) is not { } operation)
        {
            return null;
        }

        var content = operation.RequestBody?.Content;
        if (content is null)
        {
            return null;
        }

        var json = content.FirstOrDefault(static entry => IsJson(entry.Key)).Value;
        return json?.Schema?.ActualSchema;
    }

    private static async Task<string?> BufferAsync(HttpContext context, int maxBytes)
    {
        var request = context.Request;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        request.Body = new MemoryStream(bytes, writable: false);
        request.ContentLength = bytes.Length;
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool HasJsonBody(HttpRequest request) =>
        (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0) && IsJson(request.ContentType);

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';')[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(EventId = 4310, Level = LogLevel.Debug, Message = "Contract {ContractUrl} not loaded yet; request forwarded unvalidated")]
    private static partial void LogNoContract(ILogger logger, Uri contractUrl);
}
