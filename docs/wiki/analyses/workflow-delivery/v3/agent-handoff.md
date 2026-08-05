# Workflow Delivery v3 AI Agent Handoff

## Purpose and Authority

Read this page before acting on any Workflow Delivery v3 request.

This page is an AI operating handoff, not a second architecture specification.
If it conflicts with the
[requirements](./requirements.md),
[HLD](./high-level-design.md),
[glossary](./architecture-glossary.md), or an MLD, the normative document wins.

## Current Checkpoint

- Requirements, HLD, and all five MLDs are confirmed.
- Work is at the MLD-to-LLD transition. No v3 implementation has started.
- The first vertical slice is `hcoona-release-smoke-npm`.
- The slice has one Node project, one package variant, and one npm artifact.
- It covers CI Qualification, live Buddy publication to GitHub Packages, and
  Official npmjs dry-run.
- GitHub Release publication and live Official npmjs publication are outside
  this slice.
- The existing v2 `three.release.yml`, workflows, and control types are
  reference material only. The v3 slice must define its own contracts and must
  not inherit the v2 profile model.
- The next task is to confirm the slice scenarios and write a brief LLD.
- Do not enter implementation until the user approves the LLD and supplies any
  remaining implementation instructions.

## Required Reading Order

1. This handoff and the [v3 entry point](./README.md).
2. [Requirements](./requirements.md).
3. [High-Level Design](./high-level-design.md).
4. [Architecture Glossary](./architecture-glossary.md).
5. The five MLDs linked from the v3 entry point.
6. [Migration and Document Policy](./migration-strategy.md).
7. Current repository code only for implementation facts.

v1 is the production compatibility baseline. v2 is an archived prototype and
mechanism source. Neither is normative for v3.

## Lifecycle and Decision Protocol

Use the waterfall sequence:

1. interactively confirm requirements;
2. confirm HLD;
3. confirm MLDs;
4. confirm a brief LLD;
5. develop; and
6. test and review.

Do not skip a gate. For unresolved design choices:

- ask one bounded question at a time;
- present concrete options, trade-offs, and a recommendation;
- test abstractions against concrete scenarios;
- obtain explicit user confirmation; and
- update the coherent document set before advancing.

Do not silently infer policy from existing implementation.

## Architecture Guardrails

- CI Qualification and Release Delivery are peer bounded contexts; Delivery
  Governance is external authority; Shared Foundation owns mechanisms only.
- CI and Release do not share runtime Plans, Evidence, artifacts, or verdicts.
- Control code is same-revision, and target-controlled execution never shares a
  trust boundary with publication capability.
- Plans and scopes close before execution; unknown, incomplete, conflicting, or
  unprovable required state fails closed.
- NBGV is the sole canonical version authority. Release rebuilds and qualifies
  the complete Release Unit variant set.
- Release uses whole-release replay, not failed-job replay. GitHub concurrency
  is execution serialization, not a correctness lock.

Do not expand the system boundary, add channels, destinations, services,
credentials, or generalized abstractions without user approval.

## Design and Security Discipline

- Rely on documented lower-layer abstractions and reasonable engineering trust.
  Do not reimplement a platform merely to prove its own contract.
- If a required lower-layer guarantee is unavailable, mark the capability
  unsupported or blocked; do not simulate a weaker substitute.
- Introduce an abstraction only when concrete scenarios demonstrate independent
  identity, behavior, lifecycle, or policy.
- Balance security against realistic threats, implementation cost, and
  maintenance cost.
- For unsupported extreme cases, fail closed and document the boundary instead
  of expanding into endless defensive mechanisms.

## Development Discipline

- Assess scope before coding. Decompose large work into dependency-ordered,
  human-reviewable commits.
- Implement one thin end-to-end slice before expanding ecosystems or
  destinations.
- Keep v3 on a clean implementation line. Port only reviewed v2 mechanisms
  behind v3 boundaries; never import v2 domain or authority types.
- Make surgical changes and do not fix unrelated pre-existing issues.
- Never activate parallel authoritative v1/v3 CI decisions or live publishers.

## Testing and Review Discipline

- Use scenario tests as the primary coverage for business behavior.
- Use strict unit, contract, golden, and negative-binding tests for core
  algorithms, schemas, canonicalization, identity, and fail-closed behavior.
- Avoid over-constraining ordinary business code with brittle unit tests.
- Use real integration tests where platform or ecosystem contracts matter.
- Run the complete affected project test suite, then the applicable HK and
  commit-hook gates.
- After implementation and local validation, launch multiple independent
  subagents for different review angles.
- Split mixed findings into atomic findings. Assign each atomic finding to its
  own independent subagent for TP or FP classification; do not batch findings
  into one adjudication.
- Fix every TP and return the changes to the same original reviewers until each
  explicitly reports no findings.

Do not claim completion before the expected outcome is persistent and verified.

## Documentation Discipline

- Write code, comments, commits, and documentation in American English.
- Use a professional tone for senior engineers.
- Follow the Gricean maxims: be truthful, relevant, clear, and no more detailed
  than necessary.
- Keep requirement IDs stable and preserve relative links.
- Update existing pages coherently; do not create duplicate normative sources.
- Keep `docs/wiki/log.md` append-only.
- When the phase or selected slice changes, update this handoff, the v3 README,
  overview, index, and log as applicable in the same documentation change.
