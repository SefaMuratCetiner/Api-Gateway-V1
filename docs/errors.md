# Gateway errors

Every error the gateway produces itself is an RFC 9457 problem document (`application/problem+json`) in one fixed
shape. Errors returned by a backend are passed through **unchanged**: the gateway never rewrites a response that
came from upstream.

## Shape

```json
{
  "type": "urn:apigateway:error:gateway.route_not_found",
  "title": "No route matches the request.",
  "status": 404,
  "detail": "Check the URL and the API version in the path.",
  "instance": "/api/v9/orders",
  "code": "gateway.route_not_found",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

| Member | Meaning |
|---|---|
| `type` | `Errors:TypeBaseUri` + `code`. A URN by default; set an https base to link to this catalogue. |
| `title`, `detail` | Localized from `Accept-Language` (`Errors:SupportedCultures`, default `en`). `Content-Language` says which language was used. |
| `status` | HTTP status, same as the response. |
| `instance` | Request path, truncated to `Errors:MaxInstanceLength`. |
| `code` | Stable, machine-readable. Gateway codes always start with `gateway.`; clients branch on this. |
| `traceId` | Same value as the `X-Trace-Id` header. Quote it when reporting a problem. |

Members are always written in this order. Extensions never override them. Errors never contain exception text,
type names, stack traces, internal host names or destination addresses; those go to the log with the trace ID.

Every error response carries `Cache-Control: no-store`, `Vary: Accept-Language`, `X-Trace-Id`, `traceresponse` and
`X-Correlation-Id`. Responses for 429 and 503 also carry `Retry-After`.

## Status map

| Status | Code | When |
|---|---|---|
| 400 | `gateway.bad_request` | Malformed request (Kestrel), client body failed while forwarding, host not allowed, rejected request target (double/overlong encoding, control characters, encoded slashes) |
| 400 | `gateway.request_validation_failed` | Request body violates the backend's published contract (`errors` lists the failed JSON paths) |
| 401 | `gateway.authentication_required` | Route requires authentication, no credentials sent |
| 401 | `gateway.invalid_token` | Credentials sent but invalid or expired (`WWW-Authenticate` kept) |
| 403 | `gateway.forbidden` | Route authorization failed |
| 404 | `gateway.route_not_found` | No route matches (`instance` shows the path) |
| 405 | `gateway.method_not_allowed` | Route exists for other methods (`Allow` kept) |
| 410 | `gateway.api_version_retired` | The route's API version is retired (`Link` header and `migration` member point to the migration notes) |
| 413 | `gateway.payload_too_large` | Body over the limit |
| 414 | `gateway.uri_too_long` | Request target over `Errors:ProtocolLimits:MaxUrlLength` |
| 429 | `gateway.rate_limited` / `gateway.quota_exceeded` | Rate limit / quota (with `Retry-After`) |
| 431 | `gateway.headers_too_large` | Headers over the size or count limit |
| 500 | `gateway.internal_error` | Unhandled exception in the gateway |
| 502 | `gateway.bad_upstream_response` | Upstream answered with something invalid (unsafe redirect, response over the size or header limit), or the connection broke mid-request |
| 502 | `gateway.upstream_unreachable` | Connection refused, DNS failure or TLS failure to the destination |
| 503 | `gateway.no_healthy_destination` | Cluster has no healthy destination (`Retry-After` = `Errors:NoHealthyDestinationRetryAfter`) |
| 503 | `gateway.overloaded` / `gateway.maintenance` / `gateway.route_disabled` | Load shedding / maintenance mode / kill switch |
| 504 | `gateway.upstream_timeout` | Destination did not answer in time |
| 504 | `gateway.route_timeout` | The route's overall timeout (`TimeoutPolicy` / `Timeout`) fired |

The live catalogue, localized, is served at `GET /internal/errors` on the internal listener.

## Where each error comes from

| Component | Produces |
|---|---|
| `StatusCodeProblemMiddleware` | Problem bodies for empty gateway-made 4xx/5xx (routing 404/405, auth challenges, timeouts). It skips responses marked by `UpstreamResponseMarkerTransformProvider`. |
| `GatewayExceptionHandler` | 400/413/414/431 from Kestrel's `BadHttpRequestException`, 504 for route timeouts, 500 for everything else |
| `ProtocolLimitsMiddleware` | 414/431 below Kestrel's hard limits (Kestrel's own rejections above them have an empty body) |
| `ForwarderErrorProblemMiddleware` + `ForwarderErrorClassifier` | 400/413/502/503/504 from YARP forwarding errors |
| `ProblemAuthorizationResultHandler` | 401/403 |
| `RateLimitRejectionProblemWriter` | 429 (hook for the rate limiter) |
| `RouteTimeoutProblemWriter` | 504 for the `gateway-default` request-timeout policy |
| `HostValidationMiddleware`, `RequestNormalizationMiddleware` | 400 before routing (see [request-path.md](request-path.md)) |
| `ApiLifecycleMiddleware` | 410 for retired API versions |
| `RequestBodyLimitMiddleware`, `RequestValidationMiddleware` | 413 by `Content-Length` before upload; 400 contract violations |
| `PathRewriteTransformProvider`, `ResponseLimitsTransformProvider` | 502 for unsafe backend redirects and oversized backend responses |
| `AggregateRequestHandler` | The failed call's 502/503/504 for composite endpoints that fail whole; embedded problems in partial results |

## Adding codes

Define a `GatewayError` (`GatewayErrors.Create("my_code", status)`), contribute it through an
`IErrorCatalogContributor` registered in the composition root, and add `MyCode_Title` / `MyCode_Detail` to every
`ErrorMessages.*.resx`. Startup fails if a code is malformed, duplicated, or missing a translation.
