# WP3 — AzureAuth Async Process Identity Provider

Status: **implemented in production; WSL-to-Windows and Windows developer-host acceptance recorded**

Date: **2026-07-30**

WP3 introduced the async AzureAuth access-token seam. Subsequent work packages
connected it to production composition, credential materialization, product
identity configuration, and host adapters. The frozen v1 contracts remain
unchanged.

## Scope

Implemented in WP3:

- `IAccessTokenIdentityProvider` async seam using `ValueTask` plus
  `CancellationToken`
- provider-neutral `AcquiredAccessToken` and `AcquiredAccessTokenResult`
- `AzureAuthIdentityProvider`
- bounded async `IProcessRunner` / `SystemProcessRunner` execution for
  cancellation, timeout, launch-failure, invalid-output, and output-size status

Still out of scope:

- Direct MSAL
- AzureAuth device-code invocation
- product-owned persistent derived credential caching
- Windows-native Git, Visual Studio, and NuGet.exe acceptance

## Live WSL acceptance

On 2026-07-30, commit `31e60f70` was exercised from WSL against the installed
Windows AzureAuth `0.9.5.0` executable. An isolated product configuration root
recorded an operator-supplied tenant with no account preference. The production
apphost:

1. discovered the supported Windows installation through the WSL path;
2. invoked AzureAuth with the exact argv documented below and no explicit
   `--mode`;
3. completed through AzureAuth's Windows default broker path without opening a
   browser;
4. materialized the supported Git token-as-password form;
5. returned safe login status while printing and persisting no token; and
6. removed the isolated identity configuration afterward.

The installed executable reported version `0.9.5.0` and SHA-256
`6764403f10e806d39dad7cc8d804f2b9fdb0d1634474a4f4296b3bd9284ba985`.
No account identifier, tenant identifier, or token material is recorded here.
This acceptance closes the WSL-to-Windows AzureAuth row only; it does not claim
Windows-native host-tool acceptance.

## Native Windows developer-host acceptance

On 2026-07-30, commit `11b669b9` was published as a framework-dependent
`win-x64` apphost and exercised natively on Windows 11 Enterprise x64 build
`10.0.26200` with the installed .NET `10.0.10` runtime. Using a disposable
Windows-local application and configuration root, the production apphost:

1. started through `azureauth-credprovider.exe`;
2. configured an operator-supplied tenant without persisting credential
   material or claiming identity verification;
3. discovered and invoked the installed AzureAuth `0.9.5` through its default
   Windows authentication ordering with no explicit mode override;
4. completed the browser-allowed login command without terminal input;
5. returned only safe status fields and did not print credential material; and
6. unconfigured identity state and removed the disposable Windows root.

No account identifier, tenant identifier, or token material is recorded here.
This is source-build evidence from Windows 11 build `26200`, not the frozen
Windows 11 24H2 build `26100` baseline. It therefore does not establish exact
Windows 11 24H2, Windows Server, installer-produced binary, native Git helper,
Visual Studio, or NuGet.exe acceptance, so the combined Windows-first release
row remains deferred.

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
  `21258ff3a2cbb01d6891243114a55abe9ae3587e` defines the Windows default
  authentication order as broker and then web. The product omits `--mode` and
  relies on that pinned default.
- `--domain` is only best-effort cached-account filtering. It is derived from
  the optional bound account when a usable suffix follows `@`.
- `--output json` is not used because AzureAuth manually interpolates
  user/display-name text and can produce malformed JSON.
- On Windows, the default mode tries the WAM broker first and then browser
  authentication if the broker fails promptly. The broker itself tries the OS
  account and broker-backed cache before showing WAM interaction.
- An unanswered WAM dialog consumes AzureAuth's global timeout; AzureAuth does
  not continue to web after a flow-level timeout.

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
