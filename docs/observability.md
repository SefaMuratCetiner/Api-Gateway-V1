# Gateway observability

## Architecture

| Project | Responsibility |
|---|---|
| `ApiGateway.Abstractions` | Request features (`IGatewayRequestFeature`, `IUpstreamTimingFeature`, `ICorrelationIdFeature`), telemetry names, `IGatewayMetrics`, `IAuditLog` |
| `ApiGateway.Observability` | Provider-neutral: correlation, trace trust and sampling, metrics, redaction rules, IP anonymization, body capture, audit, usage, synthetic checks |
| `ApiGateway.Observability.Serilog` | Access log enrichment, redacting sink, logger configuration (sinks, retention, audit/usage sub-loggers) |
| `ApiGateway.Host/CompositionRoot` | Wires everything: options, Serilog, OpenTelemetry, middleware order, endpoints |

Libraries log through `ILogger<T>`; only the Serilog adapter references Serilog. No library registers itself.

## Listeners

| Listener | Port (default) | Serves |
|---|---|---|
| Public | `Listeners:PublicPorts` (8080) | Proxied routes only |
| Internal | `Listeners:InternalPort` (8081) | `/metrics`, `/internal/errors`, `/health/synthetic` |

Requests on the internal port never reach a proxy route. The internal listener is not traced or access-logged
unless `Observability:AccessLog:IncludeInternalListener` is set.

## Correlation and tracing

- **`X-Correlation-Id`**: an inbound value (or `X-Request-ID`) is accepted only if it is at most
  `Correlation:MaxLength` characters of `[A-Za-z0-9._:-]`. Otherwise the gateway creates a UUIDv7. The ID is
  forwarded upstream, echoed on every response and attached to spans and logs.
- **Trace trust** (`Tracing:TrustMode`):
  - `Always`: the inbound context is always trusted (development).
  - `Never`: every request starts a new trace that links to the inbound one (the link is added only when the
    inbound `traceparent` parses).
  - `TrustedNetworks`: trusted only from the CIDRs in `Tracing:TrustedNetworks`.

  The decision is made from the TCP peer, never from headers. When the context is untrusted, inbound
  `traceparent`, `tracestate` and `baggage` are not forwarded.
- **Response headers**: `X-Trace-Id`, `traceresponse` (W3C level 2) and `X-Correlation-Id` are on every
  response, including gateway errors.
- **Parent/child**: the gateway server span is the parent of the forwarder's client span, and YARP propagates it
  to the backend.
- **Span attributes**: `gateway.route_id`, `gateway.cluster_id`, `gateway.destination_id`, `gateway.client_id`,
  `gateway.tenant_id`, `gateway.correlation_id`, `error.code`, `gateway.upstream.wait_ms`.

### Sampling

| Stage | Behaviour |
|---|---|
| Head (`GatewaySampler`) | A trusted parent's decision is followed. New traces are sampled at `Tracing:SamplingRatio`. Anything not sampled is still *recorded* locally. |
| Local tail (`TailSamplingProcessor`) | Unsampled spans are held until the request ends. If it failed (5xx or error status) or took at least `Tracing:SlowThreshold`, the spans are exported; otherwise they are dropped. Memory is bounded by `TailBufferMaxTraces` and `TailBufferMaxSpansPerTrace`. |
| Collector (`deploy/observability/otel-collector.yaml`) | `tail_sampling` across gateway **and** backend spans. |

**Limitation:** when the gateway rescues an unsampled trace, the backend spans of that trace were not sampled,
because the outbound `traceparent` carried the unsampled flag. Use collector tail sampling with always-on backend
recording for complete error traces.

## Metrics

All gateway instruments come from meter `ApiGateway`, plus `ApiGateway.Errors` and `ApiGateway.Synthetic`. The
built-in ASP.NET Core, Kestrel, HttpClient and runtime meters are also collected. Prometheus names are shown below.

| Metric | Labels | Notes |
|---|---|---|
| `gateway_requests_total` | route, cluster, method, status class, client type, synthetic | RPS and error rate |
| `gateway_request_duration_seconds` | same | Total latency; p50/p95/p99 via `histogram_quantile` |
| `gateway_upstream_duration_seconds` | route, cluster | Upstream wait: from sending the request until the response headers arrive |
| `gateway_overhead_duration_seconds` | route, cluster | Latency added by the gateway (total minus upstream) |
| `gateway_request_body_size_bytes`, `gateway_response_body_size_bytes` | route | Counted while streaming |
| `gateway_errors_total` | `error_code`, status | Gateway-made errors only |
| `gateway_rejections_total` | `reason` (rate_limit, quota, token, shed, concurrency, blocked, maintenance), route | Reported through `IGatewayMetrics` |
| `gateway_destination_health_changes_total` | cluster, new health | Decorator on YARP's health updater |
| `gateway_cluster_destinations` | cluster, health | Observed from YARP state |
| `gateway_consumer_requests_total` | consumer (capped), status class | Per-consumer usage |
| `gateway_synthetic_checks_total`, `gateway_synthetic_duration_seconds` | probe, outcome | |
| `gateway_tracing_tail_decisions_total`, `gateway_tracing_tail_evictions_total` | outcome | Tail-sampling health |
| `http_server_request_duration_seconds` (ASP.NET Core) | + `gateway_route_id`, `gateway_cluster_id` | Enriched |

**Cardinality.** Only bounded values are used as labels: route IDs, cluster IDs, method (unknown methods become
`_OTHER`), status class, error code and client type. Raw paths and raw IDs are never labels.
- `CardinalityGuard` caps the distinct values per label (`Metrics:MaxLabelValues`) and maps the rest to `_other`.
  The consumer label has its own cap (`Metrics:MaxConsumerLabelValues`).
- OpenTelemetry's `CardinalityLimit` (`Metrics:CardinalityLimit`) is the backstop for every stream.

## Logs

### Access log

The access log has one event per request (`UseSerilogRequestLogging` + `AccessLogEnricher`). Its fields are:
- Request: `RequestMethod`, `RequestPath` (no query), `StatusCode`, `Elapsed`.
- Routing: `RouteId`, `ClusterId`, `DestinationId`, `UpstreamMs`.
- Caller: `ClientIp` (anonymized), `ClientId`, `TenantId`, `ClientType`.
- Correlation and errors: `CorrelationId`, `TraceId` and `SpanId` (native in Serilog), `ErrorCode`.
- Transfer: `RequestBytes`, `ResponseBytes`, `Protocol`, `Host`.
- Optional: an allow-listed `RequestHeaders` set and the redacted `QueryString` when
  `AccessLog:IncludeQueryString` is set.

Level: 5xx is Error, 4xx is Warning, everything else is Information.

### Body logging

Body logging is off unless a route opts in through its metadata:

```json
"Metadata": {
  "Observability.BodyLogging": "true",
  "Observability.BodyLogging.MaxBytes": "2048",
  "Observability.BodyLogging.ContentTypes": "application/json",
  "Observability.BodyLogging.Request": "false",
  "Observability.BodyLogging.Response": "true"
}
```

- Bodies are teed while streaming: only the first `MaxBytes` (capped by `BodyLogging:HardMaxBytes`) are kept and
  nothing is buffered.
- JSON values of sensitive properties are masked, and other text is pattern-scrubbed.
- Events go to category `ApiGateway.Body`.
- Unknown or invalid `Observability.*` keys reject the proxy configuration, and the running configuration is
  kept.

### Redaction

Redaction applies to **all** logs, framework logs included.

- **Sensitive names** are always masked, whether they appear as a header, query parameter, JSON property or log
  property. They include `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`,
  `Idempotency-Key`, `password`, `secret`, `token`, `access_token`, `refresh_token`, `client_secret` and similar,
  plus any name *containing* `password`, `secret`, `token`, `apikey`, `authorization`, `cookie`, `credential` or
  `privatekey`. Add your own names with `Redaction:AdditionalSensitiveNames`.
- **Patterns** are scrubbed from any string: bearer/basic credentials, JWTs, and `key=value` secrets.
- **Where it runs:** `RedactingSink` wraps every sink, including test or DI-registered ones. It redacts
  properties, nested values, pre-formatted message templates and exception text (`ScrubbedException`). It runs
  inside the async wrapper, so it costs nothing on the request path.

### Client IP and retention

| `ClientIp:Mode` | Logged value | Default in |
|---|---|---|
| `None` | as is | Development |
| `Truncate` | /24 (IPv4), /48 (IPv6) | Production |
| `Hash` | `h:` + HMAC-SHA256 (16 hex), key from the secret store | — |
| `Erase` | nothing | — |

Retention is configured per sink in `Observability:Logging`:

| Sink | Retention |
|---|---|
| Main file (`File:RetainedDays`, `File:RetainedFileCountLimit`) | 14 days |
| Audit (`ApiGateway.Audit`) | 400 days |
| Usage (`ApiGateway.Usage`) | 90 days |

The main retention is also exported as the resource attribute `gateway.log.retention_days`, so a central log
store can apply a matching policy.

Every sink sits behind a bounded async buffer (`AsyncBufferSize`). When a sink stalls, new events are dropped and
requests are never blocked.

## Audit log

`AuditLog` writes to `ApiGateway.Audit`. Each entry carries the actor, action, target, before/after SHA-256
fingerprints, a sequence number and a hash chained to the previous entry. A deleted or edited line therefore
breaks the chain.

Audited today:
- Proxy configuration: loaded, applied, rejected, and per-route/cluster added, changed and removed
  (`ProxyConfigAuditListener`, a YARP `IConfigChangeListener`).
- Hot reloads of `Errors`, `Observability` and `Observability:Logging` options.

Configuration contents are never logged, only fingerprints. The admin API will write its changes with the
authenticated principal as actor.

## Usage analytics

`UsageAggregator` counts requests, 4xx, 5xx, bytes in and bytes out per consumer and route. It flushes every
`Usage:FlushInterval` to each `IUsageSink`; the default `LoggingUsageSink` writes to `ApiGateway.Usage`.
- Synthetic traffic is excluded.
- Memory is bounded by `Usage:MaxEntries`; overflow is counted under `_other`, so totals stay correct.

## Synthetic checks

`Observability:Synthetic:Probes` lists URLs **through the public listener**. `SyntheticProbeService` calls each
one on its interval with an HMAC-signed `X-Synthetic-Check` header (key: `Synthetic:SigningKey`, from the secret
store).
- The gateway honours the header only if the signature is valid and fresh, and it always strips the header
  before forwarding.
- Results go to `gateway_synthetic_*` metrics and logs, and to `GET /health/synthetic` on the internal listener.
- That health check is **not** part of readiness.

## Alerts and dashboards

- `deploy/observability/prometheus/alerts.yml`:
  - 5xx ratio per route and gateway error codes.
  - Clusters without healthy destinations and flapping destinations.
  - Failing synthetic checks.
  - p99 latency and gateway overhead.
  - Rejection, token-rejection and shedding spikes.
  - Tail-buffer evictions.
- `deploy/observability/grafana/gateway-overview.json`:
  - RPS, 5xx ratio, p50/p95/p99, upstream vs overhead.
  - Errors by code and rejections.
  - Destination health, top consumers.
  - Synthetic checks and tail sampling.

### High 5xx rate

1. Split by `error_code` in `gateway_errors_total`. A `gateway.*` code means the gateway made the error. If there
   is no gateway code, the backend returned the 5xx.
2. For `upstream_unreachable` and `no_healthy_destination`, check `gateway_cluster_destinations` and the
   destination health-change log events.
3. Pick a `traceId` from an error response or the access log; error traces are always exported.
