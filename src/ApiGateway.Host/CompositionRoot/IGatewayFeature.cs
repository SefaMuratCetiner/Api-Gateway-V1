namespace ApiGateway.Host.CompositionRoot;

/// <summary>
/// One gateway capability's service registrations, in one file under <c>Features/</c>. A feature registers
/// services only: configuration is bound by <see cref="GatewaySettings"/>, and everything whose order matters
/// (middleware, the proxy pipeline, YARP config filters and transforms, endpoints) stays in
/// <see cref="GatewayCompositionRoot"/>, where the whole order can be read in one place.
/// </summary>
internal interface IGatewayFeature
{
    void Register(GatewayBuilder gateway);
}
