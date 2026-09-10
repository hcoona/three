# Workspace and Toolchain Setup

The repository owner maintains shared setup guidance; contributors use it before
running project checks. Workspace/build manifests and tool configuration own
membership and executable settings. This guide routes to those authorities
rather than maintaining a second project database.

## JavaScript and TypeScript

[The pnpm workspace](../../pnpm-workspace.yaml) defines members and their shared
lockfile/linking settings. [Root scripts](../../package.json) define the build,
test, lint and format fan-out. Use [mise](../../mise.toml) for tool version intent
and the lockfile for resolved tool versions.

From the repository root, `pnpm install` prepares the workspace. `pnpm run build`
runs package builds; the test, lint and format scripts use `--if-present` for
packages without those scripts. Review needed install scripts with
`pnpm approve-builds`; do not globally disable the package-manager checks.

## Python

[pyproject.toml](../../pyproject.toml) owns uv workspace membership. Members live
under `src/`; there is no separate top-level `OnePython/` workspace. Project
manifests own their dependencies, supported versions and package contracts.

## .NET

[dirs.proj](../../dirs.proj) is the active C# traversal for `src/`, `tests/` and
`src/lab/`. Shared SDK and package selection use
[global.json](../../global.json) and
[Directory.Packages.props](../../Directory.Packages.props).
The [compatibility rationale](dotnet-compatibility.md) explains retained local
exceptions; the referenced build files own their values.

## Repository toolchain

Install the tool versions recorded in `mise.lock` from the repository root:

```bash
mise install --locked
```

The repository validates Linux x64 and Windows x64 lock entries. When tool
versions change, refresh both mise configuration roots instead of running an
unlocked platform-specific install:

```bash
mise lock --platform linux-x64,windows-x64
mise -C src/public/lib/hexo-renderer-asciidoc lock --platform linux-x64,windows-x64
```

MISE 2026.7.17 and later provide the built-in `core:dotnet` backend used by this
repository. Do not install `version-fox/vfox-dotnet` manually. If that legacy
plugin is already installed, remove it before installing the repository tools:

```bash
mise plugins uninstall dotnet
mise install --locked
```

HK is the sole Git hook and lint runner; the former prek-generated pre-commit
configuration is no longer maintained.

Install the repository-managed Git hooks after installing the toolchain:

```bash
mise exec -- hk install --mise
```
