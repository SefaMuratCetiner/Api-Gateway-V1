namespace ApiGateway.Host.CompositionRoot;

/// <summary>Decorates an existing registration in place, keeping its lifetime.</summary>
internal static class ServiceDecoration
{
    public static void Decorate<TService>(IServiceCollection services, Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        var descriptor = services.LastOrDefault(static d => d.ServiceType == typeof(TService) && !d.IsKeyedService)
            ?? throw new InvalidOperationException($"No registration of {typeof(TService).Name} to decorate.");

        var index = services.IndexOf(descriptor);
        services[index] = ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)CreateInner(provider, descriptor)),
            descriptor.Lifetime);
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
}
