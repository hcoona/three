# Cloudflare DDNS Updater High-Level Design

## Design Status

This document captures the confirmed high-level design for the Cloudflare DDNS Updater MVP. It intentionally stays above class, method, protocol-detail, and API-shape design.

## Confirmed System Boundary

The application is a single-run DDNS reconciler:

- The application reads configuration, discovers current public IP addresses, reconciles Cloudflare DNS records, logs outcomes, and exits.
- Cloudflare owns zone metadata, DNS record state, and DNS record mutations.
- Scheduling is external to the application. Linux scheduling is represented by a `systemd` service and timer example.
- Windows x64 is a required NativeAOT publish target, but Windows scheduling integration is outside the MVP.

The application must not become a daemon, scheduler, background service, notification agent, or cleanup service.

## High-Level Components

### Configuration

Configuration is environment-variable only and uses the `HCOONA_CLOUDFLARE_DDNS_UPDATER_` prefix to avoid global namespace pollution.

The high-level configuration surface is limited to:

- API token
- Domains to update
- Optional IPv6 disable switch

The MVP does not expose zone IDs, record types, IP sources, proxied mode, or TTL settings.

### IP Discovery

IP discovery uses Cloudflare Trace only.

The application discovers:

- A public IPv4 address for A record reconciliation.
- A public IPv6 address for AAAA record reconciliation unless IPv6 is disabled.

IPv4 and IPv6 discovery are separate high-level targets. Each Cloudflare Trace request must be explicitly constrained to the requested address family. Each discovery result must match the expected address family and must be public.

### Cloudflare Discovery

The user provides domain names, not zone IDs.

For each configured domain, the application discovers the best matching Cloudflare zone by normalizing the domain name, walking suffixes from most specific to least specific, and selecting the first exact zone match visible to the API token. The application then reads the relevant DNS records. If zone discovery is unavailable, ambiguous, or unauthorized, the affected target fails.

### Reconciliation

The application reconciles DNS-only records conservatively:

- IPv4 maps to an A record.
- IPv6 maps to an AAAA record.
- Existing matching records are updated only when their content differs from the detected public IP.
- Missing records are created as DNS-only records.
- Proxied records, CNAME conflicts, and duplicate same-type records fail closed.
- Unrelated record types at the same owner name are preserved and do not block reconciliation.
- New records are created with Cloudflare automatic TTL.
- IP-only updates preserve existing TTL and other non-owned record fields.

The application does not delete records, does not delete duplicate or stale records, and does not attempt automatic conflict resolution.

Failures are target-scoped where possible. The application must attempt remaining independent targets, then return a non-zero exit code after the run completes if any target failed.

### Observability and Logging

Observability uses standard .NET logging abstractions. The CLI host may route those logs to console output, but the implementation must not rely on ad hoc console-only logging.

The MVP requires clear console-oriented logging for:

- Configuration validation failures
- IP discovery results and failures
- Zone discovery failures
- DNS no-op, create, update, and conflict outcomes
- Final run summary

The MVP does not include notifications, heartbeat integrations, webhooks, metrics endpoints, or external observability backends.

### Deployment

The implementation is C# and must support NativeAOT publishing for:

- Linux x64
- Windows x64

Linux deployment examples include:

- A `systemd` service that runs one update cycle.
- A `systemd` timer that triggers the service periodically.

Windows deployment is limited to producing a Windows x64 NativeAOT executable.

## Confirmed High-Level Flow

1. Read and validate environment configuration.
2. Discover public IPv4 and, unless disabled, public IPv6 via Cloudflare Trace.
3. For each domain, discover the matching Cloudflare zone.
4. For each enabled address family, read the exact DNS state for the domain and inferred record type.
5. Reconcile missing or stale DNS-only records conservatively.
6. Log target-level outcomes and exit non-zero if any target failed.

## Explicitly Deferred

- Built-in scheduling or daemon mode
- Provider selection or provider fallback
- User-configurable zone IDs
- User-configurable DNS record types
- Proxied records
- TTL configuration
- WAF list management
- Notifications, heartbeat integrations, and webhooks
- Cleanup-on-stop behavior
- Legacy JSON configuration
- Windows scheduling integration
