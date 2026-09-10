# Workflow Delivery v3 AI Agent Handoff

## Authority and Purpose

Read this page before acting on Workflow Delivery v3.
The [document-set entry](./README.md) states the authority boundary and source
provenance for these records.

This is an operating handoff, not a second specification. The current
[requirements](./requirements.md), [HLD](./high-level-design.md),
[glossary](./architecture-glossary.md), five MLDs,
[migration policy](./migration-strategy.md), and applicable slice LLD
([npm](./hcoona-release-smoke-npm-lld.md) or
[NuGet](./hcoona-release-smoke-github-packages-lld.md)) are authoritative.

v3 is the only normative line. Use v1 or v2 only when a v3 document explicitly
requests mechanism extraction and revalidation. Git and delivery PRs carry
chronology. Retained decision evidence lives in
[this project's research and validation records](./README.md#historical-source-rule).
Repository work authorization and generic contribution procedure route to the
[Delivery Wave](../../../../../docs/delivery-wave.md),
[record policy](../../../../../docs/governance/record-system.md), and
[contribution guide](../../../../../CONTRIBUTING.md). The domain gates below
remain additional prerequisites; repository governance does not grant native
operations, publication, or another npm proving run.

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
  Git and PR #652 record the exact delivery chronology.
- The dependency-ordered Publication/Finalizer implementation was delivered
  through PR #653 as `1c0effa8471c58e517cf1c307af65f4ae7b19acb`.
  Component and whole-group gates, independent OCR rereview, final
  contraction, exact reviewed/merged-tree comparison, and post-merge
  Continuous Integration and CodeQL are complete.
- The replacement runtime implements strict Governance v2, active-only
  Observation, fresh
  exact-satisfied proof, profile-bound one-shot publication, immutable
  marker/Result terminal transport, and the tagged current-DAG Outcome.
  Receipt, ActionResult, and the superseded marker and proof formats have no
  runtime aliases.
- Retained-ref compatibility and semantic no-reference proof are complete.
  The exact legacy Environment was removed after the protected delivery
  gates; all other Environment configurations were unchanged.
- The native tooling and exact storage-origin correction were
  protected-delivered through PRs #655 and #656. The subsequent native suite
  rejected its v1 reservation contract: publishing the deleted D version again with
  identical bytes succeeded, creating a new version object while the original
  remained deleted.
- The active-lifecycle design was protected-delivered through PR #658:
  active versions remain non-overwritable, but administrator deletion ends
  that lifetime and retained deleted records do not reserve coordinates.
  The active-only v2 tooling was protected-delivered through PR #659.
- One explicitly authorized fresh v2 generation completed all five native
  probes and six active captures and passed independent audit. PR #660
  protected-delivered its exact admitted contract and fresh `ready` Governance
  with `live_enabled: true`. Reviewed/merged trees, post-merge CI and CodeQL,
  and authenticated platform readback were verified.
- PR #661 protected-delivered the supported package-level npm metadata reader.
  Its reviewed/merged tree, post-merge CI, CodeQL, and fresh platform readback
  passed. The publication profile and admitted native generation are unchanged.
- PR #662 protected-delivered the transport-to-logical-basename correction.
  Its exact reviewed/merged tree, post-merge CI and CodeQL, and fresh platform
  and neutral NBGV preflight passed.
- The separately authorized second run, `34332094944`, rebuilt and qualified
  its own artifact, received Approval after its new Snapshot and archive were
  reviewed, and emitted an authoritative `published` Outcome with
  `possibly-mutated: false`. Its Publication Result records a successful
  invocation and exact destination bytes/witness. All 23 current-run artifact
  bodies are retained. Independent lineage and fresh destination-byte audit
  passed: native version `1227103825` is byte-identical to the approved archive,
  and only the intended version and target tag were added. The auditable
  normal-Live proving objective is complete.
- Both authorized dispatches are spent. No third dispatch, GitHub rerun,
  prior-run artifact or Approval adoption, automatic retry, additional native
  probe, administrative operation, or grant change is authorized.
- Preserve the first run, `34320726590`, as independently audited
  `failed-before-publication`, `possibly-mutated: false`, with no invocation.
  Both incomplete v1 generations and separately unresolved original-D recovery
  remain unchanged. Exact evidence is preserved in the
  [retained source extracts](./validation/native-and-normal-live-evidence.md)
  with their pinned source provenance.

## NuGet Second-Slice Implementation

The NuGet research delivery is complete. The user has selected
`Hcoona.ReleaseSmoke.GithubPackages` and confirmed its operator-controlled,
smoke-only use without production dependencies or existing compatibility
obligations. Scope, trust, and acceptance are now confirmed as `WD-NUGET-*`.
The HLD, five MLD extensions, and brief NuGet LLD define the design. The
[NuGet handoff](./nuget-smoke-research-handoff.md) routes its admission gates.
Design delivery is complete through PR #669. The user subsequently delegated
end-to-end implementation and proving of this C# smoke project without
intermediate confirmation. Continue within the confirmed scope; close each
concrete native request and its bounded budget before execution. This grant
does not waive native guarantees, protected delivery, current-run Approval,
independent audit, or ambiguity stop conditions, and does not reopen npm.

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md#advance-workflow-delivery-v3)
supplies repository work authorization. [Issue #676](https://github.com/hcoona/three/issues/676)
coordinates the concrete NuGet advancement and retains delivery evidence;
neither this handoff nor the Issue enlarges the Wave or domain effect bounds.

The native Provider and frozen Build/Quality foundation, independent
authoring/compiler contracts, one-shot HTTP adapter, and blocked NuGet
Governance are protected-delivered through PR #678. PR #679 protected-delivered
explicit NuGet Release records, planning from matched admitted Model/Provider
inputs, and original-archive content/consumer Qualification.

Disabled destination integration adds a separate native Eligibility Decision,
no-tag active observation and action records, and original `.nupkg` publication.
It reuses Publication Snapshot, current-Attempt Approval/Authorization, fresh
exact-satisfied proof, marker/Result transport, and the shared Outcome Finalizer.
Imported HTTP profiles retain their original runtime/source/TLS identity;
publication compares the actual profile before its one-shot invocation.
Initial protected-main control evidence and later protected-path freshness
remain distinct. The native Eligibility Decision carries no npm static-reference
placeholder. Modeled service/admission scenarios establish local contracts only.
Inspect current Git/PR state before claiming this unit is protected-delivered.

Workflow/CLI entry delivery, native acceptance tooling, activation, and real
publication remain subsequent work. Existing publisher entries remain npm-only.
The checked-in NuGet source stays blocked and its native/atomic admission sets
stay empty. Sufficient GitHub-owned atomic non-overwrite assurance remains an
activation gate; sequential duplicate tests cannot supply it.

## Git Inspection and Implementation Scope

Do not trust a recorded branch name, SHA, dirty-state claim, or PR status.
Inspect current branch, `HEAD`, local and remote `main`, merge base, status,
untracked paths, the complete diff, and the implementation PR state before
acting. Skip delivery steps already completed; a recorded checkpoint does not
prove that a branch is unpushed, a PR is unopened, or a change is unmerged.

For the completed npm objective, the implementation surface was the release record and transport
model, strict Governance eligibility, GitHub Packages Adapter, Live
materialization/finalization and CLI wiring, the normal-Live workflows,
protected Governance v2 document, affected scenario/contract tests,
and synchronized current-state documentation. Treat that as a bounded purpose,
not an exhaustive path allowlist; inspect every changed path and reject
unrelated scope.

That inventory is not authorization to reopen npm implementation. NuGet
implementation follows the protected design and the subsequent end-to-end
delegation described above. Changes to shared code preserve npm contracts;
NuGet receives its own Provider facts, policy, Governance, and native evidence.

When these paths are under review, preserve unrelated worktree changes and do
not reset or overwrite them.

## External State

### Approval Environment

`workflow-delivery-v3-buddy-approval` is the retained authority-bearing Environment:

- ID `20895030723`; reviewer rule `64124473`; sole reviewer `hcoona` / `712433`;
- `prevent_self_review: false`; zero wait; no secrets; no branch/tag restriction; `can_admins_bypass: false`;
- sentinel `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`; and
- recorded deployments `6344271579` and `6346279889` from the two real runs.
  This is an observed checkpoint, not a zero-deployment Governance requirement.

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
- Package access remains unchanged. Protected Governance is ready with
  `live_enabled: true`; the corrected publisher has emitted a real `published`
  Outcome for `1.0.0-beta.253.g6325e93`.
- Authorized native probes created disposable scenario versions and tags.
  Original-D recovery remains unresolved; the active replacement is retained.
  The first normal-Live run failed before publication; the second published
  after its own Approval. No explicit restoration occurred.

### Native Acceptance Evidence

The profile requires the LLD section 18.6 suite: exact creation, both active
duplicates, and the bounded distinct-version tag race. The implemented v2
tooling uses five probes, three versions, two scenario tags, and six complete
active captures, with no deleted state, deletion, or restoration. The retired
v1 suite and legacy fixed-coordinate/retry-5 helpers are not alternatives.

The fresh v2 suite completed and passed independent native audit. Its exact
generation, run identities, byte and state digests, and provenance limits are
recorded in the [activation evidence](./validation/native-and-normal-live-evidence.md#2026-09-09-query--prepare-normal-live-activation-from-audited-native-evidence).
The admitted identity in Governance binds that canonical evidence, not the
failed v1 interpretation.

Current acceptance-only components live in
`three_workflow_delivery_v3.acceptance`. The distinct
`workflow-delivery-v3-native-npm-acceptance.yml` entry invokes one probe with
the actual Actions-issued repository `GITHUB_TOKEN`. It accepts only an
explicitly confirmed manual request on protected `main` by the accepted actor
at attempt one. Its job compares the GitHub-resolved SHA with the requested
tooling SHA before any steps, rejecting intervening `main` movement.
It does not revive any retired Buddy acceptance identity or
enter the normal-Live workflow graph. Inspect Git and protected delivery
before treating the entry as available remotely.

The probe retains immutable request, actual fixture, matched profile, process
facts, and platform context. Those facts are not a native acceptance verdict:
the operator must collect complete active destination state and apply every
scenario gate in sequence. Missing evidence, ambiguity, or an
unexpected delta stops mutation; no probe or workflow failure permits a blind
retry. The collector retains actual complete inventories, scenario bytes, and
raw native responses; the audit reader binds downloaded probe evidence to its
exact run, request, and tooling revision. The fixed-suite local operator
connects those components without a retry or recovery protocol. The
[LLD tooling boundary](./hcoona-release-smoke-npm-lld.md#1861-native-suite-tooling-boundary)
separates these responsibilities.

The operator confirmed the pre-existing public
`@hcoona/hcoona-release-smoke-npm-dual` container, ID `12047077`, associated
with `hcoona/three`, is operator-controlled and has no production dependency.
The separately approved five-probe v2 execution is complete. No further native
generation is authorized. The lifecycle revision and passing suite authorize
no additional administrative operation; unrelated versions, tags, and access
changes remain outside their scope.

The first generation stopped on the now-corrected storage-origin policy.
Its created version remains untouched. The second v1 generation reached the
deleted/restorable scenario and stopped when the identical duplicate publish
succeeded instead of failing. Read-only inventory distinguishes the deleted
original from the new active object; this was not restoration.

This remains a valid counterexample to the superseded reservation requirement,
not another reader defect or a passing revised generation. The different-byte
deleted probe and explicit restoration were not attempted. Do not delete the
replacement, retry restoration, or install either incomplete generation.
Recovery still needs a separate explicit decision. The approved correction
changes the lifecycle requirement rather than adding runtime history or
administrative compensation. See the [native rejection record](./validation/native-and-normal-live-evidence.md#2026-09-08-query--reject-the-native-deleted-version-primitive)
for exact run and object identities.

The standard-publish probes must use the real Actions-issued repository token;
do not relabel a local PAT as `GITHUB_TOKEN`, add secrets or access grants
implicitly, or send administrative credentials into normal runtime. No
acceptance generation may be installed before a real passing suite.

### Local Native Operator

The following runbook is for a future separately authorized v2 generation,
not a request to repeat the completed acceptance. Its exact tooling revision
must be protected-delivered and its bounded five-probe execution confirmed.
Retired v1 tooling is not an alternative path.

Use a clean POSIX checkout of the exact protected tooling revision, with the
repository's locked pnpm dependencies and Python 3.13 uv environment prepared.
Windows operators need a configured POSIX environment such as WSL. Existing
classic gh authentication must support package reads, dispatch, and
`gh run watch`; the revised suite needs no delete/restore capability.
No command installs credentials or expands grants.
If the isolated process cannot access the desktop keyring, provide the same
credential through the supported process-local `GH_TOKEN` environment
variable. Never print it, persist it in audit files, or pass it to Actions.

The example below is **not executable authorization**. Replace every
placeholder only after confirming the exact pre-existing, operator-controlled
disposable package has no production dependency and obtaining bounded
five-probe approval. The flag acknowledges prior approval; it does not
grant it. Use a fresh lowercase hexadecimal generation and two distinct
target SHAs whose scenario tags are absent in that package.

```bash
uv run --no-sync --python 3.13 --package three-workflow-delivery-v3 \
  python -m three_workflow_delivery_v3.acceptance suite \
  --package '@hcoona/<approved-disposable-name>' \
  --generation '<fresh-generation>' \
  --tooling-sha '<verified-protected-main-sha>' \
  --creation-target '<creation-target-sha>' \
  --race-target '<race-target-sha>' \
  --repository-root '<absolute-clean-checkout>' \
  --audit-directory '<new-absolute-directory-outside-checkout>' \
  --authorized-disposable
```

Use `suite --help` for the input contract. Configure any machine-specific
trusted CA location in the operator environment; never disable TLS
verification or add application certificate fallbacks.

The fixed sequence dispatches five acceptance probes: create A, identical
active A duplicate, differing active A duplicate, create W, and candidate V.
It performs no administrative mutation or deleted-state read. These are not
normal-Live dispatches.
Any failure stops mutation and preserves partial audit data. Inspect exact
recorded runs and destination state read-only before deciding an explicitly
authorized recovery; do not rerun the command against the same audit, dispatch
again blindly, or use the suite as an administrative recovery command.

Successful completion writes `suite-evidence.json` and prints its path and
digest. Its `scenario_verdict: "passed"` records completed supplied-fact gates;
it is not proof of native provenance or installed admission. Independently
audit the actual run/artifact references, raw observations, empty deltas, and
creation/tag-race results before preparing the Activation PR. Retain the local evidence
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

Normal Live requires the exact admitted native contract, fresh ready
Governance, and protected Activation delivery. The completed native evidence
does not waive post-merge readback or prove a normal-Live outcome. The design deliberately
treats a post-Observation tag race as bounded routing damage: supported
consumers resolve exact `name@version`, and exact version bytes, digests, and
witness remain authoritative.

Active-version non-overwrite is not lifetime-global coordinate immutability.
Administrator deletion may permit a new object and different content at the
same coordinate; caches may retain an earlier lifetime's content. This risk
is accepted only within the smoke-only, sole-writer TCB boundary. Normal
publication neither performs nor compensates for administrative operations.

## Explicit Authorization Boundary

The end-to-end authorization covered:
design delivery, disabled implementation and protected merge, bounded native
acceptance, obsolete-Environment cleanup after proof, ready Governance v2
activation, and auditable normal-Live proving. The failed first run consumed
the initial dispatch authorization. The separately authorized second new run
has also completed. Both dispatch authorizations are spent; this checkpoint
grants no standing permission for another run.

The authorization is contract-bounded. Do not change package or repository
access, touch unrelated packages or tags, use package-admin authority in
runtime, weaken review or readback gates, perform a GitHub rerun, reuse the
first run's artifacts, Snapshot, or Approval, or issue a third proving dispatch.
The separately approved native v2 generation is complete; another generation
requires new authorization.
Revised native acceptance performs no deletion or
restoration. Recovery of the original disposable D and its replacement remains
a separate explicit operator decision, not implied permission from the
lifecycle correction or a passing revised suite. Any ambiguous external response stops
mutation and triggers read-only investigation rather than retry.

## Required Reading Order

After the initial Git inspection, use this authority order for relevant
decisions. The NuGet research handoff provides decision-specific starting
sections; do not preload every historical appendix or treat the npm LLD as a
NuGet specification:

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
11. the applicable slice LLD:
    [npm](./hcoona-release-smoke-npm-lld.md) or
    [NuGet](./hcoona-release-smoke-github-packages-lld.md); and
12. current repository code only for implementation facts.

Do not infer policy from stale runtime behavior or archived designs.

## Next Executable Workflow

The implementation, activation, real publication, and independent proving audit
are complete. No further runtime or proving action is needed for this objective.

Perform the required Git inspection before using this documentary checkpoint.
Preserve both actual runs and the retained native evidence; do not repeat
provisioning, acceptance, either dispatch, or either Approval. Original-D
recovery and any future mutation require separate explicit authorization.
This completed task authorizes no third proving dispatch.

The NuGet slice follows its linked handoff and protected design package under
the subsequent end-to-end delegation. Continue the disabled implementation
units and their delivery gates without repeating design delivery. Native
operations still require concrete bounded requests and satisfied technical
gates; missing service assurance keeps activation disabled.

## Validation and Review Protocol

- Validate the complete affected implementation and design set, not only
  README, handoff, and evidence records.
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
- Use the `open-code-review-delegate` skill for multi-agent review, including
  the complete Git path manifest when OCR filtering omits documentation.
- Insert contraction after every two review iterations, after all changes and
  reviews, and before creating a PR.
- Do not claim validation, review, delivery, or merge before persistent evidence exists.

For later runtime work, complete affected tests, root HK, and hooks before multi-review. Documentation work uses the
applicable documentation and repository gates but keeps the same validate-before-review order.

## Architecture, Design, Testing, and Documentation Discipline

### Architecture and Design

- Preserve the waterfall gates: interactive requirements confirmation, HLD,
  MLDs, brief LLD, development, then test and review.
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

- Follow the [repository contribution guide](../../../../../CONTRIBUTING.md)
  for generic documentation and Git procedure.
- Keep requirement IDs stable, links valid, and current-state prose concise.
- Do not preserve obsolete retry ledgers, PR narratives, test-count histories, artifact tables, or superseded
  mechanisms in current-state pages.
- Use Git and delivery work carriers for chronology. Preserve verbatim source
  evidence and provenance in the project research and validation records.
- Make commits dependency-ordered and human-reviewable.
- Update the handoff, v3 README, and affected concern records when phase or
  slice status changes; keep run chronology in the delivery work carrier.
- Keep claims truthful, relevant, clear, and no more detailed than necessary.
