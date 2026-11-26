# Product-Boundary Requirements

## V2-REQ-001: Delegated Public-Client Scope

V2 must provide delegated Microsoft Entra public-client token acquisition. It must not
implicitly select confidential-client, service-principal, managed-identity, or
workload-identity flows.

## V2-REQ-002: No Implicit Product Expansion

Git credential protocols, a daemon, a GUI, and general SDK credential chaining must
remain outside the core unless separately accepted. Personal-access-token behavior is
governed by [V2-REQ-005](#v2-req-005-no-personal-access-tokens).

Downstream credential-provider products and host-tool adapters must remain separate
consumers of the v2 authentication protocol.

## V2-REQ-003: Unofficial Product Identity

V2 must identify the project as unofficial and must not imply an official Microsoft
release, upstream support, or ownership of a Microsoft application registration.

The following presentation surfaces satisfy this identification:

- the repository presentation, including its README;
- the CLI help text, which must state that the project is unofficial;
- before an interactive authentication call, a bounded unofficial identity indication
  written to the designated diagnostic channel; it is best-effort, and whether a caller
  shows that channel to the user is caller-dependent; and
- a fixed unofficial title supplied to the provider's interactive surface when the
  provider supports one; its actual presentation is provider-controlled.

No surface may contradict that identity. V2 must not require a per-use confirmation or
acknowledgment of the unofficial status. The machine-readable result protocol is not a
user-facing surface for this requirement.

## V2-REQ-004: One Authentication Request Per Process

Each native authentication process must handle one machine request and terminate after
its terminal outcome. A daemon, persistent multi-request service, and batch or
multiplexed authentication protocol remain outside native v2.

## V2-REQ-005: No Personal Access Tokens

V2 must not require, solicit, accept, generate, exchange for, persist, return, or use
personal access tokens (PATs) as authentication credentials. This includes Azure DevOps
`Compact` tokens.

PATs must not be a prerequisite, a normal acquisition path, or a fallback for a supported
journey. Request options, Client Profiles, and provider defaults must not enable PAT
behavior. If an eligible delegated access-token path cannot complete, V2 must return the
applicable existing failure outcome without requesting or using a PAT.

Enforce this boundary through permitted provider operations and their credential
semantics. Access tokens remain opaque under
[V2-REQ-033](result-and-process-protocol.md#v2-req-033-opaque-access-tokens).
