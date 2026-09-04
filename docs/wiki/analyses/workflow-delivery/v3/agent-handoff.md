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

- The user approved the replacement normal-Live requirements baseline on
  2026-08-31.
- Requirements, HLD, glossary, all five MLDs, migration policy, and the
  replacement LLD were merged through PR #637 as
  `e5379fea3d4cf1a63b8b29a9f06604e22b3ec058`; post-merge run
  `33372040715` succeeded.
- The user separately authorized disabled runtime contraction.
- The first dependency-ordered implementation unit, the bounded
  static-reference foundation, was protected-squash-merged through PR #644 as
  `14cfc212da56bed35d887c88f66c1edeb55b0d23`.
- The second dependency-ordered implementation unit, record-model
  contraction, was rebase-merged through PR #648 as
  `20740ade74a0b25d8b2ca300e63e12c5c4f0879a`.
- The merged tree
  `2ecbb191a72deed7bd5900f7ee0f3175f353a28b` exactly matches the reviewed
  PR-head tree. Post-merge Continuous Integration run `33717307779` and
  CodeQL run `33717307744` succeeded.
- The record-model unit retired Actions-history authority, Environment
  Profile, capability-group records, and standalone Receipt transport. It
  preserved current-Attempt authority, Simulation and CI attempt bindings,
  direct successful `ActionResult` lineage, and zero-or-one Publication
  Snapshot cardinality.
- After that merge, the user explicitly continued into the next disabled
  Governance/authorization unit. The local unit is complete on branch
  `workflow-delivery-v3-governance-authority` from exact
  `main@20740ade74a0b25d8b2ca300e63e12c5c4f0879a` in dependency order:
    - `7d8e00cbbcb1536bdbae6a9f31d9a9179c6f6f7b` adds the isolated complete-
      history Governance Git reader and package coverage; and
    - `4a51aac3b9b3f81e04146ae14984335d02297c33` contracts Governance,
      authorization, CLI, workflow, publisher, and replacement tests;
    - `52a86b6213b58041201418f55b1f17dff9585ff1` removes redundant repository-
      wide object scanning and advertisement/fetch identity coupling while
      retaining the required fetched-history continuity proof;
    - `5d316fcd8e50567e8d447ed636407589c8777cf5` contracts the active disabled
      publisher preflight to current authority-closure validation followed by
      deterministic rejection, and replaces workflow topology snapshots with
      authority-critical properties; and
    - `0f25204b93a6ef1a3a71f613763f8cfb46627175` removes duplicate and
      premature R5 platform-termination precedence assertions while retaining
      the no-Result fallback and ActionResult binding checks.
- The replacement parser now requires only
  `workflow-delivery/v3/normal-live-governance-attestation-v1`; superseded
  selected-ref parsers therefore reject the protected document before
  Execution lookup, Attempt creation, or any Environment job.
- The disabled protected document has migrated to that replacement schema
  while retaining `live_enabled: false`. Its closed disabled state records
  unsatisfied activation gates instead of inventing native, retention, or
  destination-race evidence. The enabled state is valid only with the complete
  native Approval Environment, repository-retention, destination-primitive,
  and conditional non-overwrite evidence required by the LLD.
- Eligibility, Approval, and exact-satisfied freshness use isolated complete
  Git state. Eligibility binds `eligibility-main-sha`, Git object format,
  exact regular-blob OID, canonical content digest, and admitted semantics.
  Later checks require descendant lineage and no protected-path touch through
  full-history path traversal; byte equality alone is insufficient. The
  fetched protected `main` ref is authoritative for each read; advertisement
  is used only to determine Git object format, so an unrelated concurrent
  `main` advance is not rejected.
- A zero-action exact-satisfied Snapshot takes no Environment, Approval Bundle,
  Publication Authorization, publisher, marker, Result, or Receipt. An
  action-bearing Snapshot persists one complete Approval Bundle before the
  Environment wait; the Approval job then emits the sole complete Publication
  Authorization. There is no post-approval Capability Admission authority.
- No current GitHub Packages primitive is admitted for conditional
  non-overwriting version-and-tag creation. Absent destination state remains
  unsupported and activation-blocking; standard `npm publish --tag` must not
  be installed as an admitted normal-Live primitive.
- The active disabled publisher path validates only the persisted publication,
  authorization, qualification, and artifact closure, then deterministically
  rejects the unimplemented primitive. It accepts no tarball, package token,
  Governance client, clock, or output path and makes no future publisher-
  freshness claim.
- Local primitive admission is code-owned and empty. Governance cannot declare
  implementation capability, and a forged persisted passing eligibility
  decision is rejected.
- Final root HK, package construction and membership, type, lint, workflow,
  and complete v3 tests passed after review fixes. Independent Governance,
  records, publisher/CLI, and workflow-topology rereviews report no remaining
  finding.
- Native activation-evidence authoring, exact Publication Authorization
  digest lineage in ActionResult/Receipt, and durable Result versus platform-
  termination precedence remain explicit later-unit work.
- The runtime remains disabled through
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` with
  `live_enabled: false`.
- No normal Live dispatch, Approval deployment, Governance refresh, package
  mutation, Environment mutation, cleanup, activation, approval, publication,
  or other operational external mutation is authorized by implementation or
  Git delivery alone.
- PR #651 is the recorded delivery vehicle. Its live review, check, and merge
  state must be inspected rather than inferred from this handoff.

The authorized Governance/authorization implementation boundary is complete.
Complete only the review or protected-merge work that remains for PR #651.
Creation or merge of that PR does not authorize obsolete-Environment cleanup,
Governance refresh, destination acceptance, activation, dispatch, approval,
publication, or any other operational external mutation.

## Hot Context

- A normal Buddy selected ref may be any same-repository ref. Its resolved exact SHA is both control revision and
  Release target. It must strictly admit the active replacement Governance
  schema; incompatible older control fails before any Environment job.
- Protected Governance is read separately from `refs/heads/main`; protected `main` never substitutes for selected-ref
  control code.
- The replacement exact Governance schema is
  `workflow-delivery/v3/normal-live-governance-attestation-v1`. Superseded
  selected-ref parsers must reject it before Release Execution, Attempt, or
  Environment creation.
- Fresh package inventory disproved package-specific token isolation. The GitHub Packages principal is repository
  `hcoona/three`.
- `hcoona` is the sole accepted writer and publisher TCB member. Self-approval is operator confirmation, not
  independent review.
- The target architecture has one Approval Environment and no Capability Environment or Environment Profile.
- Normal-Live authority is current-Attempt only. Native Actions history is diagnostic.
- Every authoritative normal-Live job guards attempt 1; normal-Live records omit run attempt. Simulation keeps its
  existing run-attempt identity.
- Capability groups and standalone Receipt transport are retired. A successful
  `ActionResult` owns its Receipt evidence directly, and Attempt Outcome binds
  the ActionResult digest directly.
- Publication Snapshot admits at most one materialized action. The current
  Governance unit replaces transitional Capability Admission with one
  pre-wait Approval Bundle and one complete Publication Authorization.
- Exact-satisfied performs a fresh read-only Governance continuity proof and
  creates no authorization-shaped record.
- Activation remains blocked on an admitted conditional destination primitive,
  durable mutation-marker admission and ordering, pre-marker failure Result
  formation, direct durable-marker evidence, exact success formation, and
  faithful cancelled, failed, and dependency-skipped conclusion semantics.
- Retry is a new dispatch and full rebuild. GitHub rerun is unsupported.
- Each retained static-reference selector binds one exact Ecosystem Authority
  Graph. Authoritative manifest or lockfile state, stable official libraries or
  CLIs, and published standards may compose across distinct semantic layers.
  Adapters receive exact source bytes directly or through a minimal isolated
  snapshot and emit stable normalized facts. Handwritten ecosystem grammars or
  schemas, competing-authority hardening, candidate execution, network access,
  and fallback worktree reads are forbidden.
- The static-policy invocation schema rejects an omitted or unknown source kind
  and malformed required source parameters before Result construction. Once a
  source request is admitted, exact-source enumeration, read, or minimal
  materialization failure is `source-acquisition-failed`; required cleanup
  failure may override it.
- The retained first-slice surface includes npm manifests, pnpm v9
  locks/workspace manifests, and NuGet lock/config models. npm, uv, and Yarn
  locks, unevaluated MSBuild project/central manifests, standalone Python
  manifests, shell and PowerShell scripts, GitHub workflow/composite-action
  files, and Node import subpaths are outside this policy revision.
- The tracked Hexo example uses `file:../..`; its isolated pnpm v9 lock carries
  the matching typed file-directory reference. Both files remain selected,
  with no example-path exception.
- The npm publish-request fixture is tracked as non-candidate
  `package-manifest.json` and materialized as `package/package.json` only in
  test-owned temporary storage. The producer name occurs in tracked
  `package.json` only at the producer path; no fixture-path exception remains.
- Activation uses one protected Activation PR and a run-ID-returning API dispatch. There is no Preparation PR,
  repository-wide `main` freeze, activation SHA/tag, or blind redispatch.

Do not treat the current merged runtime as policy where it conflicts with the replacement documents.

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

It is inert in the replacement design, but current merged runtime still uses it. Do not alter or delete it until the
replacement implementation is merged, exact repository inspection proves it is no longer input or authority, and
separate cleanup authorization is granted.

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

Normal Live has an additional activation blocker: standard
`npm publish --tag` can overwrite a conflicting target-derived tag introduced
after Observation. It is not an admitted primitive for the complete
version-and-tag projection. Do not plan activation until a separately reviewed
supported primitive passes the conditional non-overwrite race acceptance.

## Replacement Contract

This is a navigation summary. The normative documents own exact schemas and failure rules.

### Identity, Eligibility, and Static References

- Purpose branches before live eligibility, Product or Execution lookup, coalescing, admission, or Attempt creation.
- Each request compiles and reuses one same-revision, purpose-bound Repository Model Snapshot.
- Live Eligibility combines exact-target `git-target` evidence with protected Governance from `main`.
- The static-reference source kinds are exactly `git-target`, `index`, and `worktree`. Only `git-target` is Live
  evidence; index and worktree are HK feedback and never commit identity.
- The bounded policy prevents supported direct accidental references through
  the LLD's selector-to-fact matrix and exact Ecosystem Authority Graphs. It
  makes no evaluator/dataflow, fixed-inventory, exhaustive-consumer, or
  credential-isolation claim and contains no handwritten ecosystem grammar or
  schema.
- Governance has maximum age 90 days and binds repository/ref/path plus blob/content or generation. Any later path
  touch invalidates the Attempt, including edit/revert; unrelated `main` commits do not.

### Qualification, Observation, and Action Cardinality

- Release independently rebuilds and qualifies the complete Release Unit and consumes no CI runtime record.
- First-slice artifact bytes are deterministic for the same frozen target, inputs, Build Definition, and toolchain.
- Exact remote state includes ownership, coordinate, bytes, in-package witness, target, and target-derived tag.
- Observation classifies state as absent, exact, partial, conflicting, unknown, or unprovable.
- Only absent or exact state may form a ready first-slice Publication Snapshot, with exactly zero or one action.

Exact state takes `exact-satisfied`: no Approval deployment, Approval Bundle,
Authorization, publisher, destination write or publication credential,
Publication Capability, marker, Result, or Receipt. Minimum read-only
Observation authority may have been used. The path repeats fresh protected
Governance continuity checks before success and binds the no-op proof into
Finalizer admission.

Absent state may take one compound version-and-target-tag action only after the
destination primitive passes the conditional non-overwrite race:

1. persist the Snapshot, deterministic reviewer summary, and Approval Bundle before the wait;
2. wait on `workflow-delivery-v3-buddy-approval`;
3. validate the sentinel first, fresh Governance, and complete closure;
4. persist the complete Publication Authorization;
5. start the separate ordinary-success-dependent publisher;
6. revalidate Authorization, artifacts, action, resources, and Governance;
7. persist the mutation-may-have-started marker;
8. invoke the one admitted destination primitive;
9. if it uses npm, require highest-precedence `fetch-retries=0` so npm cannot
   automatically resend the mutating `PUT`;
10. perform exact readback; and
11. persist one Publication Result.

Only `published` embeds exactly one Receipt. A controlled failed Result may omit it and must retain mutation
classification. Marker without durable Result is unknown and possibly mutated.

There is no Capability Environment/Profile, `approval-finalizer`, Capability Admission Decision, capability group,
group manifest/bundle/Result, or standalone Receipt artifact in the target architecture.

### Attempts, Retry, History, and Concurrency

- Attempt identity is Release Execution Identity plus unique `workflow_run_id`.
- Every authoritative normal-Live job independently requires `github.run_attempt == 1`.
- Normal-Live records, artifacts, Snapshots, and Authorization omit run attempt; simulation retains it.
- Both GitHub rerun commands are unsupported. Retry is a new manual dispatch that recompiles, rebuilds, requalifies,
  reobserves, and reapproves when needed.
- No prior Attempt Snapshot, artifact, Evidence, approval, Authorization, Result, or Receipt is reused.
- Native Actions history is diagnostic only; it is not authority or aggregate Execution state.
- Read-only Finalization is best effort and may leave no durable Outcome after cancellation or transport loss.
- Preserve caller-held Release Execution concurrency and separate publisher mutable-resource concurrency. Neither is
  authorization, token isolation, or a distributed lock.

### Activation Sequence

1. Merge the coherent design.
2. Separately authorize and implement the runtime contraction while disabled.
3. Validate, independently review, adjudicate, and merge the disabled implementation.
4. Prove retained dispatchable refs either implement the one-Environment
   contract or reject the replacement Governance schema before any Environment
   job, then separately authorize obsolete-Environment cleanup after exact
   no-reference proof.
5. Prove a reviewed destination primitive passes the conditional
   non-overwrite version-and-tag race; standard `npm publish --tag` is not
   admitted.
6. Refresh native and Governance evidence, including authenticated repository
   artifact retention of at least 45 days.
7. Merge one small Activation PR.
8. Read back protected, retention, and native state.
9. Dispatch once through the supported API that returns a run ID.
10. Read back exact workflow/run, actor, event, actual head,
    `refs/heads/main`, and attempt 1.

Ambiguous dispatch response means read-only reconciliation, never blind redispatch.

## Explicit Authorization Boundary

The user's continuation after PR #648 merge authorizes local requirements
confirmation, design resolution, implementation, tests, hooks, dependency-
ordered commits, and independent review for the disabled
Governance/authorization unit.

It does not authorize push, PR creation, PR review approval, protected merge,
or work assigned to the later publication/finalizer unit. Those boundaries
require separate human decisions.

Do not change package/repository access; create, alter, or delete an
Environment or marker; refresh Governance; set `live_enabled: true`; submit a
PR review approval; dispatch, rerun, approve, or cancel normal Live; create a
deployment; publish; mutate packages or tags; perform cleanup or remediation;
or change any other operational external resource without later explicit
authorization.

Disabled implementation does not authorize obsolete-Environment cleanup.
Cleanup does not authorize Governance refresh, activation, dispatch, approval,
or publication.

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

1. Inspect the exact branch, upstream, protected `main`, and live state of
   PR #651 before any continuation. Do not infer delivery from this page.
2. If PR #651 is open, address only outstanding review and checks, then perform
   an authorized protected merge when clean. If it is already merged, verify
   the resulting protected revision and post-merge checks; do not repeat push,
   PR creation, or merge.
3. After protected implementation delivery, separately authorize the
   marker/Result/final-Outcome unit. It must close exact Publication
   Authorization digest lineage and durable Result versus platform-
   termination precedence.
4. Keep native activation-evidence authoring, obsolete-Environment cleanup,
   destination-primitive acceptance, Governance refresh, activation, dispatch,
   approval, and publication behind their own later authorization and proof
   gates.

Do not perform Live or operational external work.

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

The replacement baseline, static-reference foundation, and record-model
contraction are merged. The Governance/authorization and exact-satisfied unit
is validated and independently rereviewed; inspect PR #651 for its live
delivery state. Publication marker/Result/final-Outcome work remains the next
dependency-ordered implementation unit after a separate authorization.
External-resource, cleanup, activation, and Live boundaries still require
their own explicit authorization.
