# Compatibility and Migration Policy

## Current Commitment

V2 has no current promise of compatibility with v1 commands, output, environment
variables, configuration, cache data, installation paths, or fallback behavior.

Compatibility with official AzureAuth is a bounded adapter concern, not a constraint on
the V2 core.

For updates of AzureAuth Unofficial V2 itself,
[`V2-REQ-037`](requirements/result-and-process-protocol.md#v2-req-037-supported-protocol-compatibility)
owns preservation of each still-supported protocol major's calling contract. An unchanged
adapter can rely on that published contract, not on indefinite support for older majors.
Protocol version and the V2 product-generation name are distinct. No public wire schema
or supported protocol-major set is selected by this policy.

## Side-by-Side First

Any future v2 artifact must:

- use the independent operational identities that
  [`V2-REQ-044`](requirements/cache-security-and-operational-identity.md#v2-req-044-independent-distribution-identity)
  requires;
- avoid placing an `azureauth` compatibility shim by default;
- leave upstream installation and state untouched during install, authentication, upgrade,
  downgrade, uninstall, and cleanup.

## Migration Rules

V2 provides no importer for v1 configuration, aliases, account records, token caches,
credentials, PATs, telemetry configuration, or device identifiers. The product must not
read, modify, delete, or take over upstream state as migration. Users must configure v2
independently. This does not require a fresh sign-in when compatible broker-owned
operating-system state can be reused under
[`V2-REQ-041`](requirements/cache-security-and-operational-identity.md#v2-req-041-safe-reusable-state-recovery-and-concurrency).
Such reuse is not an import of upstream application state and does not relax account,
authorization, interaction, or deadline constraints.

## Externally Owned Client Profile Gate

A profile that uses a client application not owned by this repository may be accepted
only when:

- public evidence identifies the owner and the intended or unsupported reuse boundary;
- the required account, resource, authority, host, redirect, and broker combinations have
  bounded validation;
- availability, explicit user opt-in, and failure behavior are decided consistently with
  [`V2-REQ-018`](requirements/request-identity-and-authority.md#v2-req-018-explicit-client-profile-selection),
  which excludes implicit or default profile selection;
- consent, audit attribution, and branding under that registration are publicly reviewed
  and validated for the intended hosts;
- cache and configuration partitioning prevent silent identity collisions;
- public documentation states ownership, support, and availability limits.

The [primary user journey](user-stories.md#primary-journey-personal-azure-devops-git-access)
does not satisfy this gate or select the Visual Studio compatibility candidate. Its
MSA behavior, strict-email feasibility, and reusable-state behavior remain evidence
obligations under the [validation strategy](../validation/strategy.md#primary-journey-gate).

Decision [0003](../decisions/0003-treat-client-registration-as-an-external-dependency.md)
records the one owner exception to this gate: `azureauth-unofficial` prerelease archives
bundle the Visual Studio compatibility candidate as an explicit opt-in file while the
gate's remaining conditions stay open. The exception does not accept the profile, enable
it by default, or support it.

## Compatibility Adapter Gate

A v1 compatibility adapter may be accepted only when:

- the supported commands, flags, aliases, output fields, exit codes, and environment
  variables are enumerated;
- each input maps deterministically to a v2 request;
- changed behavior is documented;
- ambiguous or unsafe behavior fails rather than silently widening policy;
- compatibility output is isolated from the native v2 protocol;
- deprecation and removal rules are defined.

Until that gate is met, scripts must not assume that `main-v2` is a drop-in replacement.
