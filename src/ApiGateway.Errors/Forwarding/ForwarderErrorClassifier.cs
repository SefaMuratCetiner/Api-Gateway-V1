using System.Net.Sockets;
using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Errors.Forwarding;

/// <summary>What to tell the client about a forwarding failure.</summary>
/// <param name="Error">The gateway error to write, or <c>null</c> when nothing should be written.</param>
/// <param name="ClientGone">True when the client canceled; there is nobody to answer.</param>
public readonly record struct ForwarderErrorClassification(GatewayError? Error, bool ClientGone)
{
    public static ForwarderErrorClassification None => new(null, ClientGone: false);

    public static ForwarderErrorClassification Client => new(null, ClientGone: true);

    public static ForwarderErrorClassification Of(GatewayError error) => new(error, ClientGone: false);
}

/// <summary>
/// Maps YARP's <see cref="ForwarderError"/> to the gateway status map: 502 for an invalid or unreachable
/// upstream, 503 when no healthy destination exists, 504 on timeout, 400/413 when the client's own body failed.
/// Pure function; the exception is inspected by type only, never by message.
/// </summary>
public static class ForwarderErrorClassifier
{
    public static ForwarderErrorClassification Classify(ForwarderError error, Exception? exception) => error switch
    {
        ForwarderError.None => ForwarderErrorClassification.None,

        ForwarderError.NoAvailableDestinations => ForwarderErrorClassification.Of(GatewayErrors.NoHealthyDestination),

        ForwarderError.RequestTimedOut or ForwarderError.UpgradeActivityTimeout =>
            ForwarderErrorClassification.Of(GatewayErrors.UpstreamTimeout),

        ForwarderError.RequestCanceled
            or ForwarderError.RequestBodyCanceled
            or ForwarderError.ResponseBodyCanceled
            or ForwarderError.ResponseBodyClient
            or ForwarderError.UpgradeRequestCanceled
            or ForwarderError.UpgradeResponseCanceled
            or ForwarderError.UpgradeResponseClient => ForwarderErrorClassification.Client,

        ForwarderError.RequestBodyClient or ForwarderError.UpgradeRequestClient =>
            ForwarderErrorClassification.Of(IsPayloadTooLarge(exception) ? GatewayErrors.PayloadTooLarge : GatewayErrors.BadRequest),

        ForwarderError.Request => ForwarderErrorClassification.Of(
            IsConnectFailure(exception) ? GatewayErrors.UpstreamUnreachable : GatewayErrors.BadUpstreamResponse),

        ForwarderError.RequestCreation => ForwarderErrorClassification.Of(GatewayErrors.InternalError),

        ForwarderError.RequestBodyDestination
            or ForwarderError.ResponseHeaders
            or ForwarderError.ResponseBodyDestination
            or ForwarderError.UpgradeRequestDestination
            or ForwarderError.UpgradeResponseDestination => ForwarderErrorClassification.Of(GatewayErrors.BadUpstreamResponse),

        _ => ForwarderErrorClassification.Of(GatewayErrors.BadUpstreamResponse),
    };

    /// <summary>True when the request never reached the destination (refused, unresolvable, TLS failure).</summary>
    internal static bool IsConnectFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError }:
                    return true;
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound or SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.TryAgain or SocketError.TimedOut }:
                    return true;
            }
        }

        return false;
    }

    private static bool IsPayloadTooLarge(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                return true;
            }
        }

        return false;
    }
}
