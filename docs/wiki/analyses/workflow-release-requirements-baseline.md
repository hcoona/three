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

## Confirmed Artifact Rules

- A single profile may publish to multiple targets.
- Different targets may use different packaging forms.
- Packaging differences must not lead to divergent binary builds.
- Binary production should be canonical and unified for the profile or declared
  binary variant, then reused for target-specific packaging and publication.

### Practical Meaning

Examples of acceptable behavior:

- GitHub Release receives a raw binary plus an installer derived from that
  binary.
- NuGet.org receives a NuGet package.
- Both outputs originate from the same underlying build result where that build
  is meant to represent the same shipped binary.

Examples of disallowed behavior:

- rebuilding a supposedly identical binary separately for GitHub Release and
  NuGet publication in ways that can drift;
- letting target-specific publication logic silently choose different binary
  content for the same declared release variant.

## Confirmed Security Rule

- If a publication target supports OIDC or trusted publishing, the workflow must
  use a passwordless or secretless flow.
- Static credentials are only acceptable when the target platform lacks that
  capability.
- At the moment, there are no known target platforms in scope that lack OIDC or
  trusted publishing support.

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
- The first delivery scope must support rerunning the entire release against the
  same input.
- If whole-release rerun is later proven technically infeasible, that reduction
  must be re-confirmed with the user instead of being assumed by the team.
- The first delivery scope does not require single-target retry.
- Replay concerns should be handled primarily through detection-based skipping
  and idempotent retry behavior.
- The first delivery scope must support a dry-run or validation-only mode that
  performs input and descriptor validation without publishing to external
  targets.
- The first delivery scope may leave externally visible partial-success results
  in place when one or more targets have already succeeded.
- The first delivery scope does not require automatic compensation or rollback
  for partial success; manual remediation is acceptable.
- Lifecycle rules for cancellation, supersession, and tag-driven initiation
  remain to be defined.

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
- The exact target-class list is still to be defined.

## Design Implications

The future release descriptor needs to express, at minimum:

- whether the project participates in workflow release at all;
- profile-level declarations for `buddy` and `official`;
- publish targets per profile;
- canonical binary-production variants;
- target-specific packaging or transformation steps derived from those binaries;
- credential or identity expectations where a target requires publication.

## What Changed in the Existing Analysis

Compared with the earlier repo landscape analysis, the requirements baseline is
now tighter in seven places:

1. OIDC is no longer just a preferred direction; it is the current hard
   requirement for all known in-scope targets.
2. Target-specific packaging flexibility is allowed, but binary generation must
   remain unified to avoid inconsistent outputs.
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
   first delivery scope must cover multiple target classes rather than only one.

## Still Open for Later Requirement Work

- the final descriptor filename and syntax;
- the exact schema shape and reuse model;
- acceptance criteria for the first workflow-release delivery scope;
- the remaining lifecycle rules beyond initial manual triggering, whole-release
  rerun, dry run, and partial-success preservation;
- the exact supported target-class list for the first workflow-release delivery
  scope;
- the remaining failure-handling details beyond preserving partial success and
  allowing manual remediation.

## Related Pages

- [Workflow Release Requirements Interview](../sources/2026-04-21-workflow-release-requirements-interview.md)
- [Repository Release Landscape](./repository-release-landscape.md)
