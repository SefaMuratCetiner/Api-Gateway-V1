# Request path: routing, normalization, headers, handling, limits

What happens to a request between Kestrel and the backend, and how to configure it. Errors produced along the way
are listed in [errors.md](errors.md).

## Order

| Stage | Component | Does |
|---|---|---|
| Server | Kestrel (`Limits`) | Header size/count, connections, HTTP/2 streams, minimum body data rate, global body cap; no `Server` header |
| 1–3 | Correlation, tracing, access log, metrics, compression, error handling, 414/431 soft limits | (see [observability.md](observability.md), [errors.md](errors.md)) |
| 4 | `HostValidationMiddleware` | `Host` and every `X-Forwarded-Host` value must match `Routing:AllowedHosts` (public listener only) |
| 4 | `RequestHeaderStrippingMiddleware` | Removes `Headers:StrippedRequestHeaders` (client `X-Forwarded-*`, `Forwarded`, `X-Real-IP`, `X-Tenant-Id`, `X-User-Id`, `X-Internal-*`) |
| 4 | `RequestNormalizationMiddleware` | Rejects bad request targets; canonicalizes the path (rewrite or 308) |
| 5 | Routing, listener guard, CORS, timeouts, authorization | CORS preflights are answered here |
| Proxy | `RouteContext` → `ApiLifecycle` → `RequestBodyLimit` → `RequestValidation` → body logging → forwarder errors → `TenantRouting` → affinity, load balancing, passive health | |
| Transforms | marker, response header stripping, upstream CORS header removal, path rewrite (`X-Forwarded-Prefix`, `Location`, `Set-Cookie`, redirect check), response limits | YARP sets `X-Forwarded-For/Proto/Host/Prefix` itself (action `Set`) and handles hop-by-hop headers |

## Routing conventions (`Routing`)

Every YARP route is checked when configuration loads (`ProxyConventionsFilter`); a violation rejects the
configuration (startup fails; a reload keeps the running config):

- `Match.Path` under `Routing:PublicPathPrefix` (`/api`). Health, metrics and the error catalog live on the
  internal listener; the error catalog is also served publicly in Development.
- `Match.Methods` listed explicitly; other methods get 405.
- `Match.Hosts` listed explicitly. Routes without hosts and wildcard hosts (`*`, `*.example.com`) are accepted only
  in Development, where `appsettings.Development.json` sets them to `*`. `Routing:AllowedHosts` follows the same
  rule.

Routing itself is YARP's: path, host, method, header and query matching; per-route path transforms
(`PathRemovePrefix`, `PathPrefix`, `PathPattern`, `PathSet`). Paths are forwarded unchanged unless a route says
otherwise. Splitting a service out needs a more specific route plus a cluster; clients see no change.

| Feature | Configuration |
|---|---|
| API versions | One route per version, by path (`/api/v2/**`) or header (`Match.Headers: Api-Version`) |
| Version lifecycle | Route metadata `ApiLifecycle.Version`, `ApiLifecycle.Deprecation`, `ApiLifecycle.Sunset` (ISO 8601), `ApiLifecycle.Link` (migration notes, required with Sunset/Retired), `ApiLifecycle.Retired`. Adds `Deprecation: @<unix>` (RFC 9745), `Sunset` (RFC 8594) and `Link: <…>; rel="deprecation"`; from the sunset on (or when retired) answers 410 `gateway.api_version_retired` |
| Service discovery | `Routing:DnsDiscovery:Enabled` / `RefreshPeriod`: destination host names (Kubernetes services, Consul DNS, plain DNS) resolve to one destination per address. A name that does not resolve is kept as is (logged) instead of rejecting the configuration |
| Per-tenant destinations | Destination metadata `Tenancy.Tenants` (comma-separated). A tenant with dedicated destinations uses only those; everyone else uses destinations without `Tenancy.Tenants`. The tenant comes from identity (`IGatewayRequestFeature.TenantId`) or the route value named by route metadata `Tenancy.RouteValue` |
| Canary / weighted split | Cluster `LoadBalancingPolicy: "Weighted"`, destination metadata `LoadBalancing.Weight` (0..10000, default 1) |
| `Location` / `Set-Cookie` | Mapped back to the public host and path when a route rewrites paths or hosts (destination `Domain` dropped, `Path` mapped) |

## Normalization

Rejected with 400 `gateway.bad_request` before routing: double percent-encoding (`%252e`), invalid or overlong UTF-8
(`%c0%ae`), NUL and control characters in path, query or header values, malformed path escapes, and — unless
`Routing:Normalization:AllowEncodedSlashes` — `%2F`, `%5C` and `\` in the path.

The path is then canonicalized: `.`/`..` segments removed (never above the root), duplicate slashes collapsed,
trailing slash removed (`RemoveTrailingSlash`), optionally lower-cased (`LowercasePath`; off by default since route
matching is case-insensitive and backends may treat IDs case-sensitively). `Handling: Rewrite` (default) replaces
the path; `Redirect` answers 308 with a path-only `Location` that always starts with a single `/`.

Open redirects: backend 3xx `Location` values that are protocol-relative, contain `\` or control characters, use a
non-http(s) scheme, or point to a host outside `Routing:AllowedHosts` and `Routing:ExternalRedirectHosts` are
replaced with 502 `gateway.bad_upstream_response`.

Synthetic probe URLs (`Observability:Synthetic:Probes`) must use an allowed host.

## Request and response handling

| Feature | Configuration |
|---|---|
| Header transforms | YARP route `Transforms` (`RequestHeader`, `ResponseHeader`, …) |
| CORS | `Cors:Policies:<name>` (`Origins`, `Methods`, `Headers`, `ExposedHeaders`, `AllowCredentials`, `PreflightMaxAge`); routes select one with `CorsPolicy`. Origins are explicit or `https://*.domain`; `*` is rejected together with credentials. Gateway headers (`X-Correlation-Id`, `X-Trace-Id`, `Retry-After`, `Deprecation`, `Sunset`, `Link`) are always exposed. Backend `Access-Control-*` headers are dropped on those routes |
| Compression | `Compression:Enabled` (Brotli, gzip, also over HTTPS); route metadata `Compression.Enabled: false` for responses that mix secrets with attacker-controlled input (BREACH) |
| Contract validation | Route metadata `Validation.Contract` (URL of the backend's OpenAPI 3 JSON or JSON Schema), `Validation.ContractType` (`OpenApi`/`JsonSchema`), `Validation.MaxBodyBytes`. Contracts are re-fetched every `Validation:RefreshInterval` with `If-None-Match`; the last good version is kept. JSON bodies only; until a contract is loaded, requests pass unvalidated |
| Aggregation (BFF) | `Aggregation:Routes:<id>`: `Path`, `Methods` (GET/HEAD), `Hosts`, `FailureMode` (`FailWhole`/`Partial`), `Calls:<name>` (`ClusterId`, `Path` with `{routeValue}`, `ForwardQuery`, `Timeout`, `MaxResponseBytes`). Response: `{"data": {...}, "errors": {...}}`, errors as problem objects. Read at startup |
| `Idempotency-Key` | Forwarded unchanged |

## Limits (`Limits`)

| Limit | Where | Result |
|---|---|---|
| URL length, header size/count | `Limits:Protocol:MaxUrlLength`, `MaxRequestHeadersTotalSize`, `MaxRequestHeaderCount` (Kestrel's own limits stay above) | 414 / 431 |
| Request body | `Limits:MaxRequestBodySize` (global), YARP route `MaxRequestBodySize` (`-1` = unlimited for uploads/streaming) | 413, by `Content-Length` before the body is read (so `Expect: 100-continue` clients never upload it), or by Kestrel while streaming |
| Response body | `Limits:MaxResponseBodySize`, route metadata `Limits.MaxResponseBodySize` (`-1` = unlimited) | 502 when declared; connection aborted when a stream passes it |
| Response headers | `Limits:MaxResponseHeadersSizeKb` | 502 |
| Server | `Limits:Server:MaxConcurrentConnections`, `MaxConcurrentUpgradedConnections`, `Http2MaxStreamsPerConnection`, `MinRequestBodyDataRate` (+ grace period) | Kestrel |

Bodies stream through unbuffered (multipart uploads, downloads); `Range` requests and 206 responses pass through
and are never compressed. Only routes with contract validation buffer the request body, up to their limit.
