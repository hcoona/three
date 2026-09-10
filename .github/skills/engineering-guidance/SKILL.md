---
name: engineering-guidance
description: Use when making or reviewing architecture, abstraction, dependency-boundary, security/recovery, or test/evidence strategy decisions in this monorepo. Apply shared engineering principles with the owning project's records. Pure text edits, test execution and faithful status summaries alone do not need this guidance.
---

# Apply Shared Engineering Guidance

Read the relevant sections of [shared engineering principles](../../../docs/engineering/engineering-principles.md) and apply them to the current decision. The guide owns the principles; this Skill routes their use.

Locate each affected project from the task's paths. Start with its existing README or handoff, then read the requirements, design, security or validation records needed for this decision. Keep cross-project authorities and local identifiers separate. A README-only project can remain README-only.

Choose the sections needed by the decision:

- [Design](../../../docs/engineering/engineering-principles.md#design): assumptions, dependency responsibilities, abstractions, failure behavior and architecture views.
- [Security](../../../docs/engineering/engineering-principles.md#security): threats, trust boundaries, mitigation cost and bounded recovery.
- [Testing](../../../docs/engineering/engineering-principles.md#testing): scenario coverage, core/contract tests and sufficient evidence.

A decision may need more than one section; load the applicable combination.

Use concrete project facts to explain the material choice and its evidence limits. Put the result in the requested design, implementation recommendation or existing review/work carrier; no separate compliance report is needed. Preserve accepted project gates and work/effects bounds. If the guidance and a project authority conflict, identify the conflict for owner disposition rather than inventing precedence. When reviewing, retain the [existing independent review](../../../docs/governance/record-system.md#record-system-gate) and [finding-disposition procedure](../../../docs/governance/governance-system.md#gov-011-separate-findings-from-disposition).
