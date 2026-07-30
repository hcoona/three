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
- bounded async `IProcessRunner` / `SystemProcessRunner` updates for
  cancellation, timeout, launch-failure, invalid-output, and output-size status
- AzureAuth launch options for bounded execution and allowlisted environment
  construction; launch directories come only from trusted runtime inspection

Still out of scope:

- credential exchange or host-tool materialization (WP4)
- opaque CI token work (WP5)
- production composition across persisted adapters and stores
- registry lifecycle
- live AzureAuth acceptance

## Exact AzureAuth Invocation

WP3 uses the pinned executable path from the trusted deployment config only. No
PATH lookup or fallback is allowed.

Exact argv:

```text
AzureAuth.exe
  aad
  --client 872cd9fa-d31f-45e0-9eab-6e460a02d1f1
  --tenant <bound-tenant-id>
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default
  --mode web
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
- The pinned upstream `aad` options contain no exact-account enforcement switch. Production uses
  best-effort account selection: request hints must match the binding, the tenant is passed
  explicitly, and returned Azure DevOps JWT audience, tenant, and time claims are validated.

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

## Trust and Binding Prerequisites

Before each launch, WP3:

1. validates `AzureAuthProviderConfig`
2. calls `IAzureAuthArtifactTrustInspector` through `AzureAuthTrustPolicy`
3. requires `Trusted` plus an exact current deployment-key match
4. requires a `Bound` AzureAuth binding with the same provider and deployment
5. rejects invalid or mismatched account or tenant hints before process launch
6. takes the working directory and PATH entries only from the inspector's
   runtime-only `TrustedWorkingDirectory` and snapshotted `TrustedPathEntries`

The inspector owns filesystem ownership and writability checks. WP3 validates
only basic nonblank absolute Windows shapes for its attested launch context. The
inspector contract still provides inspection evidence, not a retained launch
lease. Production evaluates trust once per acquisition and reuses that result
for launch; it does not claim to eliminate the remaining path-based TOCTOU
window.

Default behavior remains fail closed:

- `DeferredAzureAuthArtifactTrustInspector` => trust deferred => no launch
- unsupported secure-store composition is still outside WP3 runtime composition

## Environment, Working Directory, and Stdio Isolation

WP3 AzureAuth launches use `ProcessEnvironmentMode.ExplicitOnly`.

Allowed child environment keys are only:

- `SystemRoot`
- `WINDIR`
- `TEMP`
- `TMP`
- `LOCALAPPDATA`
- `USERPROFILE`
- `PATH` only when explicit path entries are configured
- `OEAUTH_MSAL_DISABLE_CACHE=1` for `NoCache`,
  `ProductPersistentCacheDisabled`, and `NonPersistentCi`

Everything else is omitted, including representative secret, proxy, and loader
variables such as `ADO_TOKEN`, `HTTP_PROXY`, `DOTNET_ROOT`, `NODE_OPTIONS`,
`COMPLUS_*`, `COREHOST_*`, CLR/CoreCLR profiler controls,
`DOTNET_STARTUP_HOOKS`, `LD_*`, `DYLD_*`, and `PYTHON*`.

For WSL interoperability launches, the explicit Linux process environment
carries the snapshotted `WSL_INTEROP` endpoint and bridges the launch controls
selected by production: `SystemRoot`, `WINDIR`, the trusted `PATH`, fixed
`PATHEXT`, and cache policy. Production discovery does not derive or require
`TEMP`, `TMP`, `LOCALAPPDATA`, or `USERPROFILE`; the Windows process may receive
the normal Windows-host values so browser and MSAL integration use the host
user. The representative filtering above describes the Linux launcher process,
not replacement of the complete environment that WSL creates for a Windows
process. The PowerShell trust probe additionally bridges its target-path
variable.

Additional launch rules:

- working directory and PATH are required to come from current trusted inspector
  evidence rather than caller launch options
- current-directory inheritance is forbidden
- stdin is redirected, receives no bytes when input is `null`, and is then
  explicitly closed; no ecosystem stdout/stderr forwarding occurs
- child stdout and stderr are always captured, never forwarded to protocol
  stdout

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

The general runner defaults to a 15-minute timeout and 1 MiB per captured stream.
It rejects timeouts above one hour and any per-stream byte or character limit
above 16 MiB before process start. AzureAuth keeps its tighter 8 KiB stream
defaults.

On timeout or output-limit breach, the runner attempts
`Kill(entireProcessTree: true)`, then waits for process exit and output drains for
at most two seconds before returning a product-controlled status. Caller
cancellation performs the same bounded cleanup and then throws
`OperationCanceledException`, preserving existing runner behavior; the
AzureAuth provider catches and maps it to its canceled result.

The runner continues monitoring until both process exit and output drains
complete. This prevents an exited root with inherited pipes held by descendants
from hanging indefinitely. The direct tree-kill attempt plus bounded cleanup is
not full containment: trusted Windows runner/container composition must provide
job-object or equivalent whole-tree containment when that guarantee is needed.

## Raw Token Output Rules

WP3's process boundary first treats AzureAuth stdout as opaque token text and
validates the transport shape:

- allow no line ending, one LF, or one CRLF, trimmed once
- reject a bare CR
- reject empty output
- reject multiline output
- reject leading or trailing whitespace around the token
- reject control characters
- reject oversized output through runner bounds

WP4 subsequently requires strict Azure DevOps JWT claim-consistency validation
before returning the acquired token and derives `iat`, `nbf`, and `exp`
metadata. This is not local signature authentication; see
[`phase-wp4-token-materialization.md`](phase-wp4-token-materialization.md).
The tenant and deployment key still come from constraints enforced for launch,
not from JWT claims. AzureAuth cannot force exact account selection, so account
identity remains unknown (`null`). Interactive production launch is allowed
after trust, deployment, and binding prerequisites pass; the returned JWT's
audience, tenant, and time claims remain validated. Ecosystem credential exchange
remains outside the WP3 provider.

All currently accepted cache policies disable AzureAuth's upstream MSAL file
cache with the explicit product-controlled value above. A
`FuturePersistentCacheRequested` request fails closed until persistent-cache
behavior is designed and attested. No ADO token or PAT environment fallback is
used.

## Stable Error Codes

WP3 public result codes are stable product strings, including:

- `AzureAuthAcquisitionModeRequired`
- `SilentAcquisitionUnavailable`
- `AzureAuthRequestRejected`
- `AzureAuthPolicyRejected`
- `AzureAuthDeviceCodeUnsupported`
- `AzureAuthPersistentCacheUnsupported`
- `AzureAuthProviderSelectionMismatch`
- `AzureAuthTrustDeferred`
- `AzureAuthTrustRejected`
- `AzureAuthBindingRequired`
- `AzureAuthBindingProviderMismatch`
- `AzureAuthBindingDeploymentMismatch`
- `AzureAuthBindingAccountMismatch`
- `AzureAuthBindingTenantMismatch`
- `AzureAuthProcessLaunchFailed`
- `AzureAuthProcessExitNonZero`
- `AzureAuthProcessCanceled`
- `AzureAuthProcessTimedOut`
- `AzureAuthProcessOutputTooLarge`
- `AzureAuthProcessOutputInvalid`
- `AzureAuthTokenOutputInvalid`
- `AzureAuthTokenClaimsInconsistent`
- `AzureAuthProviderFailure`

Raw stderr and token content are never copied into these public errors.
