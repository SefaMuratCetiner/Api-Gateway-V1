using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Errors.Localization;
using ApiGateway.Errors.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.Errors.Problems;

/// <summary>
/// The single writer of gateway-made errors. Produces the fixed problem shape, localized text, the
/// <c>Retry-After</c>/<c>Content-Language</c>/<c>Vary</c>/<c>Cache-Control</c> headers, marks the error on the
/// request (for the access log) and on the current span, and counts it.
/// </summary>
public sealed class ProblemResponseWriter(
    IErrorMessageLocalizer localizer,
    AcceptLanguageResolver languages,
    ErrorMetrics metrics,
    IOptions<ErrorsOptions> errorsOptions) : IProblemResponseWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, SkipValidation = false };

    private readonly ErrorsOptions _options = errorsOptions.Value;

    public async ValueTask<bool> WriteAsync(HttpContext context, GatewayError error, ProblemWriteOptions? options = null)
    {
        var response = context.Response;
        if (response.HasStarted)
        {
            return false;
        }

        var culture = languages.Resolve(context);
        var document = Build(context, error, culture, options);

        // Anything a previous component set for its own body no longer applies.
        response.Headers.Remove(HeaderNames.ContentLength);
        response.Headers.Remove(HeaderNames.ContentEncoding);
        response.Headers.Remove(HeaderNames.ETag);
        response.Headers.Remove(HeaderNames.LastModified);

        response.StatusCode = error.Status;
        response.ContentType = ProblemDocument.ContentType;
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentLanguage = culture.Name;
        response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptLanguage);
        if (options?.RetryAfter is { } retryAfter)
        {
            response.Headers.RetryAfter = RetryAfterSeconds(retryAfter).ToString(CultureInfo.InvariantCulture);
        }

        context.Features.Set<IGatewayErrorFeature>(new GatewayErrorFeature(error));
        MarkActivity(error);
        metrics.RecordError(error);

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return true;
        }

        await using (var writer = new Utf8JsonWriter(response.BodyWriter, WriterOptions))
        {
            document.WriteTo(writer);
        }

        await response.BodyWriter.FlushAsync(context.RequestAborted);
        return true;
    }

    public void WriteEmbedded(Utf8JsonWriter writer, HttpContext context, GatewayError error) =>
        Build(context, error, languages.Resolve(context)).WriteTo(writer);

    /// <summary>Builds the document without writing it (also used by tests and the catalogue endpoint).</summary>
    public ProblemDocument Build(HttpContext context, GatewayError error, CultureInfo culture, ProblemWriteOptions? options = null)
    {
        var text = localizer.Localize(error, culture);
        return new ProblemDocument(
            Type: _options.TypeBaseUri + error.Code,
            Title: text.Title,
            Status: error.Status,
            Detail: text.Detail,
            Instance: InstanceOf(context.Request.Path, _options.MaxInstanceLength),
            Code: error.Code,
            TraceId: TraceIdOf(context),
            Extensions: options?.Extensions);
    }

    /// <summary>The trace ID echoed in <c>traceId</c> and in the <c>X-Trace-Id</c> header.</summary>
    public static string TraceIdOf(HttpContext context) =>
        Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;

    internal static string? InstanceOf(PathString path, int maxLength)
    {
        if (!path.HasValue)
        {
            return null;
        }

        var value = path.Value!;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    internal static long RetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));

    private static void MarkActivity(GatewayError error)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag(GatewayTelemetry.Tags.ErrorCode, error.Code);
        if (error.Status >= StatusCodes.Status500InternalServerError)
        {
            activity.SetTag(GatewayTelemetry.Tags.ErrorType, error.Code);
            activity.SetStatus(ActivityStatusCode.Error, error.Code);
        }
    }
}
