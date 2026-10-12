using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApiGateway.Abstractions.Http;
using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Synthetic;

/// <summary>Latest result of one probe.</summary>
public sealed record SyntheticProbeResult(string Probe, bool Success, int? StatusCode, TimeSpan Duration, DateTimeOffset At, int ConsecutiveFailures);

/// <summary>Shared, thread-safe store of the latest probe results (read by the health check).</summary>
public sealed class SyntheticCheckState
{
    private readonly ConcurrentDictionary<string, SyntheticProbeResult> _results = new(StringComparer.Ordinal);

    public IReadOnlyCollection<SyntheticProbeResult> Results => [.. _results.Values];

    public SyntheticProbeResult Update(string probe, bool success, int? statusCode, TimeSpan duration, DateTimeOffset at) =>
        _results.AddOrUpdate(
            probe,
            static (name, state) => new SyntheticProbeResult(name, state.success, state.statusCode, state.duration, state.at, state.success ? 0 : 1),
            static (name, previous, state) => new SyntheticProbeResult(name, state.success, state.statusCode, state.duration, state.at, state.success ? 0 : previous.ConsecutiveFailures + 1),
            (success, statusCode, duration, at));
}

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
    private readonly ILogger<SyntheticProbeService> _logger;
    private readonly Counter<long> _checks;
    private readonly Histogram<double> _duration;

    public SyntheticProbeService(
        IHttpClientFactory clients,
        SyntheticSignature signature,
        SyntheticCheckState state,
        IOptions<ObservabilityOptions> options,
        TimeProvider time,
        IMeterFactory meterFactory,
        ILogger<SyntheticProbeService> logger)
    {
        _clients = clients;
        _signature = signature;
        _state = state;
        _options = options.Value.Synthetic;
        _time = time;
        _logger = logger;
        var meter = meterFactory.Create(GatewayTelemetry.SyntheticMeter);
        _checks = meter.CreateCounter<long>("gateway.synthetic.checks", unit: "{check}", description: "Synthetic checks through the public listener, by probe and outcome.");
        _duration = meter.CreateHistogram<double>("gateway.synthetic.duration", unit: "s", description: "Duration of synthetic checks.");
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

        var tags = new TagList { { GatewayTelemetry.Tags.Probe, probe.Name }, { GatewayTelemetry.Tags.Outcome, outcome } };
        _checks.Add(1, tags);
        _duration.Record(elapsed.TotalSeconds, new TagList { { GatewayTelemetry.Tags.Probe, probe.Name } });

        if (!success)
        {
            LogProbeFailed(_logger, probe.Name, outcome, status, result.ConsecutiveFailures);
        }
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Synthetic check {Probe} failed: {Outcome} (status {StatusCode}, {ConsecutiveFailures} in a row)")]
    private static partial void LogProbeFailed(ILogger logger, string probe, string outcome, int? statusCode, int consecutiveFailures);
}
