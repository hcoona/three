# Workflow Release Requirements Baseline

## Purpose

This page captures the currently confirmed business requirements for adding
workflow-based release support to the monorepo during the requirements phase of
the waterfall process.

## Confirmed Scope

- Include all public projects.
- Also include the private projects:
    - `src/private/app/qidian-novel-downloader/`
    - `src/private/app/vscode-copilot-telegram-hook/`

## Confirmed Participation Rule

- A project is releasable only when it owns a release descriptor file.
- If the descriptor file does not exist, workflow release must skip the project.
- Workflow release must not infer participation from directory structure alone.

## Confirmed Profile Rules

- Every in-scope project must support both `buddy` and `official`.
- Both profiles must be explicitly declared in the project-owned descriptor.
- A declared profile may legally have zero publish targets.
- If a profile declares any target other than GitHub Release, that same profile
  must also declare GitHub Release.
- A zero-target profile may omit GitHub Release and remain valid.
- When GitHub Release is declared, `buddy` always means a pre-release and
  `official` always means a release.

## Confirmed Artifact Rules

- A single profile may publish to multiple targets.
- Different targets may use different packaging forms.
- The same final target class may still require different packaging pipelines for
  different project kinds.
- Packaging differences must not lead to divergent binary builds.
- For a declared binary variant, compilation must stay canonical and unified even
  when that same build also emits target-ready packages or installers.
- The business requirement is to avoid recompiling a different binary for the
  same variant's different publication targets, not to force a separate build
  stage and packaging stage.
- Target-specific metadata or identity transforms are allowed when required by a
  target platform's constraints.

### Practical Meaning

Examples of acceptable behavior:

- A canonical build may emit a raw binary plus an installer for that same binary
  variant in one pass.
- NuGet.org receives a NuGet package.
- Both outputs originate from the same underlying build result where that build
  is meant to represent the same shipped binary.
- A library project may publish both its original package assets and a NuGet
  package to GitHub Release.
- An app project may publish either an Inno Setup installer or a host-specific
  `dotnet publish` binary, depending on the project's declared packaging path.
- A Node package may add a scope when targeting GitHub Packages while preserving
  its established community package name when targeting npmjs.

Examples of disallowed behavior:

- recompiling a supposedly identical binary variant separately for GitHub
  Release and registry publication in ways that can drift;
- letting target-specific publication logic silently choose different binary
  content for the same declared release variant;
- forcing a second compilation for a package or installer when that output could
  have come from the same canonical build for the same variant.

## Confirmed Security Rule

- If a publication target supports OIDC or trusted publishing, the workflow must
  use a passwordless or secretless flow.
- Static credentials are only acceptable when the target platform lacks that
  capability.
- At the moment, there are no known in-scope targets that require repository-
  stored static publishing credentials.
- GitHub Packages is the known non-OIDC path in scope: workflow publication uses
  the built-in `GITHUB_TOKEN`, which is still accepted as a secretless flow for
  this requirements baseline.

## Confirmed Approval Rule

- `buddy` is treated as a day-to-day delivery action.
- `buddy` may be triggered by repository users with `write` permission or higher.
- `buddy` does not require additional approval.
- `official` is treated as a repository-maintenance action.
- `official` may be triggered by repository users with `maintain` permission or
  higher.
- `official` requires an additional approval before publication.
- Any repository user with `maintain` permission or higher may approve an
  `official` release.
- If the initiator is `admin`, self-approval is allowed.
- If the initiator is `maintain` but not `admin`, self-approval is not allowed.

## Confirmed Lifecycle Rule

- The first delivery scope should prioritize manual `workflow_dispatch`
  initiation.
- Each workflow-dispatch run may target one or more projects selected by input
  parameters.
- `buddy` and `official` are separate workflow entry points rather than a shared
  profile selector inside one workflow entry.
- The first delivery scope must support rerunning the entire release against the
  same input.
- For rerun purposes, "the same input" means the same workflow entry point, the
  same selected project scope, and the same release inputs.
- If whole-release rerun is later proven technically infeasible, that reduction
  must be re-confirmed with the user instead of being assumed by the team.
- The first delivery scope does not require single-target retry.
- Replay concerns should be handled primarily through detection-based skipping
  and idempotent retry behavior.
- Replay handling should stay automatic; the workflow does not need extra manual
  replay-choice controls.
- The first delivery scope must support a dry-run or validation-only mode that
  performs input and descriptor validation without publishing to external
  targets.
- The first delivery scope may leave externally visible partial-success results
  in place when one or more targets have already succeeded.
- The first delivery scope does not require automatic compensation or rollback
  for partial success; manual remediation is acceptable.
- The first delivery scope has no exceptional cases that require automatic
  rollback.
- The first delivery scope must support manual operator cancellation.
- The first delivery scope does not require a repo-defined supersession model
  across release requests.
- If GitHub Actions native concurrency controls conveniently support canceling
  an older in-progress run for the same workflow entry point and the same
  commit, the workflow may adopt that behavior.
- For this optional native-cancellation rule, duplicate means the same workflow
  entry point and the same commit, regardless of project subset selection or
  other release inputs.
- If native duplicate-run cancellation is used, ordinary cancelled status is
  sufficient; no distinct superseded status is required.
- When a release is cancelled, whether manually or by native duplicate-run
  cancellation, it must stop the remaining unpublished targets while leaving
  already published results visible for manual follow-up.
- `buddy` and `official` use the same visible handling rules for failure,
  cancellation, and partial success.
- Manual remediation does not require a separate workflow-level closure or gate;
  any out-of-band follow-up is outside the workflow's scope.
- The first delivery scope does not require automatic release triggering from a
  Git tag.
- Workflow release should create or verify the required Git tag automatically
  when the selected run includes at least one GitHub Release publication,
  rather than relying on manual tag operations for those runs.
- A zero-target run, or any other run whose selected publish nodes contain no
  GitHub Release publication, must not imply a tag side effect.

## Confirmed Versioning and Immutability Rule

- Version identity is determined primarily by the Git commit being built.
- Outputs built from the same commit should share the same version identity.
- Outputs built from different commits should not share the same version
  identity.
- Overwrite should generally be avoided.
- `official` does not allow overwrite.
- `buddy` overwrite is allowed only as an exceptional explicit `FORCE` action.
- Target-platform constraints always take precedence over any business desire to
  overwrite.
- `buddy` to `official` promotion is in scope and must stay on the same commit
  and the same version identity.
- Promotion does not require reusing the exact same built artifact; rebuilding is
  allowed when `buddy` and `official` build configurations differ.
- For GitHub Release, same-commit `buddy` to `official` promotion must keep the
  same release tag while converging to the full official publish intent for that
  tag: desired release state, final asset set, and asset labels.
- Replay handling for that promotion must evaluate whether that full GitHub
  Release publish intent is already satisfied, not merely whether the tag
  exists or whether only the release state matches.
- Package-registry promotion on the same registry and the same published package
  name is prohibited.
- `buddy` and `official` may share a registry only when the published package
  names differ because the descriptor declares different target-side identities.
- `official` does not require a prior `buddy`; it may also be published directly
  from the same commit.
- `official` is the higher-status state for a version.
- Once a version enters `official`, that version is considered formally frozen
  and may no longer be force-overwritten through `buddy`.
- `buddy FORCE` keeps the same authorization boundary as ordinary `buddy`:
  `write+`, no extra approval, and no required reason field.

## Confirmed Delivery-Scope Rule

- The first delivery scope must cover multiple target classes from the start.
- The first delivery scope is not allowed to ship with support for only one
  target class.
- The target model must distinguish ecosystem-specific publication families
  rather than treating "package registry" as one undifferentiated bucket.
- The first delivery scope includes GitHub Release plus the NuGet, PyPI, npm,
  and RubyGems publication families.
- GitHub Release is mandatory for any non-zero-target profile, but a zero-target
  profile may omit it.
- Package-registry publication remains descriptor-driven for both `buddy` and
  `official`; there is no repo-wide default registry mapping.
- The same project may declare multiple package-registry targets within the same
  ecosystem and same profile.
- `buddy` and `official` must not publish to the same package registry under the
  same published package name.
- GitHub Packages may be used as either a `buddy` target or an `official`
  target when the ecosystem is supported there, but that platform capability
  does not create a repo default.
- Python is the known exception for GitHub Packages among the first-delivery
  ecosystems: GitHub Packages is not available as a Python package target, so
  Python `buddy` falls back to GitHub Release only, and Python `official`
  package publication uses PyPI when declared.

## Confirmed Acceptance Rule

- The first delivery scope must be accepted against real projects rather than
  against workflow skeletons alone.
- Acceptance coverage must include at least these representative scenarios:
    - a C# library project;
    - a C# app published as a host-specific `dotnet publish` binary;
    - a C# app published through an Inno Setup installer path;
    - a Python package project;
    - a Node package project;
    - a Ruby package project.
- Acceptance must cover both `buddy` and `official` overall, but not every
  representative project is required to publish through both profiles if one of
  its profiles intentionally has zero targets.
- Acceptance must include real publication, not only dry-run or validation-only
  execution.
- Acceptance must include at least one real `official` publication.
- Acceptance must prove one real `buddy` to `official` promotion on the same
  commit, including the same-tag GitHub Release pre-release to release case
  when GitHub Release is part of that run.
- Acceptance must respect immutable-registry constraints and must not rely on
  publishing the same package name to the same registry from both profiles.
- Acceptance must also prove one real direct `official` publication without a
  prior `buddy`.
- Acceptance must explicitly prove multi-project `workflow_dispatch` scope in at
  least one real run.
- Acceptance must explicitly prove dry-run or validation-only behavior.
- Acceptance must explicitly prove whole-release rerun behavior against the same
  input, including rerun after partial success on immutable targets.
- Acceptance must explicitly prove manual cancellation behavior.
- Acceptance must explicitly prove the approval boundary between `buddy` and
  `official`, including `admin` self-approval and the prohibition on plain
  `maintain` self-approval.
- If any representative project declares a GitHub Packages target in the first
  delivery scope, acceptance must include at least one real GitHub Packages
  publication.

## Design Implications

The future release descriptor needs to express, at minimum:

- whether the project participates in workflow release at all;
- profile-level declarations for `buddy` and `official`;
- publish targets per profile;
- canonical binary-production variants;
- target-specific packaging or transformation steps derived from those binaries;
- project-kind-specific packaging paths even for the same target class;
- target-specific metadata and identity transforms such as scoped package names.

## What Changed in the Existing Analysis

Compared with the earlier repo landscape analysis, the requirements baseline is
now tighter in ten places:

1. Secretless publication is no longer just a preferred direction; it is the
   current hard requirement for all known in-scope targets, with GitHub
   Packages explicitly handled through `GITHUB_TOKEN`.
2. Target-specific packaging flexibility is allowed, including project-kind-
   specific pipelines and target-specific identity transforms, but binary
   generation must remain unified to avoid inconsistent outputs.
3. Approval authority is now role-based: `buddy` is `write+` without extra
   approval, while `official` is `maintain+` plus an approval gate.
4. The first delivery scope now prioritizes manual `workflow_dispatch`
   initiation.
5. The first delivery scope must support whole-release rerun and dry run, while
   replay concerns should be addressed with skip detection and idempotent
   behavior rather than mandatory single-target retry.
6. The first delivery scope may preserve partial success and rely on manual
   remediation instead of mandatory automatic rollback.
7. Version identity is commit-based, `official` is the freezing state, and the
   first delivery scope must cover multiple ecosystem-specific target classes
   rather than only one.
8. GitHub Release is now conditionally mandatory for every non-zero-target
   profile, with fixed `buddy` = pre-release and `official` = release semantics
   whenever it is declared.
9. Package targets remain explicitly project-declared even when GitHub Packages
   supports the ecosystem, Python now has an explicit GitHub Release / PyPI
   split because GitHub Packages is not a Python target, and immutable
   registries may not be shared across profiles under the same published package
   name.
10. The first delivery scope now has concrete acceptance expectations around
    real-project coverage, real publication, real `official`, promotion,
    direct-official validation, multi-project dispatch, dry-run, rerun,
    cancellation, approval boundaries, and GitHub Packages coverage when that
    target is exercised.

## Still Open for Design Work

- the final descriptor filename and syntax;
- the exact schema shape and reuse model;
- the exact workflow YAML and job decomposition that implements these rules.

## Related Pages

- [Workflow Release Requirements Interview](../sources/2026-04-21-workflow-release-requirements-interview.md)
- [GitHub Packages Supported Registries](../sources/2026-04-22-github-packages-supported-registries.md)
- [Repository Release Landscape](./repository-release-landscape.md)
