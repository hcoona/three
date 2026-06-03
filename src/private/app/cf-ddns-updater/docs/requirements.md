# Cloudflare DDNS Updater Requirements

## Status

This document freezes the minimum viable product scope for a small Cloudflare DDNS updater implemented in C# and published with NativeAOT.

## Goals

- Provide a minimal, deterministic DDNS updater for Cloudflare DNS records.
- Keep the runtime model simple: execute once, reconcile DNS state, and exit.
- Rely on an external scheduler instead of embedding a daemon loop.
- Produce self-contained NativeAOT binaries for Linux x64 and Windows x64.

## Non-Goals

- No built-in scheduler, daemon loop, cron parser, or background service mode.
- No provider abstraction beyond Cloudflare Trace.
- No proxied record support.
- No WAF list management.
- No notifications, heartbeat integrations, or webhooks.
- No cleanup-on-stop behavior.
- No legacy JSON configuration format.
- No configurable TTL in the MVP.
- No IP source configuration in the MVP.
- No per-family or per-domain provider configuration in the MVP.

## Runtime Model

The executable runs a single update cycle:

1. Read configuration from environment variables.
2. Resolve the current public IPv4 address with Cloudflare Trace.
3. Resolve the current public IPv6 address with Cloudflare Trace unless IPv6 is disabled.
4. Resolve the Cloudflare zone for each configured domain.
5. Query the current DNS state.
6. Reconcile the matching A and, when enabled, AAAA records to the detected public addresses.
7. Exit with a success or failure code.

Periodic execution is owned by the host environment. On Linux, the supported deployment model is a `systemd` service triggered by a `systemd` timer. Windows x64 is a supported NativeAOT publish target, but Windows scheduling integration is outside the MVP scope.

## Configuration

The MVP uses environment variables only.

| Variable                                      | Required | Description                                                          |
| --------------------------------------------- | -------- | -------------------------------------------------------------------- |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN`    | Yes      | Cloudflare API token used for zone lookup and DNS record updates.    |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS`      | Yes      | Comma-separated fully qualified domain names to update.              |
| `HCOONA_CLOUDFLARE_DDNS_UPDATER_DISABLE_IPV6` | No       | When set to `true`, disables IPv6 discovery and AAAA reconciliation. |

Example:

```env
HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN=...
HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS=home.example.com,photos.example.com
```

The MVP does not expose `ZONE_ID`, `type`, `source`, `proxied`, or `ttl` configuration.

## Cloudflare API Permissions

The API token must be able to:

- Read zones so the updater can resolve the zone for each configured domain.
- Read DNS records in the resolved zones so the updater can detect no-op updates, CNAME conflicts, and duplicate A records.
- Edit DNS records in the resolved zones.

If zone lookup is not permitted, the updater must fail with an actionable error. The MVP intentionally does not require users to configure zone IDs.

## IP Detection

The only supported IP detection mechanism is Cloudflare Trace.

- The updater must perform a Cloudflare Trace request that is explicitly constrained to IPv4.
- Unless IPv6 is disabled, the updater must perform a Cloudflare Trace request that is explicitly constrained to IPv6.
- If a trace request cannot be constrained to the requested address family, that family must fail.
- Returned addresses must parse as valid public IP addresses for the requested family.
- Private, loopback, link-local, multicast, unspecified, and otherwise non-public addresses must be rejected.
- The inferred DNS record type is `A` for IPv4 and `AAAA` for IPv6.

If IPv6 is enabled and IPv6 discovery fails, that failure must be reported and must contribute to a non-zero process exit code.

## DNS Reconciliation

For each domain in `HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS`, the updater must reconcile the IPv4 target and, unless IPv6 is disabled, the IPv6 target.

For each enabled target, the updater must:

1. Canonicalize the domain name for comparison and Cloudflare API calls.
2. Resolve the best matching Cloudflare zone by walking suffixes from the most specific candidate to the least specific candidate and selecting the first exact zone match visible to the API token.
3. Query existing DNS records for the exact canonical name.
4. Fail closed if a CNAME record exists at the same name.
5. Query existing records for the inferred record type at the exact canonical name.
6. Fail closed if more than one matching record of that type exists.
7. Create a DNS-only record if none exists.
8. Update the existing record only when the content differs from the detected public address.
9. Leave the record unchanged when it already matches.

The updater must not delete duplicate records, delete stale records, or attempt automatic conflict resolution. A failed target must not prevent other independent targets from being attempted, but any target failure must cause a non-zero process exit code after the run completes.

At a target owner name, only a CNAME record and duplicate records of the inferred type are conflicts. Unrelated DNS record types must be preserved and must not block reconciliation.

## Proxied Records

The MVP does not support Cloudflare proxied records.

- New records must be created as DNS-only records.
- Existing records must be DNS-only records.
- If the matching record is proxied, the updater must fail closed with an actionable error.
- The updater must not expose a `PROXIED` configuration option.

## TTL Behavior

The MVP does not expose TTL configuration.

- Existing record TTL must not be changed as part of an IP-only update.
- New records should use Cloudflare automatic TTL.

## Update Semantics

Updates must be idempotent and conservative.

- If the current A record content equals the detected IPv4 address, no Cloudflare write should be issued.
- If the current AAAA record content equals the detected IPv6 address, no Cloudflare write should be issued.
- If an update is required, the request must preserve all existing record fields except the content value being reconciled and any fields Cloudflare requires to be explicitly repeated in the request.
- The updater must use standard .NET logging mechanisms and produce clear logs for no-op, create, update, and failure outcomes.

## Error Handling

The updater must fail closed on ambiguous or unsafe state:

- Invalid or missing configuration.
- Invalid domain names.
- Zone lookup failure.
- Multiple candidate zones for a domain.
- CNAME conflict at the target name.
- Multiple records of the inferred type at the target name.
- Proxied target record.
- Invalid or non-public Cloudflare Trace IP result.
- Cloudflare API authorization or permission failures.

Transient network failures, HTTP 429 responses, and Cloudflare 5xx responses may be retried with a small bounded retry policy and jitter. Non-transient errors must not be retried.

## Packaging

The implementation language is C#.

NativeAOT publishing is required for:

- Linux x64
- Windows x64

The project should be structured so the application can be published as a standalone executable for each target runtime.

## Deployment Artifacts

The MVP should include Linux deployment examples for:

- A `systemd` service that runs one update cycle.
- A `systemd` timer that triggers the service periodically.

Windows service or Windows Task Scheduler integration is outside the MVP scope.
