using System.Net.Sockets;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Errors.Forwarding;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.UnitTests.Errors;

public sealed class ForwarderErrorClassifierTests
{
    [Fact]
    public void No_available_destinations_is_503()
    {
        ForwarderErrorClassifier.Classify(ForwarderError.NoAvailableDestinations, null).Error.ShouldBe(GatewayErrors.NoHealthyDestination);
    }

    [Fact]
    public void Timeout_is_504()
    {
        ForwarderErrorClassifier.Classify(ForwarderError.RequestTimedOut, null).Error.ShouldBe(GatewayErrors.UpstreamTimeout);
    }

    [Fact]
    public void Connection_refused_is_upstream_unreachable()
    {
        var exception = new HttpRequestException(HttpRequestError.ConnectionError, "refused", new SocketException((int)SocketError.ConnectionRefused));

        ForwarderErrorClassifier.Classify(ForwarderError.Request, exception).Error.ShouldBe(GatewayErrors.UpstreamUnreachable);
    }

    [Fact]
    public void Other_request_failures_are_bad_upstream_responses()
    {
        var exception = new HttpRequestException(HttpRequestError.InvalidResponse, "garbage");

        ForwarderErrorClassifier.Classify(ForwarderError.Request, exception).Error.ShouldBe(GatewayErrors.BadUpstreamResponse);
        ForwarderErrorClassifier.Classify(ForwarderError.ResponseHeaders, null).Error.ShouldBe(GatewayErrors.BadUpstreamResponse);
    }

    [Fact]
    public void Client_body_over_the_limit_is_413()
    {
        var exception = new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge);

        ForwarderErrorClassifier.Classify(ForwarderError.RequestBodyClient, exception).Error.ShouldBe(GatewayErrors.PayloadTooLarge);
        ForwarderErrorClassifier.Classify(ForwarderError.RequestBodyClient, new IOException()).Error.ShouldBe(GatewayErrors.BadRequest);
    }

    [Theory]
    [InlineData(ForwarderError.RequestCanceled)]
    [InlineData(ForwarderError.RequestBodyCanceled)]
    [InlineData(ForwarderError.ResponseBodyClient)]
    public void Client_cancellation_gets_no_body(ForwarderError error)
    {
        var classification = ForwarderErrorClassifier.Classify(error, null);

        classification.ClientGone.ShouldBeTrue();
        classification.Error.ShouldBeNull();
    }

    [Fact]
    public void Every_forwarder_error_is_classified()
    {
        foreach (var error in Enum.GetValues<ForwarderError>().Where(static e => e != ForwarderError.None))
        {
            var classification = ForwarderErrorClassifier.Classify(error, null);
            (classification.ClientGone || classification.Error is not null).ShouldBeTrue(error.ToString());
        }
    }
}
