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
- The user separately authorized disabled runtime contraction. The active
  branch is `workflow-delivery-v3-disabled-runtime-contraction`, based on that
  merged design.
- The first dependency-ordered implementation unit is the bounded
  static-reference foundation. No implementation commit exists yet; the
  untracked prototype contains superseded handwritten ecosystem readers and
  must not be committed as-is.
- This revision corrects the static-reference design from parser/library-only
  authority to an exact Ecosystem Authority Graph that may compose
  authoritative artifacts, official libraries or CLIs, and published
  standards. Its thirteenth-round cross-layer, authority-boundary, exact-API,
  and delivery reviews reported no findings.
- The runtime remains disabled through
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` with
  `live_enabled: false`.
- No normal Live dispatch, Approval deployment, or package mutation has occurred.
- Retry-5 destination acceptance is complete historical evidence. Exact `.17` through `.20` versions and tags remain
  retained and must not be reused; detailed chronology stays in Git and the log.

The immediate boundary is final commit-hook validation and a docs-only local
commit of the authority-graph correction; then deliver it through a protected
PR, merge and reconcile the exact result on `main`, and create or rebase the
disabled implementation branch before implementing the static-reference
foundation. All Live activation and external action remain unauthorized.

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
- Before the root static gate is enabled, migrate the tracked Hexo example's
  local dependency from `link:../..` to `file:../..` and regenerate its
  isolated pnpm v9 lock. Both current files are selected; no example-path
  exception is allowed.
- Move the tracked npm publish request fixture content away from the
  `package.json` basename and materialize it only in test-owned temporary
  storage. The exact producer name is allowed in a tracked `package.json` only
  at the producer path; the old fixture-path exception must be removed.
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

The current boundary conditionally authorizes the disabled runtime contraction,
including the code, tests, schemas, descriptors, workflow definitions, and
exact implementation dependencies required by the approved replacement
design. That standing authorization may be exercised only after this
authority-graph correction is merged through protection, its exact result is
reconciled on `main`, and the implementation branch is created or rebased from
that exact result. Changes remain local and disabled until separately reviewed
and delivered.

Without later explicit authorization, do not change package/repository access;
create, alter, or delete an Environment or marker; refresh Governance; set
`live_enabled: true`; dispatch, rerun, approve, or cancel normal Live; create a
deployment; mutate packages or tags; run remediation; or change any external
resource.

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

1. Inspect current branch, status, identities, untracked paths, and complete
   diff before changing anything.
2. Complete the Ecosystem Authority Graph correction across the normative
   design, overview, log, and this handoff.
3. Run formatting, Markdown lint, diff, and applicable HK documentation gates
   using a Session-owned temporary root, then remove it by exact path.
4. Obtain independent multi-angle design reviews, adjudicate every atomic
   finding independently, fix true positives, and return the fixes until all
   original reviewers report no findings.
5. Create one human-reviewable design-correction commit containing only the
   approved documentation paths.
6. Deliver that documentation-only commit through a protected PR, merge it,
   and reconcile the exact merged state on `main`. Then create or rebase a
   disabled implementation branch from that merged design.
7. Implement the bounded static-reference foundation in dependency order:
   exact Git source acquisition and snapshot transport; exact authority
   adapters; normalized policy projection; then semantic scenarios.
8. Migrate the selected Hexo example manifest and isolated lock from
   unsupported `link:../..` to the admitted typed `file:../..` projection.
   Move the acceptance package manifest fixture to a non-candidate source and
   materialize `package/package.json` only in test temporary storage. Remove
   the superseded whole-file exception, handwritten readers, and parser-branch
   tests rather than adapting them incrementally.
9. Run targeted tests, root HK, and commit hooks with isolated temporary
   storage and exact cleanup.
10. Obtain independent multi-angle implementation reviews, adjudicate each
    finding atomically, and iterate to no findings before creating the first
    implementation commit.
11. Continue the remaining disabled contraction in dependency order: record
    model, Governance/authorization, publication/finalization, workflows, and
    separately authorized cleanup.

Do not perform Live or external work.

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

The replacement baseline is merged. Disabled implementation is conditionally
authorized only after this authority-graph correction is protected-merged,
reconciled on exact `main`, and used as the base of the implementation branch.
External-resource, cleanup, and Live boundaries still require separate
explicit authorization.
