# 0005: Establish an Independent Operational Identity

## Context

The upstream product owns executable, package, installation, cache, configuration,
telemetry, signing, update, and support identifiers. Reusing mutable upstream namespaces
would create collision, cleanup, audit, and sponsorship ambiguity.

The final v2 product name has not been selected.

## Decision

Before distribution or persistent state is implemented, v2 will choose independently
owned operational identifiers.

V2 will not send telemetry under the upstream product identity. Network telemetry is
absent unless a future decision defines a fork-owned endpoint, data contract, notice,
retention policy, and user control.

## Consequences

- Final names do not block mechanism-neutral design or empirical research.
- Packaging and persistence remain blocked until the namespace registry is resolved.
- Install, logout, cleanup, and uninstall must leave upstream state untouched.
- A compatibility shim remains explicit and optional. V2 provides no upstream-state
  importer under the current compatibility policy.
