# API Gateway — Functionality Checklist

**Purpose:** Single managed entry point for APIs. Handles cross-cutting concerns (auth, rate limiting, transformation, versioning) so backend services don't have to. Operates at L7, API-aware.

## Routing
- [x] Routing by path, host, method, header, query
- [x] Explicit hosts per route; wildcard hosts in Development only
- [x] Explicit allowed methods per route (others 405)
- [x] Path rewriting and prefix stripping per route; paths forwarded unchanged by default, so `Location`, ETags and logs match both sides
- [x] `Location` and `Set-Cookie` path/domain rewritten when a route rewrites paths or hosts
- [x] API versioning (path/header based)
- [x] `Deprecation` and `Sunset` headers per route/version
- [x] Retired API versions return 410 with a link to migration notes
- [x] Public listener routes only the public prefix (e.g. `/api/**`); health, metrics and docs on the internal listener; docs on the public listener in Development only
- [ ] Destinations via service discovery (Kubernetes, Consul, DNS); same config locally and deployed
- [x] Per-tenant routing (tenant-specific destinations)
- [x] Splitting a service out needs only a route + cluster entry; clients see no change
- [x] Canary / weighted traffic splitting

## Request Normalization
- [x] Path canonicalized before routing and auth: `../`, `./`, duplicate slashes, trailing slashes, case rules
- [x] Double/overlong percent-encoding rejected
- [x] Null bytes and control characters rejected
- [x] `Host` checked against the allowed-hosts list; `X-Forwarded-Host` validated the same way
- [x] Open-redirect protection in redirects and rewrites

## Request & Response Handling
- [ ] Request/response transformation (headers, body, format) per route
- [x] Request aggregation / composition (BFF endpoints):
  - timeout per sub-call
  - partial-failure handling (fail whole request or return partial result per route)
  - errors in the same problem-details shape
- [ ] Protocol translation (REST ↔ gRPC, HTTP ↔ WebSocket)
- [x] Request validation against schema (OpenAPI/JSON Schema), reloaded when the backend's published contract changes
- [x] Response compression (Brotli/gzip); disabled per route where responses mix secrets with attacker-controlled input (BREACH)
- [x] CORS: named policy, explicit origins from config, no `*` with credentials, wildcard-subdomain matching, preflight `max-age`; preflight answered at the gateway
- [x] Maximum upstream response size and response header size
- [x] `Idempotency-Key` forwarded intact

## Response Caching
- [ ] GET/HEAD only, per route
- [ ] Cache key includes tenant, and user for user-specific responses
- [ ] Honours `Cache-Control` and `Vary`; only cacheable status codes stored
- [ ] Invalidation by tag, shared with the backend's invalidation
- [ ] Stampede protection: one rebuild per key, stale served while rebuilding
- [ ] Cache poisoning protection: unkeyed headers ignored or normalized
- [ ] Negative caching (404) with a short TTL
- [ ] Authenticated purge endpoint on the internal listener

## Headers & Correlation
- [x] Strip client-supplied `X-Forwarded-*`, `X-Tenant-Id`, `X-User-Id`, `X-Internal-*` before forwarding
- [x] Set `X-Forwarded-For/Proto/Host/Prefix` itself, replacing client values
- [x] Generate `X-Correlation-Id` if missing; accept inbound only if short and well-formed, else replace
- [x] Trace starts at the gateway; inbound `traceparent` trusted per environment (untrusted → new trace, inbound kept as link)
- [x] `X-Trace-Id`, `traceresponse`, `X-Correlation-Id` on every response, including gateway-made errors
- [ ] Rate-limit headers from both gateway and backend passed to the client
- [x] Hop-by-hop headers handled by the proxy library, not custom code
- [x] `Server`, `X-Powered-By` and other stack-revealing headers removed

## Authentication & Authorization
- [ ] API keys: hashed, scoped, expiring, rotatable; checked before forwarding
- [ ] OAuth2 / OIDC token validation against an external IdP
- [ ] JWT verification: signature, issuer, audience per route, lifetime, configurable clock skew
- [ ] Token size limit
- [ ] Opaque token introspection, with a short-lived result cache
- [ ] Token revocation / denylist check
- [ ] Signing keys fetched from JWKS, cached and rotated; unknown `kid` triggers a rate-limited refresh; readiness does not depend on the IdP
- [ ] Gateway needs only authority + audience, no IdP client of its own
- [ ] Invalid/expired tokens rejected early (401 + `WWW-Authenticate`)
- [ ] Requests without a token passed through unless the route requires authentication
- [ ] Scope/role/claim-based authorization per route (coarse checks; backend stays authoritative for permission, tenant and object-level checks)
- [ ] Identity propagation: `Authorization` forwarded unchanged by default; token exchange (RFC 8693) / re-signing per route; trusted identity headers set only by the gateway
- [ ] mTLS: client certificates for partners; gateway ↔ backend
- [ ] Backend-for-frontend: session cookie + antiforgery for browser front ends

## Traffic Management
- [ ] Rate limiting per API key, user, IP, client, tenant and route
- [ ] Burst/spike control (token bucket or equivalent), not only fixed windows
- [ ] Per-route concurrency limit, so one route can't use every backend connection
- [ ] Per-tenant isolation: noisy-neighbour limits and per-tenant config
- [ ] Counters shared across instances in a distributed store, keys prefixed (`gateway:`)
- [ ] Client IP from the TCP connection, or `X-Forwarded-For` only from known load balancers (right-most trusted hop in multi-hop chains)
- [ ] Quotas (daily/monthly) per client/tenant, configured per environment
- [ ] Limits and quotas configurable per client type and tenant tier
- [ ] 429 with `Retry-After`
- [ ] Circuit breaker per cluster (open after repeated failures, half-open probes)
- [ ] Retries with backoff and jitter: off by default, enabled per route, idempotent methods only (POST only with `Idempotency-Key`)
- [ ] Retry budget (retries capped as a share of requests); connect failures retried first; no retry after the response has started
- [ ] Timeouts: connect, idle/activity, response-header and route timeout, set separately
- [ ] Timeouts ordered outside-in: gateway route > backend request > outbound/DB
- [ ] Streaming routes (SSE, downloads): longer timeout, response buffering off
- [ ] WebSocket, SSE and gRPC streaming limits per client: open connections, message size, idle timeout
- [ ] Load balancing across upstream instances with active and passive health checks

## Overload Protection
- [ ] Load shedding: reject early with 503 + `Retry-After` when queue length or latency passes a threshold
- [ ] Backpressure: bounded request queues, no unbounded buffering
- [ ] Bulkheads: separate connection pools and concurrency limits per cluster
- [ ] Priority traffic classes: critical routes shed last
- [ ] Admission control on global concurrency

## Emergency Controls
- [ ] Kill switch per route: instant 503 problem details without redeploy
- [ ] Maintenance mode, global or per route, with `Retry-After`
- [ ] Instant block of an API key, client, IP, tenant or route through the admin API

## Dependency Failure Behaviour
- [ ] Counter store down: fail-open or fail-closed per route, with a metric
- [ ] IdP/JWKS down: cached keys keep validating; introspection fail-open or fail-closed per route
- [ ] Service discovery down: last known destinations kept
- [ ] DNS failure: last good resolution kept; TTL respected otherwise
- [ ] Config source down: running config kept
- [ ] Cache store down: requests bypass the cache
- [ ] Telemetry/log sink down: bounded buffer, oldest dropped, requests never blocked

## Request Limits
- [x] Server limits: header size/count, connections, HTTP/2 streams, minimum request data rate (slow clients)
- [x] Global body size cap with per-route overrides (uploads, streaming)
- [x] URL length limit (414), header limit (431), body limit (413)
- [x] Streaming multipart uploads; range requests (206) passed through for downloads
- [x] `Expect: 100-continue` handled; body rejected before upload when limits fail

## Security
- [ ] TLS termination, TLS 1.2+, defined minimum version and cipher policy
- [ ] Certificate issuance/renewal and hot reload without restart
- [ ] HTTPS redirection (308) and HSTS
- [ ] Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, locked-down CSP
- [ ] Input sanitization / WAF integration (e.g. OWASP Coraza)
- [ ] Injection, request smuggling and malformed-request protection
- [ ] Protocol-attack protection:
  - HTTP/2 rapid reset (stream reset rate limit)
  - HPACK/QPACK header-compression bombs
  - slowloris and slow-read variants
  - chunked encoding and trailer abuse
  - h2c upgrade smuggling blocked
- [ ] IP allow/deny lists, globally and per route (admin/partner endpoints)
- [ ] Bot/abuse detection
- [ ] Incoming webhook routes: signature + timestamp check instead of token check
- [ ] Egress allowlist for the gateway's own outbound calls (JWKS, introspection, discovery)

## Errors
- [x] Gateway-made errors as RFC 9457 problem details (`application/problem+json`): `title`, `detail`, stable `code`, `traceId`
- [x] Same shape as backend errors; `gateway.` code prefix
- [x] Status map: 401 bad token, 403 route authorization, 404 no route, 405 method, 413 body, 414 URL, 431 headers, 429 (+`Retry-After`), 502 invalid upstream response, 503 no healthy destination / shed / maintenance, 504 timeout
- [x] Backend error responses passed through unchanged
- [x] No internal hostnames, destination addresses or exception text in errors
- [x] Localized error messages (`Accept-Language`)

## Observability
- [x] Structured access log per request: method, route, cluster, status, duration, upstream duration, client IP, client ID, tenant, trace ID, correlation ID
- [x] Body logging off by default; opt-in per route with size limit
- [x] Redaction of `Authorization`, cookies, tokens, idempotency keys and secrets in all logs
- [x] Client IP anonymization (truncate/hash) per environment; configurable log retention
- [x] Metrics per route/cluster/consumer: RPS, error rate, latency (p50/p95/p99), upstream wait, gateway-added latency, rate-limit rejections, token rejections, shed requests, destination health changes
- [x] Metric cardinality limits: no raw IDs or paths as labels
- [x] Distributed tracing (W3C Trace Context / OpenTelemetry); gateway span is parent of backend spans
- [x] Trace sampling: configurable rate; errors and slow requests always sampled
- [x] Audit log of config and admin changes (who, what, when)
- [x] Alerting and dashboards
- [x] Synthetic checks through a public route, so broken routes surface
- [x] Usage analytics per consumer

## Consumers & API Lifecycle
- [ ] Consumer/application registry: clients, keys, tiers, owners
- [ ] Developer portal: self-service registration, key issuance, contract browsing
- [ ] Mock/sandbox environment for consumers
- [ ] Monetization/billing hooks
- [ ] Outgoing event hooks/webhooks: signed payloads, retries, delivery log

## Extensibility
- [ ] Plugin/middleware system
- [ ] Custom policies (scripting or compiled plugins)

## Configuration & Operations
- [ ] Declarative config, GitOps / IaC friendly
- [ ] Per-environment config, validated at startup
- [ ] Config versioning and promotion between environments
- [ ] Config signing; unsigned config rejected
- [ ] Admin API and hot reload without downtime
- [ ] Admin API: authentication, RBAC, change approval, separate internal listener/network
- [ ] Bad config never replaces good; rollback to last known good
- [ ] Scheduled config/state export for backup and restore
- [ ] Separation of control plane and data plane
- [ ] Stateless data plane, horizontally scalable
- [ ] High availability, multi-zone deployment across failure domains
- [ ] `/health/live` and `/health/ready` (config loaded and valid; independent of backend and cache) on a separate internal port
- [ ] Warm-up before taking traffic: config loaded, JWKS fetched, connection pools primed
- [ ] Graceful shutdown: stop accepting, drain in-flight within a limit, flush telemetry

## Packaging & Deployment
- [ ] Minimal (chiseled) image, non-root, read-only filesystem
- [ ] Signed images with build provenance
- [ ] Dependency versions pinned (lock files, central package versions)
- [ ] Secrets and TLS certificates from a secret store, never from config files
- [ ] CPU/memory requests and limits
- [ ] Autoscaling on CPU, RPS or concurrency; scale-in drains connections first
- [ ] Time sync (NTP) on hosts, for token validation and log correlation
- [ ] Backends on a private network reachable only from the gateway (network policy, not only config)
- [ ] Shared code (JSON defaults, problem details, correlation, rate limiter) in a small library without heavy dependencies

## Documentation
- [ ] API documentation (OpenAPI); contract's server URL is the gateway's public URL
- [ ] Gateway behaviours (rate-limit headers, 401/403/413/414/431/429/502/503/504) in the contract's common responses
- [ ] Error code catalogue, including `gateway.` codes
- [ ] Rate-limit and quota values per client type and tenant tier
- [ ] Per-environment config: what lives in config files, environment variables and the secret store
- [ ] Config promotion process between environments
- [ ] Breaking-change policy, deprecation windows and sunset process
- [ ] Extension points (middleware, plugins, custom policies)
- [ ] Dependency failure matrix: behaviour of each feature when each dependency is down
- [ ] Backend responsibilities:
  - validates every token (service-to-service calls bypass the gateway)
  - authoritative authorization (permissions, tenant, object-level)
  - trusts forwarded headers only from the gateway's network
  - stricter per-endpoint body limits
  - request timeout shorter than the gateway route timeout
  - flag to re-enable edge features (HTTPS, HSTS, CORS, security headers, compression) when running without the gateway
- [ ] Rotation procedures: TLS certificates, JWT signing keys, API keys, mTLS certificates, admin credentials
- [ ] Disaster recovery: RTO/RPO, config backup and restore procedure
- [ ] Capacity and sizing per instance: RPS, connections, memory
- [ ] Privacy: log retention periods, IP anonymization policy, deletion of consumer data and logs (GDPR)
- [ ] Vulnerability (CVE) patch response times
- [ ] Runbooks: add/split a route, change a limit safely, kill switch and maintenance mode, gateway/backend/counter store/IdP down
