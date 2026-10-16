using System.Diagnostics;
using ApiGateway.Abstractions.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Synthetic;

/// <summary>
/// Calls each configured probe through the gateway's <em>public</em> listener on its interval, exactly like a
/// client would, so a broken route, transform or certificate surfaces even when no client is calling. Results go
/// to metrics (<c>gateway.synthetic.checks</c>, <c>gateway.synthetic.duration</c>), logs and the
/// <c>synthetic</c> health check.
/// </summary>
public sealed partial class SyntheticProbeService : BackgroundService
{
    public const string HttpClientName = "gateway-synthetic";

    private readonly IHttpClientFactory _clients;
    private readonly SyntheticSignature _signature;
    private readonly SyntheticCheckState _state;
    private readonly SyntheticOptions _options;
    private readonly TimeProvider _time;
    private readonly SyntheticMetrics _metrics;
    private readonly ILogger<SyntheticProbeService> _logger;

    public SyntheticProbeService(
        IHttpClientFactory clients,
        SyntheticSignature signature,
        SyntheticCheckState state,
        IOptions<ObservabilityOptions> options,
        TimeProvider time,
        SyntheticMetrics metrics,
        ILogger<SyntheticProbeService> logger)
    {
        _clients = clients;
        _signature = signature;
        _state = state;
        _options = options.Value.Synthetic;
        _time = time;
        _metrics = metrics;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.Probes.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(_options.Probes.Select(probe => RunProbeLoopAsync(probe, stoppingToken)));
    }

    private async Task RunProbeLoopAsync(SyntheticProbeOptions probe, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(probe.Interval, _time);
        try
        {
            do
            {
                await RunOnceAsync(probe, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    internal async Task RunOnceAsync(SyntheticProbeOptions probe, CancellationToken stoppingToken)
    {
        var started = Stopwatch.GetTimestamp();
        int? status = null;
        string outcome;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(probe.Timeout);

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(probe.Method), probe.Url);
            request.Headers.TryAddWithoutValidation(GatewayHeaders.SyntheticCheck, _signature.Create(probe.Name));
            using var response = await _clients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            status = (int)response.StatusCode;
            outcome = status == probe.ExpectedStatus ? "success" : "unexpected_status";
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            outcome = "timeout";
        }
        catch (HttpRequestException)
        {
            outcome = "connection_error";
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var success = string.Equals(outcome, "success", StringComparison.Ordinal);
        var result = _state.Update(probe.Name, success, status, elapsed, _time.GetUtcNow());

        _metrics.RecordCheck(probe.Name, outcome, elapsed);

        if (!success)
        {
            LogProbeFailed(_logger, probe.Name, outcome, status, result.ConsecutiveFailures);
        }
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Synthetic check {Probe} failed: {Outcome} (status {StatusCode}, {ConsecutiveFailures} in a row)")]
    private static partial void LogProbeFailed(ILogger logger, string probe, string outcome, int? statusCode, int consecutiveFailures);
}
