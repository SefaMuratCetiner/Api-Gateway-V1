using ApiGateway.Abstractions.Telemetry;
using ApiGateway.Observability.Metrics;
using ApiGateway.Observability.Usage;
using ApiGateway.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace ApiGateway.UnitTests.Observability;

public sealed class MetricsTests
{
    [Fact]
    public void Cardinality_guard_collapses_values_beyond_the_limit()
    {
        var guard = new CardinalityGuard();

        guard.Guard("route", "a", 2).ShouldBe("a");
        guard.Guard("route", "b", 2).ShouldBe("b");
        guard.Guard("route", "c", 2).ShouldBe(GatewayTelemetry.Overflow);
        guard.Guard("route", "a", 2).ShouldBe("a");
        guard.Guard("route", null, 2).ShouldBe(GatewayTelemetry.Unknown);
        guard.CountOf("route").ShouldBe(2);
    }

    [Fact]
    public void Cardinality_guard_holds_under_concurrency()
    {
        var guard = new CardinalityGuard();

        Parallel.For(0, 1000, i => guard.Guard("consumer", "c" + i, 50));

        guard.CountOf("consumer").ShouldBe(50);
    }

    [Theory]
    [InlineData(200, "2xx")]
    [InlineData(404, "4xx")]
    [InlineData(503, "5xx")]
    [InlineData(999, "other")]
    public void Status_classes(int status, string expected) => GatewayMetrics.StatusClassOf(status).ShouldBe(expected);

    [Fact]
    public void Unknown_methods_are_bucketed()
    {
        GatewayMetrics.NormalizeMethod("get").ShouldBe("GET");
        GatewayMetrics.NormalizeMethod("PROPFIND").ShouldBe("_OTHER");
    }

    [Fact]
    public void Usage_aggregates_per_consumer_and_route_and_overflows_safely()
    {
        var time = new FakeTimeProvider();
        var usage = new UsageAggregator(TestOptions.Observability(o => o.Usage.MaxEntries = 10), time);

        usage.Record("acme", "orders", 200, 10, 100);
        usage.Record("acme", "orders", 503, 10, 0);
        for (var i = 0; i < 20; i++)
        {
            usage.Record("c" + i, "orders", 200, 0, 0);
        }

        time.Advance(TimeSpan.FromMinutes(1));
        var records = usage.Drain();

        var acme = records.Single(static r => r.Consumer == "acme");
        acme.Requests.ShouldBe(2);
        acme.ServerErrors.ShouldBe(1);
        acme.BytesIn.ShouldBe(20);
        records.Sum(static r => r.Requests).ShouldBe(22);
        records.Count.ShouldBeLessThanOrEqualTo(11);
        usage.Drain().ShouldBeEmpty();
    }
}
