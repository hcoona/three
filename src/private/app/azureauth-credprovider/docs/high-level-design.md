# AzureAuth Credential Provider High-Level Design

Status: **Draft design baseline**

## Design Summary

AzureAuth Credential Provider should be one product with one shared credential core and one human-facing CLI. It should expose additional machine-facing entry points only where host tools require a specific executable name, package entry point, file layout, or protocol invocation.

The design intentionally separates implementation ownership from host-tool discovery. Git, NuGet, Python keyring, and npm-compatible tooling should not receive four independent credential implementations. They should receive thin adapters that delegate to the same credential core.

## Target Command Model

The primary user-facing command is a standard CLI:

```powershell
azureauth-credprovider login
azureauth-credprovider logout
azureauth-credprovider status
azureauth-credprovider configure git
azureauth-credprovider configure nuget
azureauth-credprovider configure python
azureauth-credprovider configure npm
azureauth-credprovider doctor
```

The human-facing CLI owns:

- login and account selection,
- global and per-ecosystem configuration,
- diagnostics,
- cache inspection and cleanup,
- CI guidance,
- installation verification.

Protocol adapters are installed and configured by the CLI, but they are not the primary interface users interact with.

## High-Level Components

```text
                      +-----------------------------+
                      | azureauth-credprovider CLI  |
                      | login/configure/doctor      |
                      +--------------+--------------+
                                     |
                                     v
                      +-----------------------------+
                      | Shared credential core      |
                      | identity/cache/policy       |
                      +---+-----------+--------+----+
                          |           |        |
          +---------------+           |        +----------------+
          v                           v                         v
+---------------------+     +---------------------+     +---------------------+
| Git adapter         |     | NuGet plugin        |     | npm config updater  |
| git credential I/O  |     | NuGet plugin I/O    |     | npmrc/yarnrc I/O    |
+----------+----------+     +----------+----------+     +----------+----------+
           |                           |                           |
           v                           v                           v
      Git client                 NuGet clients             npm/pnpm/Yarn

          +------------------------------------------------+
          | Python keyring backend and keyring CLI adapter |
          +-------------------------+----------------------+
                                    |
                                    v
                          pip / twine / uv
```

## Shared Credential Core

The shared core owns credential behavior that must not diverge between ecosystems:

- Azure DevOps host and feed canonicalization,
- tenant and account selection,
- interactive browser or device-code login,
- non-interactive service identity flows,
- token exchange and refresh,
- secure credential cache access,
- cache partitioning,
- redaction,
- policy enforcement,
- diagnostic event generation.

The core must not assume a single protocol output format. Protocol adapters are responsible for host-tool input and output.

## Machine-Facing Entrypoints

| Entrypoint                     |                       Requirement level | Integration contract                                                         |
| ------------------------------ | --------------------------------------: | ---------------------------------------------------------------------------- |
| `git-credential-azureauth`     |                    Strongly recommended | Git credential helper stdin/stdout protocol.                                 |
| NuGet plugin entry point       |                                Required | NuGet plugin handshake and authentication messages; launched with `-Plugin`. |
| Python keyring backend package |                                Required | Python `keyring.backends` discovery and backend API.                         |
| `keyring` executable shim      | Required for uv and pip subprocess mode | Keyring CLI-compatible `get` behavior.                                       |
| `azureauth-credprovider npm`   |                                Required | Reads and updates npm/Yarn registry config.                                  |
| npm alias binary               |                                Optional | Compatibility wrapper for documentation or existing scripts.                 |

## Git Adapter

The Git adapter should support the Git credential helper protocol:

```text
git-credential-azureauth get
git-credential-azureauth store
git-credential-azureauth erase
```

The adapter reads credential records from stdin and writes only Git credential fields to stdout. It delegates account selection and token acquisition to the shared core.

Recommended configuration:

```powershell
git config --global credential.helper azureauth
git config --global credential.https://dev.azure.com.useHttpPath true
```

The `useHttpPath` setting is required for `dev.azure.com` because the organization is in the URL path. Legacy `<org>.visualstudio.com` remotes carry the organization in the host name and do not require the same setting.

The configuration command should avoid shell snippets as the default. Shell snippets are useful for development, but they are harder to quote safely on Windows and less reliable in GUI Git clients. The installer must either place `git-credential-azureauth` where Git itself can discover it or configure a carefully quoted absolute helper path. `doctor` should validate helper discovery by invoking Git, not just by checking the current shell's `PATH`.

## NuGet Adapter

The NuGet adapter must be structured as a NuGet plugin because NuGet launches plugin files and passes fixed plugin arguments. It should not rely on a standard subcommand such as:

```powershell
azureauth-credprovider nuget plugin
```

unless the top-level executable itself is the plugin path and can enter plugin mode when launched with NuGet's arguments.

The NuGet adapter must:

- support NuGet plugin handshake behavior,
- support authentication request handling,
- respect non-interactive restore,
- emit only protocol-valid content to stdout,
- route diagnostics safely,
- support dotnet CLI restore scenarios,
- support Visual Studio/MSBuild/NuGet.exe scenarios where required by project scope.

NuGet discovery options should be documented and diagnosed explicitly:

| Mode                 | Shape                                                                                   | Operational note                                                                               |
| -------------------- | --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Direct plugin path   | Full path to the plugin entry point                                                     | Advanced override; cannot include extra subcommand arguments; can shadow convention discovery. |
| Convention discovery | `.nuget/plugins/netcore/<name>/<name>.dll` and `.nuget/plugins/netfx/<name>/<name>.exe` | Preferred for broad developer-machine compatibility.                                           |
| PATH discovery       | `nuget-plugin-*` executable where supported                                             | Still must enter NuGet plugin mode and speak the NuGet plugin protocol.                        |

Interactive behavior is controlled by the invoking NuGet client. `dotnet restore` should require `--interactive` before the plugin initiates first-time user interaction. MSBuild restore should require `/p:NuGetInteractive=true`. NuGet.exe may prompt by default. The plugin must honor NuGet protocol `NonInteractive` and `CanShowDialog` values and must not prompt or block when `NonInteractive` is true.

## Python Adapter

Python requires two adapter shapes for full coverage:

1. A Python keyring backend package for twine and pip import mode.
2. A `keyring` executable-compatible shim for uv and pip subprocess mode.

The Python keyring backend should be intentionally thin. It should delegate credential acquisition to the shared core through an absolute helper path, a signed local broker, or a small trusted library boundary. It should not import a large credential implementation into arbitrary project virtual environments.

The backend package must be available in the same Python environment as the tool that imports keyring. `configure python` and `doctor` should account for active virtual environments, pipx-installed twine, tox/nox environments, and isolated CI environments. A unified CLI alone cannot satisfy twine because twine imports Python keyring rather than invoking an arbitrary external subcommand.

Keeping the large credential implementation outside the project virtual environment reduces the trusted code imported into arbitrary Python environments, but it is not a security boundary. The invoking package-manager process must receive credentials, so a compromised environment can still observe returned credentials.

The `keyring` shim should support:

```text
keyring get <service> <username>    # stdout is the password
keyring get <service> --mode creds  # stdout is newline-separated username and password
```

It must print only the expected keyring response to stdout.

The shim must define exit behavior for no credential, unsupported host, and fatal errors. It should either delegate unsupported keyring commands to a real Python `keyring` CLI or avoid global shadowing by using controlled PATH injection. pip subprocess mode requires a username in the index URL and may ignore a `keyring` executable installed only in the current Python environment's scripts directory. uv invokes `keyring` from `PATH` directly and can use `--mode creds` when no username is present.

## npm Adapter

The npm-compatible adapter can be a standard CLI subcommand because npm, pnpm, and Yarn use registry configuration files rather than a credential-provider plugin protocol.

The adapter should:

- discover workspace-level `.npmrc` and `.yarnrc.yml` files,
- parse registry and scoped registry entries,
- request feed-specific credentials from the shared core,
- read registry declarations from workspace files but write developer credentials only to user-level configuration by default,
- support CI output modes that avoid persistent writes,
- optionally support `pnpm:devPreinstall` or explicit bootstrap invocation.

Recommended explicit invocation:

```powershell
azureauth-credprovider npm
```

Optional package-manager script:

```json
{
    "scripts": {
        "pnpm:devPreinstall": "azureauth-credprovider npm"
    }
}
```

This script requires the CLI to be available before `pnpm install` begins. npm `preinstall` should not be treated as a reliable first-auth hook for private dependency resolution; npm and Yarn should use an explicit bootstrap step before registry access that requires credentials.

## Cache Model

Credential cache keys must include:

- ecosystem,
- Azure DevOps host,
- organization,
- project when relevant,
- service identity,
- account,
- tenant,
- token audience,
- credential type.

Package-feed cache entries should include feed identity. Azure Repos Git should normally key credential storage by host and organization rather than by full repository path unless an explicit per-repository policy requires finer partitioning. No adapter should read a generic "current token" without ecosystem and audience validation.

## CI Model

CI mode should be explicit. The CLI should detect common CI environments but should not silently persist credentials just because it is running in CI.

CI behavior should prefer:

- workload identity federation,
- managed identity where available,
- Azure Pipelines system access token for Azure Pipelines scenarios,
- short-lived tokens over personal access tokens,
- temporary config files or environment variables over user-global writes.

CI behavior must:

- disable interactive prompts unless explicitly enabled,
- redact all secrets,
- avoid writing credentials into build caches,
- provide cleanup guidance or cleanup hooks,
- fail closed when required identity material is absent.

## Diagnostics

`azureauth-credprovider doctor` should validate:

- Git helper executable discovery and `credential.useHttpPath` behavior for Azure Repos hosts,
- NuGet plugin discovery and runtime compatibility,
- Python keyring backend discovery,
- `keyring` shim availability for uv,
- npm and Yarn registry configuration format and required entries,
- cache health and account selection,
- host/feed canonicalization,
- CI mode and secret handling.

Diagnostics should never print access tokens, refresh tokens, PATs, Basic auth headers, npm tokens, NuGet API keys, or generated passwords.

## Explicitly Deferred

- Implementing package manager behavior.
- Hosting or proxying Azure Artifacts feeds.
- SSH key management for Azure Repos.
- Visual Studio UI integration beyond NuGet plugin compatibility.
- Automatically migrating existing user credential stores.
- Transparent credentials for arbitrary non-Azure registries.
- Requiring a background daemon; a broker process may be evaluated later but is not assumed.

## Design Decision

The project should proceed with a hybrid entry point architecture:

```text
one product
one shared credential core
one primary human CLI
thin machine-facing adapters where host tools require them
```

This design satisfies real host-tool invocation constraints without splitting credential behavior into four independent products.
