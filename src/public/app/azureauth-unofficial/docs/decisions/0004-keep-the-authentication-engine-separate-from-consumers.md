# 0004: Keep the Authentication Engine Separate From Consumers

## Context

Authentication tokens may be consumed by Git, NuGet, Python, npm, IDE, or other
credential-provider products. Those products have host-specific protocols,
configuration, credential materialization, and lifecycle requirements.

Conflating a consumer product with the authentication engine would expand v2 into
unrelated protocol and configuration ownership.

## Decision

Keep v2 limited to delegated public-client authentication and its process protocol.

Downstream credential providers and host adapters are separate consumers. This
repository does not own their Git, NuGet, Python, npm, repository-binding, or installation
behavior.

## Consequences

- The v2 result contract must be usable by external consumers without knowing their host
  protocols.
- Host-specific adapters do not belong in the v2 core.
- Consumer requirements may motivate authentication capabilities but cannot redefine the
  core boundary implicitly.
- A dedicated adapter may be added only through a separate scope decision.
