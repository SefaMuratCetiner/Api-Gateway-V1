using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiGateway.Abstractions.Context;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Proxy.Aggregation;

/// <summary>Endpoint metadata of a composite endpoint (keeps it off the internal listener, names it in telemetry).</summary>
public sealed record AggregateRouteMetadata(string RouteId);

/// <summary>
/// Serves a composite endpoint: runs its calls in parallel against YARP clusters (their available destinations and
/// load-balancing policy), each with its own timeout, and writes
/// <c>{"data": {"&lt;call&gt;": &lt;json&gt; | null, ...}, "errors": {"&lt;call&gt;": &lt;problem&gt;, ...}}</c>.
/// <c>errors</c> appears only for partial results and holds problem objects in the gateway's problem shape. When the
/// route fails whole (<see cref="PartialFailureMode.FailWhole"/>, or every call failed) the response is the first
/// failed call's problem: 502 unreachable/invalid backend, 503 no destination, 504 timeout.
/// </summary>
public sealed partial class AggregateRequestHandler
{
    /// <summary>Request headers passed to every call (identity and language propagate unchanged).</summary>
    public static readonly string[] PropagatedHeaders = [HeaderNames.Authorization, HeaderNames.AcceptLanguage, GatewayHeaders.CorrelationId];

    private const string DefaultLoadBalancingPolicy = "PowerOfTwoChoices";

    private readonly IProxyStateLookup _proxyState;
    private readonly Dictionary<string, ILoadBalancingPolicy> _loadBalancing;
    private readonly IProblemResponseWriter _writer;
    private readonly ILogger<AggregateRequestHandler> _logger;

    public AggregateRequestHandler(
        IProxyStateLookup proxyState,
        IEnumerable<ILoadBalancingPolicy> loadBalancing,
        IProblemResponseWriter writer,
        ILogger<AggregateRequestHandler> logger)
    {
        _proxyState = proxyState;
        _loadBalancing = loadBalancing.ToDictionary(static policy => policy.Name, StringComparer.OrdinalIgnoreCase);
        _writer = writer;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context, string routeId, AggregateRouteOptions route)
    {
        context.GetGatewayRequest().RouteId = routeId;
        Activity.Current?.SetTag(GatewayTelemetry.Tags.RouteId, routeId);

        var calls = route.Calls.ToArray();
        var results = await Task.WhenAll(calls.Select(call => CallAsync(context, call.Key, call.Value)));
        try
        {
            if (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            var failed = results.Where(static result => result.Error is not null).ToArray();
            if (failed.Length > 0 && (route.FailureMode == PartialFailureMode.FailWhole || failed.Length == results.Length))
            {
                await _writer.WriteAsync(context, failed[0].Error!, new ProblemWriteOptions
                {
                    Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["failedCalls"] = failed.Select(static result => result.Name).ToArray() },
                });
                return;
            }

            await WriteAsync(context, results);
        }
        finally
        {
            foreach (var result in results)
            {
                result.Body?.Dispose();
            }
        }
    }

    private async Task WriteAsync(HttpContext context, CallResult[] results)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json; charset=utf-8";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await using (var json = new Utf8JsonWriter(response.BodyWriter))
        {
            json.WriteStartObject();
            json.WriteStartObject("data");
            foreach (var result in results)
            {
                json.WritePropertyName(result.Name);
                if (result.Body is { } body)
                {
                    body.RootElement.WriteTo(json);
                }
                else
                {
                    json.WriteNullValue();
                }
            }

            json.WriteEndObject();

            if (results.Any(static result => result.Error is not null))
            {
                json.WriteStartObject("errors");
                foreach (var result in results.Where(static result => result.Error is not null))
                {
                    json.WritePropertyName(result.Name);
                    _writer.WriteEmbedded(json, context, result.Error!);
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        await response.BodyWriter.FlushAsync(context.RequestAborted);
    }

    private async Task<CallResult> CallAsync(HttpContext context, string name, AggregateCallOptions call)
    {
        if (!_proxyState.TryGetCluster(call.ClusterId, out var cluster)
            || cluster.DestinationsState.AvailableDestinations is not { Count: > 0 } available
            || PickDestination(context, cluster, available) is not { } destination)
        {
            return CallResult.Failed(name, GatewayErrors.NoHealthyDestination);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(call.Timeout);
        try
        {
            using var request = CreateRequest(context, call, cluster, destination);
            using var response = await cluster.Model.HttpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogCallFailed(_logger, name, (int)response.StatusCode);
                return CallResult.Failed(name, GatewayErrors.BadUpstreamResponse);
            }

            if (response.Content.Headers.ContentLength > call.MaxResponseBytes)
            {
                return CallResult.Failed(name, GatewayErrors.BadUpstreamResponse);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            await CopyBoundedAsync(stream, buffer, call.MaxResponseBytes, timeout.Token);
            buffer.Position = 0;
            return new CallResult(name, await JsonDocument.ParseAsync(buffer, cancellationToken: timeout.Token), null);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            return CallResult.Failed(name, GatewayErrors.UpstreamTimeout);
        }
        catch (HttpRequestException exception)
        {
            LogCallException(_logger, name, exception);
            return CallResult.Failed(name, IsConnectFailure(exception) ? GatewayErrors.UpstreamUnreachable : GatewayErrors.BadUpstreamResponse);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            LogCallException(_logger, name, exception);
            return CallResult.Failed(name, GatewayErrors.BadUpstreamResponse);
        }
    }

    private DestinationState? PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> available)
    {
        var policyName = cluster.Model.Config.LoadBalancingPolicy ?? DefaultLoadBalancingPolicy;
        return _loadBalancing.TryGetValue(policyName, out var policy)
            ? policy.PickDestination(context, cluster, available)
            : available[Random.Shared.Next(available.Count)];
    }

    internal static HttpRequestMessage CreateRequest(HttpContext context, AggregateCallOptions call, ClusterState cluster, DestinationState destination)
    {
        var config = destination.Model.Config;
        var path = ExpandPath(call.Path, context.Request.RouteValues);
        var query = call.ForwardQuery ? context.Request.QueryString.ToUriComponent() : "";
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(config.Address.TrimEnd('/') + path + query))
        {
            Version = cluster.Model.Config.HttpRequest?.Version ?? HttpVersion.Version20,
            VersionPolicy = cluster.Model.Config.HttpRequest?.VersionPolicy ?? HttpVersionPolicy.RequestVersionOrLower,
        };

        foreach (var header in PropagatedHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var values))
            {
                request.Headers.TryAddWithoutValidation(header, (IEnumerable<string?>)values);
            }
        }

        request.Headers.TryAddWithoutValidation(HeaderNames.Accept, "application/json");
        request.Headers.TryAddWithoutValidation(GatewayHeaders.XForwardedFor, context.Connection.RemoteIpAddress?.ToString());
        request.Headers.TryAddWithoutValidation(GatewayHeaders.XForwardedProto, context.Request.Scheme);
        request.Headers.TryAddWithoutValidation(GatewayHeaders.XForwardedHost, context.Request.Host.ToUriComponent());
        if (!string.IsNullOrEmpty(config.Host))
        {
            request.Headers.Host = config.Host;
        }

        DistributedContextPropagator.Current.Inject(Activity.Current, request, static (carrier, key, value) =>
            ((HttpRequestMessage)carrier!).Headers.TryAddWithoutValidation(key, value));
        return request;
    }

    /// <summary>Replaces <c>{name}</c> with the escaped route value (segment by segment, so catch-alls keep their slashes).</summary>
    internal static string ExpandPath(string template, RouteValueDictionary values) =>
        AggregationOptionsValidator.Placeholder().Replace(template, match =>
        {
            var value = values.TryGetValue(match.Groups["name"].Value, out var raw) ? raw?.ToString() ?? "" : "";
            return string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
        });

    private static async Task CopyBoundedAsync(Stream source, Stream target, int maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Response exceeds the call's size limit.");
            }

            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool IsConnectFailure(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError;

    [LoggerMessage(EventId = 4400, Level = LogLevel.Warning, Message = "Aggregate call {Call} answered {StatusCode}")]
    private static partial void LogCallFailed(ILogger logger, string call, int statusCode);

    [LoggerMessage(EventId = 4401, Level = LogLevel.Warning, Message = "Aggregate call {Call} failed")]
    private static partial void LogCallException(ILogger logger, string call, Exception exception);

    private sealed record CallResult(string Name, JsonDocument? Body, GatewayError? Error)
    {
        public static CallResult Failed(string name, GatewayError error) => new(name, null, error);
    }
}
