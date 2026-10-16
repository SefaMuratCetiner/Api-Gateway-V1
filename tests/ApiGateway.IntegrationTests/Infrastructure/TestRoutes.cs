namespace ApiGateway.IntegrationTests.Infrastructure;

/// <summary>
/// Routes, clusters and composite endpoints the integration tests exercise, as configuration settings. Every cluster
/// has its own destination host, so tests can tell from the forwarded request URI which one was used.
/// </summary>
internal static class TestRoutes
{
    public const string ContractUrl = "http://contracts.test/orders.schema.json";

    public static IEnumerable<KeyValuePair<string, string>> Settings
    {
        get
        {
            var settings = new SettingsBuilder();
            Matching(settings);
            SplitService(settings);
            PrefixStripping(settings);
            Lifecycle(settings);
            Tenancy(settings);
            Weighted(settings);
            Limits(settings);
            Compression(settings);
            Validation(settings);
            Aggregation(settings);
            return settings.Values;
        }
    }

    private static void Matching(SettingsBuilder settings)
    {
        // Matching by header and by query parameter, next to a default route on the same path.
        settings.Destination("v2", "v2-1", "http://v2.test/");
        settings.Route("match-default", "orders", "/api/match/{**rest}", "GET");
        settings.Route("match-header", "v2", "/api/match/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:match-header:Match:Headers:0:Name", "X-Api-Version");
        settings.Set("ReverseProxy:Routes:match-header:Match:Headers:0:Values:0", "2");
        settings.Route("match-query", "v2", "/api/match/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:match-query:Match:QueryParameters:0:Name", "variant");
        settings.Set("ReverseProxy:Routes:match-query:Match:QueryParameters:0:Values:0", "beta");
    }

    private static void SplitService(SettingsBuilder settings)
    {
        // A service split out of orders: a more specific route and its own cluster.
        settings.Destination("invoices", "invoices-1", "http://invoices.test/");
        settings.Route("orders-invoices", "invoices", "/api/orders/invoices/{**rest}", "GET");
    }

    private static void PrefixStripping(SettingsBuilder settings)
    {
        // Prefix stripping to a destination with a path base.
        settings.Destination("legacy", "legacy-1", "http://legacy.test/svc/");
        settings.Route("legacy", "legacy", "/api/legacy/{**rest}", "GET", "POST");
        settings.Set("ReverseProxy:Routes:legacy:Transforms:0:PathRemovePrefix", "/api/legacy");
    }

    private static void Lifecycle(SettingsBuilder settings)
    {
        // API versions: deprecated (sunset far ahead) and retired.
        settings.Route("v1", "orders", "/api/v1/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:v1:Metadata:ApiLifecycle.Version", "1");
        settings.Set("ReverseProxy:Routes:v1:Metadata:ApiLifecycle.Deprecation", "2025-01-01T00:00:00Z");
        settings.Set("ReverseProxy:Routes:v1:Metadata:ApiLifecycle.Sunset", "2099-01-01T00:00:00Z");
        settings.Set("ReverseProxy:Routes:v1:Metadata:ApiLifecycle.Link", "https://docs.example.com/migrate-to-v2");
        settings.Route("v0", "orders", "/api/v0/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:v0:Metadata:ApiLifecycle.Retired", "true");
        settings.Set("ReverseProxy:Routes:v0:Metadata:ApiLifecycle.Link", "https://docs.example.com/migrate-to-v2");
    }

    private static void Tenancy(SettingsBuilder settings)
    {
        // Tenant-specific destinations next to a shared pool.
        settings.Destination("tenants", "shared", "http://shared.test/");
        settings.Destination("tenants", "acme", "http://acme.test/");
        settings.Set("ReverseProxy:Clusters:tenants:Destinations:acme:Metadata:Tenancy.Tenants", "acme");
        settings.Route("tenant", "tenants", "/api/t/{tenant}/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:tenant:Metadata:Tenancy.RouteValue", "tenant");
    }

    private static void Weighted(SettingsBuilder settings)
    {
        // Weighted split: the stable side is drained (weight 0), so every request goes to the canary.
        settings.Set("ReverseProxy:Clusters:canary:LoadBalancingPolicy", "Weighted");
        settings.Destination("canary", "stable", "http://stable.test/");
        settings.Set("ReverseProxy:Clusters:canary:Destinations:stable:Metadata:LoadBalancing.Weight", "0");
        settings.Destination("canary", "next", "http://next.test/");
        settings.Set("ReverseProxy:Clusters:canary:Destinations:next:Metadata:LoadBalancing.Weight", "5");
        settings.Route("canary", "canary", "/api/canary/{**rest}", "GET");
    }

    private static void Limits(SettingsBuilder settings)
    {
        // Limits: a small request body and a small response.
        settings.Route("small-body", "orders", "/api/small-body/{**rest}", "POST");
        settings.Set("ReverseProxy:Routes:small-body:MaxRequestBodySize", "16");
        settings.Route("small-response", "orders", "/api/small-response/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:small-response:Metadata:Limits.MaxResponseBodySize", "16");
    }

    private static void Compression(SettingsBuilder settings)
    {
        // Compression switched off for a BREACH-sensitive route.
        settings.Route("no-compression", "orders", "/api/no-compression/{**rest}", "GET");
        settings.Set("ReverseProxy:Routes:no-compression:Metadata:Compression.Enabled", "false");
    }

    private static void Validation(SettingsBuilder settings)
    {
        // Contract validation against a published JSON Schema.
        settings.Route("validated", "orders", "/api/validated/{**rest}", "POST");
        settings.Set("ReverseProxy:Routes:validated:Metadata:Validation.Contract", ContractUrl);
        settings.Set("ReverseProxy:Routes:validated:Metadata:Validation.ContractType", "JsonSchema");
    }

    private static void Aggregation(SettingsBuilder settings)
    {
        // Composite endpoints: partial results, and fail-whole.
        foreach (var (id, mode) in new[] { ("summary", "Partial"), ("summary-strict", "FailWhole") })
        {
            var prefix = $"Aggregation:Routes:{id}";
            settings.Set($"{prefix}:Path", $"/api/bff/{id}/{{id}}");
            settings.Set($"{prefix}:Methods:0", "GET");
            settings.Set($"{prefix}:Hosts:0", "localhost");
            settings.Set($"{prefix}:FailureMode", mode);
            settings.Set($"{prefix}:Calls:order:ClusterId", "orders");
            settings.Set($"{prefix}:Calls:order:Path", "/orders/{id}");
            settings.Set($"{prefix}:Calls:item:ClusterId", "catalog");
            settings.Set($"{prefix}:Calls:item:Path", "/items/{id}");
            settings.Set($"{prefix}:Calls:item:Timeout", "00:00:00.200");
        }
    }

    private sealed class SettingsBuilder
    {
        public List<KeyValuePair<string, string>> Values { get; } = [];

        public void Set(string key, string value) => Values.Add(new(key, value));

        public void Route(string id, string cluster, string path, params string[] methods)
        {
            Set($"ReverseProxy:Routes:{id}:ClusterId", cluster);
            Set($"ReverseProxy:Routes:{id}:Match:Path", path);
            Set($"ReverseProxy:Routes:{id}:Match:Hosts:0", "localhost");
            for (var i = 0; i < methods.Length; i++)
            {
                Set($"ReverseProxy:Routes:{id}:Match:Methods:{i}", methods[i]);
            }
        }

        public void Destination(string cluster, string id, string address) =>
            Set($"ReverseProxy:Clusters:{cluster}:Destinations:{id}:Address", address);
    }
}
