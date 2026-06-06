# Phase 1R Secure-Cache Re-scope Decision

Status: **Accepted MVP re-scope**

Date: **2026-06-06**

Decision ID: **phase-1r-secure-cache-rescope**

Gate name: **Phase 1R re-scope for Phase 1.6 secure-cache behavior**

Owner: **PL with ID and ARCH**

## Decision Summary

| Field                      | Decision                                                                                                                                                                                                                                                                                                         |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Evidence links             | `phase-1.6-secure-cache-evidence.md` accepted the no-plaintext fallback policy but blocked full persistent-cache defaults because Windows, macOS, and Linux write/read/delete secure-store validation was unavailable. `phase-1.2-azureauth-suitability.md` identified AzureAuth plaintext fallback risk.        |
| User decision              | Human-in-the-loop decision: MVP does not implement a product-owned persistent derived credential cache.                                                                                                                                                                                                          |
| Decision                   | Re-scope MVP secure-cache behavior to non-persistent product-owned derived host-tool credentials by default. The product may rely on selected identity-provider or MSAL cache behavior only where that behavior is policy-compliant and does not become this product's derived credential cache.                 |
| Scope change               | Phase 1.6 no longer blocks MVP contract and implementation work on full platform secure-store defaults for product-owned derived credentials, because those persistent defaults are deferred out of MVP. The no-plaintext fallback policy remains mandatory for any future product-owned persistent cache.       |
| Implementation may proceed | Yes for Phase 2 and later work that models product-owned persistent derived credential cache as non-MVP and disabled by default. No implementation or documentation may claim MVP support for default product-owned persistent host-tool credential caching until the acceptance conditions in this record pass. |

## MVP Support Statement

For MVP, the product must not persist product-owned derived host-tool
credentials by default. This includes credentials generated or transformed by
the shared core for Git credential-helper responses, NuGet plugin authentication
responses, Python keyring backend or `keyring` shim responses, and npm, pnpm, or
Yarn registry authentication material.

MVP behavior may still:

1. Acquire credentials through the selected identity provider.
2. Use policy-compliant identity-provider or MSAL account/token cache behavior
   where the selected provider owns that cache and does not silently downgrade to
   plaintext storage.
3. Return protocol-required credentials to host tools through protocol stdout or
   host-tool response channels.
4. Emit configuration-manager-approved npm-compatible or CI temporary credential
   writes when the relevant phase record explicitly allows the target, scope,
   credential kind, ownership metadata, and cleanup behavior.

These allowed behaviors are not a product-owned persistent derived credential
cache. They remain governed by their own phase records, protocol contracts,
configuration-manager policy, CI policy, and redaction requirements.

## Cache Boundary

The identity-provider cache boundary is separate from the product-owned derived
credential cache boundary:

| Boundary                                      | MVP position                                                                                                                                                                              |
| --------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Identity provider or MSAL account/token cache | Allowed only when the selected provider's behavior is policy-compliant for the current platform and mode. The product must not rely on an unprotected fallback to satisfy product policy. |
| AzureAuth MSAL and account token cache        | Not a required runtime substrate. AzureAuth's inspected unprotected fallback risk cannot weaken this product's no-plaintext policy.                                                       |
| AzureAuth ADO PAT cache                       | Not the product's derived host-tool cache and not an accepted MVP dependency. It does not prove this product may persist derived Git, NuGet, Python keyring, or npm credentials.          |
| Product-owned derived credential cache        | Deferred out of MVP and disabled by default. Adding it later requires the acceptance conditions below.                                                                                    |

The shared core may still define cache-key data shapes, no-cache policies, and
typed results that leave room for a later persistent cache. It must not require
platform secure-store write/read/delete behavior for MVP execution.

## No-Plaintext Fallback Policy

The accepted Phase 1.6 policy remains unchanged:

1. The product must never silently persist credential material as plaintext when
   a secure store is unavailable, locked, denied, unsupported, or unverified.
2. A future product-owned persistent cache must fail closed with a typed
   `CacheUnavailable` or equivalent policy error instead of downgrading to
   plaintext.
3. Plaintext persistence may be introduced only by a separate explicit security
   decision that names the mode, makes it non-default, presents user-visible
   risk, and proves cleanup and redaction behavior.
4. Existing reference-tool behavior, including AzureAuth or other tools that can
   use unprotected fallback files, is negative evidence for this product policy,
   not permission to adopt that fallback.

## Affected Requirements and Designs

| Source                               | Impact                                                                                                                                                                                                                                                 |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `phase-0-decisions.md`               | Mandatory gate failures may enter Phase 1R. This record uses that path to accept revised MVP scope for the Phase 1.6 blocker while preserving no-plaintext fallback and release validation discipline.                                                 |
| `phase-1.2-azureauth-suitability.md` | The direct MSAL path remains preferred for now. AzureAuth remains optional and cannot be used to weaken product secure-cache policy. Its MSAL/account token and ADO PAT caches do not satisfy or replace a product-owned derived credential cache.     |
| `phase-1.6-secure-cache-evidence.md` | The blocked full platform secure-store default no longer blocks MVP because product-owned persistent derived credential caching is deferred. The accepted no-plaintext fallback policy remains mandatory for any later persistent cache.               |
| `project-breakdown.md`               | Phase 2 and Phase 6 may proceed only with MVP contracts that support non-persistent product-owned derived credentials by default. Later persistent cache work needs a new accepted gate or superseding record before defaults are locked.              |
| `requirements.md`                    | Functional requirement 6 remains one shared core. Non-functional cache partitioning remains a design constraint for any future persistent cache, but MVP satisfies credential handling through non-persistent derived credentials and provider policy. |
| `high-level-design.md`               | The shared core still owns identity, policy, redaction, and cache partitioning. The cache model is retained as a future-ready partitioning model, not as an MVP requirement to persist derived credentials.                                            |
| `mid-level-design.md`                | The secure-cache submodule may be represented by disabled, in-memory, or no-persistent-cache implementations for MVP. `CacheUnavailable`, protocol stdout safety, CI temporary configuration, and configuration-manager write policy remain required.  |

## Dependency Impacts

- Phase 2 contract freeze may proceed if credential contracts can express
  no-cache, cache-disabled, cache-unavailable, and future persistent-cache
  extension behavior without requiring persistent writes.
- Phase 4 configuration-manager work remains in scope. Credential-bearing
  configuration writes are not a secure-cache fallback and still require explicit
  write policy, ownership metadata, redaction, and cleanup.
- Phase 6 credential-core work may implement the identity-provider abstraction,
  direct MSAL readiness, fake-provider tests, cache-key construction, redaction,
  and non-persistent derived credential behavior. It must not enable
  product-owned persistent derived credential storage by default.
- Phases 9 through 12 may return protocol-required credentials to Git, NuGet,
  Python keyring, and npm-compatible tools. Those responses are protocol outputs,
  not product-owned persistent cache entries.
- CI support may use non-persistent identity material, environment variables, or
  configuration-manager-owned temporary files where approved. CI must not silently
  persist derived credentials in user-global or repository-local cache files.
- Phase 15 and Phase 16 must not claim release support for product-owned
  persistent derived credential caching unless this record is superseded by an
  accepted support-expansion decision.

## Phase 0-required Follow-up Actions

These actions are required by the Phase 0 gate path before dependent phases may
claim closure under this re-scope. They preserve the accepted MVP decision and do
not approve product-owned persistent derived credential caching by default.

| Required work                                                                                                                | Owner persona(s) | Dependency effect                                                                                                                                                                       | Target phase |
| ---------------------------------------------------------------------------------------------------------------------------- | ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| Freeze contract records so credential flows can represent no-cache, cache-disabled, cache-unavailable, and future extension. | ARCH, PL, ID     | Phase 2 may freeze contracts only after ARCH accepts the contract text and PL/ID policy text makes persistent product-owned derived credential caching non-MVP and disabled by default. | Phase 2      |
| Add MVP non-persistence test evidence for shared-core and adapter paths that issue derived host-tool credentials.            | ID, QA           | Phase 6 may proceed only with evidence that default execution does not create product-owned persistent derived credential cache entries.                                                | Phase 6      |
| Keep configuration-manager credential writes governed outside the secure-cache boundary with explicit scope and cleanup.     | PL, ID, QA       | Phase 4 remains unblocked, but its approved writes cannot be counted as secure-cache persistence or used to bypass the no-plaintext fallback policy.                                    | Phase 4      |
| Record protocol-output evidence separately for Git, NuGet, Python keyring, and npm-compatible adapters.                      | ID, QA           | Phases 9 through 12 may proceed only when protocol responses are proven to be host-tool outputs, not product-owned persistent cache entries.                                            | Phases 9-12  |
| Carry the re-scope into release, hardening, and user-facing statements without claiming persistent derived credential cache. | PL, QA           | Phase 15 and Phase 16 must block any default persistent-cache support claim until a superseding accepted support-expansion decision provides required evidence.                         | Phases 15-16 |

## MVP Non-persistence Acceptance Checklist

Acceptance artifacts are required in phase order so later adapter evidence does
not block earlier contract or shared-core closure.

### Phase 2 Contract Acceptance

Phase 2 may close only when these pass/fail artifacts are linked from Phase 2
evidence:

- [ ] ARCH accepts the credential contract freeze for every MVP
      credential-producing path, including no-cache, cache-disabled,
      cache-unavailable, and future persistent-cache extension states.
- [ ] PL and ID accept the policy text that marks product-owned persistent
      derived credential caching as non-MVP and disabled by default.
- [ ] Phase 2 sign-off states that persistent product-owned derived credential
      cache reintroduction remains blocked until the later acceptance conditions
      in this record or a superseding record pass.

### Phase 6 Shared-Core Acceptance

Phase 6 may close only when these pass/fail artifacts are linked from Phase 6
evidence:

- [ ] Shared-core tests prove default cache policy returns no-cache,
      cache-disabled, or cache-unavailable behavior without creating
      product-owned persistent derived credential files, records, or platform
      secure-store entries.
- [ ] Negative shared-core tests force secure-store unavailable, denied,
      unsupported, and verification-failed outcomes and prove the product never
      silently falls back to plaintext persistence.
- [ ] Filesystem and configuration snapshots before and after default shared-core
      MVP flows prove no product-owned persistent derived credential cache is
      created in user-global, repository-local, or tool-local cache locations.
- [ ] Identity-provider or MSAL cache evidence is documented separately and does
      not count as a product-owned derived credential cache or permit an
      unprotected fallback to satisfy product policy.
- [ ] Phase 6 sign-off states that persistent product-owned derived credential
      cache reintroduction remains blocked until the later acceptance conditions
      in this record or a superseding record pass.

### Phase 9-12 Adapter Acceptance

Each adapter phase may close only when its own pass/fail artifacts are linked
from that phase's evidence:

- [ ] Phase 9 Git adapter tests prove protocol-required credentials are emitted
      only through Git credential-helper protocol stdout or host-tool response
      channels and do not create product-owned persistent cache entries.
- [ ] Phase 10 NuGet adapter tests prove protocol-required credentials are
      emitted only through NuGet plugin response channels and do not create
      product-owned persistent cache entries.
- [ ] Phase 11 Python keyring or `keyring` shim adapter tests prove
      protocol-required credentials are emitted only through the relevant
      keyring response channel and do not create product-owned persistent cache
      entries.
- [ ] Phase 12 npm, pnpm, and Yarn adapter tests prove protocol-required
      credentials are emitted only through approved package-manager response or
      configuration-manager channels and do not create product-owned persistent
      cache entries.
- [ ] Configuration-manager evidence for any approved credential-bearing writes
      separately identifies target, scope, credential kind, ownership metadata,
      cleanup behavior, and redaction tests.

### Phase 15-16 Release Audit

Phase 15 or Phase 16 release and hardening evidence must include these pass/fail
artifacts before any release statement may mention persistent derived credential
cache support:

- [ ] Release audit confirms user-facing CLI, `doctor`, hardening, and release
      notes do not claim default product-owned persistent derived credential
      cache support.
- [ ] Release audit blocks any default persistent-cache support claim until a
      superseding accepted support-expansion decision provides the required
      platform secure-store and no-plaintext evidence.

## Acceptance Conditions for Adding Product-Owned Persistent Cache Later

A later decision may add product-owned persistent derived credential caching only
when all applicable conditions are satisfied and linked from a superseding
record:

1. Windows, macOS, and Linux secure-store write/read/delete/remove evidence is
   available for fake credential values on the target release matrix.
2. Failure-mode evidence covers unavailable stores, locked stores, denied access,
   headless Linux, missing DBus or Secret Service, missing or invalid `pass` or
   GPG setup, DPAPI or Credential Manager failures, and macOS Keychain denial.
3. Implementation tests prove that secure-store open, verification, write,
   read, update, delete, corruption, lock timeout, and cancellation failures do
   not create plaintext credential files.
4. Cache keys include ecosystem, host, organization, project when relevant, feed
   when relevant, service identity, account, tenant, audience, and credential
   kind before any credential is stored or reused.
5. The selected identity-provider cache behavior is documented separately from
   product-owned derived credential cache behavior and cannot downgrade product
   policy through unprotected fallback.
6. Configuration-manager-approved package-manager or CI temporary writes remain
   separate from secure-cache persistence and include explicit scope, ownership,
   expiry or cleanup behavior, and redaction tests.
7. Protocol adapters continue to emit only protocol-valid stdout and do not log,
   trace, dry-run, or diagnose stored credential values.
8. User-facing CLI, `doctor`, and release notes state the enabled platforms,
   unsupported modes, cleanup behavior, and consequences of `CacheUnavailable`.
9. A security review accepts any non-default plaintext mode separately before it
   exists, and tests prove that mode is never selected silently.
10. Phase 15 or equivalent hardening includes filesystem scans proving default
    persistent-cache tests do not leave plaintext token, PAT, Basic auth, npm,
    NuGet, or generated-password material.

## Residual Risks

- Users may expect faster repeated host-tool authentication from a persistent
  product cache; MVP documentation and diagnostics must set expectations.
- Selected identity-provider cache behavior may vary by platform. The product
  must report policy-incompatible provider cache behavior as disabled or
  unavailable rather than weakening the product policy.
- Non-persistent derived credentials may increase token acquisition frequency.
  Phase 6 and identity-flow work must handle this without adding undocumented
  persistence.
- Future support expansion remains blocked until platform secure-store evidence
  and no-plaintext tests are available.
