using System.Security.Cryptography;
using System.Text;
using ApiGateway.Abstractions.Audit;
using ApiGateway.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace ApiGateway.Observability.Audit;

/// <summary>
/// Writes audit events to the <c>ApiGateway.Audit</c> log category, which the logging provider routes to a
/// dedicated sink with its own retention. Every entry carries a sequence number and a SHA-256 hash chained to the
/// previous entry, so a removed or edited line breaks the chain and is detectable when the log is verified.
/// </summary>
public sealed partial class AuditLog : IAuditLog
{
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private string _previousHash = GenesisHash;
    private long _sequence;

    public AuditLog(ILoggerFactory loggerFactory, TimeProvider time)
    {
        _logger = loggerFactory.CreateLogger(GatewayTelemetry.AuditLogCategory);
        _time = time;
    }

    /// <summary>Hash the first entry of each process chains to.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public void Write(AuditEvent auditEvent)
    {
        lock (_lock)
        {
            var sequence = ++_sequence;
            var timestamp = _time.GetUtcNow();
            var hash = ComputeHash(_previousHash, sequence, timestamp, auditEvent);

            LogAudit(
                _logger,
                auditEvent.Actor,
                auditEvent.Action,
                auditEvent.Target,
                sequence,
                timestamp,
                auditEvent.BeforeHash,
                auditEvent.AfterHash,
                auditEvent.Details,
                _previousHash,
                hash);

            _previousHash = hash;
        }
    }

    /// <summary>The chain hash of an entry; recomputing it from the logged fields verifies the chain.</summary>
    public static string ComputeHash(string previousHash, long sequence, DateTimeOffset timestamp, AuditEvent auditEvent)
    {
        var canonical = string.Join(
            '\n',
            previousHash,
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            auditEvent.Actor,
            auditEvent.Action,
            auditEvent.Target,
            auditEvent.BeforeHash ?? "",
            auditEvent.AfterHash ?? "",
            auditEvent.Details ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Audit {AuditAction} on {AuditTarget} by {AuditActor} (#{AuditSequence} at {AuditTimestamp}; {BeforeHash} -> {AfterHash}; {AuditDetails}; prev {PreviousHash}; hash {EntryHash})")]
    private static partial void LogAudit(
        ILogger logger,
        string auditActor,
        string auditAction,
        string auditTarget,
        long auditSequence,
        DateTimeOffset auditTimestamp,
        string? beforeHash,
        string? afterHash,
        string? auditDetails,
        string previousHash,
        string entryHash);
}
