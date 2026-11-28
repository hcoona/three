# HK Execution and Platform Boundaries

Repository tooling maintainers maintain this guide from [`hk.pkl`](../../hk.pkl),
[`src/private/lib/hk/`](../../src/private/lib/hk/), and
[`eng/scripts/workflow_delivery_v3_hk.py`](../../eng/scripts/workflow_delivery_v3_hk.py).
[`docs/engineering/workspaces.md`](workspaces.md) owns installation;
[`record-system.md`](../governance/record-system.md) owns control governance.

## Profiles and Scope

`mise.toml` sets local `HK_PROFILE=small,medium`; CI runs `small,medium,large`.
`pre-commit` uses Git stashing (`stash = "git"`), while explicit manual checks
report their selected path/snapshot mode. `hk.pkl` requires HK `2.13.0+`,
configures hook-level `timeout = "12m"` across `pre-commit`, `check`, and `fix`,
and excludes generated `.agents/` outputs and specialized trees from general
linters; workflow YAML uses `actionlint`.

## CI Execution Contract

HK owns fast source, configuration, link, and record conformance (including
`SANY` syntax/semantic parsing for `*.tla` and Alloy parsing/type checking for
`*.als`); project unit, scenario, and integration suites—as well as Alloy
bounded solving, `TLC` finite-state model checking and `PlusCal`
translation—run in CI jobs selected by Workflow Delivery
([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) and
[`src/private/app/workflow-delivery/docs/design/ci.md`](../../src/private/app/workflow-delivery/docs/design/ci.md))
or through explicit local commands (`mise run alloy:exec`, `mise run tla:tlc`)—not
HK's `large` profile.

- **Single `Validate` merge gate:** The `main` ruleset requires `Validate`,
  which depends on every other job in `ci.yml` and fails on failed, skipped,
  missing, or canceled upstream outcomes (`!cancelled()` on worker jobs so
  superseded runs stay cancelable while failed scope selection still fails
  closed).
- **Affected selection and fail-closed tools:** General CI selects affected
  language/platform work from the actual Git comparison (or all work in explicit
  full mode). Tests that CI must run fail closed when `CI=true` and a required
  tool is missing; OS/capability skips are summarized from TRX outputs without
  masking missing-tool failures.
- **Validation reporting:** Record the tested commit, executed commands, and
  outcomes in the Issue or PR; collect JUnit/CI timing and inode/temp metrics
  only when diagnosing performance or resource issues.

## Direct Command Execution, File Operands, and Lock Widening

- **Shell-free `Config.Command`:** `hk.pkl` and `src/private/lib/hk/` execute
  both guard steps and file-reading linters directly via structured
  `Config.Command` `argv` (passing `--` before `{{files}}` on positional-file
  linters so leading-dash filenames stay literal, `--force-exclude` on `typos`
  so `.typos.toml` exclusions apply, and `./node_modules/.bin/<tool>` rather
  than `pnpm exec` for `pnpm`-locked binaries so HK's Windows `.cmd` shim
  command-line sizing caps `cmd.exe` batches below 8,191 characters). HK
  `2.13.0+` natively filters out deleted and absent file operands before
  expanding `{{files}}` and skips filtered steps when no existing operands
  remain while still running unconditional checks.
- **`eng/scripts/workflow_delivery_v3_hk.py`:** Passes NUL-delimited impact
  paths (`--files0`, retaining deletions and rename endpoints) from `--staged`
  or `--from-ref` / `--to-ref`, omits operands replaced on disk by regular
  directories so HK does not recursively walk them, and fails closed on symlinks
  to directories. When `mise.lock`, `uv.lock`, or `pnpm-lock.yaml` changes,
  steps whose structured `Config.Command` linter version changed (or cannot be
  resolved to an unchanged lock entry, including multi-tool steps such as
  `tla-sany` across `java` and `github:tlaplus/tlaplus`) automatically widen to
  `--all --step=<step>`.

## Retained Windows Evidence

The [2026-04-11 Windows snapshot](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/HK_WINDOWS_STEP_STATUS.md)
verified quoted-path and invocation compatibility while `hk check --all` still
exited `1` on repository lint/format findings, and noted that `actionlint`'s
`shellcheck`/`pyflakes` integrations were disabled by default—differing from the
former `actionlint` wrapper at `b673ee27aab8553a4ee259bcaca87a197178463c`.
Subsequent migration to direct `Config.Command` execution ran on Linux and did
not revalidate Windows parity or settle that observation; recheck actual Windows
behavior before relying on that snapshot for release hardening.
