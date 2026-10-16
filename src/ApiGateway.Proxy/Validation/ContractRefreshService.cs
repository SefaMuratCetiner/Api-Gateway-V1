using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Proxy.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;

namespace ApiGateway.Proxy.Validation;

/// <summary>
/// Fetches every contract the current routes reference, at startup and then every
/// <see cref="ContractValidationOptions.RefreshInterval"/>, with <c>If-None-Match</c>: a changed contract replaces the
/// old one, an unchanged one is kept, and a failed fetch or an unparsable document keeps the last good version.
/// </summary>
public sealed partial class ContractRefreshService(
    IProxyStateLookup proxyState,
    RoutePolicyResolver<ContractValidationPolicy> policies,
    ContractStore store,
    IHttpClientFactory httpClients,
    IOptions<ContractValidationOptions> options,
    TimeProvider time,
    ILogger<ContractRefreshService> logger) : BackgroundService
{
    public const string HttpClientName = "gateway-contracts";

    private readonly ContractValidationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RefreshInterval, time);
        do
        {
            await RefreshAllAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Fetches every referenced contract now (also used on demand, e.g. right after a contract was published).</summary>
    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var wanted = proxyState.GetRoutes()
            .Select(route => policies.Resolve(route.Config))
            .Where(static policy => policy.Enabled)
            .Select(static policy => (Url: policy.ContractUrl!, policy.Kind))
            .Distinct();

        foreach (var (url, kind) in wanted)
        {
            await RefreshAsync(url, kind, cancellationToken);
        }
    }

    internal async Task RefreshAsync(Uri url, ContractKind kind, CancellationToken cancellationToken)
    {
        var current = store.Find(url, kind);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.FetchTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (current?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var loaded = await ContractStore.ParseAsync(json, kind, response.Headers.ETag?.ToString(), time.GetUtcNow());
            store.Set(url, loaded);
            LogLoaded(logger, url, current is null ? "loaded" : "reloaded");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFetchFailed(logger, url, current is not null, exception);
        }
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Information, Message = "Contract {ContractUrl} {Outcome}")]
    private static partial void LogLoaded(ILogger logger, Uri contractUrl, string outcome);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning, Message = "Fetching contract {ContractUrl} failed; keeping the last good version: {HasLastGood}")]
    private static partial void LogFetchFailed(ILogger logger, Uri contractUrl, bool hasLastGood, Exception exception);
}
