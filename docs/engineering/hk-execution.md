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

## Selecting Checks by Their Inputs

Select a check from the files it consumes, including configuration, fixtures,
and build dependencies. Project ownership alone is not a test dependency, and
a file extension alone does not establish that a file is narrative prose.
Keep the selectors in root `hk.pkl`. Normal hooks share file checks and an
impact dispatcher; the internal `impact-check` hook owns product test selection.
Both use HK's matcher against complete change paths. File-reading wrappers
then select existing operands before invoking their tools.

| Check responsibility     | Selection basis                                                            | Example                                                                                                                 |
| ------------------------ | -------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| File formatting and lint | Supported file types and existing exclusions                               | Markdown prose receives the applicable Markdown checks.                                                                 |
| Product regression tests | Implementation, executable contracts, fixtures, and execution dependencies | v3 source, smoke package inputs, descriptors, locks, and inherited .NET build configuration select `v3-control-pytest`. |
| Repository invariants    | The invariant's accepted execution boundary                                | The bounded static-reference scan runs whenever root HK runs and requires prepared authorities.                         |

The v3 selectors group implementation, contract, and execution inputs. Within
the declared engineering roots, only the root `README.md` and Markdown under
`docs/` are narrative exclusions from the expensive suite. Other files remain
selected, including new extensions and non-Markdown files under `docs/`.
The exclusions belong to that test step, not to general linting or static
references. CODEOWNERS continues to cover the documentation it owns.

Markdown in smoke packages, test fixtures, and executable source directories
remains an input. The archived workflow retirement notices also remain explicit
contract inputs because tests read their contents. The scholarly publication
step retains its own skill/package inputs; executable skill instructions do
not inherit v3's prose exclusions. v3's selector and path transport also accept
binary fixtures and symbolic links. The Pkl schema matches the pinned HK 1.56.1
runtime so the binary-input option is effective.

When a consumer starts reading an excluded document, update its selector and
regression coverage in the same change. Likewise, when a build or test gains
an external input, register that dependency rather than widening the selector
to an unrelated project. Keep unknown files within declared implementation
roots selected until their role is established. HK configuration changes
themselves select the full v3 suite so selector changes receive regression
validation.

HK's native Git selection omits deleted paths and rename sources. The
[changed-path helper](../../eng/scripts/workflow_delivery_v3_hk.py) retains
both with Git name-status output. Its `--files0` mode passes the complete list
to HK through a temporary NUL-delimited file, preserving empty lists and path
boundaries without large shell arguments. Both CI callers use this mode with
explicit refs; `--staged` provides the corresponding local entry. The file
lasts until the child exits. The helper never guesses CI refs from the index.
The general CI workflow's manual entry uses `--all` because it has no event
baseline for a change range.

The file-linter steps set `HK_SKIP_MISSING_FILES=1` only for their subprocesses.
The existing execution, actionlint, and Pkl wrappers then omit absent file
operands. If none remain, they return without invoking the file reader; they
never turn a deletion-only check into an implicit whole-repository scan.
Commands without explicit file operands retain their existing behavior.
For helper-driven checks, scholarly tests, Node authority validation, lock checks,
and generated-file checks retain the complete HK input list and their original
profiles/fix/staging behavior. Product test subprocesses do not inherit the
file-linter setting.

During normal pre-commit, the dispatcher reads complete staged changes after
HK's configured stash. Other direct calls use HK's explicit file list, including
`--all`. Raw HK ref selection and default staged `hk check` do not preserve
deleted paths; use the helper for complete change-impact checks. The dispatcher
runs only once, after authority preparation, and passes complete paths to the
internal hook. This staged-history augmentation is specific to v3; other native
pre-commit steps retain HK's original file selection. The internal hook is for
selection and dispatch, not a replacement for the root validation gate.

Outer `--plan` previews file checks and the dispatcher without running it.
For a staged product-selection preview, use the helper with `--staged --files0 -- hk run
impact-check --plan --json`; for a CI preview substitute both refs for `--staged`.
The [contribution guide](../../CONTRIBUTING.md#local-checks) gives complete local
commands. The internal hook does not repeat preparation or static scanning.

The pinned HK integration tests exercise staged pre-commit and the complete
staged/CI helper entries using real Git and HK plans. They cover prose-only and
mixed changes, machine-consumed Markdown, deletion and rename boundaries, and
explicit all-files selection. Plans establish selection, not successful check
execution. A selected suite still runs in full; this configuration does not
attempt per-test dependency analysis or cache previous results.

The existing profile membership is preserved: `small` does not mean every
selected step is cheap. Explicit `--all` continues to select the v3 suite.
The [v3 CI contract](../../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md#source-tree-conformance-through-hk)
owns the composite obligation and unconditional static-reference requirement.

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
