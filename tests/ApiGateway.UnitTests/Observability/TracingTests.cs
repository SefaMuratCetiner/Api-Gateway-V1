using System.Diagnostics;
using ApiGateway.Observability.Correlation;
using ApiGateway.Observability.Tracing.Sampling;
using ApiGateway.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;

namespace ApiGateway.UnitTests.Observability;

public sealed class TracingTests : IDisposable
{
    private readonly TestMeterFactory _meters = new();

    public void Dispose() => _meters.Dispose();

    [Fact]
    public void Sampler_follows_a_sampled_parent_and_records_everything_else()
    {
        var sampler = new GatewaySampler(0.0);
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);

        sampler.ShouldSample(new SamplingParameters(parent, parent.TraceId, "x", ActivityKind.Server)).Decision.ShouldBe(SamplingDecision.RecordAndSample);
        sampler.ShouldSample(new SamplingParameters(default, ActivityTraceId.CreateRandom(), "x", ActivityKind.Server)).Decision.ShouldBe(SamplingDecision.RecordOnly);
    }

    [Fact]
    public void Tail_sampling_exports_unsampled_traces_that_failed()
    {
        var inner = new CollectingProcessor();
        using var processor = new TailSamplingProcessor(inner, new TailSamplingLimits(TimeSpan.FromSeconds(2), 16, 16), new TailSamplingMetrics(_meters));

        using var root = new Activity("root").Start();
        using var child = new Activity("child").SetParentId(root.TraceId, root.SpanId).Start();
        child.Stop();
        processor.OnEnd(child);
        inner.Ended.ShouldBeEmpty();

        root.SetStatus(ActivityStatusCode.Error);
        root.Stop();
        processor.OnEnd(root);

        inner.Ended.Count.ShouldBe(2);
        inner.Ended.ShouldAllBe(static span => span.Recorded);
    }

    [Fact]
    public void Tail_sampling_drops_fast_successful_unsampled_traces()
    {
        var inner = new CollectingProcessor();
        using var processor = new TailSamplingProcessor(inner, new TailSamplingLimits(TimeSpan.FromSeconds(2), 16, 16), new TailSamplingMetrics(_meters));

        using var root = new Activity("root").Start();
        root.SetTag("http.response.status_code", 200);
        root.Stop();
        processor.OnEnd(root);

        inner.Ended.ShouldBeEmpty();
    }

    [Fact]
    public void Slow_requests_are_interesting()
    {
        using var root = new Activity("root").Start();
        root.SetEndTime(root.StartTimeUtc.AddSeconds(3));

        TailSamplingProcessor.IsInteresting(root, TimeSpan.FromSeconds(2)).ShouldBeTrue();
    }

    [Fact]
    public void Traceresponse_header_has_the_w3c_shape()
    {
        using var activity = new Activity("x");
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        activity.Start();

        var value = TraceResponseHeadersMiddleware.FormatTraceResponse(activity);

        value.ShouldBe($"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01");
    }

    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("0193a6f0-7c1e-7d4e-9a9f-0a1b2c3d4e5f", true)]
    [InlineData("has space", false)]
    [InlineData("<script>", false)]
    [InlineData("", false)]
    public void Inbound_correlation_ids_must_be_short_and_well_formed(string value, bool accepted)
    {
        CorrelationIdMiddleware.IsAcceptable(value, 64).ShouldBe(accepted);
    }

    [Fact]
    public void Overlong_correlation_ids_are_replaced()
    {
        var headers = new HeaderDictionary { ["X-Correlation-Id"] = new string('a', 65) };

        var resolved = CorrelationIdMiddleware.Resolve(headers, new CorrelationOptions { MaxLength = 64 });

        resolved.ShouldNotBe(new string('a', 65));
        resolved.Length.ShouldBe(32);
    }
}
