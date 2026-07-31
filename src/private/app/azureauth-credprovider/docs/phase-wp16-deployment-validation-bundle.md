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

| Path            | Purpose                                                                  |
| --------------- | ------------------------------------------------------------------------ |
| `app/`          | Complete published CLI payload and NuGet netcore plugin entrypoint.      |
| `launchers/`    | CLI and Git credential-helper launchers.                                 |
| `python/`       | The `azureauth-credprovider-keyring` wheel.                              |
| `manifest.json` | Artifact identity, boundaries, entrypoints, lengths, and SHA-256 hashes. |
| `install.ps1`   | Per-user physical installation without ecosystem activation.             |
| `uninstall.ps1` | Configuration-aware removal of product-owned payloads.                   |

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

The NuGet netcore plugin payload is installed separately under
`~/.nuget/plugins/netcore/azureauth-credprovider` on both platforms, using the
current user's profile directory.

The product root contains:

- `app/` for the complete application payload;
- `bin/` for CLI and Git launchers;
- `python/` for the wheel; and
- `installation.json` for the exact installed roots and source identity.

Physical installation does not mutate global PATH, shell profiles, the Windows
registry, Git configuration, NuGet configuration, or Python environments. The
wheel must be installed into the exact Python environment that imports the
backend. On Linux, `configure python` then writes the backend manifest and
controlled-PATH `keyring` shim. Default installation roots are disjoint from
those configuration targets, so payload replacement does not erase owned Python
configuration.

## Lifecycle

Installation rejects an artifact whose manifest does not declare the internal
non-release schema, supported operating system, and host-matching x64 RID. It
validates the manifest file inventory and hashes, copies the full app payload to
both the product app root and NuGet's conventional netcore plugin root, installs
launchers and the wheel, restores Unix executable modes, and records an install
receipt.

By default, uninstallation invokes:

```text
azureauth-credprovider unconfigure git
azureauth-credprovider unconfigure nuget
azureauth-credprovider unconfigure python
```

It then removes only the product and NuGet plugin roots recorded in the install
receipt. `-SkipConfigurationCleanup` is an explicit escape hatch for damaged or
incomplete installations; it does not bypass receipt validation. If the product
executable is missing, normal uninstall stops rather than silently leaving stale
ecosystem configuration.

`-Force` replaces only an empty target or an existing installation whose receipt
matches both requested roots. It rejects non-empty unrelated directories and
root changes that could orphan the prior NuGet payload. Product and NuGet roots
must be disjoint; neither may contain the other. Payload copying uses literal
source paths, including when the extracted bundle path contains PowerShell
wildcard characters. If installation fails after creating its target roots, it
removes those partial roots before propagating the failure.

## Linux Lifecycle Evidence

On 2026-07-31, an isolated `linux-x64` bundle completed:

1. Archive manifest and file-hash validation.
2. Installation into isolated product and NuGet roots.
3. CLI and Git launcher invocation.
4. Git, NuGet, and Python configuration without authentication.
5. Wheel installation into an isolated virtual environment.
6. Backend-manifest loading and exact `python-keyring` apphost argv verification.
7. Configuration-aware uninstallation.
8. Removal of the product and NuGet payload roots.
9. Refusal to delete a damaged installation without the explicit cleanup bypass.
10. Rejection of unsupported generation and host-mismatched RID requests.
11. Refusal to force-replace unrelated or receipt-mismatched roots.
12. Cleanup of roots created by a failed installation.
13. Rejection of nested product and NuGet roots.
14. Installation from an extraction path containing wildcard characters.
15. Rejection of hidden files absent from the bundle manifest.

No token acquisition, AzureAuth launch, browser interaction, WAM interaction,
device code, or private-feed authorization occurred.

## Explicit Boundaries

This evidence does not establish:

- a signed or production release installer;
- Windows bundle execution;
- Windows Python subprocess mode and its required real `keyring.exe` launcher;
- standalone Ubuntu 24.04 acceptance;
- exact Windows 11 24H2 or Windows Server acceptance;
- installer-produced binary authentication;
- private-feed authorization; or
- macOS support.

Those remain separate release evidence gates.
