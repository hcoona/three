# Unified Azure DevOps Credential Provider Research Notes

Status: **Draft research baseline**

## Executive Summary

Azure Repos Git, Azure Artifacts NuGet feeds, Azure Artifacts Python feeds, and Azure Artifacts npm feeds can be covered by one credential product, but not by a single command-line invocation pattern. The research indicates a common credential core should be shared across ecosystems, while host-tool integration must respect each ecosystem's native credential discovery mechanism.

The most important implementation finding is that NuGet, Python keyring, and npm ultimately delegate to the Azure Artifacts Credential Provider pattern for Azure Artifacts package feeds. Git does not use that provider; Azure Repos Git authentication is handled by Git Credential Manager-style credential helper integration.

## Local Source Inventory

The following implementation references are available under `/workspace/public/`:

| Area                           | Local path                                         | Role                                                                                                                                               |
| ------------------------------ | -------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Azure Repos Git                | `/workspace/public/git-credential-manager`         | Git Credential Manager source and Azure Repos host provider implementation.                                                                        |
| NuGet and .NET package restore | `/workspace/public/artifacts-credprovider`         | Azure Artifacts Credential Provider source for NuGet, dotnet, and MSBuild.                                                                         |
| Python keyring                 | `/workspace/public/artifacts-keyring`              | Python keyring backend source for pip and twine Azure Artifacts authentication.                                                                    |
| npm and pnpm                   | `/workspace/public/artifacts-npm-credprovider`     | Extracted `@microsoft/artifacts-npm-credprovider` package version `1.1.3`.                                                                         |
| Node wrapper for the provider  | `/workspace/public/artifacts-credprovider-wrapper` | Extracted `@microsoft/artifacts-credprovider-wrapper` package version `1.1.4`; its installer downloaded the Linux x64 Credential Provider payload. |
| AzureAuth helper               | `/workspace/public/microsoft-authentication-cli`   | Related Microsoft MSAL-based authentication CLI with Azure DevOps token commands; candidate identity substrate, not a host-tool protocol adapter.  |

`uv` and `MicrosoftDocs/azure-devops-docs` are useful references, but they are not credential-provider implementation source trees for this project.

## Package Feed Credential Dependency Chain

The three package-feed ecosystems converge on Azure Artifacts Credential Provider:

```text
NuGet / dotnet
  -> artifacts-credprovider

Python / pip / twine / uv
  -> artifacts-keyring
  -> bundled or invoked artifacts-credprovider

npm / pnpm / Yarn
  -> artifacts-npm-credprovider
  -> artifacts-credprovider-wrapper
  -> downloaded artifacts-credprovider payload
```

Azure Repos Git is separate:

```text
Git / Azure Repos
  -> git-credential-manager-style helper
  -> Azure Repos OAuth/PAT/service identity flows
```

## Git and Azure Repos

Azure Repos supports current `dev.azure.com` HTTPS clone URLs and legacy `*.visualstudio.com` HTTPS clone URLs. SSH uses Azure DevOps SSH URL formats and does not use Git Credential Manager.

Git credential helper discovery is sensitive to command form. Local experiments confirmed:

```text
credential.helper=<helper-name>
  -> Git invokes git-credential-<helper-name> get

credential.helper=!<primary-cli> git credential-helper
  -> Git invokes <primary-cli> git credential-helper get

credential.helper=/abs/path/<primary-cli> git credential-helper
  -> Git invokes /abs/path/<primary-cli> git credential-helper get

credential.helper=<primary-cli> git credential-helper
  -> Git attempts git credential-<primary-cli> ...
  -> This fails unless a git-credential-<primary-cli> helper exists.
```

Implication: a single CLI can implement the logic, but a production installation should provide a Git helper-shaped entry point or configure an absolute helper command carefully. The recommended resulting user configuration is the standard helper shorthand plus explicit `dev.azure.com` path forwarding:

```text
[credential]
  helper = <helper-name>
[credential "https://dev.azure.com"]
  useHttpPath = true
```

with an installed `git-credential-<helper-name>` executable that delegates to the shared core. The product CLI must write these settings directly to the selected user Git configuration file through ConfigurationManager or a Git configuration writer, not by invoking `git config --global`. The target file follows Git's official global selection behavior: use `~/.gitconfig` when present, otherwise an existing XDG Git config file, otherwise `~/.gitconfig`. ConfigurationManager owns dry-run equivalence, selector-based ownership metadata, conflict handling, and removal. Any `git config --global` examples in user-facing guidance are illustrative equivalents for the resulting file content only, not the writer implementation. AzureAuth is not a required runtime, shared-core replacement, or protocol-adapter replacement for this configuration write path. The `useHttpPath` setting is required for `dev.azure.com` because the Azure DevOps organization is in the URL path. Legacy `<org>.visualstudio.com` remotes carry the organization in the host name and do not need the same setting.

The angle-bracketed values in the previous snippet are substitution placeholders, not literal command text. The configure command must either install the helper into a location Git itself can discover or configure a carefully quoted absolute helper path. The doctor command should validate discovery through Git, not only through the current shell, because GUI Git clients may run with a different `PATH`.

Git identity flows should be treated as separate support levels rather than a
single generic authentication mode. The Phase 4D MVP scope is limited to the
accepted browser, device-code, narrow PAT compatibility, and Azure Pipelines
system access token flows. Generic direct bearer token injection and service
principal, managed identity, or workload identity federation are future,
deferred, or background options unless a later phase explicitly promotes them.

| Flow                                                                 | Phase 4D qualification                                                                                                                  |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Interactive user OAuth                                               | MVP developer flow for HTTPS remotes through accepted browser or device-code authentication.                                            |
| PAT                                                                  | MVP narrow compatibility flow; avoid embedding tokens in remotes.                                                                       |
| Direct bearer token                                                  | Future or background CI technique for controlled commands such as `http.extraheader`; not Phase 4D MVP Git helper support.              |
| Service principal, managed identity, or workload identity federation | Deferred; viable only if a later phase adds support and the Azure DevOps organization and repository permissions are configured for it. |
| Azure Pipelines system access token                                  | MVP pipeline-specific bootstrap flow; configure as CI state, not as normal desktop Git helper state.                                    |

## NuGet and .NET

Azure Artifacts NuGet feeds use NuGet service index URLs:

```text
https://pkgs.dev.azure.com/<ORG>/<PROJECT>/_packaging/<FEED>/nuget/v3/index.json
https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/nuget/v3/index.json
https://<ORG>.pkgs.visualstudio.com/<PROJECT>/_packaging/<FEED>/nuget/v3/index.json
https://<ORG>.pkgs.visualstudio.com/_packaging/<FEED>/nuget/v3/index.json
```

NuGet integration is plugin-based. NuGet discovers plugin files and launches them with fixed plugin arguments. Local experiments confirmed:

```text
NUGET_PLUGIN_PATHS=/tmp/nuget-plugin-probe
  -> plugin launched with argv: -Plugin

NUGET_PLUGIN_PATHS="/tmp/nuget-plugin-probe nuget"
  -> plugin was not launched
```

Implication: NuGet cannot be configured to call a normal `<primary-cli> nuget plugin` subcommand through `NUGET_PLUGIN_PATHS`. The product needs one of these shapes:

1. A top-level executable that itself handles NuGet's `-Plugin` invocation.
2. A NuGet plugin-shaped executable or DLL that delegates to the shared core.
3. Conventional NuGet plugin installation paths for modern `dotnet` / .NET SDK scenarios.

The existing Azure Artifacts Credential Provider demonstrates this dual-mode model: it can run as a NuGet plugin and can also be invoked in standalone credential-acquisition mode.

NuGet discovery modes have different operational consequences:

| Mode                 | Shape                                                                             | Notes                                                                                  |
| -------------------- | --------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Direct plugin path   | Full path to a plugin executable or DLL                                           | Advanced override; no extra arguments or subcommands; can shadow convention discovery. |
| Convention discovery | `.nuget/plugins/netcore/<name>/<name>.dll`                                        | Default Phase 4D MVP path for modern `dotnet` / .NET SDK clients, including .NET 10.   |
| Deferred convention  | `.nuget/plugins/netfx/<name>/<name>.exe`                                          | .NET Framework / NuGet.exe / Visual Studio legacy host shape; out of scope for MVP.    |
| PATH discovery       | `nuget-plugin-*` executable on `PATH` where supported by the NuGet client version | Useful for tool-style distribution, but still requires plugin protocol behavior.       |

Prefer conventional installation under NuGet plugin folders for normal
developer machines. For Phase 4D MVP, project only
`.nuget/plugins/netcore/<name>/<name>.dll`; `netcore` is the NuGet convention
for modern `dotnet` / .NET SDK plugins and remains correct for .NET 10. The
`.nuget/plugins/netfx/<name>/<name>.exe` shape is deferred unless a later phase
explicitly adds .NET Framework / NuGet.exe / Visual Studio legacy host support.
Use `NUGET_PLUGIN_PATHS` only as an advanced or diagnostic override.

## Python, pip, twine, uv, and keyring

Azure Artifacts Python feeds use these endpoints:

```text
https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/pypi/simple/
https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/pypi/upload/
https://pkgs.dev.azure.com/<ORG>/<PROJECT>/_packaging/<FEED>/pypi/simple/
https://pkgs.dev.azure.com/<ORG>/<PROJECT>/_packaging/<FEED>/pypi/upload/
```

Python tooling has two materially different credential integration paths:

1. In import mode, pip and twine import Python `keyring` and discover backend packages through Python package metadata.
2. uv and pip subprocess mode call a command named `keyring`.

Local subprocess-shape testing confirmed that a `keyring` executable must support the subset of the Python keyring CLI used by pip and uv:

```text
keyring get <service> <username>    # stdout is the password
keyring get <service> --mode creds  # stdout is newline-separated username and password
```

pip and uv both have subprocess keyring modes, but their discovery semantics differ. uv invokes `keyring` from `PATH` directly. pip subprocess mode calls `keyring get <service> <username>` and requires a username in the index URL. pip also avoids the current Python environment's scripts directory when selecting a subprocess `keyring` executable, so a shim installed only into a project virtual environment may be ignored by pip subprocess mode.

Implication: a normal `<primary-cli> python keyring` subcommand is not directly discoverable by all Python tools. A robust product needs:

- a Python keyring backend package for import-mode tools such as twine and pip import mode,
- a `keyring` executable-compatible shim for uv and subprocess-mode tools,
- both delegating to the shared credential core.

The existing `artifacts-keyring` package is a keyring backend, not just a standalone CLI. It invokes the Azure Artifacts Credential Provider and adapts the returned credentials to Python keyring.

The Python backend package must be installable into the exact Python environment that runs pip or twine. A globally installed CLI is not enough for twine installed in a project virtual environment, pipx environment, tox/nox environment, or isolated CI environment. Supported bootstrap paths should include installing the backend into the active environment, injecting it into a pipx-managed twine environment, or using subprocess mode where the host tool supports it.

Keeping the large credential implementation outside the project virtual environment reduces how much trusted code is imported into arbitrary Python environments, but it is not a security boundary. Any process running pip, twine, uv, a Python keyring backend, or a PATH-resolved `keyring` shim can observe credentials returned to it. The design should treat project virtual environments as untrusted for implementation integrity while recognizing that the requesting package-manager process must receive credentials to complete authentication.

## npm, pnpm, and Yarn

Azure Artifacts npm feeds use npm-compatible registry URLs:

```text
https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/npm/registry/
https://pkgs.dev.azure.com/<ORG>/<PROJECT>/_packaging/<FEED>/npm/registry/
```

The npm ecosystem does not expose a NuGet-style credential-provider plugin protocol for Azure Artifacts feeds. Authentication is driven by registry configuration files:

- `.npmrc` for npm and pnpm,
- `.yarnrc.yml` for Yarn Berry.

The extracted `@microsoft/artifacts-npm-credprovider` package is a CLI, not a package-manager plugin. It reads npm or Yarn registry configuration, obtains credentials through `@microsoft/artifacts-credprovider-wrapper`, and writes credential material back to the relevant npm or Yarn configuration location.

Local lifecycle script testing confirmed npm and pnpm can run arbitrary commands:

```text
npm preinstall -> arbitrary command
pnpm:devPreinstall -> arbitrary command before pnpm install
```

`pnpm:devPreinstall` can refresh credentials before a local `pnpm install`, but only when the credential-provider CLI is already available before pnpm starts. npm `preinstall` is an arbitrary lifecycle script, not a reliable initial-auth bootstrap hook for private registry resolution. npm and Yarn should use an explicit external bootstrap step, a globally or otherwise preinstalled CLI, or CI setup before running `npm install`, `npm ci`, or `yarn install`.

Implication: npm-compatible tooling can use a standard CLI subcommand such as:

```text
<primary-cli> npm
```

`<primary-cli>` is a substitution placeholder for the final executable name, not a literal command.

An npm-specific alias is optional for clearer documentation and compatibility with existing scripts, but it is not required by npm, pnpm, or Yarn mechanics.

## Entrypoint Architecture Finding

The research rejects two oversimplified designs:

1. **Four separate products.** This would duplicate credential logic, increase version skew, and fragment cache policy.
2. **One CLI with only four standard subcommands.** This does not satisfy NuGet and Python discovery contracts and is fragile for production Git integration.

The recommended design is:

```text
One product
  -> one shared credential core
  -> one human-facing CLI
  -> thin machine-facing adapters only where required
```

Minimum recommended machine-facing surfaces:

| Surface                        | Reason                                                     |
| ------------------------------ | ---------------------------------------------------------- |
| `git-credential-<helper-name>` | Stable Git helper discovery.                               |
| NuGet plugin entry point       | NuGet launches plugin files with fixed `-Plugin` behavior. |
| Python keyring backend package | Required for twine and pip import mode.                    |
| `keyring` executable shim      | Required for uv and pip subprocess mode.                   |
| `<primary-cli> npm`            | Sufficient for npm, pnpm, and Yarn config-file workflows.  |

## Security Findings

1. Protocol adapters must never print human diagnostics to protocol stdout.
2. Token caches must be partitioned by ecosystem, host, organization, project when relevant, service identity, feed identity for package-feed adapters, account, tenant, token audience, and credential type.
3. Git shell snippets are convenient but should not be the default production configuration on Windows or GUI Git clients.
4. NuGet plugin mode must not emit banners, update notices, prompts, or non-protocol JSON to stdout.
5. Python keyring adapters should be small and should avoid importing a large trusted authentication implementation from arbitrary project virtual environments.
6. npm config updates must avoid writing raw long-lived secrets into repository-local `.npmrc` files by default.
7. CI mode should be explicit, non-interactive, ephemeral, and log-safe.

## Integration Removal Implications

The same host-tool discovery mechanisms that require explicit configuration also require explicit teardown semantics. Removal should be scoped to configuration and adapter registrations owned by this product, not to all related host-tool state.

Research implications by ecosystem:

- Git removal should remove only this product's credential helper configuration and any `dev.azure.com` path-forwarding setting installed by this product or explicitly selected by the user.
- NuGet removal should remove this product's plugin installation or explicit plugin-path override without deleting unrelated NuGet package sources or package source credentials.
- Python removal should remove this product's keyring backend or shim registration from the targeted Python environment without uninstalling unrelated keyring backends.
- npm removal should remove generated credential entries while preserving registry declarations and unrelated npm or Yarn configuration.

## Monorepo Integration Notes

This repository already has:

- root pnpm workspace configuration,
- root `nuget.config`,
- Central Package Management for C#,
- root uv workspace configuration,
- GitHub Actions CI.

The current repository does not already contain active Azure Artifacts feed configuration, `pkgs.dev.azure.com` package source settings, `tool.uv.index`, `artifacts-keyring` project configuration, `npmAuthenticate`, `NuGetAuthenticate`, or Azure Pipelines YAML. Azure Artifacts support would be additive.

## Evidence Quality

The findings are based on:

- source inspection of local Microsoft credential provider repositories and extracted packages,
- official documentation research,
- local Git credential helper protocol experiments,
- local NuGet plugin path experiments,
- local keyring command-shape experiments,
- local npm and pnpm lifecycle script experiments,
- independent ecosystem-focused research agents,
- adversarial architecture, cross-platform, and security reviews.
