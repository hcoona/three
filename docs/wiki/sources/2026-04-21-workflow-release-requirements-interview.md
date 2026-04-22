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
- Replay handling should stay automatic; no extra manual replay-choice controls
  are needed.
- The first delivery scope must support dry run or validation-only execution.
- The first delivery scope may preserve partial success and treat the release as
  awaiting manual remediation rather than forcing automatic rollback.
- The first delivery scope has no exceptional cases that require automatic
  rollback.
- The first delivery scope must support manual operator cancellation.
- A newer release request supersedes and cancels any older unfinished request in
  the same project and same profile.
- `buddy` and `official` do not supersede each other across profiles.
- When a release is cancelled, whether manually or by supersession, it should
  stop the remaining unpublished targets while leaving already published results
  visible for manual follow-up.
- Superseded releases do not need a distinct business status; ordinary
  cancelled status is sufficient.
- `buddy` and `official` use the same visible handling rules for failure,
  cancellation, and partial success.
- Manual remediation does not require a separate workflow-level closure or gate;
  any out-of-band follow-up is out of scope.
- The first delivery scope should use manual release triggering rather than
  triggering a release automatically from a Git tag.
- Release workflow itself should create the needed Git tags automatically for
  both `buddy` and `official` rather than relying on humans to create them.
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
- The first delivery scope treats GitHub Release, NuGet, PyPI, npm, and
  RubyGems as explicit target families rather than one generic package-registry
  bucket.
- GitHub Release is mandatory for every in-scope project.
- For GitHub Release, `buddy` always means pre-release and `official` always
  means release.
- Package-registry targets remain project-declared for both `buddy` and
  `official`; GitHub Packages support does not create a repo-wide default
  mapping.
- A single project may publish to multiple package registries within the same
  ecosystem and the same profile.
- `official` may use GitHub Packages when the ecosystem is supported there.
- Even when the final target is the same, different project kinds may require
  different packaging paths.
- For GitHub Release, a library may need original package assets plus a NuGet
  package, while an app may need an Inno Setup installer or a host-specific
  published binary.
- Target-specific transforms are allowed, such as adding a scope for GitHub
  Packages while preserving the established package name for npmjs.
- For a given binary variant, one canonical build may emit both the binary and
  the related package or installer outputs; the business rule is to prevent
  divergent recompilation for different targets of that same variant.
- Python is a special case in the current capability matrix: GitHub Packages is
  not a Python package target, so Python `buddy` falls back to GitHub Release
  only, while Python `official` package publication uses PyPI when declared.
- GitHub-native audit trails are considered sufficient; the workflow does not
  need to emit an extra repo-owned release-record artifact as a requirements
  baseline.
- The first delivery scope must be accepted with real projects, real
  publication, and overall coverage of both profiles.
- Acceptance must cover a C# library, a C# app `dotnet publish` path, a C# app
  Inno Setup path, a Python package, a Node package, and a Ruby package.
- Acceptance must include at least one real `official` publication.
- Acceptance must prove both a real same-commit `buddy` to `official`
  promotion and a real direct `official` publication.

## Important Claims

- The future workflow system must be descriptor-driven rather than inferred from
  directory conventions or default target rules.
- Packaging differences may be emitted in the same canonical build for a binary
  variant, but they must not come from divergent recompilation per target.
- The current requirements baseline does not assume any known secret-based
  exceptions for registry publishing.
- Release authority should be expressed in repository-role terms rather than in
  a bespoke actor model.
- If whole-release rerun later turns out to be technically infeasible, that
  downgrade requires a new user confirmation rather than an implementation-side
  assumption.
- The first delivery scope should optimize for traceable, replayable, manually
  recoverable releases rather than transactional rollback across all targets.
- Version identity should be commit-centric rather than profile-centric.
- The business meaning of a target includes both its ecosystem family and any
  target-specific transformation constraints.
- GitHub's native audit and workflow history are sufficient for the current
  requirements baseline; no separate release-record artifact is required.
- Release triggering should stay manual even though the workflow itself owns Git
  tag creation.
- Replay handling should stay automatic rather than operator-driven.

## Related Pages

- [Workflow Release Requirements Baseline](../analyses/workflow-release-requirements-baseline.md)
- [GitHub Packages Supported Registries](./2026-04-22-github-packages-supported-registries.md)
- [Repository Release Landscape](../analyses/repository-release-landscape.md)

## Open Questions

No major requirement-phase open questions remain from this interview. The
remaining work is design-oriented.

## Source Location

- User input from the requirements discussion on 2026-04-21
