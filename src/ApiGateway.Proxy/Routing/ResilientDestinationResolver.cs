using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace ApiGateway.Proxy.Routing;

/// <summary>
/// Wraps a destination resolver (DNS discovery) so one name that does not resolve cannot reject the whole proxy
/// configuration: when resolving a cluster fails, destinations are resolved one by one, and a destination whose
/// name fails is kept under its host name (resolved again on connect; passive health checks deal with it) and
/// logged. The next refresh tries again.
/// </summary>
public sealed partial class ResilientDestinationResolver(IDestinationResolver inner, ILogger<ResilientDestinationResolver> logger) : IDestinationResolver
{
    public async ValueTask<ResolvedDestinationCollection> ResolveDestinationsAsync(
        IReadOnlyDictionary<string, DestinationConfig> destinations,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.ResolveDestinationsAsync(destinations, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogClusterResolutionFailed(logger, exception);
        }

        var resolved = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
        var tokens = new List<IChangeToken>();
        foreach (var (id, destination) in destinations)
        {
            try
            {
                var single = await inner.ResolveDestinationsAsync(new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { [id] = destination }, cancellationToken);
                foreach (var (resolvedId, resolvedDestination) in single.Destinations)
                {
                    resolved[resolvedId] = resolvedDestination;
                }

                if (single.ChangeToken is { } token)
                {
                    tokens.Add(token);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDestinationUnresolved(logger, id, exception);
                resolved[id] = destination;
            }
        }

        return new ResolvedDestinationCollection(resolved, tokens.Count == 0 ? null : new CompositeChangeToken(tokens));
    }

    [LoggerMessage(EventId = 4500, Level = LogLevel.Warning, Message = "Destination discovery failed for a cluster; resolving its destinations one by one")]
    private static partial void LogClusterResolutionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4501, Level = LogLevel.Warning, Message = "Destination {DestinationId} could not be resolved; kept under its host name until the next refresh")]
    private static partial void LogDestinationUnresolved(ILogger logger, string destinationId, Exception exception);
}
