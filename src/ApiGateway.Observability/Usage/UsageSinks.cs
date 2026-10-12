using ApiGateway.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace ApiGateway.Observability.Usage;

/// <summary>Destination of flushed usage records (log, analytics store, billing system, ...).</summary>
public interface IUsageSink
{
    ValueTask WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken);
}

/// <summary>
/// Default sink: one structured event per record in the <c>ApiGateway.Usage</c> category, which the logging
/// provider routes to its own sink. Analytics pipelines aggregate from there.
/// </summary>
public sealed partial class LoggingUsageSink : IUsageSink
{
    private readonly ILogger _logger;

    public LoggingUsageSink(ILoggerFactory loggerFactory) =>
        _logger = loggerFactory.CreateLogger(GatewayTelemetry.UsageLogCategory);

    public ValueTask WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            LogUsage(_logger, record.Consumer, record.Route, record.PeriodStart, record.PeriodEnd, record.Requests, record.ClientErrors, record.ServerErrors, record.BytesIn, record.BytesOut);
        }

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Usage of {Consumer} on {Route} from {PeriodStart} to {PeriodEnd}: {Requests} requests, {ClientErrors} 4xx, {ServerErrors} 5xx, {BytesIn} bytes in, {BytesOut} bytes out")]
    private static partial void LogUsage(ILogger logger, string consumer, string route, DateTimeOffset periodStart, DateTimeOffset periodEnd, long requests, long clientErrors, long serverErrors, long bytesIn, long bytesOut);
}
