# Unified Azure DevOps Credential Provider Requirements

Status: **Draft requirements baseline**

## Goal

Build a unified credential provider for Azure DevOps and Azure Artifacts that covers four developer ecosystems:

1. Azure Repos Git authentication.
2. NuGet and .NET package restore against Azure Artifacts feeds.
3. Python package consumption and publishing through pip, twine, uv, and Python keyring.
4. npm-compatible package consumption for npm, pnpm, and Yarn.

The product should provide a consistent setup, login, diagnostics, and configuration experience while satisfying each host tool's native credential discovery and protocol contract.

## Audience

This project targets senior software engineers who maintain developer tooling, internal platform SDKs, CI bootstrap flows, and cross-platform authentication integrations.

## Product Boundary

The product is a credential acquisition, refresh, and configuration layer. It is not a package manager, Git client, feed server, Azure DevOps client replacement, or CI orchestration system.

The product owns:

- interactive and non-interactive credential acquisition,
- account and tenant selection,
- host/feed canonicalization,
- identity-provider cache policy and future product-cache boundaries,
- protocol adapters for host-tool credential exchange,
- user-facing configuration and diagnostics commands.

Azure DevOps and Azure Artifacts own:

- repository and feed authorization,
- token validation,
- package and Git data plane operations,
- service-side policy enforcement.

Host tools own:

- Git clone/fetch/push behavior,
- NuGet restore/push behavior,
- pip/twine/uv package operations,
- npm/pnpm/yarn install/publish behavior.

## Functional Requirements

1. Provide one human-facing CLI for setup, login, logout, status, diagnostics, and ecosystem configuration.
2. Support Azure Repos HTTPS Git remotes hosted on `dev.azure.com` and legacy `*.visualstudio.com` hosts.
3. Support Azure Artifacts NuGet v3 feeds for Phase 4D MVP `dotnet` restore through NuGet `netcore` plugin convention discovery; treat NuGet.exe, MSBuild, and Visual Studio (`netfx`) restore support as deferred post-MVP scope.
4. Support Azure Artifacts Python simple-index and upload endpoints for pip, twine, and uv workflows.
5. Support Azure Artifacts npm registry endpoints for npm, pnpm, and Yarn workflows.
6. Reuse a single credential core for token acquisition, account selection,
   cache policy and key partitioning, and policy enforcement.
7. Provide entry points that conform to each host tool's required discovery and invocation protocol.
8. Avoid duplicating credential logic across ecosystem adapters.
9. Preserve host-tool protocol boundaries: protocol adapters must write only protocol-valid content to stdout.
10. Provide configuration commands that can install, verify, and remove each ecosystem integration.
11. Support non-interactive CI operation without persisting secrets by default.
12. Use AzureAuth (`microsoft-authentication-cli`) 0.9.5 as the current Windows, WSL, and native Linux identity path; keep Direct MSAL unimplemented behind the same provider abstraction.
13. Support interactive browser acquisition, explicit native Linux device-code
    login, and explicit Azure Pipelines system access tokens; keep PAT
    compatibility, service principal, managed identity, and workload identity
    federation unavailable or deferred until implemented.
14. Provide a `doctor` command that validates Git helper configuration through
    Git's own discovery behavior, NuGet plugin discovery, Python keyring
    availability, npm registry configuration, identity-provider readiness and
    silent-cache availability, and common CI misconfigurations.
15. Provide an internal deployment-validation bundle that exercises complete
    application, Git, NuGet, Python wheel, installation, and uninstallation
    shapes without representing that bundle as a signed release installer.

## Non-Functional Requirements

1. Use American English in all product documentation and command help.
2. Maintain a professional tone suitable for experienced engineers and platform owners.
3. Support Windows as a first-class platform, including Git for Windows, Visual Studio/MSBuild, PowerShell, `.exe`, `.cmd`, and path-with-spaces scenarios.
4. Support Linux and macOS developer and CI environments.
5. Keep protocol adapters small, deterministic, and testable.
6. Keep token handling centralized and auditable.
7. Redact secrets in stdout, stderr, logs, traces, dry-run output, and error messages.
8. Model cache keys by ecosystem, service identity, feed or host, account,
   tenant, token audience, and credential type without requiring a
   product-owned persistent cache.
9. Prefer short-lived or identity-derived credentials over long-lived personal access tokens.
10. Avoid writing credentials into repository-local configuration files by default.
11. Keep product-owned derived credentials non-persistent by default and never
    add a product plaintext cache fallback. On headless native Linux, permit
    pinned AzureAuth 0.9.5's documented provider-owned cache fallback under
    owner-only directory and file modes so device-code login can support later
    silent host-tool acquisition.

## Integration Requirements by Ecosystem

### Git

1. Provide a Git credential helper-compatible entry point.
2. Read Git credential records from stdin and write only Git credential fields to stdout.
3. Support `get`, `store`, and `erase` operations.
4. Configure Azure Repos hosts in a way that preserves organization identity for `dev.azure.com` URLs.
5. Configure `credential.https://dev.azure.com.useHttpPath=true` for `dev.azure.com` HTTPS remotes so the helper receives the organization path.
6. Prefer helper configuration that avoids shell snippets when an installed helper entry point is available.

### NuGet

1. Provide a NuGet plugin-compatible entry point that supports NuGet's plugin handshake and authentication request protocol.
2. Enter plugin mode when launched by NuGet with fixed plugin arguments.
3. Support .NET Core plugin discovery for `dotnet restore`.
4. Keep .NET Framework-compatible plugin discovery for Visual Studio, MSBuild, or NuGet.exe as a deferred post-MVP compatibility target; it is out of Phase 4D MVP scope.
5. Respect NuGet's interactive and non-interactive restore settings.
6. Prefer NuGet's conventional plugin installation locations for default setup, with `NUGET_PLUGIN_PATHS` and `NUGET_NETCORE_PLUGIN_PATHS` reserved for optional process-scoped diagnostics or explicit temporary overrides only.
7. Do not persist NuGet plugin-path environment overrides as user-global or machine-global state in Phase 4D MVP.

### Python

1. Provide a Python keyring backend package for tools that import Python keyring directly.
2. Provide a `keyring` command-compatible shim for tools that use subprocess keyring mode.
3. Support pip, twine, and uv without requiring credentials in source-controlled project files.
4. Keep trusted credential logic outside arbitrary project virtual environments where practical by using a thin backend that invokes the installed product helper by a configured absolute path after ordinary existence and executable checks.
5. Support Azure Artifacts Python simple-index and upload endpoints in both organization-scoped and project-scoped forms.
6. Provide supported bootstrap paths that make the Python keyring backend discoverable in the exact Python environment running pip or twine, including virtual environments, pipx-managed tools, and isolated CI environments.
7. Configure the backend with the installed product apphost's absolute path and
   provide a separate controlled-PATH `keyring` shim that delegates to the
   wheel-provided `azureauth-keyring` console script; Windows subprocess mode
   requires a real `.exe` launcher and remains deferred until that launcher is
   implemented and validated.

### npm, pnpm, and Yarn

1. Provide an npm ecosystem command that reads npm and Yarn registry configuration and updates credential material safely.
2. Support `.npmrc` registry entries for npm and pnpm.
3. Support `.yarnrc.yml` registry entries for Yarn Berry (Yarn 2+), including `npmRegistryServer`, `npmScopes`, and auth material under `npmRegistries`.
4. Support invocation from package-manager lifecycle or bootstrap scripts when the CLI is already available.
5. Avoid writing raw long-lived tokens to repository-local `.npmrc` files by default.

## Non-Goals

1. Do not implement a Git remote transport.
2. Do not implement a package manager.
3. Do not host package feeds or Git repositories.
4. Do not require developers to learn protocol adapter commands for normal operation.
5. Do not rely on a single standard CLI subcommand model for every host-tool integration when the host tool requires a different discovery contract.
6. Do not create four independent credential products with separate token acquisition implementations.
7. Do not store secrets in project files by default.
8. Do not support unauthenticated HTTP remotes or package feeds.
9. Do not authenticate Azure Repos SSH remotes or manage SSH keys; Git support is HTTPS credential-helper only.

## Open Questions

1. Whether the shared credential core should be a library, a local broker process, or a single executable invoked by adapters.
2. How to package deferred netfx support if NuGet.exe/MSBuild/Visual Studio compatibility is added after the Phase 4D MVP netcore-only scope.
3. Which signing or trusted-publishing mechanisms should be required for Python release artifacts and installers.
4. Whether npm compatibility aliases should be provided in addition to the primary npm credential refresh command.

## Acceptance Criteria for the Design Phase

1. The design documents explain why one shared core is required and why some host tools still need protocol-specific entry points.
2. The design identifies the minimum entry point surface for Git, NuGet, Python, and npm-compatible tooling.
3. The design documents source evidence from existing Microsoft tools and local protocol experiments.
4. The design distinguishes implementation boundaries from host-tool discovery boundaries.
5. The design provides security requirements for token cache partitioning, protocol stdout, CI operation, and configuration-file writes.
