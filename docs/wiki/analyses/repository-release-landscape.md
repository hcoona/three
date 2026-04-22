# Repository Release Landscape

## Executive Summary

The monorepo already has enough metadata to tell which projects are intended to
be external, but it does not yet have a complete workflow layer for releases.
The clearest current patterns are:

- public C# libraries are packable by path;
- one public C# app already uses `dotnet publish` plus Inno Setup packaging;
- another public C# app and several private apps still rely on
  `Microsoft.Build.Artifacts`;
- `nbgv-python` and `hexo-renderer-asciidoc` are the clearest publishable
  Python and Node.js packages;
- buddy and official channels already exist in policy scripts, but not yet as
  checked-in GitHub workflow files in this worktree.

## Desired Model Captured by This Analysis

This analysis assumes the intended future release shape is:

- most public projects should be releasable;
- some private projects also need release automation;
- releases should support `buddy` and `official` profiles;
- OIDC or other passwordless flows should be required wherever registries
  support them; no unsupported target platform is currently known;
- C# apps should publish binaries through explicit `dotnet publish` steps rather
  than through the current Artifacts SDK pattern;
- some C# apps also need a second-stage Inno Setup packaging step;
- target-specific packaging may vary, but the produced binary should stay
  unified across targets to avoid inconsistent outputs.

## Repository Inventory

### C# Projects

| Scope                      | Findings                                                                                           | Release Signals                                                                                                                                                  |
| -------------------------- | -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/public/lib`           | 9 projects, including 8 packable libraries plus `Memoization.Generators` as a non-packable helper. | Path-based `IsPackable=true` for public libraries; representative packages already define `PackageId`, README, release notes, and compatibility baselines.       |
| `src/public/app`           | 2 projects: `ImageOcclusionEditorWinUI3` and `PhiFailureDetector.ConsoleApp`.                      | `ImageOcclusionEditor` already has a `dotnet publish` + Inno path; `PhiFailureDetector.ConsoleApp` still uses `Microsoft.Build.Artifacts`.                       |
| `src/private/app`          | 6 projects.                                                                                        | These are not packable by rule, but several still use `Microsoft.Build.Artifacts`, so some private-app binary release work likely already matters operationally. |
| `src/lab` and `src/sample` | non-packable executables and samples.                                                              | Good default is internal-only until a release decision says otherwise.                                                                                           |

### Python

| Scope                                      | Findings                                                   | Release Signals                                                                       |
| ------------------------------------------ | ---------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `src/public/lib`                           | `nbgv-python` is the only public library workspace member. | Clearly public metadata, console script, Hatch entry point, public URLs.              |
| `src/public/app`                           | `markdown-hybrid-search-mcp`.                              | Lives under `public/app` but is still marked `Private :: Do Not Upload`.              |
| `src/private/app`, `src/lab`, `src/sample` | 10 additional workspace members.                           | All inspected manifests are internal-facing or explicitly `Private :: Do Not Upload`. |

### JS/TS

| Scope                                                      | Findings                                                     | Release Signals                                                                                                                       |
| ---------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------- |
| `src/public/lib`                                           | `hexo-renderer-asciidoc` and `steam-account-history-to-csv`. | `hexo-renderer-asciidoc` looks publishable; `steam-account-history-to-csv` is private and behaves like an extension artifact project. |
| `src/private/app/im-acp-gateway/poc/*`                     | 3 package manifests.                                         | All are private proof-of-concept packages.                                                                                            |
| `src/public/lib/hexo-renderer-asciidoc/examples/hexo-site` | example app.                                                 | Private example, not a release target.                                                                                                |

## Current Release Plumbing

### What already exists

1. **Policy layer for channels and registries**
    - `eng/scripts/release_orchestrate_policy_publish_targets.sh` already
      distinguishes `official` and `buddy`.
    - It already validates Node.js, Ruby, Python, and WXT publish targets.
    - It already warns about `packages: write` permissions for GitHub Packages.
2. **A passwordless pattern already exists**
    - `eng/scripts/publish_rubygems_org_idempotent.sh` explicitly expects trusted
      publishing or OIDC-style credential setup by the caller.
3. **A good C# app pattern already exists**
    - `ImageOcclusionEditor` uses direct `dotnet publish` into a conventional
      `out/` layout.
    - Its installer is built as a separate Inno Setup step.
4. **C# library packaging intent is already explicit**
    - `src/Directory.Build.props` makes library projects packable and apps
      non-packable.

### What is still missing

1. **Workflow layer**
    - No checked-in `.github/workflows/*.yml` release workflows were found in
      this worktree.
2. **C# channel policy**
    - The release policy script has no NuGet.org or GitHub Packages branch for
      C# yet.
3. **Per-project release matrix metadata**
    - Visibility alone is not enough to decide release behavior.
    - Examples: `markdown-hybrid-search-mcp` is under `public/app` but private in
      metadata; `steam-account-history-to-csv` is under `public/lib` but private
      and archive-oriented.

## Important Tensions and Migration Targets

### 1. Public path does not always mean publishable artifact

The repo currently mixes path-based intent and per-project metadata:

- public C# libraries are consistent with the directory rule;
- public Python and JS/TS projects can still opt out via metadata;
- browser extension and MCP-server style apps blur the boundary between
  "public", "publishable package", and "public binary/tool".

This means the eventual release system should store an explicit publishability
matrix instead of guessing from the path alone.

### 2. C# apps are split between the future model and the old model

`ImageOcclusionEditor` already follows the target shape:

- explicit `dotnet publish`;
- structured publish output;
- optional second-stage installer packaging.

`PhiFailureDetector.ConsoleApp` and multiple private apps still reference
`Microsoft.Build.Artifacts`, which conflicts with the intended future direction.
These projects are the obvious first migration candidates.

### 3. OIDC should be the baseline release posture

The Ruby publishing script already documents the pattern the repo should prefer:

- let the workflow acquire a short-lived identity;
- keep publish scripts credential-free;
- make reruns safe by checking whether a version already exists.

The same design should be replicated for:

- NuGet.org;
- GitHub Packages for NuGet or npm;
- PyPI or TestPyPI if Python packages are published there.

Under the current requirements discussion, this is stronger than a preference:
no known target platform currently lacks OIDC or trusted publishing support, so
the baseline assumption should be passwordless publication.

### 4. Packaging can diverge, but binaries should not

The current requirements baseline allows target-specific packaging differences,
such as attaching a raw binary to GitHub Release while pushing a package format
to a registry. However, those targets should be fed from the same canonical
binary output for the profile rather than from independently rebuilt binaries.

This implies the future release descriptor should distinguish:

- canonical binary production;
- optional post-build packaging transforms;
- target-specific distribution choices.

## Recommended Release Taxonomy

| Dimension       | Suggested meaning                                                                       |
| --------------- | --------------------------------------------------------------------------------------- |
| Visibility      | `public`, `private`, `lab`, `sample` indicates audience and default posture.            |
| Kind            | `lib`, `app`, `tool`, `extension`, `generator` affects output shape.                    |
| Release profile | `buddy` vs `official` determines release page type, registry targets, and approval bar. |
| Output form     | `package`, `binary`, `installer`, `archive`, or combinations.                           |
| Credential mode | Prefer OIDC/trusted publishing; use static secrets only when unavoidable.               |

## Practical Implications for Future Workflows

1. **C# libraries**
    - `buddy`: GitHub pre-release + GitHub Packages NuGet.
    - `official`: GitHub release + NuGet.org.
    - Needed next: add C# branches to the publish-target policy script and define
      pack/push steps that use passwordless auth where supported.
2. **C# apps**
    - Build binaries with explicit `dotnet publish`, probably in a matrix by RID
      or host target.
    - Keep the binary build canonical per declared variant, then derive any
      target-specific packaging from that shared output.
    - Package installers only for projects that declare the need, such as
      `ImageOcclusionEditor`.
    - Migrate remaining `Microsoft.Build.Artifacts` app projects to the same
      model.
3. **Python**
    - Start with `nbgv-python`.
    - Decide whether buddy uses TestPyPI, GitHub Release only, or a GitHub-hosted
      package story.
4. **JS/TS**
    - Start with `hexo-renderer-asciidoc`.
    - Treat WXT/browser-extension outputs separately from npm publication.

## Open Decisions

- Which private apps deserve recurring binary releases?
- Should public-path but metadata-private projects stay private, or should their
  metadata be updated to match the directory contract?
- What file should become the repo-wide source of truth for per-project release
  targets and profiles?
- Should buddy releases for Python use TestPyPI, or skip registry publication
  initially?

## Source Pages

- [README Monorepo Layout](../sources/2026-04-21-readme-monorepo-layout.md)
- [C# Packability Rules](../sources/2026-04-21-src-directory-build-props-packability.md)
- [Root Python Workspace](../sources/2026-04-21-root-pyproject-python-workspace.md)
- [Root pnpm Workspace](../sources/2026-04-21-root-package-json-pnpm-workspace.md)
- [mise Tooling Profile](../sources/2026-04-21-mise-tooling-profile.md)
- [HK Hook Profiles](../sources/2026-04-21-hk-hook-profiles.md)
- [Release Publish-Target Policy Script](../sources/2026-04-21-release-policy-publish-targets-script.md)
- [RubyGems Trusted Publishing Script](../sources/2026-04-21-publish-rubygems-script.md)
- [ImageOcclusionEditor WinUI3 Project](../sources/2026-04-21-image-occlusion-editor-winui3-csproj.md)
- [ImageOcclusionEditor dotnet publish Script](../sources/2026-04-21-image-occlusion-editor-dotnet-publish-script.md)
- [ImageOcclusionEditor Inno Installer Script](../sources/2026-04-21-image-occlusion-editor-inno-installer-script.md)
- [PhiFailureDetector Console App](../sources/2026-04-21-phi-failure-detector-console-csproj.md)
- [Hjg.Pngcs C# Package Metadata](../sources/2026-04-21-hjg-pngcs-csproj.md)
- [nbgv-python Metadata](../sources/2026-04-21-nbgv-python-pyproject.md)
- [markdown-hybrid-search-mcp Metadata](../sources/2026-04-21-markdown-hybrid-search-mcp-pyproject.md)
- [hexo-renderer-asciidoc Package Metadata](../sources/2026-04-21-hexo-renderer-asciidoc-package-json.md)
- [steam-account-history-to-csv Package Metadata](../sources/2026-04-21-steam-account-history-to-csv-package-json.md)
- [Workflow Release Requirements Interview](../sources/2026-04-21-workflow-release-requirements-interview.md)
