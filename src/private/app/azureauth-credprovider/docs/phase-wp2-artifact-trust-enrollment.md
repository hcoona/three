# WP2 — AzureAuth Contract and Policy Package

Status: **implemented as contract and policy scaffolding only**

Date: **2026-07-20**

WP2 freezes three persisted contracts:

- `azureauth-deployment-config-v1`
- `azureauth-provider-config-v1`
- `azureauth-account-binding-v1`

WP2 intentionally does **not** ship:

- a Windows artifact inspection implementation
- an OS secure-record-store implementation
- AzureAuth process launch authorization
- production runtime composition changes

The current runtime remains the existing WP1 direct-MSAL scaffold.
WP5 owns any future production composition change.

## Trust model

- `IAzureAuthArtifactTrustInspector` is a **trusted** WP3 platform adapter.
- Its job is to perform canonical-path, no-reparse, same-artifact, SHA-256,
  Authenticode signer, publisher, version, provenance, owner, and writability
  checks safely on the platform.
- When it emits evidence, the string fields must already be the adapter's
  normalized observations: canonical Windows path casing, lowercase SHA-256,
  exact signer and publisher text, exact version and provenance text, and
  trimmed stable identity and owner identifiers.
- WP2 validates the inspector's structured result and exact configured pins.
- WP2 never repairs or case-folds trusted evidence. Non-normalized, malformed,
  or mismatched evidence is treated as `Untrusted`.
- `AzureAuthTrustPolicy.EnsureValid(...)` is strict only for `Trusted`:
  `Deferred` and `Untrusted` may carry raw diagnostic evidence, including
  mismatched or non-normalized fields, but they are never ready.
- WP2 does **not** try to sandbox or harden against a malicious or
  contract-violating inspector implementation.
- `Trusted` means exact canonical-path equality plus exact pin equality and
  successful ownership and writability checks.
- Cached `AzureAuthTrustResult` values are valid only for the same current
  `AzureAuthDeploymentConfig`. WP2 recomputes the deployment key from the
  current pins plus trusted evidence before accepting cached trust or any
  AzureAuth binding.
- `Deferred` and `Untrusted` are never ready.
- The built-in WP2 inspector is the explicit
  `DeferredAzureAuthArtifactTrustInspector` placeholder until WP3 exists.

## Deployment contract

`AzureAuthDeploymentConfig` lives in the Contracts assembly and uses a dedicated
strict source-generated JSON facade.

The path policy is intentionally narrow:

- exact uppercase-drive absolute Windows path such as
  `C:\Program Files\AzureAuth\AzureAuth.exe`
- exact `AzureAuth.exe` filename casing
- no UNC paths
- no `\\?\` or `\\.\` device prefixes
- no alternate data streams
- no traversal segments
- no environment expansion markers
- no forward slashes
- no trailing dot or trailing space components
- no DOS device names
- no 8.3 short-name aliases
- no non-ASCII characters

Deployment pins remain exact:

- lowercase SHA-256
- exact signer identity
- exact publisher
- exact executable version
- exact provenance identifier

## Provider and binding contracts

- `AzureAuthProviderConfig` persists provider selection plus the nested pinned
  deployment config when `AzureAuth` is selected.
- The explicit default factory is `DirectMsal`.
- `AzureAuth` is opt-in and requires a deployment config.
- WP2 does not change `CredentialCoreService` or CLI composition.

`AzureAuthBinding` persists only:

- `Bound` or `Unbound` state (`Unspecified = 0` exists only as an invalid
  fail-closed sentinel)
- provider selection
- deployment key for trusted AzureAuth bindings
- lowercase account ID
- lowercase tenant ID
- exact UTC timestamp serialized only as `yyyy-MM-ddTHH:mm:ssZ`

Observed account and tenant inputs are normalized narrowly:

- raw input must already contain only printable ASCII
- tabs, newlines, control characters, non-ASCII, and Unicode whitespace are
  rejected before normalization
- only ordinary ASCII space is trimmed from the ends
- only ASCII `A-Z` is lowercased
- identifiers that become empty after ASCII-space trim are rejected

Bindings do **not** store tokens, passwords, or other secrets.

## Strict persisted JSON behavior

Only the persisted public wire contracts use strict JSON facades and direct
generic `System.Text.Json` blocking.

Those facades reject:

- unknown properties
- wrong-case property names
- duplicate properties
- malformed JSON
- numeric type mismatches

Runtime doctor and store result objects are ordinary runtime types.
They do not use the old runtime-wide JSON blocker machinery.

## Secure store

- `IAzureAuthSecureRecordStore` is a **trusted** platform adapter implemented
  later.
- Its contract guarantees safe-root placement, no-follow behavior,
  current-user ownership, owner-only permissions, linearizable compare-revision
  checks for no-op validation, atomic compare-exchange for mutations, durable
  writes, and opaque ABA-safe revision tokens.
- Every successful committed compare-exchange returns a new nonblank revision
  token that is never reused for a later state at the same record path.
- WP2 does not add lease, capability, or delegation semantics to those
  revisions.
- WP2 validates only safe relative record names, read and write statuses,
  revision shape, UTF-8 decoding, strict parse results, compare-revision
  outcomes, and compare-exchange outcomes.
- Provider configuration supports explicit `Create`, `Replace`, and `Repair`.
- Binding supports explicit `Bind`, `Rebind`, and `Unbind`.
- `Rebind` and `Unbind` may repair malformed bytes.
- `Bind` does not repair malformed bytes.
- Logical no-op `Bind` and `Unbind` still validate the expected revision
  linearly before returning success, so stale snapshots conflict without
  churning bytes or revisions.
- The built-in WP2 store is an explicit `Unsupported` placeholder.

## Doctor

The WP2 doctor is read-only.

It combines:

- current provider config
- current trust result
- current binding read result

It reports actionable provider, readiness, and binding checks without mutating
state and without exposing secrets.

## Boundary summary

- WP1 remains the current runtime scaffold.
- WP2 adds the persisted contracts, trust policy, doctor policy, binding state
  machine, and secure-store seams.
- WP3 owns the real trusted inspector and secure-store implementations.
- WP5 owns future production composition and any real AzureAuth launch path.
