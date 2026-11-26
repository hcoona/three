# Cache, Security, and Operational-Identity Requirements

## V2-REQ-040: Secure Authentication State

V2 must persist reusable authentication material only in broker-owned or platform-secure
storage. It must not fall back to plaintext persistence. The first version must apply a
product-defined state policy rather than expose caller-selected or Client-Profile-selected
cache modes.

## V2-REQ-041: Safe Reusable-State Recovery and Concurrency

V2 must treat unreadable, undecryptable, corrupt, or incompatible reusable state as a
cache miss and must not consume it. Recovery must preserve the account email, effective
tenant policy, Client Profile, scopes, interaction permission, and original overall
deadline.

Unless the caller explicitly selects `force-interactive`, V2 must reuse valid, safe
authentication state when it can satisfy the request without another user sign-in.
A change of downstream consumer, package ecosystem, or working
repository alone must not require another sign-in for an otherwise compatible request.
Reuse must preserve the requested account, effective tenant policy, Client Profile,
resource and scopes, and applicable security context rather than treating different
contexts as interchangeable.

Eligible state includes compatible operating-system sign-in state available on first V2
use. V2 must not require a prior V2 sign-in or V2-created cache before considering that
state. Reuse remains subject to unique real-account pre-resolution and final validation
under `V2-REQ-020` and `V2-REQ-022`, interaction permission, and the original deadline.
An OS sign-in alone does not establish that these conditions can be satisfied.

Once acquisition and all required success validation complete within the original
deadline, V2 must not wait for reusable-state persistence before delivering the result.
When safe persistence has failed or is not confirmed complete at result delivery, V2
must return success with a machine-readable persistence warning rather than wait or fail
solely because of persistence. The incidental
deadline-expiry latitude in `V2-REQ-015` still applies; persistence status must not
override cancellation, denial, or validation failure.

Unfinished V2-controlled persistence work must end with the request under `V2-REQ-025`;
it must not continue in the background for an ended request. This does not select an
asynchronous mechanism or a storage lifecycle.

A later invocation with usable state remains subject to its explicit interaction mode
under `V2-REQ-013`, `V2-REQ-014`, and `V2-REQ-020`. Forced interaction does not clear or
invalidate that state; normal provider-managed updates and persistence limits still apply.

Concurrent processes sharing authentication state must preserve locking and atomic-update
integrity and observe consistent state. This does not promise cross-process interaction
single-flight. File names, locking mechanisms, serialization, and storage lifecycle are
not selected here.

Reuse does not require byte-identical access tokens. Ecosystem-specific credential
materialization and its lifecycle remain outside the authentication engine.

The first version must not expose Logout, Cache Clear, caller-visible Force Refresh, or
Account List operations.

## V2-REQ-042: Client Registration as Configuration

Public-client application IDs must be treated as nonsecret configuration and supplied
through Client Profiles rather than hard-coded into the authentication core. A profile
using an externally owned registration must make its ownership and external-dependency
status explicit. An integration whose provider mapping is defined for one specific
registration may pin that identity: it validates that the Profile-supplied client ID
equals the registration's fixed value and rejects any other value. The client ID is
still supplied by the Profile; such a pin restricts which Profiles the integration
admits and does not make the engine supply a client ID. V2 must not imply ownership of
that registration or its owner's
endorsement or support for the project.

## V2-REQ-043: No Upstream Telemetry Reuse

The unofficial fork must not send telemetry under the upstream Microsoft product
identity or use upstream ingestion configuration.

## V2-REQ-044: Independent Distribution Identity

Any distributed v2 artifact must use independently defined package, executable,
installation, configuration, cache, lock, telemetry, diagnostic, signing, and update
identities.

## V2-REQ-045: Headless-Linux Reusable-State Support

A headless Linux combination may claim support for repeated noninteractive acquisition
only when an accepted authentication-state mechanism can be reused across invocations
without requiring interactive unlock on every call. If no such mechanism satisfies the
accepted cache-security policy, that capability for the combination must be declared
unsupported rather than silently weakening storage.

## V2-REQ-046: Optional Telemetry Semantics

V2 must provide optional telemetry. Network export must be disabled unless explicitly
configured, and it remains unavailable until a decision under decision
[`0005`](../../decisions/0005-establish-independent-operational-identity.md) defines its
fork-owned endpoint, data contract, notice, retention policy, and user control. Export or
bounded-flush failure must not change the authentication result or its process status,
trigger authentication retry, fallback, or interaction, or prevent process termination
beyond a finite bound.

Telemetry remains subject to the authentication-material containment in `V2-REQ-035`,
the account and host identifier exclusions in `V2-REQ-036`, and the independent
identity requirements in `V2-REQ-043`
and `V2-REQ-044`.
