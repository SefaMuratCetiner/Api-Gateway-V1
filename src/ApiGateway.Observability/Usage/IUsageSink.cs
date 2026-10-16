namespace ApiGateway.Observability.Usage;

/// <summary>Destination of flushed usage records (log, analytics store, billing system, ...).</summary>
public interface IUsageSink
{
    ValueTask WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken);
}
