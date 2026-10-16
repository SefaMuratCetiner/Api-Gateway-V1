using ApiGateway.Abstractions.Errors;
using ApiGateway.Proxy.Routing;
using ApiGateway.Proxy.Transforms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy.Limits;

/// <summary>
/// Proxy-pipeline middleware: a request whose <c>Content-Length</c> exceeds the route's body limit (YARP
/// <c>MaxRequestBodySize</c>, else the global cap) is answered with 413 before the body is read and before the
/// backend is contacted, so with <c>Expect: 100-continue</c> the client never uploads it. Chunked bodies are cut off by
/// Kestrel at the same limit while streaming (413 as well, via the forwarder error mapping).
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IProblemResponseWriter writer)
{
    public Task InvokeAsync(HttpContext context)
    {
        var limit = EffectiveLimit(context);
        return limit is { } max && context.Request.ContentLength > max
            ? writer.WriteAsync(context, GatewayErrors.PayloadTooLarge).AsTask()
            : next(context);
    }

    /// <summary>The body limit for this request, or <c>null</c> when unlimited.</summary>
    internal static long? EffectiveLimit(HttpContext context)
    {
        if (RouteContext.FindRoute(context)?.Config.MaxRequestBodySize is { } routeLimit)
        {
            return routeLimit < 0 ? null : routeLimit;
        }

        return context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
    }
}

/// <summary>Per-route response limit, from metadata.</summary>
/// <param name="MaxResponseBodySize">Largest response body forwarded, or <c>null</c> when unlimited.</param>
public sealed record ResponseLimitsPolicy(long? MaxResponseBodySize);

/// <summary>Route metadata key <c>Limits.MaxResponseBodySize</c>: bytes, or <c>-1</c> for unlimited.</summary>
public sealed class ResponseLimitsMetadataParser(IOptions<RequestLimitsOptions> options) : IRouteMetadataParser<ResponseLimitsPolicy>
{
    public const string MaxResponseBodySizeKey = "Limits.MaxResponseBodySize";

    private static readonly IReadOnlySet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal) { MaxResponseBodySizeKey };

    private readonly long? _default = options.Value.MaxResponseBodySize;

    public string Prefix => "Limits.";

    public ResponseLimitsPolicy Parse(IReadOnlyDictionary<string, string>? metadata) =>
        long.TryParse(RouteMetadata.Get(metadata, MaxResponseBodySizeKey), System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? new ResponseLimitsPolicy(value < 0 ? null : value)
            : new ResponseLimitsPolicy(_default);

    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? metadata)
    {
        var problems = new List<string>();
        RouteMetadata.ValidateKnownKeys(metadata, Prefix, KnownKeys, problems);
        var raw = RouteMetadata.Get(metadata, MaxResponseBodySizeKey);
        if (raw is not null && (!long.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value) || value is 0 or < -1))
        {
            problems.Add($"'{MaxResponseBodySizeKey}' must be a positive number of bytes or -1, got '{raw}'");
        }

        return problems;
    }
}

/// <summary>
/// Enforces the response body limit. A backend response that declares a larger <c>Content-Length</c> is replaced with
/// 502 <c>gateway.bad_upstream_response</c> before anything is sent; one that streams past the limit without declaring
/// it is cut off (the connection is aborted, as nothing can be written once the response has started).
/// </summary>
public sealed partial class ResponseLimitsTransformProvider(
    RoutePolicyResolver<ResponseLimitsPolicy> policies,
    IProblemResponseWriter writer,
    ILogger<ResponseLimitsTransformProvider> logger) : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var policy = policies.Resolve(context.Route);
        if (policy.MaxResponseBodySize is not { } max)
        {
            return;
        }

        context.AddResponseTransform(async transformContext =>
        {
            if (transformContext.ProxyResponse is not { } response)
            {
                return;
            }

            var http = transformContext.HttpContext;
            if (HttpMethods.IsHead(http.Request.Method))
            {
                return;
            }

            if (response.Content.Headers.ContentLength > max)
            {
                LogTooLarge(logger, max);
                await UpstreamResponseBlocker.BlockAsync(transformContext, writer, GatewayErrors.BadUpstreamResponse);
                return;
            }

            http.Response.Body = new ResponseSizeLimitStream(http.Response.Body, max, () =>
            {
                LogTooLarge(logger, max);
                http.Abort();
            });
        });
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Backend response exceeded the {MaxBytes}-byte response limit")]
    private static partial void LogTooLarge(ILogger logger, long maxBytes);
}

/// <summary>Write-through stream that fails once more than <c>maxBytes</c> have been written.</summary>
internal sealed class ResponseSizeLimitStream(Stream inner, long maxBytes, Action onExceeded) : Stream
{
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Count(count);
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Count(int count)
    {
        _written += count;
        if (_written > maxBytes)
        {
            onExceeded();
            throw new IOException("The response exceeded the gateway's response size limit.");
        }
    }
}
