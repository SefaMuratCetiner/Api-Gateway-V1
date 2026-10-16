using ApiGateway.Proxy.Aggregation;
using ApiGateway.Proxy.Compression;
using ApiGateway.Proxy.Cors;
using ApiGateway.Proxy.Validation;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

namespace ApiGateway.Host.CompositionRoot;

public sealed partial class GatewayCompositionRoot
{
    private void ComposeRequestHandling()
    {
        // CORS: named policies from configuration; routes select one with YARP's CorsPolicy.
        Services.AddCors();
        Services.AddSingleton<IConfigureOptions<CorsOptions>, GatewayCorsSetup>();

        // Compression (Brotli, gzip), with the per-route opt-out for BREACH-sensitive routes.
        AddRoutePolicy<CompressionPolicy, CompressionMetadataParser>();
        if (Compression.Enabled)
        {
            var mimeTypes = Compression.AdditionalMimeTypes;
            Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, .. mimeTypes];
            });
            Services.AddSingleton<ResponseCompressionProvider>();
            Services.AddSingleton<IResponseCompressionProvider, RouteAwareResponseCompressionProvider>();
        }

        // Request validation against the backends' published contracts, re-fetched when they change.
        AddRoutePolicy<ContractValidationPolicy, ContractValidationMetadataParser>();
        Services.AddSingleton<ContractStore>();
        Services.AddHttpClient(ContractRefreshService.HttpClientName);
        Services.AddHostedService<ContractRefreshService>();

        // Composite (BFF) endpoints.
        Services.AddSingleton<AggregateRequestHandler>();
    }
}
