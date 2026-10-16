using ApiGateway.Host.CompositionRoot;
using ApiGateway.Proxy.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace ApiGateway.Host.Features;

/// <summary>Response compression (Brotli, gzip), with the per-route opt-out for BREACH-sensitive routes.</summary>
internal sealed class CompressionFeature : IGatewayFeature
{
    public void Register(GatewayBuilder gateway)
    {
        gateway.AddRoutePolicy<CompressionPolicy, CompressionMetadataParser>();

        var compression = gateway.Settings.Compression;
        if (!compression.Enabled)
        {
            return;
        }

        var mimeTypes = compression.AdditionalMimeTypes;
        gateway.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, .. mimeTypes];
        });
        gateway.Services.AddSingleton<ResponseCompressionProvider>();
        gateway.Services.AddSingleton<IResponseCompressionProvider, RouteAwareResponseCompressionProvider>();
    }
}
