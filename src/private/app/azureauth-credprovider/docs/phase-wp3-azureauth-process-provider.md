# WP3 — AzureAuth Async Process Identity Provider

Status: **implemented as an explicit optional async provider path only**

Date: **2026-07-20**

WP3 adds a narrowly scoped, test-instantiated AzureAuth execution path behind a
new async access-token seam. The existing frozen v1 synchronous
`CredentialCoreService` path remains unchanged and is still the current runtime
default.

## Scope

Implemented in WP3:

- `IAccessTokenIdentityProvider` async seam using `ValueTask` plus
  `CancellationToken`
- provider-neutral `AcquiredAccessToken` and `AcquiredAccessTokenResult`
- `AzureAuthIdentityProvider`
- bounded async `IProcessRunner` / `SystemProcessRunner` execution for
  cancellation, timeout, launch-failure, invalid-output, and output-size status

Still out of scope:

- credential exchange or host-tool materialization (WP4)
- opaque CI token work (WP5)
- production composition across persisted adapters and stores
- registry lifecycle
- live AzureAuth acceptance

## Exact AzureAuth Invocation

WP3 uses the absolute executable path produced by supported-version installation
discovery. No PATH lookup or shell command construction is used.

Exact argv:

```text
azureauth.exe
  aad
  --client 872cd9fa-d31f-45e0-9eab-6e460a02d1f1
  --tenant <bound-tenant-id>
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default
  --mode web
  [--domain <best-effort bound account domain>]
  --output token
```

Frozen upstream constants:

- Azure DevOps resource: `499b84ac-1321-427f-aa17-267ca6975798`
- Azure DevOps default scope:
  `499b84ac-1321-427f-aa17-267ca6975798/.default`
- AzureAuth `aad` client:
  `872cd9fa-d31f-45e0-9eab-6e460a02d1f1`

Notes:

- `ado token` is intentionally not used here.
- `--output token` is required; WP3 does not consume AzureAuth JSON output.
- secrets are never placed in argv.
- AzureAuth `0.9.5` source commit
  `21258ff3a2cbb01d6891243114a55abe9ae3587e` supports repeated `--scope` and
  `--mode`, plus `--domain` and bounded raw token output.
- `--domain` is only best-effort cached-account filtering. It is derived from
  the optional bound account when a usable suffix follows `@`.
- `--output json` is not used because AzureAuth manually interpolates
  user/display-name text and can produce malformed JSON.
- `--mode web` tries `CachedAuth` first and then browser interaction.

## Request Matrix

| Request shape                                                                                                                    | WP3 AzureAuth behavior                                               |
| -------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `contractMajor: 2`, `acquisitionMode: interactionAllowed`, browser, human interactive policy, non-CI, accepted WP1 request shape | Allowed to reach process preflight                                   |
| Device code                                                                                                                      | Rejected; no process launch                                          |
| `acquisitionMode: unspecified`                                                                                                   | Fail closed before process                                           |
| Valid `acquisitionMode: silentOnly`, `interactivePolicy: never`, non-CI frozen v1 shape                                          | Valid acquisition request; `SilentAcquisitionUnavailable`; no launch |
| Invalid `silentOnly` combinations, including interactive policy, explicit CI, or opaque token                                    | Reject before process                                                |
| Invalid `interactionAllowed` combinations, explicit CI, unsupported cache or frozen-request drift                                | Reject before process                                                |

`AzureAuthIdentityProvider` accepts only `CredentialRequestV2`.

## Installation and Binding Prerequisites

Before launch, production composition:

1. validates `AzureAuthProviderConfig`
2. discovers the current user's versioned AzureAuth `0.9.5` installation
3. requires an ordinary file whose reported version is `0.9.5`
4. requires a bound AzureAuth tenant and optional account preference
5. rejects mismatched account or tenant request hints before process launch

Native Windows derives the installation from `LocalApplicationData`. WSL uses
fixed Windows PowerShell only to read `LocalApplicationData` and file version,
then maps the absolute `C:` path under `/mnt/c`. The integration trusts normal
OS and framework abstractions; it does not implement hashes, Authenticode, ACL,
owner, ancestor, reparse, stable-identity, or TOCTOU proofs.

## Environment, Working Directory, and Stdio

`SystemProcessRunner` inherits the normal process environment. A
`ProcessStartSpec` may override or remove individual variables; it does not
clear or attest the complete environment. AzureAuth-specific environment
selection belongs to the AzureAuth integration package.

The runner uses the supplied working directory without a separate launch-time
attestation callback. Stdin is redirected, receives no bytes when input is
`null`, and is then closed. Stdout and stderr are redirected and drained
concurrently, never forwarded to an ecosystem protocol stream.

## Async Runner Bounds and Statuses

`SystemProcessRunner` now uses async APIs only on the new AzureAuth path.

Captured process statuses:

- success
- nonzero exit
- timeout
- cancellation
- output too large
- invalid output encoding
- launch failure

The general runner defaults to a 15-minute timeout and a 1 MiB byte limit for
each captured stream. Callers may select another positive timeout or byte
limit. A byte limit also bounds decoded text, so a second character limit is
not maintained.

On timeout, invalid UTF-8, or output-limit breach, the runner makes a best-effort
`Kill(entireProcessTree: true)` call and waits for ordinary process exit. Caller
cancellation performs the same cleanup and then throws
`OperationCanceledException`. This is normal `Process` API behavior, not a
containment or descendant-attestation guarantee.

## Raw Token Output Rules

WP3's process boundary first treats AzureAuth stdout as opaque token text and
validates the transport shape:

- allow no line ending, one LF, or one CRLF, trimmed once
- reject a bare CR
- reject empty output
- reject multiline output
- reject whitespace or control characters within the token
- reject oversized output through runner bounds

WP4 may read only `exp` as untrusted functional expiry metadata. It does not use
unsigned audience, tenant, issued-at, or not-before claims as security gates; see
[`phase-wp4-token-materialization.md`](phase-wp4-token-materialization.md).
The tenant comes from the binding and explicit `--tenant` argument. Account
identity remains a best-effort preference.

The process inherits the ordinary host integration environment.
`OEAUTH_MSAL_DISABLE_CACHE` is not set: host MSAL cache reuse is an intentional
product behavior. AzureAuth has no cache-only CLI mode, so `SilentOnly` remains
unavailable and never launches.

## Error Codes

Current AzureAuth preflight, discovery, and process results use product codes
including:

- `AzureAuthAcquisitionModeRequired`
- `SilentAcquisitionUnavailable`
- `AzureAuthRequestRejected`
- `AzureAuthPolicyRejected`
- `AzureAuthDeviceCodeUnsupported`
- `AzureAuthPersistentCacheUnsupported`
- `AzureAuthProviderSelectionMismatch`
- `AzureAuthInstallationMissing`
- `AzureAuthVersionMismatch`
- `AzureAuthDiscoveryUnavailable`
- `AzureAuthBindingRequired`
- `AzureAuthBindingProviderMismatch`
- `AzureAuthBindingAccountMismatch`
- `AzureAuthBindingTenantMismatch`
- `AzureAuthProcessLaunchFailed`
- `AzureAuthProcessExitNonZero`
- `AzureAuthProcessCanceled`
- `AzureAuthProcessTimedOut`
- `AzureAuthProcessOutputTooLarge`
- `AzureAuthProcessOutputInvalid`
- `AzureAuthTokenOutputInvalid`
- `AzureAuthProcessFailed`

Raw stderr and token content are never copied into these public errors.
