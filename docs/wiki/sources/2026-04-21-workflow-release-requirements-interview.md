# Workflow Release Requirements Interview

## Summary

This source digest captures the requirement clarifications gathered during the
waterfall-model requirements phase for repository-wide workflow release support.

## Key Points

- In scope: all public projects plus the private projects
  `qidian-novel-downloader` and `vscode-copilot-telegram-hook`.
- A project participates in workflow release only if it has a project-owned
  release descriptor file.
- Both `buddy` and `official` profiles must be explicitly declared.
- A declared profile may legitimately have no publish targets.
- Different targets may require different packaging forms, but the produced
  binary should remain unified to avoid inconsistencies.
- OIDC or trusted publishing is a hard requirement wherever supported, and
  there are currently no known target platforms that lack OIDC support.
- `buddy` is a day-to-day delivery action triggered by `write+` without extra
  approval.
- `official` is a `maintain+` action that requires an additional approval; any
  `maintain+` user may approve it, but self-approval is only allowed for
  `admin`.
- The first delivery scope should prioritize manual `workflow_dispatch`
  initiation.
- The first delivery scope must support rerunning the full release against the
  same input.
- The first delivery scope does not require single-target retry; replay concerns
  should instead be handled through detection-based skipping and idempotent
  retry behavior.
- The first delivery scope must support dry run or validation-only execution.
- The first delivery scope may preserve partial success and treat the release as
  awaiting manual remediation rather than forcing automatic rollback.
- Version identity is determined primarily by the Git commit being built.
- The same commit should map to one version identity; different commits should
  not share a version identity.
- `official` is non-overwritable and is the higher-status state for a version.
- `buddy` overwrite is supported only as an exceptional explicit `FORCE` action,
  still under the normal `write+` buddy permission model.
- `buddy` to `official` promotion is in scope and must stay on the same commit
  and version identity, but it may rebuild because `buddy` and `official`
  configurations can differ.
- `official` does not require a prior `buddy`; direct official publication from
  the same commit is allowed.
- The first delivery scope must support multiple target classes rather than only
  one.

## Important Claims

- The future workflow system must be descriptor-driven rather than inferred from
  directory conventions or default target rules.
- Packaging differences should happen after canonical binary production rather
  than by rebuilding divergent binaries per target.
- The current requirements baseline does not assume any known secret-based
  exceptions for registry publishing.
- Release authority should be expressed in repository-role terms rather than in
  a bespoke actor model.
- If whole-release rerun later turns out to be technically infeasible, that
  downgrade requires a new user confirmation rather than an implementation-side
  assumption.
- The first milestone should optimize for traceable, replayable, manually
  recoverable releases rather than transactional rollback across all targets.
- Version identity should be commit-centric rather than profile-centric.

## Related Pages

- [Workflow Release Requirements Baseline](../analyses/workflow-release-requirements-baseline.md)
- [Repository Release Landscape](../analyses/repository-release-landscape.md)

## Open Questions

- What business fields must the release descriptor carry before schema design
  starts?
- Which cancellation and supersession rules belong in the first accepted
  milestone?
- Which target classes must be included in the first workflow-release delivery
  scope?

## Source Location

- User input from the requirements discussion on 2026-04-21
