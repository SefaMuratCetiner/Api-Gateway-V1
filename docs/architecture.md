# Architecture

How the code is organized, which patterns it uses, and the recipes for extending it. The rules here are enforced
by tests: `tests/ApiGateway.UnitTests/Architecture/ConventionTests.cs` and
`tests/ApiGateway.IntegrationTests/GatewayCompositionTests.cs`. Change a rule here and in its test together.

## Projects

| Project | Responsibility | May reference |
|---|---|---|
| `ApiGateway.Abstractions` | Contracts shared by every library: request features, the error contract, per-route policy contracts, telemetry names, audit and metrics ports. No package references, so backends can adopt the error contract | nothing |
| `ApiGateway.Errors` | Gateway-made errors: problem details, the error catalog, localization, forwarder-error mapping | Abstractions |
| `ApiGateway.Proxy` | Everything YARP-specific on the request path: routing conventions, normalization, headers, limits, lifecycle, tenancy, load balancing, rewriting, CORS, compression, contract validation, aggregation | Abstractions |
| `ApiGateway.Observability` | Provider-neutral observability: correlation, tracing, metrics, redaction, body logging, audit, usage, synthetic checks | Abstractions |
| `ApiGateway.Observability.Serilog` | The Serilog adapter: access log, redacting sink, logger configuration | Abstractions, Observability |
| `ApiGateway.Host` | The deployable: settings, features, middleware order, endpoints | everything |

Libraries expose constructor-injected types and **never register themselves**. Only the Host knows concrete
types and decides how they are wired.

## The Host: settings, features, order

The Host splits composition into three roles, so each question has one place to look:

| Question | Where |
|---|---|
| What can be configured, and how is it validated? | `CompositionRoot/GatewaySettings.cs`: every configuration section, bound, validated and registered once |
| What does feature X register? | `Features/XFeature.cs`: one `IGatewayFeature` per capability, service registrations only |
| In which order does anything run? | `CompositionRoot/GatewayCompositionRoot*.cs`: the feature list and YARP's config filters and transforms (`.cs`), the middleware order (`.Pipeline.cs`), the proxy pipeline and the endpoints on each listener (`.Endpoints.cs`) |

Anything whose order matters stays in the composition root. A gateway is mostly defined by its order, and it
must stay readable top to bottom in one place. Features therefore never call `UseMiddleware`, `Map*`,
`AddTransforms` or `AddConfigFilter`.

Startup, in `Program.cs`:

1. `new GatewayCompositionRoot(builder)` binds and validates every section (`GatewaySettings`). Bad
   configuration fails here, before anything starts.
2. `ComposeServices()` registers shared infrastructure, then YARP with its ordered filters and transforms,
   then each feature in `Features` order.
3. `ComposePipeline(app)` adds the middleware, outermost first.
4. `ComposeEndpoints(app)` adds the proxy pipeline, composite endpoints and the internal endpoints.

## Patterns in the code

| Pattern | Where | Why |
|---|---|---|
| Composition root | `GatewayCompositionRoot` | One place decides which implementations are used |
| Feature module | `Features/*Feature.cs` | Adding or removing a capability touches one file and one line |
| Pipeline (chain of responsibility) | Middleware in `.Pipeline` and `.Endpoints` | Each concern is one step that can answer or pass the request on |
| Options | `XOptions` + validators, bound by `GatewaySettings` | Typed configuration that fails at startup, revalidated on reload |
| Policy object | `XPolicy` + `XMetadataParser` + `RoutePolicyResolver<T>` | Per-route settings parsed once per route config, validated when YARP loads it |
| Strategy | `IClientIpAnonymizer`, `WeightedLoadBalancingPolicy`, `IUsageSink` | Behavior chosen by configuration, no `switch` in the hot path |
| Decorator | `GatewayBuilder.Decorate` (`ResilientDestinationResolver`, `DestinationHealthChangeRecorder`), `RedactingSink`, `ObservedReadStream` | Add behavior around a component without changing it |
| Ports and adapters | Contracts in `Abstractions`; Serilog behind `ILogger<T>` | Libraries do not depend on the logging provider or on each other |
| Contributor | `IErrorCatalogContributor` | Each library adds its own error codes without a central list |
| Request features | `Abstractions/Features/XFeature.cs` | Per-request state shared across libraries through `HttpContext.Features` |

## Recipes

### Add a configuration section

1. Create `XOptions.cs` next to the feature that uses it, with `public const string SectionName`. Sub-sections
   get their own `YOptions.cs`.
2. Add validators in the same file: `XOptionsValidator` (`[OptionsValidator]`, from `[Range]`, `[Required]`, …)
   and, for checks attributes cannot express, `XOptionsConsistencyValidator`.
3. Add one line to `GatewaySettings`: `X = Add(XOptions.SectionName, new XOptionsValidator(), …)`.
4. Add the defaults to `appsettings.json` and document the section.

### Add a per-route setting (route metadata)

1. Create `XPolicy.cs` in the feature folder: the `XPolicy` record and `XMetadataParser : IRouteMetadataParser<XPolicy>`.
   Keys are `<FeatureFolder>.<Setting>` (e.g. `Compression.Enabled`); `Prefix` returns `"<FeatureFolder>."`; on/off
   switches end in `.Enabled`. Validate every key and call `RouteMetadata.ValidateKnownKeys` so typos are rejected.
2. In the feature's `Register`: `gateway.AddRoutePolicy<XPolicy, XMetadataParser>()`.
3. Read it where needed: `RoutePolicyResolver<XPolicy>` inside `Proxy`, `IRoutePolicyResolver<XPolicy>` elsewhere.
4. When renaming a key, keep the old one in a `RenamedKeys` map checked with `RouteMetadata.ValidateRenamedKeys`,
   so old configuration fails loudly instead of being ignored.

### Add a feature

1. Put the library code in its feature folder (`src/ApiGateway.<Library>/<Feature>/`).
2. Create `src/ApiGateway.Host/Features/XFeature.cs`: `internal sealed class XFeature : IGatewayFeature`
   registering its services through `gateway.Services`, `gateway.AddRoutePolicy`, `gateway.Decorate`, or
   `gateway.Proxy` (for YARP extensions whose order does not matter).
3. Add `new XFeature()` to `GatewayCompositionRoot.Features`, after any feature whose services it decorates.
4. If it has middleware, endpoints, transforms or config filters, add them at the right position in the
   composition root, with a comment saying why they go there.

### Add a middleware

Create `XMiddleware.cs` with a primary constructor `(RequestDelegate next, …)` and `InvokeAsync(HttpContext)`. Add
it to `.Pipeline.cs` (before routing) or to the proxy pipeline in `.Endpoints.cs` (after a route matched), and
update the order table in `docs/request-path.md`.

### Share per-request state

Create `Abstractions/Features/XFeature.cs` holding `IXFeature`, `XFeature` and `XHttpContextExtensions`
(`GetX`/`FindX`). Never pass request state through `HttpContext.Items`.

### Add a metric

Instruments are created only in `*Metrics` classes (or a `*Gauge` class whose only job is one observable gauge),
which services inject. Names start with `gateway.`; only bounded values become labels (see
[observability.md](observability.md)).

### Add an error code

See [errors.md](errors.md#adding-codes).

## Conventions

**Files and namespaces.** A file is named after its main type, and its namespace follows its folder. Only these
may share the main type's file:

- an options class's validators and its enums;
- a per-route policy record and its metadata parser (`XPolicy.cs`);
- a request feature's interface, implementation and `HttpContext` extensions (`XFeature.cs`);
- small records or enums that only exist as a parameter or result of the main type (e.g. `AuditEvent` for
  `IAuditLog`, `TailSamplingLimits` for `TailSamplingProcessor`).

**Names.**

| Suffix | Means |
|---|---|
| `*Options` | Bound configuration (a section or part of one), and nothing else |
| `*OptionsValidator` / `*OptionsConsistencyValidator` | Generated from attributes / hand-written cross-field checks |
| `*Policy` | A per-route record parsed from metadata (YARP's `ILoadBalancingPolicy` implementations keep YARP's name) |
| `*MetadataParser` | Turns route metadata into a `*Policy` |
| `*Middleware`, `*TransformProvider`, `*Service` (hosted), `*Feature` | What ASP.NET Core, YARP or the Host does with the type |
| `*Metrics`, `*Gauge` | Owns metric instruments |
| `*Args` | Per-call arguments (e.g. `ProblemWriteArgs`) |

Services that make a decision get a role name (`SensitiveDataClassifier`, `TraceTrustEvaluator`), not `*Policy`.

**Text.** American English everywhere (code, comments, docs).

**Code style** is in `.editorconfig` and enforced by the build (`EnforceCodeStyleInBuild`); CI treats warnings as
errors.

## What the tests check

| Rule | Test |
|---|---|
| Projects reference only the projects below them; Abstractions has no packages | `Projects_reference_only_the_projects_below_them` |
| Request feature interfaces live in `Abstractions/Features` | `Request_features_live_in_Abstractions_Features` |
| Only configuration is named `*Options` | `Only_bound_configuration_is_named_Options` |
| Validator names match their kind | `Validators_are_named_after_their_options_and_kind` |
| Metadata keys start with their feature folder | `Metadata_keys_start_with_their_feature_folder` |
| Instruments only in `*Metrics` / `*Gauge` classes | `Instruments_are_created_only_in_metrics_classes` |
| File named after a declared type; namespace follows folder | `Files_are_named_after_a_type_they_declare_and_namespaces_follow_folders` |
| Features never order anything | `Features_register_services_and_leave_ordering_to_the_composition_root` |
| American spelling | `Text_uses_American_spelling` |
| Every feature is in `GatewayCompositionRoot.Features` exactly once | `Every_feature_is_registered_exactly_once` |
| Features are `internal sealed`, in `ApiGateway.Host.Features`, named `*Feature` | `Features_are_internal_sealed_and_live_in_Features` |
