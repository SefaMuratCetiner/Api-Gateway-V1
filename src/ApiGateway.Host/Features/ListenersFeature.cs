using ApiGateway.Abstractions.Http;
using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Listeners;

namespace ApiGateway.Host.Features;

/// <summary>Kestrel's public and internal listeners, and which one a request arrived on.</summary>
internal sealed class ListenersFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        var listeners = gateway.Settings.Listeners;
        gateway.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var port in listeners.PublicPorts)
            {
                kestrel.ListenAnyIP(port);
            }

            kestrel.ListenAnyIP(listeners.InternalPort);

            // No "Server: Kestrel"; backend stack headers are stripped by ResponseHeaderStrippingTransformProvider.
            kestrel.AddServerHeader = false;
        });

        // For libraries that cannot see ListenerOptions.
        gateway.Services.AddSingleton<IListenerClassifier, ListenerClassifier>();
    }
}
