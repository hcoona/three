# Unified Azure DevOps Credential Provider Mid-Level Design

Status: **Draft mid-level design baseline**

## Design Scope

This document refines the requirements and high-level design into implementable
module boundaries, adapter contracts, data models, and verification plans. It
follows a waterfall progression: requirements baseline, analysis model,
architecture allocation, module design, interface design, data design, security
design, verification design, and implementation sequencing.

The design covers Azure Repos Git, Azure Artifacts NuGet feeds, Azure Artifacts
Python feeds, and Azure Artifacts npm-compatible feeds. It does not define a Git
transport, a package manager, a feed proxy, or Azure DevOps authorization
policy.

## Design Inputs and Traceability

| Source                 | Design dependency                                                                                                                                          |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `requirements.md`      | Product boundary, functional requirements, non-functional requirements, ecosystem-specific integration requirements, and design-phase acceptance criteria. |
| `research.md`          | Host-tool discovery constraints, local protocol experiments, source inventory, package-feed credential dependency chain, and security findings.            |
| `high-level-design.md` | Hybrid entry point architecture, shared credential core, primary CLI, machine-facing adapters, cache model, CI model, diagnostics, and removal semantics.  |

The central design decision remains unchanged: one product owns credential
behavior through one shared credential core, while thin machine-facing adapters
exist only where host tools require specific discovery or protocol shapes.

## Waterfall Design Baseline

### Requirements Baseline

The product must provide a consistent developer and CI experience across four
ecosystems without pretending that those ecosystems share one invocation model.
The human-facing CLI owns login, setup, diagnostics, and removal. Host-tool
adapters own protocol-compatible I/O and discovery.

### Analysis Model

The system has three classes of actors:

1. Human users who run CLI commands such as `login`, `configure`, `doctor`, and
   `unconfigure`.
2. Host tools that invoke adapters through native mechanisms such as Git
   credential helper commands, NuGet plugin launch, Python keyring discovery,
   subprocess `keyring`, and npm/Yarn configuration files.
3. CI systems that provide non-interactive identity material and require
   ephemeral, log-safe credential behavior.

The primary analysis constraint is that host-tool protocol stdout is not a user
interface. Protocol adapters must never write banners, diagnostics, update
notices, or human help text to stdout when running under a host-tool protocol.

### Architecture Allocation

The high-level architecture is allocated into the following mid-level modules:

| Module                  | Primary responsibility                                                                                                                     | Depends on                                                                              |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------- |
| Primary CLI             | User commands, orchestration, configuration, diagnostics, and removal.                                                                     | Configuration manager, credential core, installer, doctor engine.                       |
| Credential core         | Canonicalization, identity selection, token acquisition, token exchange, cache policy/key construction, redaction, and policy enforcement. | Identity provider abstraction, cache policy/key model, policy engine, diagnostics sink. |
| Adapter host library    | Shared adapter bootstrapping, mode detection, structured diagnostics routing, and fatal-error mapping.                                     | Credential core, diagnostics sink.                                                      |
| Installer               | Places adapter entry points, package artifacts, and shims where host tools can discover them.                                              | Configuration manager, platform profile, filesystem abstraction.                        |
| Git adapter             | Git credential helper protocol and Azure Repos URL handling.                                                                               | Adapter host library, credential core.                                                  |
| NuGet adapter           | NuGet plugin protocol and authentication request handling.                                                                                 | Adapter host library, credential core.                                                  |
| Python keyring backend  | Python keyring import-mode integration.                                                                                                    | Small Python adapter package, fixed external helper executable.                         |
| Keyring executable shim | Subprocess keyring command compatibility for uv and pip subprocess mode.                                                                   | Adapter host library, credential core.                                                  |
| npm adapter             | npm, pnpm, and Yarn registry analysis plus credential refresh change planning.                                                             | Credential core, npm/Yarn config parser.                                                |
| Configuration manager   | Owned configuration writes, manifests, dry-run output, and removal.                                                                        | Platform profile, filesystem abstraction, host-tool configuration adapters.             |
| Doctor engine           | Deterministic validation of discovery, configuration, cache, identity, and CI settings.                                                    | Configuration manager, platform probes, credential core diagnostics.                    |

### Module Design

Each adapter is intentionally small. It validates host-tool input, converts it
into a normalized credential request, delegates credential acquisition to the
core, and serializes only protocol-valid output.

The credential core is intentionally unaware of host-tool stdout formats. It
returns structured success, no-credential, interaction-required, unauthorized,
and fatal-error results to callers. Each caller maps those results into the
protocol behavior required by its host tool.

### Interface Design

The internal interfaces are expressed as language-neutral contracts so that the
implementation can choose a library, executable, or broker shape without
changing adapter semantics.

```text
CredentialRequest
  ecosystem
  operation
  host
  organization
  project?
  feed?
  repository?
  serviceIdentity
  serviceEndpoint
  accountHint?
  tenantHint?
  requestedAudience
  credentialKind
  identityFlow
  interactivePolicy
  cachePolicy
  ciContext?

CredentialResult
  status
  username?
  password?
  bearerToken?
  expiresAt?
  account?
  tenant?
  cacheKey?
  diagnosticsCorrelationId
```

Adapters must treat the returned credential fields as sensitive even when the
field name is `password` and the value is a generated token or short-lived
secret.

### Data Design

The MVP persistent data model contains product-owned configuration ownership
state only. It records which host-tool settings and adapter registrations were
installed by this product so removal can be scoped and reversible.

Product-owned persistent derived credential-cache state is future/deferred and
disabled by default. MVP credential requests use `noCache`,
`productPersistentCacheDisabled`, `nonPersistentCi`, or cache-key-only semantics
as appropriate. Cache keys are still modeled so protocol responses and allowed
provider/host-tool cache reuse can be partitioned consistently without creating
a product-owned persistent cache.

Configuration ownership state must not contain secrets.

### Verification Design

Verification must prove protocol shape, not only happy-path authentication.
Tests and diagnostics must exercise host-tool discovery through the host tool
where practical: Git resolves the helper through Git, NuGet launches the plugin
in plugin mode, Python import mode loads the keyring backend in the target
environment, subprocess mode resolves the intended `keyring` executable, and the
configuration manager applies npm-compatible change plans to the intended config
scope.

## Shared Credential Core

### Responsibilities

The shared credential core owns behavior that must remain consistent across all
ecosystems:

- Azure DevOps host canonicalization.
- Feed, project, organization, and repository identity extraction.
- Account and tenant selection.
- Interactive login policy.
- Non-interactive identity flow policy.
- Token acquisition and refresh.
- Token exchange into host-tool-compatible credentials when required.
- Cache policy enforcement and cache-key partitioning.
- Secret redaction.
- Diagnostic event production.

The core uses AzureAuth (`microsoft-authentication-cli`) 0.9.5 for the current
Windows, WSL, and native Linux identity path. The provider is selected and bound
explicitly. Windows and WSL derive the executable from the official per-user
installation layout; native Linux uses the official package payload. Direct
MSAL remains unimplemented behind the same identity-provider abstraction.

### Core Submodules

| Submodule                     | Responsibility                                                                                                                                                                                                       |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Canonicalization service      | Converts user input, host-tool URLs, and registry endpoints into normalized Azure DevOps resource identities.                                                                                                        |
| Identity selector             | Resolves account, tenant, service identity, and requested authentication flow.                                                                                                                                       |
| Identity provider abstraction | Acquires Microsoft Entra or Azure DevOps-compatible tokens through the identity flows selected for the current product phase. Candidate flows are gated in the identity policy table and required-prototype section. |
| Token exchange service        | Converts acquired identity material into the credential form expected by Azure Repos or Azure Artifacts host tools.                                                                                                  |
| Cache policy/key model        | Builds partitioned cache keys and enforces `noCache`, `productPersistentCacheDisabled`, `nonPersistentCi`, cache-unavailable, and future persistent-cache policy results.                                            |
| Policy engine                 | Enforces interactive, CI, persistence, allowed-host, and allowed-credential-kind decisions.                                                                                                                          |
| Redaction service             | Sanitizes logs, diagnostics, dry-run output, and errors.                                                                                                                                                             |
| Diagnostic event emitter      | Emits structured events to stderr, log files, or caller-provided sinks without polluting protocol stdout.                                                                                                            |

### Credential Request Normalization

All adapters submit requests through the same normalization pipeline:

1. Parse host-tool input into an adapter-local request.
2. Canonicalize Azure DevOps host, organization, project, feed, and service
   endpoint.
3. Assign ecosystem and token audience.
4. Resolve interaction policy from host-tool flags, CLI flags, and CI context.
5. Resolve cache policy from command mode and configuration.
6. Enforce product policy before token acquisition.
7. Acquire or refresh credential material.
8. Return a structured result to the adapter.

Unsupported hosts must be represented as an explicit no-credential result, not
as a successful empty credential. Fatal parsing, policy, cache, and identity
errors must be surfaced through adapter-specific error behavior and safe
diagnostics.

### Cache Key Model

Credential cache keys use the frozen `azdo-cache-v1` partition schema:

```text
ecosystem
azureDevOpsHost
organization
project?
feed?
repository?
serviceIdentity
account
tenant
tokenAudience
credentialKind
```

Package-feed credentials include project and feed identity when those components
are present in the canonical Azure Artifacts resource identity. Azure Repos Git
resource identity may carry validated project and repository values, but the
default Phase 2 Git cache key is host plus organization and omits project, feed,
and repository partitions. Repository, and any Git project partition coupled to
it, is included only if a future explicit per-repository Git policy enables that
finer partitioning. No adapter may read or write a generic "current token" that
is not scoped by ecosystem and audience.

### Result Classes

| Result              | Meaning                                                                           | Adapter behavior                                                              |
| ------------------- | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Success             | Credential material is available and policy-compliant.                            | Serialize protocol-valid credential output.                                   |
| NoCredential        | The request is unsupported or no matching credential will be supplied.            | Return host-tool-specific no-credential behavior without fabricating success. |
| InteractionRequired | Credential acquisition requires interaction that policy does not currently allow. | Fail closed with safe diagnostics or host-tool-specific retry guidance.       |
| Unauthorized        | Identity was acquired but does not authorize the requested resource.              | Surface a redacted authorization failure.                                     |
| Fatal               | Input, cache, platform, or identity provider operation failed.                    | Return protocol-safe failure and send details only to diagnostic sinks.       |

## Primary CLI

### Command Groups

The primary CLI exposes a stable human-facing command model:

```text
<primary-cli> login
<primary-cli> logout
<primary-cli> status
<primary-cli> configure git
<primary-cli> configure nuget
<primary-cli> configure python
<primary-cli> configure npm
<primary-cli> unconfigure git
<primary-cli> unconfigure nuget
<primary-cli> unconfigure python
<primary-cli> unconfigure npm
<primary-cli> doctor
```

`<primary-cli>` is a substitution placeholder until the executable name is
chosen.

### CLI Responsibilities

The CLI owns:

- Explicit user login and logout.
- Account and tenant selection.
- Ecosystem setup and removal orchestration.
- Dry-run and verbose diagnostic presentation.
- CI bootstrap guidance.
- Installation verification.
- Cache-key diagnostics and future-cache cleanup commands when a later phase
  enables product-owned persistent derived credential caching.
- Configuration ownership manifest management.

The CLI must not require users to learn machine-facing adapter commands for
normal use. Adapter commands may exist for testing and host-tool integration,
but they are not the product's primary user interface.

### Configuration Ownership Manifest

The configuration manager is the only module allowed to perform persistent
configuration mutations. Adapters and installers produce declarative change
plans; the configuration manager validates those plans, applies approved writes,
and records exact selectors for later removal.

Configuration commands must record product-owned writes in a manifest outside
repository-local configuration unless an explicit project-scoped setup mode is
selected. The manifest enables safe removal and diagnostics.

```text
ConfigurationOwnershipManifest
  manifestId
  ownerProductId
  scope
  entrySelector
  resourceIdentity?
  productVersion?
  safeMetadata
  entries:
    - sequence
      targetKind
      targetPathOrName
      key
```

The manifest is an operational selector sidecar, not a tamper-proof history. It
records only the target identity needed for precise cleanup and never stores
credential values or value hashes. Each ecosystem adapter must define its own
canonical entry selector and precedence model before implementing `configure`:

| Ecosystem | Canonical entry selector                                                                                                     | Precedence model                                                                                                                                           |
| --------- | ---------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Git       | Config scope, URL subsection when present, key, and exact helper value for multi-value keys.                                 | Git's effective config order, with explicit handling for multi-value `credential.helper`.                                                                  |
| NuGet     | Plugin installation path, runtime family, and explicit plugin-path environment or config setting when used.                  | NuGet conventional plugin discovery plus explicit plugin-path overrides.                                                                                   |
| Python    | Python environment identity, package distribution name, backend registration, shim path, and PATH insertion point when used. | Python environment import path and process PATH order.                                                                                                     |
| npm/pnpm  | Config file scope, registry URL selector, and auth key selector.                                                             | npm-compatible layered config order for user, global, explicit path, and CI temporary configuration; workspace files are read through `WorkspaceReadOnly`. |
| Yarn      | `.yarnrc.yml` path, `npmScopes` or `npmRegistries` selector, and auth field selector.                                        | Yarn 4+ configuration resolution with Phase 1.4-approved user-level and CI-temporary change-plan targets; direct adapter writes remain disallowed.         |

The default removal strategy is surgical deletion of product-owned entries.
Each target mutation uses the normal lower-layer write behavior without
cross-target recovery machinery. If a configure operation would replace a
non-owned entry, it stops with
`ConfigConflict`. Product-owned temporary containers are deleted during normal
remove behavior.

Persistent write paths must use this flow:

1. The adapter, installer, or CLI command emits a `ConfigurationChangePlan`.
2. The configuration manager evaluates scope, precedence, conflicts, and policy.
3. Dry-run output is generated from the same plan that would be applied.
4. The configuration manager applies the write.
5. The configuration manager updates the ownership sidecar after the target
   write completes.

Adapters must not directly write Git config, NuGet plugin registration state,
Python backend configuration, PATH shim placement, npm config, Yarn config, or
temporary CI config files. Product-owned temporary files are still created by the
configuration manager so cleanup and `doctor` checks use the same ownership
metadata.

```text
ConfigurationChangePlan
  id
  ecosystem
  targetScope
  targetPathOrConfigKey
  entrySelector
  operation
  intendedCanonicalValue
  conflictPolicy
  containsCredentialMaterial
  expiresAt?
```

Plans with `containsCredentialMaterial=true` require an explicit write policy
decision before they can be applied. Plans that target repository-local files are
rejected by default unless the command mode explicitly allows that scope.

### Dry-Run Behavior

Every `configure` and `unconfigure` command must support dry-run output that
shows intended files, configuration scopes, and host-tool commands. Dry-run
output must not include access tokens, refresh tokens, generated passwords,
Basic auth headers, npm tokens, NuGet API keys, or PAT values.

## Adapter Host Library

The adapter host library is the shared adapter runtime. It provides explicit
routing for the product's known entry-point shapes, diagnostics routing, and
error handling.

Responsibilities:

- Match the small, explicit set of supported executable and argument shapes.
- Initialize the credential core with adapter-appropriate policy.
- Provide stdout and stderr discipline helpers.
- Route diagnostic events away from protocol stdout.
- Convert core result classes into adapter-local return codes.
- Apply redaction to errors before they leave the process.

The adapter host library must not parse Git, NuGet, Python, or npm protocol
payloads. Protocol parsing remains in ecosystem adapters.

Routing relies on normal `Path` APIs and ordered exact or prefix argument
matching. It does not implement a general constraint lattice or parse native
object-manager path namespaces.

## Git Adapter Design

### Entry Point

The Git adapter is installed as:

```text
git-credential-<helper-name>
```

The default resulting Git configuration is:

```text
[credential]
  helper = <helper-name>
[credential "https://dev.azure.com"]
  useHttpPath = true
```

`<helper-name>` is a substitution placeholder. The installed helper executable
must be discoverable by Git itself, not only by the current shell.

`configure git` must write these settings to a private product Git config
through ConfigurationManager and activate it with an explicitly marked,
product-owned `[include]` block in the selected user-global Git config. It must
not invoke `git config --global` as its writer implementation. Target selection
uses `~/.gitconfig` if it exists, otherwise the existing XDG Git config file,
otherwise `~/.gitconfig`. Configure and unconfigure fail closed on modified
markers, collisions, or unrecognized private state; unconfigure removes only
the exact owned include block and private entries. Doctor queries effective
configuration with real `git config --global --includes` commands, does not
override `GIT_CONFIG_GLOBAL`, and does not invoke credential helpers.

### Supported Operations

The helper must support:

```text
git-credential-<helper-name> get
git-credential-<helper-name> store
git-credential-<helper-name> erase
```

`get` reads Git credential fields from stdin and returns credential fields on
stdout only when the core supplies a credential. `store` and `erase` must be
implemented because Git helpers may invoke them as part of the credential
lifecycle. They may delegate cache updates to the core when the credential is
owned by this product only after a future persistent-cache feature is explicitly
enabled; in MVP they must return no-op/cache-disabled behavior and must not
create product-owned persistent derived host-tool credentials.

### Input Mapping

| Git field  | Normalized request field                                                                                   |
| ---------- | ---------------------------------------------------------------------------------------------------------- |
| `protocol` | Scheme validation; HTTPS is required.                                                                      |
| `host`     | Azure DevOps host canonicalization.                                                                        |
| `path`     | Organization and repository extraction for `dev.azure.com`; repository path for legacy hosts when present. |
| `username` | Account hint when supplied.                                                                                |

For `dev.azure.com`, the organization is in the path, so
`credential.https://dev.azure.com.useHttpPath=true` is required. For legacy
`<org>.visualstudio.com` hosts, the organization is in the host name.

### Output Discipline

In `get` mode, stdout may contain only Git credential protocol fields such as:

```text
username=<value>
password=<value>
```

Diagnostics, warnings, and prompts must be routed to stderr or a configured
diagnostic sink. If interaction is required but unavailable, the helper must not
print partial credentials.

### Doctor Checks

`doctor` validates Git integration by:

1. Asking Git to resolve the configured helper.
2. Confirming `dev.azure.com` path forwarding when Azure Repos HTTPS remotes are
   present.
3. Testing helper protocol shape with a synthetic Azure Repos credential record.
4. Verifying that no shell-snippet helper is installed by default when a helper
   executable is available.
5. Reporting unrelated credential helpers without removing them.

## NuGet Adapter Design

### Entry Point

The NuGet adapter must be plugin-shaped because NuGet launches plugin files with
fixed plugin arguments. The adapter cannot rely on NuGet invoking an arbitrary
standard subcommand such as:

```text
<primary-cli> nuget plugin
```

Default setup for Phase 4D MVP uses conventional NuGet `netcore` plugin
installation for `dotnet` restore. `NUGET_PLUGIN_PATHS` and
`NUGET_NETCORE_PLUGIN_PATHS` are optional process-scoped explicit override and
diagnostic mechanisms, not default global configuration, and must not be
persisted by MVP configure flows.

### Plugin Modes

The NuGet adapter supports at least:

- Plugin handshake.
- Authentication request handling.
- Cancellation and timeout behavior required by NuGet clients.
- Non-interactive restore behavior.
- Protocol-safe error reporting.

The exact message schema and version negotiation must be implemented from the
NuGet plugin protocol source and documentation during implementation. This
mid-level design does not invent message fields beyond the source-confirmed
requirement that NuGet launches plugin entry points in plugin mode and expects a
plugin protocol.

### Request Mapping

| NuGet concept         | Normalized request field                                              |
| --------------------- | --------------------------------------------------------------------- |
| Package source URI    | Host, organization, project, feed, endpoint kind, and token audience. |
| Is retry              | Cache refresh policy.                                                 |
| Non-interactive flags | Interactive policy.                                                   |
| Client runtime        | Adapter runtime compatibility and plugin layout.                      |

Azure Artifacts NuGet feed endpoints include organization-scoped and
project-scoped `nuget/v3/index.json` URLs. Canonicalization must preserve the
project component when present.

### Interactive Policy

The plugin honors NuGet's interactive policy through the source-confirmed
`IsNonInteractive` and `CanShowDialog` request values:

- `dotnet restore` uses `--interactive` for first-time user interaction.
- MSBuild restore uses `/p:NuGetInteractive=true` when deferred `netfx` support
  is implemented.
- NuGet.exe behavior may allow prompting depending on the invoking client when
  deferred `netfx` support is implemented.
- `IsNonInteractive=true` always maps to browser, `Never`, and `SilentOnly`.
- Otherwise `CanShowDialog=true` maps to browser, `HostToolAllows`, and
  `InteractionAllowed`.
- Otherwise the request maps to device code, `HostToolAllows`, and
  `InteractionAllowed`.

When interaction is disallowed, the adapter returns a safe failure that explains
the required explicit interactive invocation through NuGet-compatible channels.
The NuGet plugin does not own a human terminal prompt stream, so its device-code
shape remains unavailable even though explicit native Linux CLI login supports
AzureAuth device code. Retry metadata never authorizes interaction.

### Runtime Layout

The packaging plan distinguishes current MVP scope from deferred compatibility:

| Layout                                     | Purpose                                                                                         |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| `.nuget/plugins/netcore/<name>/<name>.dll` | Phase 4D MVP target for `dotnet` and .NET Core-compatible client scenarios.                     |
| `.nuget/plugins/netfx/<name>/<name>.exe`   | Deferred post-MVP target for NuGet.exe, Visual Studio, and MSBuild .NET Framework plugin hosts. |

The final artifact split remains an implementation decision, but the design must
preserve separate runtime compatibility where host clients require it when
deferred `netfx` scope is explicitly enabled.

### Doctor Checks

`doctor` validates:

1. Plugin file layout.
2. Runtime compatibility for available NuGet clients.
3. Plugin launch in plugin mode.
4. Azure Artifacts source URL canonicalization.
5. Interactive policy guidance.
6. Absence of global `NUGET_PLUGIN_PATHS` or `NUGET_NETCORE_PLUGIN_PATHS`
   conflicts unless explicitly chosen for process-scoped diagnostics.

## Python Adapter Design

### Adapter Shapes

Python support requires two adapter shapes:

1. A Python keyring backend package for import-mode tools such as twine and pip
   import mode.
2. A `keyring` executable-compatible shim for uv and pip subprocess mode.

A global primary CLI alone is insufficient for full Python coverage because
twine and pip import mode load Python keyring backends from the Python
environment that runs the tool.

### Keyring Backend Package

The MVP backend package is intentionally thin. It must:

- Register through Python keyring backend discovery.
- Recognize Azure Artifacts Python feed endpoints.
- Convert keyring `get_password` or equivalent backend calls into normalized
  credential requests.
- Delegate credential acquisition to a fixed external helper executable installed
  by this product.
- Return only the credential value expected by Python keyring.

The backend package must be installable into the exact Python environment that
runs pip or twine. Supported bootstrap paths include active virtual
environments, pipx-managed twine environments, tox/nox environments, and
isolated CI environments.

For MVP, `configure python` writes or updates backend configuration so the
backend can locate the product-owned helper by absolute path. That write is a
configuration-manager change plan, not a direct backend mutation.

The backend-helper protocol is versioned. The configured helper path names the
installed product apphost. MVP uses `keyring-helper-v2`:

```text
<absolute-product-apphost> python-keyring get
  --protocol-version 2
  --service <service>
  [--username <username>]
  [--mode password|creds]
```

The backend invokes the helper with a stable, non-shell command form. Before invocation, the backend resolves the configured absolute helper path and
checks ordinary existence and executable requirements. Release artifacts remain
subject to the separate package signing, provenance, and integrity policy.

Helper stdout is limited to the keyring response shape: password only for
`password` mode and newline-separated username and password for `creds` mode.
Diagnostics go only to stderr or the configured diagnostic sink.

If the configured helper path is missing or not executable, the backend fails
closed. Unsupported Azure Artifacts hosts return keyring no-credential behavior.
Malformed endpoints, protocol-version mismatches, and helper execution failures
return keyring-compatible hard failures with redacted diagnostics. Local brokers
and embedded shared libraries are deferred implementation options until source
inspection or prototypes prove they improve reliability without weakening adapter
isolation.

### Keyring Executable Shim

On POSIX platforms, `configure python` also creates a product-owned `keyring`
shim. The shim delegates to the wheel-provided `azureauth-keyring` console script
from the activated Python environment. It is activated through controlled PATH
ordering; it is not the absolute credential helper recorded in the backend
manifest.

Windows import mode uses the same backend manifest, but subprocess mode remains
deferred until a real `keyring.exe` launcher is implemented and validated.
Writing `keyring.cmd` would not satisfy uv's direct executable launch.
Reconfiguration removes any obsolete product-owned Windows shim placeholder
from earlier development builds.

The shim must support the command forms observed in local experiments:

```text
keyring get <service> <username>
keyring get <service> --mode creds
```

For password-only mode, stdout is the password. For `--mode creds`, stdout is
newline-separated username and password. No diagnostics may be written to
stdout.

### Request Mapping

| Python input              | Normalized request field                                              |
| ------------------------- | --------------------------------------------------------------------- |
| `service` URL             | Host, organization, project, feed, endpoint kind, and token audience. |
| `username`                | Account hint or protocol username depending on tool mode.             |
| `--mode creds`            | Request both username and password material.                          |
| Active Python environment | Bootstrap and doctor target scope.                                    |

pip subprocess mode requires a username in the index URL and may ignore a shim
installed only into the current Python environment's scripts directory. uv
resolves `keyring` from `PATH` and can request credential-pair mode.

### Environment Boundary

Keeping large credential logic outside project virtual environments reduces the
amount of trusted code imported into arbitrary environments, but it is not a
security boundary. The package manager process must receive credentials to
authenticate, so a compromised Python environment can observe returned
credentials.

### Doctor Checks

`doctor` validates:

1. Whether the backend package is importable from the selected Python
   environment.
2. Whether pip, twine, and uv are using import mode or subprocess mode.
3. Whether the intended `keyring` executable is first on the relevant `PATH`.
4. Whether Azure Artifacts Python feed URLs canonicalize correctly.
5. Whether credentials are absent from source-controlled package configuration.

## npm Adapter Design

### Entry Point

The npm-compatible adapter can be a normal CLI command:

```text
<primary-cli> npm
```

npm, pnpm, and Yarn do not invoke an Azure Artifacts credential-provider plugin.
They read registry configuration files. The adapter therefore reads registry
declarations, obtains credentials from the core, and emits configuration-manager
change plans for approved credential writes.

An npm-specific alias binary is deferred pending compatibility evidence. If a
future prototype justifies one, it must be a wrapper around this adapter rather
than a separate credential implementation.

### Configuration Inputs

The adapter reads:

- Workspace and user `.npmrc` files for npm and pnpm.
- `.yarnrc.yml` for Yarn 4+.
- Scoped registry declarations.
- Registry-specific authentication blocks.

For npm, pnpm, and Yarn 4+, the adapter emits user-level credential write
plans by default, even when registry declarations are read from workspace files.
Yarn 4+ user-level and CI-temporary change-plan generation is enabled under
the Phase 1.4 evidence gate and Phase 2 contract constraints. Direct adapter
writes remain disallowed, and writing credentials into repository-local files
requires an explicit user choice or a CI mode that uses
configuration-manager-owned temporary files.

### Request Mapping

| npm/Yarn input | Normalized request field                                              |
| -------------- | --------------------------------------------------------------------- |
| Registry URL   | Host, organization, project, feed, endpoint kind, and token audience. |
| Scope name     | Diagnostic context and registry selection.                            |
| Config scope   | Persistence policy.                                                   |
| CI mode        | Ephemeral write policy and cleanup guidance.                          |

Azure Artifacts npm feed endpoints include organization-scoped and
project-scoped registry URLs. Canonicalization must preserve project scope when
present.

### Write Policy

The default npm and pnpm write policy is:

1. Read workspace registry declarations.
2. Request feed-specific credentials.
3. Emit a generated-credential change plan for user-level configuration.
4. Avoid repository-local credential writes.
5. In CI, prefer configuration-manager-owned temporary config files or
   environment-provided configuration over persistent user-global writes.

Long-lived raw tokens must not be written to repository-local `.npmrc` files by
default.

### Lifecycle Scripts

The adapter may be invoked from a package-manager lifecycle hook only when the
CLI is already installed before dependency resolution begins. `pnpm:devPreinstall`
can refresh credentials for local pnpm workflows. npm `preinstall` is not a
reliable first-auth bootstrap for private dependency resolution and should not
be documented as the primary setup mechanism.

### Doctor Checks

`doctor` validates:

1. Registry declaration syntax.
2. Azure Artifacts registry canonicalization.
3. User-level versus workspace-level credential placement.
4. Yarn `npmScopes` and `npmRegistries` consistency for user-level and
   CI-temporary change plans.
5. CI temporary config behavior.
6. Absence of raw long-lived secrets in repository-local config files.

## Configuration Manager

### Scope Model

Configuration operations target one of these scopes:

| Scope             | Usage                                                                                                                                                              |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| User              | Default developer-machine setup for Git helper, NuGet plugin registration, Python backend manifest and controlled keyring-shim activation, and npm credentials.    |
| WorkspaceReadOnly | Workspace discovery, registry declarations, and project-specific diagnostics only; this scope authorizes inspection and diagnostics, not configuration writes.     |
| ExplicitPath      | User-selected writes to an explicit configuration path, including repository-local or tool-specific files only when the command mode and policy allow that target. |
| CiTemporary       | CI or shell-temporary configuration through configuration-manager-owned temporary files or environment activation metadata.                                        |
| Global            | Explicit global host-tool configuration when selected by the user or an implementation-specific installer policy.                                                  |

The configuration manager must make scope explicit in command output and
diagnostics. CI and shell temporary configuration maps to `CiTemporary`; tool
environment identity, such as a Python environment, pipx environment, NuGet
plugin folder, or package-manager config location, is target metadata rather than
a configuration scope.

### Install Operations

Install operations are idempotent. Re-running `configure` must:

1. Detect existing product-owned settings.
2. Validate whether the installed value still matches the expected shape.
3. Update stale product-owned settings when safe.
4. Report conflicts with unrelated user settings without overwriting them
   silently.
5. Record ownership metadata for new writes.

### Removal Operations

Removal operations must be scoped to product-owned settings:

- Git removal removes this product's helper configuration and path-forwarding
  setting only when owned or explicitly selected.
- NuGet removal removes this product's plugin installation or explicit
  plugin-path override without deleting package sources.
- Python removal removes this product's backend or shim registration from the
  targeted environment without removing unrelated keyring backends.
- npm removal removes generated credential entries while preserving registry
  declarations and unrelated configuration.

## Diagnostics and Observability

### Diagnostic Channels

The product has three diagnostic channels:

| Channel         | Intended content                                       | Secret policy                                                       |
| --------------- | ------------------------------------------------------ | ------------------------------------------------------------------- |
| Protocol stdout | Host-tool protocol responses only.                     | Credentials may appear only when required by the protocol response. |
| Human stdout    | CLI status, dry-run output, and guidance.              | Secrets must be redacted.                                           |
| Stderr/log sink | Warnings, errors, trace IDs, and detailed diagnostics. | Secrets must be redacted.                                           |

Protocol adapters must default to quiet operation and must never emit human text
to protocol stdout.

### Correlation IDs

Each credential request receives a diagnostic correlation ID. The ID can
appear in host-tool-safe errors and logs. It must not encode user names, tenant
IDs, organization names, feed names, or token material.

### Doctor Result Model

`doctor` produces structured checks:

```text
DoctorCheck
  contractMajor
  checkId
  status
  severity
  target
  summary
  diagnosticsCorrelationId
  observedValue
  expectedValue
  remediation
  safeDetails
```

Statuses are the frozen Phase 2 set: `pass`, `warning`, `fail`, `skipped`,
`unsupported`, `deferred`, and `notApplicable`. Warnings indicate risky but
non-blocking configuration. Failures indicate configuration that prevents the
integration from working. Unsupported results indicate a check that cannot run
for the current platform or host-tool capability. Deferred results indicate a
known future-phase check or capability that is intentionally outside the current
implementation gate.

## Security Design

### Identity Flow Policy

Identity flows are selected by mode and policy. Phase 1A accepts only the
explicit Azure Pipelines system access token for MVP CI, and only in explicit CI
mode with a non-persistent context. Service principal, managed identity,
workload identity federation, and other short-lived CI identities are deferred
future flows.

| Mode                              | Candidate primary flows                                                                                                                                                                                                                          | Forbidden fallback                                                                                   | PAT policy                                                                                                                                                                            |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Interactive developer command     | Interactive browser or device code through the selected identity provider.                                                                                                                                                                       | Silent PAT use, CI-only tokens, or service identity without explicit selection.                      | Explicit opt-in compatibility flow only.                                                                                                                                              |
| Non-interactive developer command | Allowed provider/host-tool cache reuse for the requested cache key, future persistent-cache reuse only after that deferred feature is enabled, or explicitly selected non-interactive identity.                                                  | New interactive prompt, device code prompt, or PAT fallback.                                         | Explicit opt-in only; never inferred from environment.                                                                                                                                |
| CI command                        | Explicit Azure Pipelines system access token only, and only in explicit CI mode with a non-persistent context. Service principal, managed identity, workload identity federation, and other short-lived CI identities are deferred future flows. | Interactive auth, device code, desktop cache discovery, deferred CI identity flows, or PAT fallback. | Not accepted in explicit CI mode for MVP unless a later accepted decision reopens this gate. Non-CI PAT compatibility remains governed by the explicit PAT compatibility rules above. |

Ecosystem adapters inherit this policy from the credential core. They may further
restrict interaction based on host-tool protocol flags, such as NuGet
non-interactive restore settings or keyring subprocess behavior.

### Token Handling

MVP product-owned persistence is limited to configuration ownership metadata and
approved host-tool configuration changes. The credential core may use allowed
identity-provider or host-tool caches that are owned by those providers, but MVP
does not create a product-owned persistent derived credential cache and must
reject or disable requests for one with `productPersistentCacheDisabled` or
equivalent typed policy behavior.

Adapters and the configuration manager may serialize credential material to
host-tool configuration files only when an explicit write policy allows that
target, scope, and credential kind. This distinction separates allowed
provider/host-tool caches and protocol-required output from disallowed
product-owned persistent derived host-tool credential storage.

Token handling requirements:

- Prefer short-lived or identity-derived credentials.
- Partition cache keys by ecosystem, host, organization, service identity,
  account, tenant, audience, and credential kind; include project/feed only for
  package-feed resource identities that carry them, and keep default Git cache
  keys at host plus organization unless a future explicit per-repository Git
  policy enables finer partitioning.
- Validate token audience before reuse.
- Never log token values, Basic auth headers, generated passwords, refresh
  tokens, PATs, npm tokens, or NuGet API keys.
- Fail closed when identity material is absent in CI.

### Protocol Output Safety

Protocol stdout is treated as sensitive and format-constrained. Any accidental
diagnostic text can break host tools or leak state. Adapter tests must assert
stdout exactly for success, no-credential, and failure paths.

### CI Security

CI mode must be explicit. Detection of a CI environment may improve diagnostics,
but it must not silently enable persistent credential writes.

CI behavior must:

- Disable interaction unless explicitly enabled.
- Accept only explicit Azure Pipelines system access token identity material for
  MVP CI, and only in explicit CI mode with a non-persistent context.
- Treat service principal, managed identity, workload identity federation, and
  other short-lived CI identities as deferred future flows.
- Use temporary configuration files when possible.
- Avoid writing secrets into build caches.
- Provide cleanup commands or automatic cleanup for temporary files.
- Redact all credential material from logs.

### Repository-Local File Safety

The product must avoid writing credentials to repository-local files by default.
If a user explicitly requests a repository-local write, the CLI should show the
target path, credential type, persistence behavior, and cleanup command before
applying the change.

## Error Handling

The product uses typed errors rather than broad catch-all success-shaped
fallbacks.

| Error class          | Example                                                                          | Required behavior                                                                                         |
| -------------------- | -------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| UnsupportedHost      | Non-Azure registry or remote.                                                    | Adapter requests return no-credential behavior; CLI management commands fail explicitly.                  |
| InvalidEndpoint      | Malformed Azure Artifacts URL.                                                   | Fail with safe diagnostics and remediation.                                                               |
| InteractionBlocked   | Login required while non-interactive.                                            | Fail closed and explain the explicit interactive command required.                                        |
| CacheUnavailable     | Allowed provider cache or future persistent-cache secure store cannot be opened. | Fail closed; MVP future-cache requests remain disabled and never fall back to plaintext storage silently. |
| UnauthorizedResource | Identity lacks repository or feed permission.                                    | Report authorization failure without leaking token details.                                               |
| ConfigConflict       | Existing non-owned setting conflicts with requested setup.                       | Stop and show remediation; do not overwrite silently.                                                     |
| ProtocolViolation    | Host-tool input is malformed.                                                    | Return protocol-safe failure and diagnostic correlation ID.                                               |

## Cross-Platform Design

Windows is first-class. Design and tests must cover:

- Git for Windows helper discovery.
- PowerShell command examples.
- Paths with spaces.
- `.exe` and `.cmd` shims.
- .NET SDK plugin scenarios in MVP, with Visual Studio/MSBuild/NuGet.exe plugin
  scenarios deferred until explicit `netfx` scope is accepted.
- Windows secure credential storage.

Linux and macOS design must cover:

- Shell-independent helper discovery where possible.
- File permission checks for shims and plugin files.
- Headless CI environments.
- Platform secure storage availability and failure modes.

Documentation examples prefer PowerShell where platform-specific command
syntax matters.

## Implementation Sequence

The implementation sequence reduces protocol risk early:

1. Implement canonicalization and cache-key construction with unit tests.
2. Implement the credential core abstraction with a fake identity provider and
   fake cache-policy/key model.
3. Implement configuration-manager change plans, selector ownership manifests,
   dry-run, and precise removal.
4. Implement Git adapter protocol parsing and stdout discipline without direct
   persistent configuration writes.
5. Implement NuGet plugin proof of protocol launch and handshake using a fake
   credential core.
6. Implement Python keyring backend and subprocess shim proof of discovery using
   the versioned helper contract.
7. Implement npm/pnpm config parsing and change-plan generation; include Yarn
   Berry user-level and CI-temporary change-plan generation under the Phase 1.4
   and Phase 2 constraints.
8. Implement `doctor` checks for each ecosystem.
9. Integrate real identity provider behavior behind the abstraction.
10. Add CI-specific non-interactive flows and cleanup behavior through
    configuration-manager-owned temporary state.

Each step has host-tool shape tests before real authentication is enabled.

## Verification Matrix

| Area                  | Verification                                                                                                                                                                        |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Core canonicalization | Unit tests for `dev.azure.com`, `*.visualstudio.com`, Azure Artifacts NuGet, Python, and npm endpoint forms.                                                                        |
| Cache partitioning    | Unit tests that prove different ecosystems, feeds, tenants, accounts, audiences, and credential kinds do not collide, without requiring product-owned persistent credential writes. |
| Git adapter           | Protocol stdin/stdout tests; Git-discovery integration tests; `useHttpPath` validation.                                                                                             |
| NuGet adapter         | Plugin launch tests; handshake tests; non-interactive behavior tests; runtime-layout checks.                                                                                        |
| Python backend        | Import-mode tests in a selected Python environment; backend discovery tests; subprocess `keyring` command-shape tests.                                                              |
| npm adapter           | `.npmrc` and `.yarnrc.yml` parser tests; user-level write tests; repository-local secret prevention tests.                                                                          |
| Configuration manager | Idempotent configure tests; conflict tests; ownership-manifest removal tests.                                                                                                       |
| Doctor                | Synthetic `pass`, `warning`, `fail`, `skipped`, `unsupported`, `deferred`, and `notApplicable` result tests.                                                                        |
| Security              | Redaction tests; protocol stdout exact-output tests; CI no-persistence tests.                                                                                                       |

## Design Risks and Versioned Follow-up

| Risk or change trigger               | Required evidence                                                                                                                                                                                                                         |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Core deployment boundary             | Prototype whether adapters call a library, a single executable, or a local broker before locking packaging and failure-isolation behavior.                                                                                                |
| NuGet plugin message compatibility   | Implement a minimal source-confirmed plugin handshake before finalizing the adapter runtime package shape.                                                                                                                                |
| AzureAuth version or platform change | When changing pinned AzureAuth 0.9.5 or expanding platforms, verify the documented CLI contract, package layout, cache behavior, and required token audiences. The accepted current version does not require runtime attestation.         |
| Future CI identity flow selection    | MVP CI is limited to explicit Azure Pipelines system access token in explicit CI mode with a non-persistent context; verify service principal, managed identity, WIF, and other short-lived CI identities only for future accepted flows. |
| Python keyring environment coverage  | Prototype backend installation and discovery in virtual environment, pipx, and uv subprocess scenarios.                                                                                                                                   |
| Git GUI client PATH differences      | Validate helper discovery through Git for Windows and at least one GUI-launched Git environment before relying on PATH-only installation.                                                                                                 |
| npm and Yarn config writes           | Prototype config update behavior across npm, pnpm, and Yarn 4+ with user-level and temporary CI scopes.                                                                                                                                   |
| Future persistent cache              | Verify platform secure-store behavior and failure modes on Windows, Linux, and macOS before a later phase enables any product-owned persistent derived credential cache; MVP requests remain rejected or disabled by default.             |

These rows are future-scope gates or dependency-change triggers. Accepted
current behavior relies on the pinned dependency contract and ordinary
OS/framework abstractions; it does not require repeated runtime proof.

## Mid-Level Design Decision

Proceed with a shared-core, adapter-thin architecture:

```text
Primary CLI
  -> configuration manager
  -> doctor engine
  -> shared credential core
      -> identity provider abstraction
      -> cache policy/key model
      -> policy and redaction
  -> machine-facing adapters
      -> Git credential helper
      -> NuGet plugin
      -> Python keyring backend
      -> keyring executable shim
      -> npm-compatible config updater
```

This allocation preserves one credential policy and cache-key model while
satisfying the distinct discovery and protocol contracts of Git, NuGet, Python
tooling, and npm-compatible package managers. Product-owned persistent derived
credential caching remains a future disabled-by-default extension, not MVP
behavior.
