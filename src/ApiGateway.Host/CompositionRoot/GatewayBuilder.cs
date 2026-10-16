using ApiGateway.Abstractions.Routing;
using ApiGateway.Proxy.Routing;

namespace ApiGateway.Host.CompositionRoot;

/// <summary>What an <see cref="IGatewayFeature"/> registers into, plus the helpers features share.</summary>
internal sealed class GatewayBuilder(WebApplicationBuilder builder, GatewaySettings settings, IReverseProxyBuilder proxy)
{
    public IServiceCollection Services => builder.Services;

    public ConfigureWebHostBuilder WebHost => builder.WebHost;

    public IWebHostEnvironment Environment => builder.Environment;

    public GatewaySettings Settings => settings;

    /// <summary>YARP, already configured with its ordered config filters and transforms.</summary>
    public IReverseProxyBuilder Proxy => proxy;

    /// <summary>Registers a feature's route-metadata parser (also as validator) and its policy resolver.</summary>
    public void AddRoutePolicy<TPolicy, TParser>()
        where TPolicy : class
        where TParser : class, IRouteMetadataParser<TPolicy>
    {
        Services.AddSingleton<TParser>();
        Services.AddSingleton<IRouteMetadataParser<TPolicy>>(services => services.GetRequiredService<TParser>());
        Services.AddSingleton<IRouteMetadataValidator>(services => services.GetRequiredService<TParser>());
        Services.AddSingleton<RoutePolicyResolver<TPolicy>>();
        Services.AddSingleton<IRoutePolicyResolver<TPolicy>>(services => services.GetRequiredService<RoutePolicyResolver<TPolicy>>());
    }

    /// <summary>Decorates the existing registration of <typeparamref name="TService"/> in place, keeping its lifetime.</summary>
    public void Decorate<TService>(Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        var descriptor = Services.LastOrDefault(static d => d.ServiceType == typeof(TService) && !d.IsKeyedService)
            ?? throw new InvalidOperationException($"No registration of {typeof(TService).Name} to decorate.");

        var index = Services.IndexOf(descriptor);
        Services[index] = ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)CreateInner(provider, descriptor)),
            descriptor.Lifetime);
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
}
