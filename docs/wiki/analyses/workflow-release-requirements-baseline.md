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

- Phase 1 should prioritize manual `workflow_dispatch` initiation.
- Phase 1 must support rerunning the entire release against the same input.
- If whole-release rerun is later proven technically infeasible, that reduction
  must be re-confirmed with the user instead of being assumed by the team.
- Phase 1 does not require single-target retry.
- Replay concerns should be handled primarily through detection-based skipping
  and idempotent retry behavior.
- Phase 1 must support a dry-run or validation-only mode that performs input and
  descriptor validation without publishing to external targets.
- Lifecycle rules for cancellation, supersession, and tag-driven initiation
  remain to be defined.

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
now tighter in five places:

1. OIDC is no longer just a preferred direction; it is the current hard
   requirement for all known in-scope targets.
2. Target-specific packaging flexibility is allowed, but binary generation must
   remain unified to avoid inconsistent outputs.
3. Approval authority is now role-based: `buddy` is `write+` without extra
   approval, while `official` is `maintain+` plus an approval gate.
4. Phase 1 now prioritizes manual `workflow_dispatch` initiation.
5. Phase 1 must support whole-release rerun and dry run, while replay concerns
   should be addressed with skip detection and idempotent behavior rather than
   mandatory single-target retry.

## Still Open for Later Requirement Work

- the final descriptor filename and syntax;
- the exact schema shape and reuse model;
- acceptance criteria for the first workflow-release milestone;
- the remaining lifecycle rules beyond initial manual triggering, whole-release
  rerun, and dry run;
- failure-handling and rollback expectations.

## Related Pages

- [Workflow Release Requirements Interview](../sources/2026-04-21-workflow-release-requirements-interview.md)
- [Repository Release Landscape](./repository-release-landscape.md)
