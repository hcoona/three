# Workflow Release CI HK Source Page

## Purpose

This source page records HK facts that are relevant to the high-level design for
CI affected validation. It supports the decision that local HK validation can be
planner-aligned while still defaulting to lightweight execution.

This page is source-oriented. It does not define the repository's final HK
configuration, CI workflow YAML, or validation-plan schema.

## Source Facts

### HK supports hook and manual execution entry points

- HK is a Git hook manager for running hooks on files in a Git repository.
- HK supports standard Git hook events such as `pre-commit`, `pre-push`,
  `commit-msg`, and other Git hooks.
- HK also supports direct manual execution with `hk run <hook>`.
- HK configuration examples include standard hooks and custom/manual hooks such
  as `check` and `fix`.

Sources:

- [HK getting started](https://hk.jdx.dev/getting_started.html)
- [HK hooks](https://hk.jdx.dev/hooks.html)
- [HK run CLI](https://hk.jdx.dev/cli/run.html)
- [HK configuration](https://hk.jdx.dev/configuration.html)

### HK supports selectable execution scope

- `hk check` and `hk run` support `--all` to run against all tracked files rather
  than only staged files.
- `hk check` and `hk run` support file-range and pull-request style selection
  through options such as `--from-ref`, `--to-ref`, and `--pr`.
- `hk check` and `hk run` support step selection and omission with `--step` and
  `--skip-step`.
- `hk check` and `hk run` support file filtering options such as `--glob` and
  `--exclude`.
- `hk check` and `hk run` support `--plan`, and `--json` can output the plan as
  JSON when combined with `--plan` or `--why`.

Sources:

- [HK check CLI](https://hk.jdx.dev/cli/check.html)
- [HK run CLI](https://hk.jdx.dev/cli/run.html)
- [HK settings: all](https://hk.jdx.dev/gen/settings-config.html#all)

### HK supports lightweight versus heavyweight selection mechanisms

- HK supports profiles for steps that should run only in selected contexts.
- Profiles can be enabled through configuration, environment variables, and CLI
  flags.
- HK documents a `slow` setting as a convenience that enables the `slow` profile,
  which is suitable for slower or more thorough checks.
- Steps can be selected or skipped explicitly with `--step` and `--skip-step`.

Sources:

- [HK settings: profiles](https://hk.jdx.dev/gen/settings-config.html#profiles)
- [HK settings: slow](https://hk.jdx.dev/gen/settings-config.html#slow)
- [HK configuration: step profiles](https://hk.jdx.dev/configuration.html#step-profiles-list-string)
- [HK check CLI](https://hk.jdx.dev/cli/check.html)
- [HK run CLI](https://hk.jdx.dev/cli/run.html)

### HK supports read-only CI-style checking

- HK has a `--check` / `HK_CHECK` setting that forces check commands instead of
  fix commands.
- HK documents this as useful for CI environments where code quality should be
  verified without making changes.
- HK's hook behavior distinguishes check commands from fix commands, but the
  documentation cautions that HK does not enforce that check commands avoid
  writing files.

Sources:

- [HK settings: check](https://hk.jdx.dev/gen/settings-config.html#check)
- [HK hooks](https://hk.jdx.dev/hooks.html)

### HK supports environment and context configuration

- HK configuration is layered, with CLI flags and `HK_*` environment variables
  having high precedence for each invocation.
- HK supports hook-level and step-level environment configuration.
- HK supports step conditions and exposes Git status data to conditions and
  templates.
- `--from-hook` is used by installed hooks so HK can behave appropriately when a
  repository has no `hk.pkl` or does not define a given hook event.

Sources:

- [HK configuration](https://hk.jdx.dev/configuration.html)
- [HK getting started: manual hook configuration](https://hk.jdx.dev/getting_started.html#configuring-manually-in-gitconfig)
- [HK run CLI](https://hk.jdx.dev/cli/run.html)

## Design Implications

- HK can support a local lightweight path and a heavier explicit path using
  documented mechanisms such as profiles, the `slow` profile, step selection,
  file selection, and `--all`.
- HK can expose an inspectable execution plan through `--plan` and JSON output,
  which is useful for local diagnostics and for keeping local preflight behavior
  understandable.
- HK can be aligned with CI planning concepts without requiring local hooks to
  execute heavy CI work by default.
- Heavy checks such as full test suites, full builds, release-shaped artifact
  production, and full scheduled-style validation should remain explicit rather
  than default pre-commit work unless a later design intentionally opts in.

## Cautions

- HK does not automatically infer lightweight versus heavyweight semantics. The
  repository must define those categories through profiles, step selection, hook
  configuration, or another explicit convention.
- HK profiles and `slow` support are source-backed concepts, but generic "tags"
  were not identified as a documented HK mechanism in the reviewed sources.
- HK `check` commands are conventionally read-only, but HK does not enforce that
  they never write files.
- Local HK success must not be treated as CI success. CI remains authoritative
  for validation plans and validation evidence.
