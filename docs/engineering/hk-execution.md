# HK Execution and Platform Boundaries

Repository tooling maintainers maintain this guide from [hk.pkl](../../hk.pkl),
[the shared HK component](../../src/private/lib/hk/), and the wrapper sources.
Contributors use it when a check hangs, receives incorrectly quoted paths,
or appears to pass without inspecting its inputs. The root
[toolchain guide](../../README.md#repository-toolchain) owns installation;
[repository record policy](../governance/record-system.md) owns generic
contribution and control lifecycle. This guide explains execution behavior,
not an additional release gate or a second catalog of checks.

## Profiles and Input Scope

[mise.toml](../../mise.toml) selects `HK_PROFILE=small,medium`. These are
validation profiles, not Buddy or Official release channels. The actual HK
configuration owns step selection, exclusions, and execution points.

Root HK excludes generated APM interfaces and several specialized surfaces
from general linters. Workflow YAML has its own actionlint step; general
Markdown checks exclude `.github` paths. A skipped check is not a validation
result for the excluded files. v3 static-reference feedback and its
path-selected product suite have distinct responsibilities; a successful
lint run does not establish publication authority or destination acceptance.

The pre-commit hook uses Git stash and index checking. Preserve its existing
source-tree semantics when diagnosing hooks; do not infer the checked tree
from the editor buffer. Repository-wide checks and any manual file selection
must report which files and execution mode they actually covered.

## Quoted Paths and Watchdogs

[hk_exec.py](../../eng/scripts/hk_exec.py) normalizes quoted file arguments
and splits the command from the file list at the **last** `--`. This matters
for commands such as `pnpm exec -- prettier --check -- <files>`, where the
first separator belongs to pnpm. It also supports per-file execution,
heartbeats, timeout handling, and process-tree termination. Its controls are
`HK_EXEC_TIMEOUT_SECONDS`, `HK_EXEC_HEARTBEAT_SECONDS`, and `HK_EXEC_PER_FILE`;
the implementation owns their defaults and command-line overrides.

[hk_pkl_eval.py](../../eng/scripts/hk_pkl_eval.py) evaluates Pkl files
individually, preserving failure output and normalizing quoted paths.
[hk_actionlint.py](../../eng/scripts/hk_actionlint.py) adds a watchdog with
`HK_ACTIONLINT_TIMEOUT_SECONDS` and `HK_ACTIONLINT_HEARTBEAT_SECONDS`, plus
optional `HK_ACTIONLINT_SHELLCHECK` and `HK_ACTIONLINT_PYFLAKES` executable
overrides. In the inspected accepted source, unset integration overrides add
no `-shellcheck` or `-pyflakes` option; actionlint chooses its normal behavior.
This source observation does not prove that either executable is installed
or that a complete Windows workflow check passes.

The shared [Typos step](../../src/private/lib/hk/steps/Typos.pkl) uses
`--force-exclude` so explicit HK file lists respect [.typos.toml](../../.typos.toml).
That configuration retains targeted imported Hjg.Pngcs exclusions. If an
independently authorized cleanup changes them, recheck the affected files and
the existing Typos step; an exclusion is not evidence of clean source.

## Retained Windows Evidence and Limit

The [2026-04-11 Windows snapshot](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/HK_WINDOWS_STEP_STATUS.md)
reported that quoted-path and invocation failures had been resolved, while
`mise exec -- hk check --no-progress --no-fail-fast --all` still exited `1`
for repository lint/format findings. The source explicitly distinguished
execution compatibility from content quality. It did not establish that all
repository checks were green. Its per-step counts and completion chronology
remain in Git rather than becoming current compatibility promises.

The snapshot also said actionlint's shellcheck/pyflakes integrations were
disabled by default. That description differs from the accepted wrapper
source described above. The snapshot and implementation were inspected at
`b673ee27aab8553a4ee259bcaca87a197178463c`; this migration ran on Linux and did
not revalidate Windows parity or settle the original observation. Before
relying on that old mitigation for release hardening, the tooling maintainer
must verify the actual command, installed tools, and result in the intended
Windows environment. Any change to integration behavior remains separate
from this record relocation.
