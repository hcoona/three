# 0002: Rebuild the Authentication Core

## Context

The v1 flow model combines mechanism selection, fallback order, interaction permission,
account preference, timeout behavior, and result presentation in abstractions that cannot
express the required deterministic contract.

Incremental flags would preserve the underlying ambiguity and make every new behavior
interact with historical defaults.

## Decision

Build a new v2 policy and orchestration core based on:

- versioned requests and results;
- stable account constraints and result postconditions;
- ordered acquisition stages;
- interaction policy independent from mechanism;
- one deadline and cancellation scope;
- explicit host capabilities;
- typed failures;
- secure, versioned cache semantics.

Do not extend the v1 `AuthMode` and fixed-flow architecture as the v2 foundation.

Reuse selected MSAL, broker, browser, device-code, secure-cache, packaging, and testing
knowledge only behind v2 contracts.

## Consequences

- The core is effectively a new implementation.
- Existing mechanism code can reduce cost without defining public semantics.
- A compatibility adapter, if any, is a later bounded feature.
- The design can fail closed rather than preserve ambiguous v1 behavior.
- Real platform validation remains necessary because reuse does not prove correctness.
