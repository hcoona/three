# Cloudflare DDNS Updater Low-Level Design

## Status

This document defines the implementation-level design for the Cloudflare DDNS Updater MVP. It is intentionally narrow: it specifies concrete components, data flow, validation rules, and failure handling, but does not expand the product scope beyond the approved requirements and high-level design.

## 1. Design Goals

The implementation must:

1. Run exactly one reconciliation cycle per process invocation.
2. Use Microsoft.Extensions.Configuration for configuration loading.
3. Read configuration from environment variables only.
4. Resolve public IPv4 and optional IPv6 through Cloudflare Trace only.
5. Resolve Cloudflare zones from user-supplied domain names.
6. Reconcile A and AAAA records conservatively and fail closed on unsafe state.
7. Preserve unrelated DNS records at the same owner name.
8. Produce clear structured logs and a non-zero exit code when any target fails.
9. Publish as NativeAOT for Linux x64 and Windows x64.
10. Emit .NET-native traces for the full reconciliation flow so performance regressions can be diagnosed later.

## 2. Runtime Structure

The executable is a console application built around a single host startup path:

1. Build a minimal host.
2. Load configuration from environment variables.
3. Validate options eagerly.
4. Create typed HTTP clients.
5. Resolve public IP targets.
6. Reconcile each configured domain independently.
7. Emit a final summary and exit.

The process is not stateful across runs. No scheduler, background worker, or daemon loop is part of the application.

## 3. Configuration Model

### 3.1 Configuration Source

The application uses `Microsoft.Extensions.Configuration.EnvironmentVariables` with the prefix `HCOONA_CLOUDFLARE_DDNS_UPDATER_`.

No JSON files, command-line overrides, user secrets, or alternative providers are part of the MVP.

### 3.2 Environment Variables

| Variable                                      | Required | Binding                         |
| --------------------------------------------- | -------- | ------------------------------- |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN`    | Yes      | `CloudflareOptions.ApiToken`    |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS`      | Yes      | `CloudflareOptions.DomainsCsv`  |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_DISABLE_IPV6` | No       | `CloudflareOptions.DisableIpv6` |

The `DOMAINS` value is a comma-separated list of fully qualified domain names.

### 3.3 Binding Rules

1. Trim leading and trailing whitespace from each domain entry.
2. Drop empty entries after trimming.
3. Deduplicate domains using ordinal, case-insensitive comparison on the canonical domain form.
4. Reject a configuration that produces zero usable domains.
5. Parse `DISABLE_IPV6` as a strict boolean value.
6. Treat any missing required value as a validation failure, not as an empty default.

Canonical domain form means:

- strip a trailing dot
- convert to ASCII with IDNA
- compare with ordinal lower-case semantics
- reject values that are not valid DNS hostnames after normalization

### 3.4 Options Objects

Use a small options model rather than binding directly into business services:

- `CloudflareOptions`
    - `string ApiToken`
    - `string DomainsCsv`
    - `bool DisableIpv6`
- `CloudflareApiOptions`
    - `Uri BaseAddress`
    - `TimeSpan Timeout`

The Trace endpoints are internal constants owned by the IP discovery service. They are not configurable and do not appear in the options model.

The configuration surface remains env-var only, but this separation keeps HTTP concerns out of domain reconciliation.

## 4. Component Boundaries

### 4.1 Bootstrap Layer

Responsibilities:

- Create the host.
- Register options, HTTP clients, logging, and application services.
- Execute the runner.
- Translate unexpected top-level failures into exit code 1.

This layer should stay thin. It must not contain reconciliation logic.

### 4.2 `ConfigurationValidator`

Responsibilities:

- Validate required values.
- Canonicalize and deduplicate domain input.
- Enforce supported domain syntax.
- Reject empty domain input.

Validation should happen before any network call.

### 4.3 `TraceIpDiscoveryService`

Responsibilities:

- Request IPv4 and IPv6 addresses from Cloudflare Trace.
- Ensure each request is family-specific.
- Parse the response into a public IP address.
- Reject any non-public, malformed, or wrong-family result.

The service exposes a family-targeted API:

```csharp
ValueTask<IPAddress> DiscoverAsync(AddressFamily family, CancellationToken cancellationToken)
```

The implementation must not infer IP family from an unconstrained response.

### 4.4 `CloudflareZoneResolver`

Responsibilities:

- Normalize the requested domain name.
- Canonicalize using the same rules as configuration validation.
- Walk suffix candidates from the most specific name to the least specific name.
- Query Cloudflare zones until the first exact canonical match visible to the token is found.
- Fail closed if a candidate suffix returns more than one exact match.
- Surface ambiguity, authorization, or absence as a hard failure.

The resolver returns a resolved zone identifier plus the canonical zone name used for downstream queries.

### 4.5 `CloudflareDnsRecordClient`

Responsibilities:

- Read exact-name DNS records in a resolved zone.
- Enumerate every page for the exact owner name before conflict evaluation.
- Create DNS-only records.
- Update existing records while preserving non-owned fields.
- Provide typed methods for record lookup, create, and update.

The client should stay API-shaped but not reconciliation-aware.

### 4.6 `ReconciliationRunner`

Responsibilities:

- Iterate through domains.
- Reconcile IPv4 and IPv6 targets independently.
- Continue after per-target failures.
- Aggregate results into a final exit status.

This is the main orchestration service.

### 4.7 `Tracing`

Responsibilities:

- Create a single application `ActivitySource` for reconciliation spans.
- Start a root activity for each process invocation.
- Start child activities for configuration loading, IP discovery, zone resolution, DNS fetches, and DNS mutations.
- Preserve `Activity.Current` across downstream HTTP calls so the .NET HTTP stack can propagate trace context automatically.
- Add trace tags for domain, zone, record type, target family, outcome, and Cloudflare request identifiers such as `cf-ray` when available.

Tracing is implementation-internal, but it is mandatory because the updater must remain diagnosable when latency or throughput problems appear later.

## 5. Data Flow

### 5.1 Startup Flow

1. Host startup loads environment variables through the environment-variable configuration provider.
2. Options are bound and validated.
3. The runner attempts IPv4 discovery and records the result.
4. If IPv6 is enabled, the runner attempts IPv6 discovery and records the result.
5. The runner marks any failed discovery as a target failure, even if the other family succeeds.
6. The runner continues with any family whose discovery succeeded.
7. For each domain, the runner resolves the zone.
8. For each enabled family with a discovered IP, the runner queries exact-name DNS records.
9. The runner applies create, update, or no-op semantics.
10. The runner emits a run summary and exit code.

### 5.2 Per-Domain Flow

For each configured domain, the system resolves two independent targets:

- `domain + IPv4 -> A`
- `domain + IPv6 -> AAAA` when IPv6 is enabled

Each target is isolated so one target failure does not cancel the others.

## 6. Reconciliation Algorithm

For each `(zone, canonicalName, recordType, detectedIp)` tuple:

1. Query all records at the exact owner name.
2. If any CNAME exists at that name, fail closed.
3. Filter records of the inferred type.
4. If more than one record of that type exists, fail closed.
5. If no record exists, create a DNS-only record with Cloudflare automatic TTL.
6. If one record exists and content matches `detectedIp`, treat as no-op.
7. If one record exists and content differs, update only the content and any fields Cloudflare requires to be repeated.
8. Preserve unrelated record types at the same name.

### 6.1 Conflict Rules

The following are treated as unsafe and must fail the target:

- CNAME at the same owner name.
- More than one record of the inferred type.
- Proxied matching record.
- Unresolvable zone selection.
- Invalid or non-public discovered IP.

### 6.2 TTL and Ownership

- New records use Cloudflare automatic TTL.
- Existing records keep their TTL exactly; TTL is never rewritten as part of an IP-only update.
- The updater does not own unrelated record types and must leave them untouched.

### 6.3 Record Enumeration and Update Shape

The record client must page through the full Cloudflare response set for the exact owner name before the runner applies conflict checks.

For a create request, the client sends `name`, inferred `type`, `content`, and the DNS-only/automatic-TTL defaults required by Cloudflare.

For an update request, the client sends a payload built from the fetched record snapshot and changes only `content`. All other mutable fields returned by the GET response are copied verbatim so that no unrelated DNS metadata is cleared; this includes TTL and proxied state, which must remain DNS-only and unchanged for IP-only reconciliation.

## 7. HTTP and API Design

### 7.1 HttpClient Usage

Use `IHttpClientFactory` and typed clients for external calls:

- `CloudflareTraceClient`
- `CloudflareApiClient`

This keeps retries, timeouts, and headers centralized and NativeAOT-friendly.

### 7.2 Request Defaults

All outbound requests should include:

- A bounded timeout.
- An explicit user-agent string.
- Authorization headers only for Cloudflare API calls.

### 7.3 Retry Policy

Retry only transient failures:

- network interruptions
- HTTP 429
- HTTP 5xx

Do not retry validation failures, parse failures, authorization failures, or conflict failures.

Use a small bounded retry count with jittered backoff.
Honor `Retry-After` when Cloudflare returns it.

## 8. Logging and Exit Codes

### 8.1 Logging

Use `ILogger<T>` throughout. The application must log:

- Configuration validation failures
- Public IP discovery success and failure
- Zone resolution success and failure
- DNS no-op, create, update, and conflict outcomes
- Per-domain summary
- Final process summary

Structured logging is preferred over raw string concatenation.
Tracing complements logging; logs carry human-readable outcomes, while spans carry timing and causality.

### 8.2 Exit Codes

- `0` — all enabled targets succeeded or were already in sync
- `1` — one or more targets failed, or an unexpected fatal startup error occurred

The runner also emits a per-target summary with success, failure, and no-op counts so partial success remains visible even though the process exit code stays binary.

## 9. Error Handling Strategy

The design prefers explicit failure propagation over silent fallback.

1. Configuration errors fail before any network activity.
2. Discovery and API clients raise typed failures for unsafe state.
3. The runner catches errors at the target boundary so it can continue with remaining independent targets.
4. The outer host boundary catches only unexpected fatal exceptions and converts them into a fatal exit.

No broad recovery paths are allowed for ambiguous DNS state.

## 10. NativeAOT Considerations

The code must remain compatible with trimming and NativeAOT:

- Prefer source-generated or direct binding patterns.
- Avoid reflection-heavy serialization and runtime type discovery.
- Keep DTOs explicit and closed.
- Avoid dynamic proxies and runtime code generation.
- Keep logging and options binding conventional.
- Keep tracing explicit through `ActivitySource` rather than hidden ad hoc timers.

Configuration binding must stay simple: scalar values are bound directly, and the comma-separated domain list is parsed explicitly rather than relying on array binding behavior.

Any new dependency must be checked for trimming safety before adoption.

## 11. Testing Strategy

### 11.1 Unit Tests

Cover these areas first:

- Configuration binding and validation
- Domain canonicalization
- Trace response parsing
- Zone suffix selection
- DNS record conflict detection
- Reconciliation create/update/no-op branches

### 11.2 Contract Tests

Add focused tests for HTTP request composition:

- Trace requests are family-specific
- Cloudflare API requests preserve required fields
- Record updates do not rewrite unrelated fields

### 11.3 Behavior Tests

Cover the orchestration layer with fake clients to verify:

- Independent target failures do not short-circuit other targets
- Final exit code is non-zero when any target fails
- Final summary matches per-target outcomes

## 12. Implementation Sequence

1. Add configuration binding and validation.
2. Implement trace-based IP discovery.
3. Implement zone resolution.
4. Implement record read/create/update clients.
5. Implement reconciliation orchestration.
6. Add logging and final summary output.
7. Add unit and contract tests.
8. Add Linux `systemd` service and timer examples, while keeping Windows scheduling integration outside the MVP.

## 13. Scope Guardrails

The implementation must keep the following out of scope:

- Built-in scheduling or daemon mode
- Cron parsing
- Cleanup-on-stop behavior
- Notifications, heartbeat integrations, and webhooks
- WAF list management
- Legacy JSON configuration
- Windows scheduling integration
- Provider fallback beyond Cloudflare Trace

## 14. Design Decisions at a Glance

| Decision             | Choice                              | Rationale                                                     |
| -------------------- | ----------------------------------- | ------------------------------------------------------------- |
| Configuration source | Environment variables only          | Matches the MVP scope and avoids extra configuration surfaces |
| Config library       | Microsoft.Extensions.Configuration  | Keeps binding and validation standard and testable            |
| Runtime model        | Single-run CLI                      | Matches the external-scheduler deployment model               |
| IP source            | Cloudflare Trace only               | Avoids provider fallback and keeps behavior deterministic     |
| Zone input           | User-supplied domain names          | Removes zone ID management from the MVP                       |
| Conflict policy      | Fail closed                         | Prevents unsafe DNS mutations                                 |
| Record ownership     | Only A/AAAA at the exact owner name | Preserves unrelated DNS records                               |
| Update semantics     | Preserve non-owned fields           | Minimizes accidental drift                                    |
| AOT strategy         | Trim-safe, explicit DTOs            | Supports NativeAOT publishing                                 |
