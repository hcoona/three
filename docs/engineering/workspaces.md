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

The root `.python-version` and V3 package runtime constant are generated from
`mise.lock`, checked against `mise.toml`. After a Python lock update, run
`mise run sync:python-version`; HK checks projection drift. Synchronize an
existing environment before using `--no-sync`. For example:

```powershell
uv sync --frozen --package three-workflow-delivery-v3
mise run test:v3
```

`mise run test:python` runs all configured Python test roots. Ordinary commits
run HK source checks; affected CI owns project tests. The
[execution contract](hk-execution.md#ci-execution-contract) explains selection,
required results and timing evidence. Scholarly-publication's standalone
script runtime remains governed by its own package contract.

The root pytest configuration releases successful `tmp_path` fixtures and the
implicit temporary session directory after a successful run. Failed test-call
fixtures remain available for diagnosis within pytest's default three-session
retention window; its existing cleanup locks protect active runs. This applies
to all tests using the root configuration in explicit local commands and CI.
Workflow Delivery v3's session fixture also places native subprocess temporary
files under that session directory, so their scratch files and temporary caches
share its retention lifecycle. Its environment lasts for the pytest session and
can also affect later tests from other projects in that process.

Use an executable temporary filesystem with enough free bytes and inodes for
native integration fixtures. On Linux, check both `df -h` and `df -i` for that
filesystem. A fresh, task-owned `TMPDIR` (or `TEMP` on Windows) outside the
workspace ancestry can isolate a run; preserve its logs outside that directory.
Avoid reusing `--basetemp`: pytest clears that explicit path before a run, and
successful-session cleanup does not apply to it.

Cleanup is best effort. Forced termination can leave files and cleanup locks;
pytest considers an abandoned lock eligible after three days, so interrupted
runs do not have an immediate retention bound. For earlier recovery, preserve
needed diagnostics and confirm the owning run and its child processes have
exited before removing only that run's identified temporary directory. Never
clear shared temporary roots or other runs' data. Native tool caches outside
pytest's session directory follow their own lifecycle.

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
