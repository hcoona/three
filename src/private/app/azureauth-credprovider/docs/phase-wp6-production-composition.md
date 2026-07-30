# Work package 6: production composition

## Dependency graph

`CredentialProviderCompositionRoot` is the single runtime owner:

`provider config + binding + trust inspector + secure store + process runner + HTTP client +
clock + diagnostics + filesystem/config options`

→ `IAccessTokenIdentityProvider`

→ `CredentialMaterializationService`

→ `ICredentialAcquisitionService`

→ bounded executable-boundary adapter

→ CLI login, Git helper, NuGet plugin, Python keyring helper, and package configuration.

The Azure Pipelines opaque job-token service remains separate and never enters provider
acquisition or persistent storage.

## Production and test boundary

`CreateProduction` loads provider configuration and binding from an owner-only Linux/WSL secure
record store under `$XDG_CONFIG_HOME/azureauth-credprovider` (or
`$HOME/.config/azureauth-credprovider`). `AZUREAUTH_CREDPROVIDER_CONFIG_ROOT` and production
options provide explicit test/deployment overrides. Writes use both a canonical-root in-process
mutex and a file lock, opaque generation revisions, strict UTF-8 envelopes, atomic replacement,
file flush, and Linux parent-directory `fsync`; links, non-regular records, wrong ownership, and
pre-existing group/world permissions fail closed rather than being repaired. Every existing
config-root ancestor must be owned by root or the effective user and must not permit group or world
replacement. A standard sticky world-writable temporary directory (`01777`) is accepted only with
one of those owners because Unix sticky semantics protect its owner-only product child; current-user
write permission is normal. First-created directories and records are owner-only. A platform that
cannot provide the required file-type inspection or directory durability reports `Unsupported`.
Record content is limited to 1 MiB, and the serialized envelope has a 2 MiB hard limit that is
checked before a bounded read. File-lock contention uses bounded exponential backoff with a
three-second default; exhaustion reports a stable `Unavailable` result rather than classifying
temporary contention as unsafe storage.

Missing configuration selects
Direct MSAL as the fail-closed default. Its production seam is currently unavailable and returns
`DirectMsalNotImplemented`; it never creates deterministic identities, local fake exchanges,
in-memory stores, fake runners, or synthetic secrets.

`CreateExplicitTestScaffold` is the only composition factory for deterministic legacy test
providers. Its mode and doctor readiness are always `TestScaffold` and not ready.

AzureAuth is opt-in. It requires complete deployment pins, trusted inspection, a matching bound
account and tenant, and a validated WSL Windows-interoperability launch context with browser
support. `WSL_INTEROP` is read once, accepted only as a canonical,
control-free absolute path below `/run/WSL/`, snapshotted, and copied into the explicit-only probe
and launch environments; missing or invalid values make WSL readiness unavailable. On WSL,
production uses the fixed Windows PowerShell path below `/mnt/c` to verify the exact canonical path,
regular-file and
reparse state, SHA-256, Authenticode signer/publisher, file version, ACL, and provenance before
each launch. It also verifies the executable directory and its parent chain and
`C:\Windows\System32` and its parent chain are canonical, non-reparse directories whose ACLs do not
grant write access to untrusted principals. Allow ACEs marked `InheritOnly` are not effective on the
directory currently being inspected and are ignored; effective mutation, permission-change, and
ownership-takeover grants remain disallowed. The raw security descriptor for the executable and
every checked directory must contain a present, non-null DACL; a null or absent DACL fails closed,
while an empty present DACL is not itself a write grant. This aggregate result is required in the
strict probe evidence. The probe receives the target through an explicit environment and emits
bounded, case-sensitive JSON with unknown and duplicate members rejected. Non-WSL production
hosts report AzureAuth launch as unsupported.
The executable directory is never added to `PATH`; working directory and `PATH` are the attested
`C:\Windows\System32` only. WSL production discovery supplies `SystemRoot`, `WINDIR`, the
snapshotted `WSL_INTEROP` endpoint, trusted `PATH`, fixed `PATHEXT`, and cache controls. It does not
derive or require `TEMP`, `TMP`, `LOCALAPPDATA`, or `USERPROFILE`; normal Windows-host values may
flow to the Windows process so browser and MSAL integration use the host user. Explicit-only
filtering applies to the representative Linux launcher environment, not the complete environment
that WSL creates for a Windows process.

Root construction is side-effect free with respect to Windows trust probing and launch discovery.
Shared request preflight rejects invalid, device-code, and unsupported silent requests before
evaluating trust. Valid browser acquisition, immediate pre-launch validation, readiness, and doctor
perform their required current trust checks. WSL executable and working-directory host paths are
derived only from the configured Windows executable, attested working-directory evidence, and the
fixed `/mnt/c` mount. Production options cannot override either host path.

Interactive and silent readiness are separate. Inspection of pinned upstream commit
`de20930c34b3b86c8a0ed7bbdeeca3f662dae918` confirms that `aad` has `--tenant` but no exact-account
selection option. Production therefore treats the bound account as a best-effort preference:
request hints must match the binding, but AzureAuth may present its own account chooser. The bound
tenant is passed to `aad`, and returned Azure DevOps JWTs must pass audience, tenant, issued-at,
not-before, and expiry consistency validation. Interactive readiness is reported when deployment
trust, binding, and WSL host-launch prerequisites pass. Protocol/silent acquisition
remains `silent-unavailable` until a proven silent cache or other source exists.

npm, pnpm, and Yarn configuration requires an explicit canonical Azure Artifacts npm registry URL
from command or configuration input. For example:

`azureauth-credprovider configure npm --registry-url
https://pkgs.dev.azure.com/example/_packaging/feed-name/npm/registry/`

Missing or invalid declarations fail before token acquisition or configuration writes. Synthetic
registry targets are permitted only in explicit test fixtures.

CI unconfigure dry-runs validate and report the same job-scoped removal plan as execution without
requiring its filesystem postconditions to have occurred. If an ownership manifest is malformed,
both modes report the incomplete-manifest diagnostic without deleting anything. The malformed
manifest and its known job container remain untouched for diagnosis.

Git and NuGet unconfigure execution and dry-run use configuration-only services. They do not load
provider configuration or construct the production credential acquisition root, so malformed or
unsafe provider state cannot block removal of product-owned configuration.

## Acquisition matrix

| Caller                           | Mode                 | Interaction         |
| -------------------------------- | -------------------- | ------------------- |
| Git helper                       | `SilentOnly`         | fail closed         |
| NuGet plugin                     | `SilentOnly`         | fail closed         |
| Python keyring helper            | `SilentOnly`         | fail closed         |
| npm/pnpm/Yarn user configuration | `InteractionAllowed` | browser allowed     |
| npm/pnpm/Yarn CI configuration   | `Unspecified`        | separate CI service |
| CLI interactive-browser login    | `InteractionAllowed` | user allowed        |
| Azure Pipelines opaque job token | `Unspecified`        | separate CI service |

Frozen v1 input is translated once into an explicit internal v2 request. The v1 wire contract is
unchanged. Protocol builders produce contract-valid `SilentOnly` requests with interaction set to
`Never`. These are valid acquisition requests, not unsupported policy, but currently return
`SilentAcquisitionUnavailable` because no proven silent source exists. User package configuration
allows browser interaction; CI package configuration consumes the separate opaque job token.

The synchronous boundary invokes the provider inside a `Task.Run` worker and applies
`Task.WaitAsync` to that hard timed boundary. This bounds the caller even when a provider blocks
synchronously before returning its `ValueTask`. Timeout cancels the provider token best-effort and
returns `CredentialAcquisitionTimedOut`; caller cancellation remains
`CredentialAcquisitionCanceled`, and late worker faults are observed. An intentionally
noncooperative blocked worker may remain after the bounded caller returns.

## Stable unavailable states

| Code                                                              | Meaning / remediation                                                                                                                                    |
| ----------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ProviderNotConfigured`                                           | Configure a provider; Direct MSAL remains the unavailable default.                                                                                       |
| `DirectMsalNotImplemented`                                        | Select a fully configured AzureAuth deployment or wait for Direct MSAL.                                                                                  |
| `AzureAuthTrustDeferred` / `AzureAuthTrustRejected`               | Install and validate the pinned artifact.                                                                                                                |
| `AzureAuthBindingRequired` and binding mismatch codes             | Bind or rebind the intended account and tenant.                                                                                                          |
| `SilentAcquisitionUnavailable`                                    | Silent AzureAuth acquisition is not implemented; explicit interactive login affects interactive operations only, with no automatic protocol remediation. |
| `AzureAuthLaunchContextRequired` and launch-context codes         | Run from WSL with a validated Windows host-interoperability launch context and browser evidence.                                                         |
| `CredentialAcquisitionCanceled` / `CredentialAcquisitionTimedOut` | Retry after cancellation or timeout.                                                                                                                     |

Status and doctor print the actual provider, composition mode, separate interactive and silent
readiness, remediation, and safe codes. They never use generic provider-ready output while silent
protocol acquisition is unavailable. A production default, deferred provider, and test scaffold
are never reported ready. Tokens, configuration secrets, and process output are not printed.
