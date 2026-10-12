using ApiGateway.Observability.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Usage;

/// <summary>Flushes <see cref="UsageAggregator"/> to every <see cref="IUsageSink"/> on an interval and once more at shutdown.</summary>
public sealed partial class UsageFlushService(
    UsageAggregator aggregator,
    IEnumerable<IUsageSink> sinks,
    IOptions<ObservabilityOptions> options,
    TimeProvider time,
    ILogger<UsageFlushService> logger) : BackgroundService
{
    private readonly IUsageSink[] _sinks = [.. sinks];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Usage.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Usage.FlushInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the final flush happens in StopAsync.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (options.Value.Usage.Enabled)
        {
            await FlushAsync(cancellationToken);
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var records = aggregator.Drain();
        if (records.Count == 0)
        {
            return;
        }

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteAsync(records, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failing sink loses this period's records but never affects requests or other sinks.
                LogSinkFailed(logger, sink.GetType().Name, records.Count, exception);
            }
        }
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Usage sink {Sink} failed; {RecordCount} records dropped")]
    private static partial void LogSinkFailed(ILogger logger, string sink, int recordCount, Exception exception);
}
