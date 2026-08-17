# WP16 Deployment Validation Bundle

Status: **Accepted for internal deployment validation**

Date: **2026-07-31**

Decision ID: **`phase-wp16-deployment-validation-bundle`**

## Decision

The repository provides a deterministic internal bundle that validates the
installed product shape before release installer work begins. Every manifest and
script labels the artifact internal, unsigned, and non-release. This work does
not create or accept a release installer.

## Bundle Shape

`New-DeploymentValidationBundle.ps1` publishes one framework-dependent CLI
payload for `win-x64` or `linux-x64` and builds one Python wheel. The ZIP
contains:

| Path                   | Purpose                                                                  |
| ---------------------- | ------------------------------------------------------------------------ |
| `app/`                 | Complete published CLI payload and NuGet netcore plugin entrypoint.      |
| `launchers/`           | CLI and Git credential-helper launchers.                                 |
| `python/`              | The `azureauth-credprovider-keyring` wheel.                              |
| `.build-identity.json` | NBGV-derived app, wheel, and source identity for safe `-NoBuild` reuse.  |
| `manifest.json`        | Artifact identity, boundaries, entrypoints, lengths, and SHA-256 hashes. |
| `install.ps1`          | Per-user physical installation without ecosystem activation.             |
| `uninstall.ps1`        | Configuration-aware removal of product-owned payloads.                   |

The archive schema is
`azureauth-credprovider-deployment-validation-v1`. ZIP entry timestamps are
fixed so identical inputs produce stable archive metadata.
Generation requires a matching Windows x64 or Linux x64 host; the script does
not cross-label artifacts for another operating system or architecture.

## Installed Shape

Default product roots are:

| Platform | Product root                                         |
| -------- | ---------------------------------------------------- |
| Windows  | `%LOCALAPPDATA%\AzureAuth\CredProvider\installation` |
| Linux    | `~/.local/lib/azureauth-credprovider`                |

The product root contains:

- `app/` for the complete application payload;
- `bin/` for CLI and Git launchers;
- `python/` for the wheel; and
- `installation.json` for the exact installed roots and source identity.

Physical installation does not mutate global PATH, shell profiles, the Windows
registry, Git configuration, NuGet configuration, or Python environments. The
NuGet conventional discovery directory does not exist until `configure nuget`
copies the installed application payload to
`~/.nuget/plugins/netcore/azureauth-credprovider` and records the product-owned
activation inventory. `unconfigure nuget` removes only inventory-listed files
whose hashes still match and preserves unrelated files.

The bundle generator resolves `AssemblyInformationalVersion`, `SemVer2`, and
`GitCommitId` from the component's existing NBGV `version.json`. It validates
the published app's `--version` output and the wheel's normalized metadata
version before writing the manifest, including when `-NoBuild` reuses canonical
staging. The staging sidecar records source/build identity but cannot substitute
for payload validation. The install receipt copies those validated app and
Python package versions; callers cannot supply a separate receipt version.
Successful normal generation retains only the canonical RID staging tree and
identity sidecar needed for a direct subsequent `-NoBuild` invocation. Failed
normal generation removes incomplete staging.

The wheel must be installed into the exact Python environment that imports the
backend. On Linux, `configure python` then writes the backend manifest and
controlled-PATH `keyring` shim. The shim invokes the installed apphost directly,
so uv and pip subprocess mode can authenticate before a project environment has
been synchronized. Default installation roots are disjoint from those
configuration targets, so payload replacement does not erase owned Python
configuration.

## Lifecycle

Installation rejects an artifact whose manifest does not declare the internal
non-release schema, supported operating system, and host-matching x64 RID. It
validates the manifest file inventory and hashes, copies the full app payload to
the product app root, installs launchers and the wheel, restores Unix executable
modes, and records an install receipt. Physical installation never creates the
NuGet conventional plugin root.

By default, uninstallation invokes:

```text
azureauth-credprovider unconfigure git
azureauth-credprovider unconfigure nuget
azureauth-credprovider unconfigure python
azureauth-credprovider unconfigure npm
azureauth-credprovider unconfigure pnpm
azureauth-credprovider unconfigure yarn
```

In an identified Azure Pipelines job, uninstallation also runs
`cleanup --ci azure-pipelines` for the current `SYSTEM_JOBID`; it does not scan
or remove state for other jobs.

It then removes only the product root recorded in the install receipt. NuGet
activation removal belongs exclusively to `unconfigure nuget`.
`-SkipConfigurationCleanup` is an explicit escape hatch for damaged or
incomplete installations; it does not bypass receipt validation. If the product
executable is missing, normal uninstall stops rather than silently leaving stale
ecosystem configuration.

`-Force` replaces only an empty target or an existing installation whose receipt
matches the requested product root. It rejects non-empty unrelated directories.
Payload copying uses literal source paths, including when the extracted bundle
path contains PowerShell wildcard characters. If installation fails after
creating its target root, it removes that partial root before propagating the
failure.

## Linux Lifecycle Evidence

On 2026-08-11, an isolated `linux-x64` working-tree bundle completed:

1. Archive manifest and file-hash validation.
2. NBGV-derived app, wheel, manifest, and receipt version coherence.
3. Physical installation without a NuGet conventional discovery directory.
4. Uninstallation before NuGet configuration.
5. `configure nuget` creation of the discoverable owned plugin layout.
6. Configuration-aware uninstallation after NuGet configuration.
7. Removal of owned NuGet activation files while preserving an unrelated file.
8. Refusal to force-replace unrelated or receipt-mismatched product roots.
9. Cleanup of product roots created by a failed installation.
10. Installation from an extraction path containing wildcard characters.
11. Rejection of hidden files absent from the bundle manifest.

No token acquisition, AzureAuth launch, browser interaction, WAM interaction,
device code, or private-feed authorization occurred.

## WSL-Hosted Native Linux Authentication Evidence

On 2026-08-03, commit `63dacbac` was rebuilt and installed through the same
internal `linux-x64` deployment-validation bundle. The verified official
AzureAuth 0.9.5 Linux package was supplied through the absolute diagnostic
override. With WSL detection disabled, the installed apphost completed:

1. product `login --browser` using native AzureAuth system-browser
   authentication;
2. safe login status output with no credential material;
3. Git silent-only acquisition from AzureAuth's isolated cache;
4. Python wheel installation, backend configuration, and silent-only
   acquisition through the `python-keyring` apphost contract; and
5. configuration-aware uninstall and complete removal of the isolated product,
   configuration, and AzureAuth cache roots.

Credential responses were captured only long enough to validate their protocol
shape and were deleted without being printed. Azure CLI authentication was not
used because AzureAuth does not consume the Azure CLI token cache.

On 2026-08-04, the implementation after commit `46424808` was rebuilt into a
fresh isolated bundle and exercised again with product `login --device-code`.
AzureAuth's bounded device-code instructions reached the human prompt stream
while token stdout remained private. Login returned only safe status fields.
Git and the installed Python wheel then reused the resulting AzureAuth cache
through silent-only requests. Credential payloads were validated without being
printed and were deleted immediately.

The headless environment had no usable Linux keyring, so AzureAuth used its
documented owner-only unprotected file-cache fallback. Product configuration,
installation, credential responses, and the complete isolated AzureAuth cache
root were removed after acceptance.

The host remained WSL2 with product WSL detection disabled. By explicit operator
decision, this forced-native execution closes the repository's standalone Linux
x64 platform gate. Native system-keyring behavior, private-feed authorization,
and authentication through a release installer remain separate evidence.

## Explicit Boundaries

This evidence does not establish:

- a signed or production release installer;
- Windows bundle execution;
- Windows Python subprocess mode and its required real `keyring.exe` launcher;
- native Linux system-keyring persistence;
- exact Windows 11 24H2 or Windows Server acceptance;
- installer-produced binary authentication;
- private-feed authorization; or
- macOS support.

Those remain separate release evidence gates.
