using ApiGateway.Abstractions.Audit;
using ApiGateway.Observability.Audit;
using ApiGateway.Observability.Synthetic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace ApiGateway.UnitTests.Observability;

public sealed class AuditAndSyntheticTests
{
    [Fact]
    public void Audit_entries_form_a_hash_chain()
    {
        var collector = new FakeLogCollector();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new FakeLoggerProvider(collector)));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero));
        var audit = new AuditLog(factory, time);

        audit.Write(new AuditEvent("system:configuration", "route.added", "route:orders", AfterHash: "h1"));
        audit.Write(new AuditEvent("user:alice", "route.disabled", "route:orders"));

        var entries = collector.GetSnapshot();
        entries.Count.ShouldBe(2);
        Value(entries[0], "PreviousHash").ShouldBe(AuditLog.GenesisHash);
        Value(entries[1], "PreviousHash").ShouldBe(Value(entries[0], "EntryHash"));
        entries.ShouldAllBe(static entry => entry.Category == "ApiGateway.Audit");
    }

    [Fact]
    public void Audit_hash_changes_when_any_field_changes()
    {
        var at = DateTimeOffset.UnixEpoch;
        var original = AuditLog.ComputeHash(AuditLog.GenesisHash, 1, at, new AuditEvent("a", "b", "c"));

        AuditLog.ComputeHash(AuditLog.GenesisHash, 1, at, new AuditEvent("a", "b", "d")).ShouldNotBe(original);
        AuditLog.ComputeHash(AuditLog.GenesisHash, 2, at, new AuditEvent("a", "b", "c")).ShouldNotBe(original);
    }

    [Fact]
    public void Synthetic_signatures_verify_and_expire()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var signature = new SyntheticSignature(new string('k', 32), time, TimeSpan.FromMinutes(2));

        var header = signature.Create("orders-health");
        signature.Verify(header).ShouldBe("orders-health");

        signature.Verify(header.Replace("orders-health", "catalog", StringComparison.Ordinal)).ShouldBeNull();
        new SyntheticSignature(new string('x', 32), time, TimeSpan.FromMinutes(2)).Verify(header).ShouldBeNull();

        time.Advance(TimeSpan.FromMinutes(3));
        signature.Verify(header).ShouldBeNull();
    }

    private static string? Value(FakeLogRecord record, string key) =>
        record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;
}
