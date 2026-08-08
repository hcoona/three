# Phase 1.2 AzureAuth Suitability Gate

Status: **Superseded historical decision**

Date: **2026-06-05**

The later WP2, WP3, and WP6 implementation records supersede this gate's
direct-MSAL selection: AzureAuth 0.9.5 is now the implemented Windows, WSL, and
native Linux provider, while Direct MSAL remains unimplemented. The evidence
below is retained as decision history.

Decision ID: **phase-1.2-azureauth-suitability**

Gate name: **Phase 1.2 AzureAuth suitability gate**

Owner: **ID**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                               |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Gate status                | Closed for Phase 1.2 evidence gathering.                                                                                                                                               |
| Decision                   | Use the direct MSAL path for now behind the identity-provider abstraction. Do not make AzureAuth a required runtime substrate for the shared credential core or protocol adapters.     |
| Evidence scope             | Source inspection covers local AzureAuth source, installation scripts, documentation, and installed CLI help. No authenticated token acquisition was executed.                         |
| Implementation may proceed | Yes. Shared-core work may proceed with a direct MSAL provider plus fake-provider tests. AzureAuth remains an optional future helper only if later evidence proves a narrower safe use. |
| Phase 1R routing           | Not entered. AzureAuth suitability is an optional substrate gate; this decision does not block shared-core work or adapter evidence gates.                                             |

## Upstream Snapshot

Reference source inspected from the local mirror of
[AzureAD/microsoft-authentication-cli][upstream-repo]. The local mirror was clean
and resolved to commit
[`de20930c34b3b86c8a0ed7bbdeeca3f662dae918`][upstream-commit], described as
`0.9.6-3-gde20930`.

Commands used to identify the snapshot:

```bash
git -C /workspace/public/microsoft-authentication-cli --no-pager rev-parse HEAD
git -C /workspace/public/microsoft-authentication-cli --no-pager remote -v
git -C /workspace/public/microsoft-authentication-cli --no-pager describe --tags --always --dirty
git -C /workspace/public/microsoft-authentication-cli --no-pager status --short
```

Results:

```text
HEAD: de20930c34b3b86c8a0ed7bbdeeca3f662dae918
origin: https://github.com/AzureAD/microsoft-authentication-cli.git
version description: 0.9.6-3-gde20930
status --short: no output
```

Installed CLI help was also inspected from `/usr/bin/azureauth`, which reported
`0.9.6.0`. The installed binary was used only for help and version output.

## Decision Rationale

AzureAuth is a useful Microsoft Entra public-client authentication CLI and a
source reference for Azure DevOps token-oriented flows, but the inspected version
does not satisfy this product's Phase 1.2 substrate constraints as a required
runtime dependency.

The main blockers are:

1. AzureAuth is centered on user public-client flows and Azure DevOps PAT/token
   helpers. The product identity matrix is already frozen for Phase 1A and
   Phase 2: browser, device code, narrow PAT compatibility, and Azure Pipelines
   system access token are accepted; service principal, managed identity, and
   workload identity federation are deferred.
2. AzureAuth non-interactive behavior is environment-driven and platform-specific
   rather than an explicit per-request policy surface matching this product's
   adapter needs.
3. AzureAuth's MSAL cache can fall back to an unprotected file on headless Linux,
   which conflicts with this product's current fail-closed secure-cache design.
4. AzureAuth is a human-facing CLI whose token modes intentionally print tokens
   to stdout. A protocol adapter could shell out safely only with strict process
   isolation, stderr/stdout discipline, and redaction wrappers that are not yet
   source-confirmed.
5. AzureAuth installation is an external executable distribution and PATH
   dependency. Making it mandatory would couple this product's adapters to a
   second installer and update channel.

The accepted path is therefore direct MSAL integration in the shared credential
core, hidden behind an identity-provider abstraction. This preserves architecture:
AzureAuth, if revisited later, can sit behind the abstraction; it never replaces
Git, NuGet, Python keyring, keyring executable, or npm-compatible host-tool
adapters.

## Evidence by Gate Criterion

### Required Audiences

Evidence:

- `azureauth aad` accepts arbitrary `--resource`, `--scope`, `--client`, and
  `--tenant` inputs, so it can request public-client tokens for caller-provided
  audiences when the caller supplies app registration details
  ([`CommandAad.cs`][command-aad-options]).
- `azureauth ado token` hard-codes the Visual Studio public-client ID and Azure
  DevOps default scope `499b84ac-1321-427f-aa17-267ca6975798/.default`
  ([`Ado/AuthParameters.cs`][ado-auth-parameters],
  [`Ado/Constants.cs`][ado-constants]).
- `azureauth ado pat` supports Azure DevOps PAT scopes, including packaging and
  code scopes, and validates them against a local known-scope list
  ([`Scopes.cs`][ado-scopes]).

Finding:

AzureAuth covers Microsoft Entra public-client tokens and Azure DevOps bearer or
PAT-oriented outputs. This aligns only with the accepted browser, device-code,
narrow PAT compatibility, and Azure Pipelines system access token portions of
the frozen product identity matrix. It does not add MVP support for the deferred
service principal, managed identity, or workload identity federation flows. The
Azure Pipelines system access token appears only as PAT-like environment input
for `ado token`, not as a complete product policy model.

Decision impact:

Use direct MSAL for the core so the frozen Phase 1A and Phase 2 identity matrix
can be implemented without being constrained by AzureAuth's current public-client
CLI surface.

### Non-Interactive Behavior

Evidence:

- `AZUREAUTH_NO_USER` or `Corext_NonInteractive=1` disables interactive auth
  through `InteractiveAuthDisabled` ([`IEnvExtensions.cs`][env-extensions]).
- When interaction is disabled, AzureAuth allows only IWA on Windows, or only
  broker silent auth on non-Windows when broker was selected
  ([`AuthModeExtensions.cs`][auth-mode-extensions]).
- `ado token` returns `SYSTEM_ACCESSTOKEN` only in an Azure DevOps Pipeline
  environment, ignores it on developer machines, and fails in Azure Pipelines
  when no system token is present ([`CommandToken.cs`][command-token-pipeline]).
- Installed CLI help exposes `--mode`, `--timeout`, and token output options, but
  no explicit product-level `--non-interactive` flag was observed in help.

Finding:

AzureAuth has some non-interactive safeguards, but they are not the same as this
product's required per-request adapter policy. In particular, explicit CI mode
for the current MVP must accept only the explicit Azure Pipelines system access
token flow under the frozen non-persistent CI policy, must not advertise PAT
compatibility as usable in explicit CI mode, must not imply MVP support for the
deferred service principal, managed identity, or workload identity federation
flows, and must forbid desktop-cache discovery by default. Narrow explicit PAT
compatibility remains accepted only when the PAT request itself is an accepted
MVP request outside explicit CI availability.

Decision impact:

Implement non-interactive policy in this product's credential core rather than
delegating policy to AzureAuth environment conventions.

### MSAL and Cache Reuse

Evidence:

- AzureAuth states that it uses MSAL for authentication and caching
  ([`README.md`][readme-msal]).
- The source builds MSAL public clients with `WithLogging(...,
enablePiiLogging: false, ...)` and wires a per-tenant MSAL cache helper in
  cached, web, device-code, and IWA flows ([`CachedAuth.cs`][cached-auth],
  [`Web.cs`][web-auth], [`DeviceCode.cs`][device-code-auth],
  [`IntegratedWindowsAuthentication.cs`][iwa-auth]).
- The MSAL cache uses platform stores on macOS and Linux keyring when available
  ([`PCACache.cs`][pca-cache-secure]).
- On headless Linux, cache persistence failure triggers an unprotected file
  fallback under `~/.azureauth` ([`PCACache.cs`][pca-cache-plaintext]).
- The product's mid-level design requires fail-closed behavior when secure store
  cannot be opened and forbids silent plaintext fallback
  (`mid-level-design.md`, error class `CacheUnavailable`).

Finding:

AzureAuth can reuse MSAL cache state, but its fallback behavior does not match
the current secure-cache policy. The cache key is per tenant and MSAL client; it
does not encode this product's required ecosystem, feed, host, audience, and
credential-kind partitioning.

Decision impact:

Direct MSAL lets the product enforce its own cache partitioning and fail-closed
secure-store behavior while still using MSAL primitives.

### Logging and Redaction

Evidence:

- Telemetry is off by default unless an Application Insights token is supplied by
  environment variable or registry key ([`README.md`][readme-telemetry],
  [`Program.cs`][program-telemetry]).
- The Lasso telemetry config hides alias and machine name and collects only a
  small explicit environment variable list ([`Program.cs`][program-telemetry]).
- MSAL logging is configured with PII logging disabled in inspected auth flows
  ([`CachedAuth.cs`][cached-auth], [`Web.cs`][web-auth]).
- AzureAuth intentionally writes token material to stdout for `--output token`,
  JSON token output, `ado token`, and PAT output modes
  ([`CommandAad.cs`][command-aad-output], [`CommandToken.cs`][command-token-output],
  [`CommandPat.cs`][command-pat-output]).
- `ado token` and `ado pat` avoid the logger for token/PAT output, reducing log
  leakage risk but still producing secrets on stdout by design
  ([`CommandToken.cs`][command-token-output], [`CommandPat.cs`][command-pat-output]).

Finding:

AzureAuth has useful logging safeguards, but it is not a protocol-adapter logging
boundary. Its stdout is a token-delivery channel, while this product's adapters
must guarantee protocol-valid stdout and route all diagnostics elsewhere.

Decision impact:

Do not shell out to AzureAuth from protocol adapters as the default substrate.
If future work reintroduces AzureAuth, it must be wrapped behind a strict
identity-provider process boundary with exact stdout parsing, stderr capture,
timeouts, and redaction tests.

### Installation and Distribution

Evidence:

- Windows installation downloads a versioned GitHub release, installs under
  `%LOCALAPPDATA%\Programs\AzureAuth`, and updates the user PATH unless disabled
  by option ([`install.ps1`][install-ps1]).
- macOS installation downloads a tarball under `$HOME/.azureauth` and updates
  Bash/Zsh profiles unless disabled by environment variable
  ([`install.sh`][install-sh]).
- Linux installation downloads a versioned `.deb`, defaults its download location
  to `/tmp`, and installs through `sudo dpkg -i` to `/usr/bin/azureauth`
  ([`linux-install.sh`][linux-install-sh]).
- The README says release versions must be explicitly provided; the scripts do
  not discover the latest release automatically ([`README.md`][readme-install]).

Finding:

AzureAuth's installer model is suitable for AzureAuth as a standalone tool, but
it would add a separate mandatory executable, update channel, PATH dependency,
and privilege requirement to this product. That is not necessary for the shared
credential core.

Decision impact:

Keep AzureAuth optional. Product installation should own its own adapter
placement and identity implementation.

### Protocol-Adapter Isolation

Evidence:

- AzureAuth's root command is a human-facing MSAL CLI with `aad`, `ado`, and
  `info` subcommands ([`CommandAzureAuth.cs`][command-root]).
- It does not implement Git credential-helper stdin/stdout, NuGet plugin
  handshake, Python keyring backend, keyring executable `get`, or npm/Yarn config
  update protocols in the inspected source.
- The current project design requires AzureAuth, if used, to sit below the shared
  credential core or behind an identity-provider abstraction, not to replace
  adapters (`high-level-design.md` and `mid-level-design.md`).

Finding:

AzureAuth is not a host-tool protocol adapter. Treating it as one would violate
the current architecture and host-tool protocol boundaries.

Decision impact:

Preserve thin adapters for Git, NuGet, Python keyring, keyring executable, and
npm-compatible config workflows. The identity-provider abstraction is the only
allowed integration point for AzureAuth.

## Cheap Local Commands and Results

Source snapshot commands are listed above. Additional local commands:

```bash
azureauth --help
azureauth aad --help
azureauth ado --help
azureauth ado token --help
azureauth info
```

Results:

```text
azureauth --help: exit 0; version banner 0.9.6.0; commands aad, ado, info.
azureauth aad --help: exit 0; options include --resource, --client, --tenant,
  --scope, --mode, --output, --domain, --timeout.
azureauth ado --help: exit 0; subcommands pat and token.
azureauth ado token --help: exit 0; options include --output, --tenant, --mode,
  --domain, --timeout, --prompt-hint.
azureauth info: exit 0; reported AzureAuth Version: 0.9.6.0.
```

Attempted source help through `dotnet run`:

```bash
DOTNET_CLI_HOME="$PWD/.copilot-scratch/dotnet-home" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
dotnet run --project src/AzureAuth/AzureAuth.csproj -- --help
```

Result:

```text
exit 1
NU1301: Unable to load the service index for source
https://pkgs.dev.azure.com/office/_packaging/Office/nuget/v3/index.json.
Response status code does not indicate success: 401 (Unauthorized).
```

The restore failure is consistent with the repository's `nuget.config`, which
requires `ADO_TOKEN=$(azureauth ado token) dotnet restore` for the private Office
feed. No authenticated restore or token acquisition was attempted for this gate.
The disposable `.copilot-scratch` directory was removed after the attempt.

## Security Risks

- **Plaintext cache fallback risk:** AzureAuth can fall back to unprotected cache
  files on headless Linux. This product currently requires secure-store
  fail-closed behavior.
- **Secret stdout risk:** AzureAuth token and PAT modes print secrets to stdout
  by design. This is acceptable for a credential CLI but not sufficient as an
  unwrapped protocol-adapter substrate.
- **Policy mismatch risk:** AzureAuth environment variables and defaults do not
  encode this product's full interactive, CI, persistence, and PAT policy.
- **External executable risk:** A mandatory AzureAuth dependency would add PATH,
  installation integrity, version skew, and update-channel concerns to every
  adapter invocation.
- **Scope gap risk:** Required MVP flows are limited by the frozen Phase 1A and
  Phase 2 identity matrix. AzureAuth source inspection does not prove support for
  the deferred service principal, managed identity, or workload identity
  federation flows.

## Follow-ups

1. Preserve the frozen Phase 1A and Phase 2 identity matrix: accepted browser,
   device code, narrow PAT compatibility, and Azure Pipelines system access token
   flows; deferred service principal, managed identity, and workload identity
   federation flows.
2. Phase 6 should implement the identity-provider abstraction with a fake
   provider first, then a direct MSAL provider that enforces product cache and
   redaction policy.
3. If AzureAuth is reconsidered, scope it to an optional identity-provider
   backend only. Required evidence must include exact stdout parsing, stderr
   isolation, no plaintext fallback, version pinning, installer integrity, and
   protocol-adapter tests.
4. Secure-cache Phase 1.6 must decide final platform store behavior and confirm
   no plaintext fallback for this product.
5. Packaging phases should not assume AzureAuth is present on PATH.

## Affected Requirements and Designs

- `requirements.md`: Functional requirement 12 is satisfied by evaluation and a
  direct MSAL decision for now.
- `high-level-design.md`: The identity-provider abstraction remains the
  integration boundary; AzureAuth does not replace adapters.
- `mid-level-design.md`: The credential core should proceed with direct MSAL
  readiness, fake-provider tests, product-owned secure-cache policy, and adapter
  isolation.
- `project-breakdown.md`: Phase 1.2 exit criterion is satisfied with a direct
  MSAL decision. Shared-core work is not blocked.

[ado-auth-parameters]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Ado/AuthParameters.cs#L12-L18
[ado-constants]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Ado/Constants.cs#L33-L61
[ado-scopes]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AdoPat/Scopes.cs#L13-L160
[auth-mode-extensions]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/AuthModeExtensions.cs#L22-L43
[cached-auth]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/CachedAuth.cs#L88-L106
[command-aad-options]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAad.cs#L147-L217
[command-aad-output]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAad.cs#L402-L415
[command-pat-output]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/Ado/CommandPat.cs#L153-L174
[command-root]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAzureAuth.cs#L15-L28
[command-token-output]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/Ado/CommandToken.cs#L143-L145
[command-token-pipeline]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/Ado/CommandToken.cs#L92-L118
[device-code-auth]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/DeviceCode.cs#L75-L101
[env-extensions]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/IEnvExtensions.cs#L24-L71
[install-ps1]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/install/install.ps1#L115-L233
[install-sh]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/install/install.sh#L122-L197
[iwa-auth]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/IntegratedWindowsAuthentication.cs#L87-L105
[linux-install-sh]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/install/linux-install.sh#L57-L91
[pca-cache-plaintext]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCACache.cs#L90-L156
[pca-cache-secure]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCACache.cs#L72-L88
[program-telemetry]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Program.cs#L62-L121
[readme-install]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/README.md#L28-L90
[readme-msal]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/README.md#L10-L12
[readme-telemetry]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/README.md#L96-L109
[upstream-commit]: https://github.com/AzureAD/microsoft-authentication-cli/commit/de20930c34b3b86c8a0ed7bbdeeca3f662dae918
[upstream-repo]: https://github.com/AzureAD/microsoft-authentication-cli
[web-auth]: https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/Web.cs#L97-L118
