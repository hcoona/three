# Workflow Delivery v3 AI Agent Handoff

## Authority and Purpose

Read this page before acting on Workflow Delivery v3.

This is an operating handoff, not a second specification. The current
[requirements](./requirements.md), [HLD](./high-level-design.md),
[glossary](./architecture-glossary.md), five MLDs,
[migration policy](./migration-strategy.md), and first-slice
[LLD](./hcoona-release-smoke-npm-lld.md) are authoritative.

v3 is the only normative line. Use v1 or v2 only when a v3 document explicitly
requests mechanism extraction and revalidation. Git and the append-only
[`docs/wiki/log.md`](../../../log.md) carry chronology.

## Current Checkpoint

- The replacement baseline, bounded static-reference foundation, and
  record-model contraction are merged.
- The disabled Governance/authorization and exact-satisfied unit was merged
  through PR #651 as
  `db5d9f053baf2c16cd32a1e9e9aae38ffb8c2b74`. Its reviewed tree, protected
  merged tree, post-merge Continuous Integration, and CodeQL were verified.
- The Publication/Finalizer requirements, HLD, glossary, MLD, migration, and
  first-slice LLD contraction was delivered through PR #652. Its reviewed and
  protected trees are identical, and required PR and post-merge checks passed.
  The append-only log records the exact delivery chronology.
- The dependency-ordered Publication/Finalizer implementation is complete on
  `workflow-delivery-v3-publication-finalizer-implementation`, based on
  `main@0b2029b5c5735fa4c7dbf4de3195903770a7df3a`. Component validation,
  independent OCR rereview, and contraction are complete. Whole-group
  delivery review and protected Git delivery still require reconciliation.
- This revision implements strict Governance v2 with state-only blocked
  activation and `live_enabled: false`, active-only Observation, fresh
  exact-satisfied proof, profile-bound one-shot publication, immutable
  marker/Result terminal transport, and the tagged current-DAG Outcome.
  Receipt, ActionResult, and the superseded marker and proof formats have no
  runtime aliases.
- The native-acceptance generation registry remains empty. No native profile
  acceptance, activation, Environment cleanup, package or tag mutation,
  approval, deployment, or normal-Live dispatch was performed during this
  implementation.
- The current user instruction authorizes end-to-end completion of design
  delivery, disabled implementation, native acceptance, activation, and
  exactly one auditable real dispatch. That authorization does not waive any
  proof, review, rollback, or readback gate and does not authorize unrelated
  external changes.

## Git Inspection and Implementation Scope

Do not trust a recorded branch name, SHA, dirty-state claim, or PR status.
Inspect current branch, `HEAD`, local and remote `main`, merge base, status,
untracked paths, the complete diff, and the implementation PR state before
acting. Skip delivery steps already completed; a recorded checkpoint does not
prove that a branch is unpushed, a PR is unopened, or a change is unmerged.

The expected implementation surface is the release record and transport
model, strict Governance eligibility, GitHub Packages Adapter, Live
materialization/finalization and CLI wiring, the normal-Live workflows,
protected blocked Governance v2 document, affected scenario/contract tests,
and synchronized current-state documentation. Treat that as a bounded purpose,
not an exhaustive path allowlist; inspect every changed path and reject
unrelated scope.

When these paths are under review, preserve unrelated worktree changes and do
not reset or overwrite them.

## External State

### Approval Environment

`workflow-delivery-v3-buddy-approval` is the retained authority-bearing Environment:

- ID `20895030723`; reviewer rule `64124473`; sole reviewer `hcoona` / `712433`;
- `prevent_self_review: false`; zero wait; no secrets; no branch/tag restriction; `can_admins_bypass: false`;
- sentinel `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`; and
- zero deployments.

### Legacy Capability Environment

`workflow-delivery-v3-buddy-github-packages` still exists:

- ID `20895037877`; no reviewer or protection rule;
- zero wait; no secrets; no branch/tag restriction; `can_admins_bypass: false`;
- legacy marker
  `WDV3_CAPABILITY_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-github-packages/v1`; and
- zero deployments.

It is inert in the merged replacement runtime. Do not alter or delete it until
exact repository inspection proves every retained dispatchable ref either
implements the one-Environment contract or rejects Governance v2 before any
Environment job, and exact no-reference proof is complete.

### Packages and Live State

- The credential principal is repository `hcoona/three`.
- Known reach includes production package `hexo-renderer-asciidoc` and disposable smoke packages.
- This accepted repository-principal blast radius is not package isolation and is not an exhaustive grant inventory.
- Package access remains unchanged; protected Governance remains `live_enabled: false`.
- No normal Live run, Approval deployment, publication, tag change, package mutation, or obsolete-Environment cleanup
  has occurred.

## Accepted Risk and Threat Model

- `hcoona` is the sole accepted repository writer, reviewer, and publisher TCB member.
- A selected same-repository ref may supply branch-controlled workflow, Planner, Finalizer, Providers, Adapters,
  compiler, clients, catalogs, capability declarations, and publisher code.
- After Approval, the selected-revision publisher may use short-lived repository `GITHUB_TOKEN` with effective
  `packages: write`, no PAT, and no `id-token: write`.
- The publisher must not run target-defined product or build code.
- Protected Governance, Approval, exact bindings, permissions, static-reference checks, immutable artifacts, and
  concurrency protect against outsiders, mistakes, and accidental operators.
- They do not constrain a malicious accepted writer to the smoke package or intended workflow.
- Every package granting Actions access to `hcoona/three` is in effective reach. Coordinate and action checks govern
  intended behavior and reconciliation only.
- A clean static-reference result proves only absence of prohibited direct references in its bounded supported catalog.
- Self-approval is not independent security review. Official npmjs authority remains separate.
- Any writer, reviewer, role, team, or relevant access change requires `live_enabled: false` and a new Governance
  decision.
- Flag-off blocks fresh admission and a publisher before its final check; it is not rollback or instantaneous
  revocation after that check.

Never claim package isolation, reviewer independence, exhaustive grant discovery, universal consumer proof, or
instantaneous revocation.

Normal Live remains activation-blocked until the disabled implementation is
protected-delivered and the exact Destination Operation Profile has a fresh
native acceptance generation installed through the Activation PR. Local
scenario results do not substitute for that acceptance. The design deliberately
treats a post-Observation tag race as bounded routing damage: supported
consumers resolve exact `name@version`, and exact version bytes, digests, and
witness remain authoritative.

## Explicit Authorization Boundary

The current user instruction authorizes completing the end-to-end objective:
design delivery, disabled implementation and protected merge, bounded native
acceptance, obsolete-Environment cleanup after proof, ready Governance v2
activation, and exactly one auditable normal-Live dispatch.

The authorization is contract-bounded. Do not change package or repository
access, touch unrelated packages or tags, use package-admin authority in
runtime, weaken review or readback gates, perform a GitHub rerun, or issue more
than one real dispatch. Acceptance-only deletion must target the approved
disposable package/version, preserve restorability, and restore and verify the
original object before activation. Any ambiguous external response stops
mutation and triggers read-only investigation rather than retry.

## Required Reading Order

After the initial Git inspection, read:

1. this handoff and the [v3 entry point](./README.md);
2. [Requirements](./requirements.md);
3. [High-Level Design](./high-level-design.md);
4. [Architecture Glossary](./architecture-glossary.md);
5. [Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md);
6. [Governance Integration MLD](./governance-integration-mld.md);
7. [CI Qualification MLD](./ci-qualification-mld.md);
8. [Release Delivery MLD](./release-delivery-mld.md);
9. [Shared Foundation MLD](./shared-foundation-mld.md);
10. [Migration and Document Policy](./migration-strategy.md);
11. [`hcoona-release-smoke-npm` LLD](./hcoona-release-smoke-npm-lld.md); and
12. current repository code only for implementation facts.

Do not infer policy from stale runtime behavior or archived designs.

## Next Executable Workflow

1. Perform the required Git inspection above.
2. Reconcile the complete implementation against the normative contract and
   synchronize its current-state documentation. Keep Governance disabled and
   perform no package, Environment, approval, deployment, or dispatch mutation.
3. Complete whole-group validation and delivery review against the true base:
   targeted scenarios, the complete v3 suite, root HK and hooks, OCR
   multi-review, independent TP/FP adjudication, clean rereview, and final
   shrink.
4. Deliver the disabled implementation through a protected PR and verify the
   exact merged tree plus post-merge checks.
5. Prove retained-ref compatibility and remove the obsolete Environment only
   after exact no-reference proof. Then execute the acceptance-only native
   suite, restore and verify its disposable tombstone object, and prepare fresh
   ready Governance v2 evidence.
6. Deliver the small Activation PR, read back protected state, dispatch exactly
   once through the run-ID-returning API, and verify actor, event, workflow,
   actual `main` head, `run_attempt == 1`, current-run records, final Outcome,
   and authoritative destination state.

## Validation and Review Protocol

- Validate the complete affected implementation and design set, not only
  README, handoff, overview, and log.
- Check links, headings, requirement references, terminology, precedence, authority ordering, and failure behavior.
- Run repository-existing Prettier and Markdownlint, applicable documentation/HK gates, hooks when commits are
  prepared, and `git diff --check`.
- Search contextually for superseded target claims: Capability Environment/Profile authority, `approval-finalizer`,
  capability groups/bundles, history-derived authority, approval on zero actions, normal-Live run-attempt record
  fields, Preparation PR or `main` freeze, activation tag, rerun recovery, universal consumer proof, and package token
  isolation.
- Legacy terms are allowed only to inventory an existing resource or describe a removed mechanism for safe migration.
- Review the complete zero-action and one-action paths, cancellation, missing Result, fresh observation, and ambiguous
  activation response.
- An unresolved contradiction blocks commits; do not hide it or choose policy from implementation.
- Review follows green local validation. Each finding is atomic, independently adjudicated, fixed if true, and returned
  to the original reviewer until zero findings.
- Do not claim validation, review, delivery, or merge before persistent evidence exists.

For later runtime work, complete affected tests, root HK, and hooks before multi-review. Documentation work uses the
applicable documentation and repository gates but keeps the same validate-before-review order.

## Architecture, Design, Testing, and Documentation Discipline

### Architecture and Design

- Preserve the waterfall gates: requirements, HLD, MLDs, brief LLD, development, then test and review.
- Keep design contract-bounded; do not silently infer policy or expand channels, destinations, credentials, services,
  authority, abstractions, or external resources.
- CI Qualification and Release Delivery remain peer contexts; Shared Foundation owns mechanisms, not business policy.
- Rely on documented lower-layer guarantees. If one is absent, block the capability rather than simulate a weaker one.
- Add an abstraction only when concrete scenarios prove independent identity, behavior, lifecycle, or policy.
- Do not freeze non-authoritative topology, shell choreography, parser branches, or inventory counts as architecture.

### Testing and Review

- Use scenario-first tests for business behavior.
- Use strict unit, contract, golden, and negative-binding tests for schemas, canonicalization, identity, concurrency,
  authorization, mutation ordering, and fail-closed core contracts.
- Use real integration tests at GitHub, Git, npm, NBGV, HK, or destination contract boundaries.
- Avoid brittle tests for ordinary implementation detail.
- Run complete affected tests, HK, and hooks before independent multi-review; adjudicate findings atomically and return
  fixes to original reviewers.

### Documentation and Git

- Write code, comments, commits, and documentation in English.
- Keep requirement IDs stable, links valid, and current-state prose concise.
- Do not preserve obsolete retry ledgers, PR narratives, test-count histories, artifact tables, or superseded
  mechanisms in current-state pages.
- Use Git and the append-only log for chronology; never alter existing log bytes.
- Make commits dependency-ordered and human-reviewable.
- Keep claims truthful, relevant, clear, and no more detailed than necessary.
