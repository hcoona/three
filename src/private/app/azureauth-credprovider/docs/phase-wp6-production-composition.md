# Work package 6: production composition

## Production ownership

`CredentialProviderCompositionRoot` owns persisted provider selection, account
binding, AzureAuth installation discovery, process execution, materialization,
and readiness.

The composition root is a snapshot. After `identity configure`,
`identity reconfigure`, or `identity unconfigure` changes persistence, callers
create a fresh production root to observe the new state. Identity mutation does
not construct the current production root first, so `reconfigure` and
`unconfigure` can repair malformed records.

Missing provider configuration is `ProviderNotConfigured`; production does not
synthesize Direct MSAL or any deterministic credential provider. Direct MSAL
remains explicitly unavailable when selected. Deterministic providers exist
only through `CreateExplicitTestScaffold`.

## AzureAuth 0.9.5 discovery

The supported release is AzureAuth `0.9.5`, source commit
`21258ff3a2cbb01d6891243114a55abe9ae3587e`.

The provider config stores the selected version, not an executable path.
Production derives:

`%LOCALAPPDATA%\Programs\AzureAuth\0.9.5\azureauth.exe`

Native Windows obtains `LocalApplicationData` from
`Environment.GetFolderPath`. WSL invokes the fixed Windows PowerShell only to
obtain `LocalApplicationData` and the executable file version using normal
framework APIs, then maps the absolute `C:` path under `/mnt/c`. Discovery
requires an ordinary file and version `0.9.5`.

Discovery does not inspect hashes, Authenticode, ACLs, owners, SIDs, reparse
points, ancestors, System32 trust, or stable artifact identities. The supported
model trusts the OS and official same-user installation. Missing, wrong-version,
unsupported-host, and unavailable discovery results have actionable codes.

## Launch

Interactive acquisition uses the mapped absolute executable and its containing
directory as the working directory:

```text
azureauth.exe aad
  --client 872cd9fa-d31f-45e0-9eab-6e460a02d1f1
  --tenant <bound tenant>
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default
  [--domain <best-effort account domain>]
  --output token
```

`--domain` is derived only when the optional bound account has a usable suffix
after `@`. It is a cached-account preference, not account enforcement.

The process inherits the normal host integration environment. The product does
not set `OEAUTH_MSAL_DISABLE_CACHE`, construct `WSLENV` allow/deny lists, or
attest PATH. The product omits `--mode` and relies on pinned AzureAuth `0.9.5`'s
Windows default: WAM broker, then web authentication. The broker first tries the
OS account and its broker-backed cache, which permits unattended reuse when
Windows already has a suitable account. A prompt broker failure falls through
to web; an unanswered WAM dialog consumes AzureAuth's global timeout and does
not fall through.

AzureAuth `0.9.5` has no cache-only CLI mode. `SilentOnly` therefore always
returns `SilentAcquisitionUnavailable` without launching. Interactive and
silent readiness are independent; global compatibility readiness follows the
interactive capability rather than requiring both.

## Host interaction routing

Interaction is authorized only by an explicit host protocol signal:

| Host request                                       | Identity flow | Policy           | Acquisition mode     |
| -------------------------------------------------- | ------------- | ---------------- | -------------------- |
| NuGet `IsNonInteractive=true`                      | Browser       | `Never`          | `SilentOnly`         |
| NuGet interactive and `CanShowDialog=true`         | Browser       | `HostToolAllows` | `InteractionAllowed` |
| NuGet interactive and `CanShowDialog=false`        | Device code   | `HostToolAllows` | `InteractionAllowed` |
| Git credential helper or Python keyring subprocess | Browser       | `Never`          | `SilentOnly`         |

NuGet's non-interactive flag overrides dialog capability. AzureAuth `0.9.5`
does not support device code, so that NuGet request shape returns
`AzureAuthDeviceCodeUnsupported` before process launch. Git and Python expose no
equivalent trustworthy interaction authorization signal; the product does not
infer one from a terminal, environment variable, or process ancestry.

## Persistence

Provider and binding records use bounded plain UTF-8 JSON in the normal
XDG/HOME or Windows LocalApplicationData root. Writes use an in-process mutex,
ordinary cross-process lock, content-hash conflict revision, and same-directory
atomic move. Product-created paths receive owner-only Unix modes.

There are no Base64 envelopes, filesystem ownership/ancestor proofs, `statx`,
directory durability interfaces, `fsync` protocol, or ABA generation
guarantees. Malformed records and cooperative concurrent-write conflicts remain
explicit.

## Token handling

AzureAuth stdout is bounded raw token text. Transport validation rejects empty,
multiline, whitespace-containing, control-containing, or oversized output. JWT
signature, audience, tenant, and time-claim consistency are not inferred from
unsigned local parsing. Only `exp` may be read as untrusted functional metadata;
SPS `validTo` remains authoritative for exchanged session credentials.

## Synchronous boundary

The public synchronous adapter blocks straightforwardly on the cooperative
async acquisition service. Timeouts and cancellation are enforced at actual
process and HTTP I/O. It does not create detached `Task.Run` workers to simulate
containment of a hostile provider.

## Readiness and stable states

| Code                           | Meaning                                               |
| ------------------------------ | ----------------------------------------------------- |
| `ProviderNotConfigured`        | Run `identity configure` to record identity context.  |
| `DirectMsalNotImplemented`     | Run `identity reconfigure` to use the supported path. |
| `AzureAuthInstallationMissing` | Install AzureAuth 0.9.5 for the current Windows user. |
| `AzureAuthVersionMismatch`     | Install/select supported version 0.9.5.               |
| `AzureAuthBindingRequired`     | Run `identity configure` with the intended tenant.    |
| `AzureAuthBindingMalformed`    | Run `identity reconfigure` or `identity unconfigure`. |
| `SilentAcquisitionUnavailable` | AzureAuth 0.9.5 has no silent-only mode.              |

Status and doctor report provider selection, installation, binding, and separate
interactive/silent readiness without printing tokens or process output.
