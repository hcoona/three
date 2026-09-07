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
- The dependency-ordered Publication/Finalizer implementation was delivered
  through PR #653 as `1c0effa8471c58e517cf1c307af65f4ae7b19acb`.
  Component and whole-group gates, independent OCR rereview, final
  contraction, exact reviewed/merged-tree comparison, and post-merge
  Continuous Integration and CodeQL are complete.
- This revision implements strict Governance v2 with state-only blocked
  activation and `live_enabled: false`, active-only Observation, fresh
  exact-satisfied proof, profile-bound one-shot publication, immutable
  marker/Result terminal transport, and the tagged current-DAG Outcome.
  Receipt, ActionResult, and the superseded marker and proof formats have no
  runtime aliases.
- Retained-ref compatibility and semantic no-reference proof are complete.
  The exact legacy Environment was removed after the protected delivery
  gates; all other Environment configurations were unchanged.
- The native-acceptance generation registry remains empty. No native profile
  acceptance, activation, package or tag mutation, approval, deployment, or
  normal-Live dispatch has occurred.
- Native acceptance is the next gate. Current tooling adds shared pinned npm
  mechanics, canonical state comparisons, reproducible acceptance fixtures,
  a one-shot probe with a distinct Actions entry, complete native-state
  collection, exact-run probe-evidence admission, and the fixed-suite local
  operator. Whole-group review, protected delivery, and actual native
  execution remain separate gates. The exact approved disposable package and
  acceptance-only administrative execution boundary must be established
  before its mutation steps; see Native Acceptance Readiness below.
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

### Removed Legacy Environment

`workflow-delivery-v3-buddy-github-packages`, ID `20895037877`, was deleted
after exact retained-ref and no-authority-reference proof. Its pre-deletion
deployment inventory was empty. Complete Environment readback confirmed only
that resource was removed and the retained Approval Environment was unchanged.
Do not recreate it or repeat the deletion.

### Packages and Live State

- The credential principal is repository `hcoona/three`.
- Known reach includes production package `hexo-renderer-asciidoc` and disposable smoke packages.
- This accepted repository-principal blast radius is not package isolation and is not an exhaustive grant inventory.
- Package access remains unchanged; protected Governance remains `live_enabled: false`.
- No normal Live run, Approval deployment, publication, tag change, or package
  mutation has occurred.

### Native Acceptance Readiness

The current profile requires the complete LLD section 18.6 suite. Legacy
fixed-coordinate/retry-5 helpers do not implement its canonical complete-state
comparison, active duplicates, distinct-version tag race, or sequential
deleted/restorable probes and restoration.

Current acceptance-only components live in
`three_workflow_delivery_v3.acceptance`. The distinct
`workflow-delivery-v3-native-npm-acceptance.yml` entry invokes one probe with
the actual Actions-issued repository `GITHUB_TOKEN`. It accepts only an
explicitly confirmed manual request on protected `main` by the accepted actor
at attempt one. It does not revive any retired Buddy acceptance identity or
enter the normal-Live workflow graph. Inspect Git and protected delivery
before treating the entry as available remotely.

The probe retains immutable request, actual fixture, matched profile, process
facts, and platform context. Those facts are not a native acceptance verdict:
the operator must collect complete destination state, apply every scenario
gate in sequence, and verify restoration. Missing evidence, ambiguity, or an
unexpected delta stops mutation; no probe or workflow failure permits a blind
retry. The collector retains actual complete inventories, scenario bytes, and
raw native responses; the audit reader binds downloaded probe evidence to its
exact run, request, and tooling revision. The fixed-suite local operator
connects those components without a retry or recovery protocol. The
[LLD tooling boundary](./hcoona-release-smoke-npm-lld.md#1861-native-suite-tooling-boundary)
separates these responsibilities.

Read-only inventory found the pre-existing public
`@hcoona/hcoona-release-smoke-npm-dual` container, ID `12047077`, associated
with `hcoona/three`. This is a candidate, not authorization or proof that it
has no production dependency. The current normative documents and recovered
user instructions do not identify an approved disposable coordinate.

Before package mutation, establish that coordinate and its required
preconditions and the acceptance-only administrative execution boundary.
Reconcile the complete tooling group with protected delivery; component
completion alone does not make a remote entry available or authorize a run.
The standard-publish probes must use the real Actions-issued repository token;
do not relabel a local PAT as `GITHUB_TOKEN`, add secrets or access grants
implicitly, or send administrative credentials into normal runtime. No
acceptance generation may be installed before a real passing suite.

### Local Native Operator

Use a clean POSIX checkout of the exact protected tooling revision, with the
repository's locked pnpm dependencies and Python 3.13 uv environment prepared.
Windows operators need a configured POSIX environment such as WSL. Existing
classic gh authentication must support the documented package operations and
`gh run watch`. No command installs credentials or expands grants.

The example below is **not executable authorization**. Replace every
placeholder only after confirming the exact pre-existing, operator-controlled
disposable package has no production dependency and obtaining bounded
delete/restore approval. Both flags acknowledge prior approval; they do not
grant it. Use a fresh lowercase hexadecimal generation and three distinct
target SHAs whose scenario tags are absent in that package.

```bash
uv run --no-sync --python 3.13 --package three-workflow-delivery-v3 \
  python -m three_workflow_delivery_v3.acceptance suite \
  --package '@hcoona/<approved-disposable-name>' \
  --generation '<fresh-generation>' \
  --tooling-sha '<verified-protected-main-sha>' \
  --creation-target '<creation-target-sha>' \
  --race-target '<race-target-sha>' \
  --deleted-target '<deleted-target-sha>' \
  --repository-root '<absolute-clean-checkout>' \
  --audit-directory '<new-absolute-directory-outside-checkout>' \
  --authorized-disposable \
  --authorized-delete-restore
```

Use `suite --help` for the input contract. Configure any machine-specific
trusted CA location in the operator environment; never disable TLS
verification or add application certificate fallbacks.

The fixed sequence dispatches eight acceptance probes, deletes only the fresh
scenario D version by its captured ID, and restores that original object only
after the deleted duplicate gates pass. These are not normal-Live dispatches.
Any failure stops mutation and preserves partial audit data. Inspect exact
recorded runs and destination state read-only before deciding an explicitly
authorized recovery; do not rerun the command against the same audit, dispatch
again blindly, or assume automatic restoration.

Successful completion writes `suite-evidence.json` and prints its path and
digest. Its `scenario_verdict: "passed"` records completed supplied-fact gates;
it is not proof of native provenance or installed admission. Independently
audit the actual run/artifact references, raw observations, empty deltas, and
restoration before preparing the Activation PR. Retain the local evidence
beyond the Actions artifact lifetime when necessary. Never put detailed
tombstone facts or administrative credentials into Governance.

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

Normal Live remains activation-blocked until the exact Destination Operation
Profile has a fresh native acceptance generation installed through the
Activation PR. Local
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
2. Confirm the approved disposable coordinate, its preconditions, and the
   acceptance-only administrative execution boundary. Do not treat package
   inventory as permission.
3. Reconcile and deliver the bounded acceptance-tooling group through
   applicable scenarios, HK/hooks, OCR multi-review,
   independent TP/FP adjudication, clean rereview, final contraction, and
   protected delivery while Governance remains disabled.
4. Execute the real pinned-profile native suite. Preserve complete canonical
   comparisons and raw evidence, stop on ambiguity, and restore and verify the
   original disposable tombstone object.
5. Capture fresh Approval Environment, retention, access, package-control, and
   passing native-generation evidence for ready Governance v2.
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
