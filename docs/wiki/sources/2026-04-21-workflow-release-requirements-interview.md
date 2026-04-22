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

## Important Claims

- The future workflow system must be descriptor-driven rather than inferred from
  directory conventions or default target rules.
- Packaging differences should happen after canonical binary production rather
  than by rebuilding divergent binaries per target.
- The current requirements baseline does not assume any known secret-based
  exceptions for registry publishing.

## Related Pages

- [Workflow Release Requirements Baseline](../analyses/workflow-release-requirements-baseline.md)
- [Repository Release Landscape](../analyses/repository-release-landscape.md)

## Open Questions

- What business fields must the release descriptor carry before schema design
  starts?
- Which approval, trigger, and rollback rules belong in the first accepted
  milestone?

## Source Location

- User input from the requirements discussion on 2026-04-21
