using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.LoadBalancing;
using Yarp.ReverseProxy.LoadBalancing;

namespace ApiGateway.Host.Features;

/// <summary>Canary / weighted traffic splitting (load-balancing policy <c>Weighted</c>).</summary>
internal sealed class LoadBalancingFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway) =>
        gateway.Services.AddSingleton<ILoadBalancingPolicy, WeightedLoadBalancingPolicy>();
}
