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
- Publication/Finalizer design work is isolated on branch
  `workflow-delivery-v3-publication-finalizer` from exact
  `main@db5d9f053baf2c16cd32a1e9e9aae38ffb8c2b74`.
- The Publication/Finalizer requirements, HLD, glossary, MLD, migration, and
  first-slice LLD contraction is complete locally. Two disjoint design
  rereviews reached zero findings after independent TP/FP adjudication, and
  the final shrink rereview is clean.
- The merged runtime and protected document still use v1 and remain disabled
  through `live_enabled: false`. This design branch has no runtime, workflow,
  protected-Governance, Environment, package, tag, approval, deployment, or
  dispatch change.
- The current user instruction authorizes end-to-end completion of design
  delivery, disabled implementation, native acceptance, activation, and
  exactly one auditable real dispatch. That authorization does not waive any
  proof, review, rollback, or readback gate and does not authorize unrelated
  external changes.

## Git Inspection and Design Scope

Do not trust a recorded branch name, SHA, dirty-state claim, or PR status.
Inspect current branch, `HEAD`, local `origin/main`, merge base, status,
untracked paths, and the complete diff before acting.

The coherent design package is confined to these paths:

```text
docs/wiki/analyses/workflow-delivery/v3/README.md
docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md
docs/wiki/analyses/workflow-delivery/v3/architecture-glossary.md
docs/wiki/analyses/workflow-delivery/v3/ci-qualification-mld.md
docs/wiki/analyses/workflow-delivery/v3/governance-integration-mld.md
docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md
docs/wiki/analyses/workflow-delivery/v3/high-level-design.md
docs/wiki/analyses/workflow-delivery/v3/migration-strategy.md
docs/wiki/analyses/workflow-delivery/v3/release-delivery-mld.md
docs/wiki/analyses/workflow-delivery/v3/repository-model-release-unit-mld.md
docs/wiki/analyses/workflow-delivery/v3/requirements.md
docs/wiki/analyses/workflow-delivery/v3/shared-foundation-mld.md
docs/wiki/index.md
docs/wiki/log.md
docs/wiki/overview.md
```

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

Normal Live remains activation-blocked because the Publication/Finalizer
runtime and Governance v2 migration are not implemented and the exact
Destination Operation Profile has no fresh native acceptance generation. The
design deliberately treats a post-Observation tag race as bounded routing
damage: supported consumers resolve exact `name@version`, and exact version
bytes, digests, and witness remain authoritative.

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

1. Reinspect the isolated branch, exact `origin/main`, merge base, status, and
   complete documentation diff.
2. Validate the synchronized design set and status pages, rerun OCR delegation,
   independently adjudicate any finding, and require clean rereview and shrink.
3. Create dependency-ordered, human-reviewable design commit or commits, push
   the branch, open the design PR, complete protected review/checks, merge, and
   verify the exact merged tree and post-merge checks.
4. Start the disabled implementation from that protected design revision.
   Implement record/schema migration, strict state-only blocked Governance v2,
   Destination Operation Profile, active-only Observation, package-control
   proof admission, zero/one-action Snapshot, Approval/Authorization/marker/
   Publication Result lineage, single terminal-reference Finalizer, and
   workflow permissions/topology.
5. Run targeted scenarios, the complete v3 suite, root HK and hooks, OCR
   multi-review, independent TP/FP adjudication, clean rereview, and final
   shrink before protected implementation delivery.
6. Prove retained-ref compatibility and remove the obsolete Environment only
   after exact no-reference proof. Then execute the acceptance-only native
   suite, restore and verify its disposable tombstone object, and prepare fresh
   ready Governance v2 evidence.
7. Deliver the small Activation PR, read back protected state, dispatch exactly
   once through the run-ID-returning API, and verify actor, event, workflow,
   actual `main` head, `run_attempt == 1`, current-run records, final Outcome,
   and authoritative destination state.

## Validation and Review Protocol

- Validate the complete design set, not only README, handoff, overview, and log.
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
