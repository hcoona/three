# Wiki Overview

This page holds the current top-level synthesis of the wiki.

## Current Architecture Version

Workflow delivery architecture **v3** is active and normative.

- [Architecture version entry point](./analyses/workflow-delivery/README.md)
- [v3 requirements](./analyses/workflow-delivery/v3/requirements.md)
- [v3 high-level design](./analyses/workflow-delivery/v3/high-level-design.md)
- [v3 Repository Model and Release Unit MLD](./analyses/workflow-delivery/v3/repository-model-release-unit-mld.md)
- [v3 Governance Integration MLD](./analyses/workflow-delivery/v3/governance-integration-mld.md)
- [v3 CI Qualification MLD](./analyses/workflow-delivery/v3/ci-qualification-mld.md)
- [v3 Release Delivery MLD](./analyses/workflow-delivery/v3/release-delivery-mld.md)
- [v3 Shared Foundation MLD](./analyses/workflow-delivery/v3/shared-foundation-mld.md)
- [v3 AI Agent Handoff](./analyses/workflow-delivery/v3/agent-handoff.md)
- [v3 architecture glossary](./analyses/workflow-delivery/v3/architecture-glossary.md)
- [v3 migration and document policy](./analyses/workflow-delivery/v3/migration-strategy.md)

v1 is the historical `origin/main` baseline. v2 is an archived prototype at
commit `8824df2a12c78a1f3a851a3c2763bcb9e64f2412`. Neither version is normative
for new v3 implementation work.

Workflow Delivery v3 commit 11 retires the legacy Buddy entry routes on
[PR #552](https://github.com/hcoona/three/pull/552):
`.github/workflows/buddy.yml` and `.github/workflows/release-buddy.yml` are
removed with no `legacy-buddy.yml`, dispatch, or caller-compatibility route.
Phase 1 clean-scope repair restores `.github/workflows/ci.yml` byte-for-byte
from base `7f8f41c2`, restores the v1 Official and reusable non-Buddy release
stack, and permits only the explicit fail-closed Buddy-channel rejection in the
restored orchestrator. The dedicated
`.github/workflows/workflow-delivery-v3-ci.yml` remains the non-authoritative
shadow workflow. The inherited pre-v3 workflow-release control projects,
control scripts and tests, non-npm and dual/WXT smoke projects, legacy
`three.release.yml` descriptors, and old design-history pages are not retained.
The v3 control package, first-slice npm project and authoring descriptors,
Governance, CODEOWNERS/HK integration, and all `workflow-delivery-v3-*`
workflows remain. UV, PNPM, and Mise locks are regenerated from that retained
scope. These restoration facts are not RC-001 final validation evidence. PR #552
merged as `5a84bebd` on 2026-08-24 with normal Live disabled. Governance then
converted both deleted legacy Buddy workflow identities to
`disabled_manually`, drained all nonterminal executions, and proved that real
old refs now receive disabled-workflow rejection. Destination acceptance was
separately authorized for one-time run `32769435970`; that authorization
is consumed and the `.1`-`.4` suite cannot be reused. PR #579 merged the
runner-startedness evidence repair as `06872f2b`. Retry-2 implementation and
finalization merged as `b031e5e0` and `953c1db0`. Attempt-1 run `32805739095`
observed `.5` absent, started mutation, and observed exact post-state, but the
runner did not prove a controlled outcome; the first probe failed incomplete
and the second was skipped. Workflow ID `341728447` is `disabled_manually`,
Environment ID `20531285468` was deleted through the API, and cleanup PR #584
removed the retry workflow source. The expected old-ref rejection instead
created cleanup probe run `32809578776`. GitHub Support later terminalized it
as `completed` / `cancelled` at `2026-08-26T01:45:46Z`; it has zero jobs and
zero pending deployments. The transition ref is absent. Authenticated
read-only reconciliation confirms exact version
`0.0.0-wdv3-acceptance.5`, tag `wdv3-acceptance-5`, repository association,
SHA-1, SHA-512, and acceptance target witness
`b031e5e0bd98a95943a03a1529b64e856e1a8aa1`. The unnecessary
Platform-Orphan implementation PR #590 was closed unmerged, so no exception
authority or result entered `main`. The `.5`-`.8` block is consumed.
Acceptance remains unsuccessful, and normal Live activation remains
unauthorized.
Retry-3 preparation and finalization merged as `a61f9a4e` and `af921228`.
Exactly one attempt-1 run, `33032171094`, received Environment approval,
observed `.9` absent, started mutation, and exactly read back `.9`. The runner
again did not prove a controlled outcome, so the first probe remained
incomplete, the `.10`-`.12` probe was skipped, and terminal Governance evidence
classified the run unknown. Authenticated reconciliation confirms the exact
`.9` tag, tarball hashes, repository association, and target witness; `.10`-
`.12` remain absent. Cleanup PR #600 merged as `916ea338`; the workflow source
and workflow-only contract are absent, workflow ID `343371046` is `deleted`,
and Environment ID `20680097388` and acceptance refs are absent. No
post-deletion dispatch was attempted. The `.9`-`.12` block is consumed and must
not be retried. All three attempts remain unsuccessful; `live_enabled` remains
false and Live activation remains unauthorized.
Retry-3 documentation closure PR #601 merged as `ad70a879`. Repair PR #603
then merged as `bf174897`, retaining closed request-bound upstream diagnostics
through the acceptance proxy, runner, Adapter, and Governance while keeping
them non-authoritative. The expected-one proxy now reserves its sole qualified
request atomically before upstream forwarding. The complete v3 suite passed
3,782 tests, and focused Pyrefly, HK, independent review/adjudication, required
checks, and CodeQL passed. No destination-acceptance invocation followed that
merge before this documentation update. The repair therefore changes no
historical acceptance result, and `.1`-`.12` remain consumed.

## Confirmed v3 Shape

- CI Qualification and Release Delivery are peer bounded contexts.
- Delivery Governance is an external authority boundary.
- CI and Release each own same-revision planning, Evidence Admission, and
  finalization while GitHub Governance supplies authority.
- Shared Foundation is a logical mechanism layer with no aggregate root,
  scheduler, universal record model, authorization, or Finalizer.
- Shared Foundation provides record, artifact, provenance, Repository Model,
  Provider, Definition, Build, Quality, execution-class, outcome, cache, and
  generic client primitives without owning business policy.
- Destination projection, action, Receipt, replay, and remediation semantics are
  Release-owned; Foundation provides generic clients only.
- Providers and Adapters emit mechanical results. CI and Release independently
  form and admit their authoritative records.
- Decision, Build and Qualification, and Side-Effect Zones are separate runtime
  trust boundaries.
- Release Unit and Qualification Target are the core domain objects. Project
  Nodes and dependency relationships are discovered technical facts.
- CI and Release share Build Definitions and adapters but do not share runtime
  Plans, Evidence, artifacts, or verdicts.
- CI uses one root-authoritative opaque HK gate for source-tree conformance and
  model-driven qualification for affected-system correctness.
- Projects select ecosystem-specific quality presets or custom policy through
  cascading directory-scoped authoring; the Planner resolves concrete targets,
  dimensions, and typed reverse dependency closure.
- Required obligations feed the authoritative CI Finalizer. Advisory
  obligations use a separate non-authoritative Reporter and do not delay the
  stable required check.
- Release starts only through manual dispatch on the exact target ref. A
  candidate run branches to live Release or separately namespaced,
  request-scoped simulation before live identity lookup or admission. One
  channel-specific Release Execution contains append-only whole-release
  Attempts; simulation has no live identity, authorization, capability,
  Receipt, or mutation. Separate requests
  retain separate request and Intent records. Each admitted, non-coalesced
  request for the same Release Execution Identity creates a distinct Attempt; a
  replaced pending dispatch creates none. Official Product Identity is channel,
  Release Unit, and canonical NBGV version; Official Execution Identity adds
  target. Buddy Execution Identity is channel, Release Unit, and target.
  Different targets create separate Executions even when destination
  projections are the same. No permanent Product Identity-to-target ledger is
  required; destination resource serialization and durable observation
  determine absent, exact, or conflict.
- Each live or simulation run attempt compiles exactly one same-revision,
  purpose-bound request-local Repository Model Snapshot and reuses it throughout
  the resulting Attempt or simulation pass. A replay or other new run attempt
  compiles a new Snapshot even when request identity, run ID, and target remain.
  Simulation Identity is derived only after its Snapshot validates;
  cross-purpose and prior-attempt artifacts are rejected. Actual live mutation
  actions, inputs, and complete key sets are frozen only in the Publication
  Snapshot after build, qualification, and observation.
- Capability groups require an Authorization Record created after successful
  approval. The first-slice GitHub rejection surface cannot produce
  attempt-bound Approval Outcome Evidence, so rejection is unknown, replayable
  incomplete, and non-authorizing. Cancellation or
  platform expiry while approval is pending may terminate the run before a
  separate record or Finalizer outcome; when no capability group started, the
  platform conclusion proves no side effect and leaves a replayable incomplete
  Attempt, while possible capability execution requires reobservation.
- Qualification only declares Capability requirements. Destination Capability
  in the normal v3 live path may be requested only by an authorized side-effect
  capability group after it validates the Authorization Record and exact
  Publication Snapshot/action bindings.
- Every Release builds the complete Release Unit variant set. Buddy and Official
  differ by identity and complete destination projections, not product subsets.
- Publication uses one channel approval followed by parallel independent,
  destination-specific capability groups. Actions retain separate Receipts and
  projection-internal partial state requires reconciliation.
- GitHub concurrency coalesces duplicate requests by complete Release Execution
  Identity. Every live Destination Adapter mutating action binds complete
  deterministic resource keys, and overlapping actions serialize. Package keys
  include the exact External Package Coordinate: channel, destination, package,
  and version, plus any additional Adapter-required keys. GitHub equality groups
  may conservatively over-serialize; the first-slice GitHub Packages group uses
  physical destination plus npm package name while preserving the full
  coordinate-plus-tag key set. Distinct Buddy or Official Releases never join
  one Execution merely because they claim the same resource.
- Release uses one logical Plan lineage with immutable Qualification and
  Publication snapshots.
- Buddy is an isolated distributable preview channel. For npm it uses the
  frozen native NBGV `npmPackageVersion` unchanged; isolation from Official
  comes from the complete channel, destination, package-coordinate, and
  Capability boundary rather than an Intent-derived version.
- The first live Buddy GitHub Packages slice is a bounded risk exception
  reconfirmed before LLD on 2026-08-06. Any same-repository selected ref supplies
  the complete same-revision workflow, Planner, Finalizer, Provider, Adapter,
  compiler, client, catalog, capability-declaration, and publisher stack without
  owner-reviewed eligibility. After exact Publication Snapshot creation, the
  normal path uses dedicated Buddy Environment approval, keeps workflow-level
  permissions empty or read-only, and declares `packages: write` only on the
  `run-live-attempt` `uses`-only caller job as the reusable-workflow ceiling and
  on the called Environment-referencing publisher job as effective capability.
  `evaluate-live-eligibility` receives only `contents: read`; effective
  `actions: read` is confined to history admission and explicit
  `packages: read` to the observer. Every other job remains explicitly
  least-privilege, the callee cannot elevate beyond the caller-job ceiling, and
  no PAT or `id-token: write` exists. Every repository Write/Maintain/Admin actor
  is inside the slice publisher TCB and can bypass the normal path with
  alternate workflow YAML; Environment is a mistake/process control, not a
  malicious-writer permission ceiling. The disposable package, isolated
  destination, minimum normal-flow access, reviewer detail, no-consumer rule,
  forbidden ordinary admin actions, and Break-Glass path bound accepted risk.
  An untrusted Write/Maintain/Admin actor blocks the slice until that actor's
  access is reduced below those roles or an independently enforced publisher
  boundary makes package-write Capability and destination access unavailable to
  writer-authored workflows; ref and workflow restrictions alone are
  insufficient remediation. Official and future Buddy destinations do not
  inherit the exception, and neither do production packages.
- The first brief
  [`hcoona-release-smoke-npm` LLD](./analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md)
  was approved for implementation on 2026-08-06. It defines the clean v3 module
  and workflow topology, shadow pull-request and manual `slice-validation` CI,
  caller-held Release Execution concurrency and reusable-workflow permission
  ceilings, strict record and capability-group bundle bindings, separate npm
  tarball-content/install-import Evidence, immutable reviewer-summary linkage,
  truthful cancellation/expiry handling, strict history-only Execution
  admission, ID-only artifact transport, exact path-triggered root-HK v3 tests,
  credential-free capability admission, diagnostic-only GitHub rejection,
  caller-selected current/history admission including same-run prior-attempt
  diagnostics, full-SHA action pins with the
  current Renovate-selected Node-24-compatible major, isolated frozen-version
  npm staging that updates and verifies the staged manifest `files` allowlist,
  exact packed-tarball witness-path/content checks, SHA-512 remote exact proof,
  explicit target-specific npm dist-tag projection, 45-day Release
  retention, exact-target current-attempt Live Eligibility Decisions,
  fixed-source protected-ref human TCB/access attestations with exact
  `hcoona/three`/`refs/heads/main`/path policy fields, required boolean
  `live_enabled`, `contents: read` fresh-source validation, and bounded
  operational staleness, immediate pre-Capability
  provenance/content freshness revalidation with new-Attempt recovery,
  workflow-run-unique physical artifact names across reruns, platform-aware
  history, complete CODEOWNERS triggers and exact final-match ownership for the
  protected Governance document, permanent consumer policy and
  Governance re-attestation, legacy Buddy test/topology cleanup, direct
  repository-wide removal, disablement, and draining of both legacy Buddy
  identities before a removable protected acceptance bootstrap with independent
  `github.run_attempt == 1` probe guards, first-attempt terminal evidence capture
  that survives dependency failure and classifies ambiguous mutation state for
  reconciliation, and new-coordinate retry,
  exact-target full-history/tag NBGV checkout with shallow-history rejection,
  first-slice policy authoring, acceptance plan, and reviewable implementation
  sequence. Failed acceptance leaves all Buddy publication disabled. No legacy
  Buddy compatibility remains;
  former projects are unsupported until migrated, while v1 Official and CI
  assets remain unchanged. Legacy Buddy workflows, Buddy-specific tests and
  matrices, and Buddy docs are excluded from that preservation and are retired
  or rewritten. Commit 11 implements the legacy Buddy entry retirement locally:
  `.github/workflows/buddy.yml` and `.github/workflows/release-buddy.yml` are
  removed with no `legacy-buddy.yml`, dispatch, or caller-compatibility route;
  v1 Official and CI remain. Activation remains unauthorized.
- Repository Model compilation emits authoritative target-bound canonical and
  native NBGV projections after the NBGV Provider remains pinned to the exact
  target and proves complete ancestry and tags through `fetch-depth: 0` or an
  equivalent guarantee. Shallow or incomplete history blocks compilation. CI
  and Release Plans and Build Requests freeze the exact required value; Build
  Adapters apply and verify it without NBGV recomputation, alternative
  derivation, or fallback. Official business identity uses the canonical NBGV
  version, while ecosystem publication and dry-run use the exact frozen native
  projection unchanged.
- Release retry uses whole-release replay and normal pre-side-effect
  Remote-State Observation.
- Break-Glass Remediation is independently authorized and append-only and
  reuses exactly the original action's complete frozen Adapter-declared
  resource-key set.
- Platform-native retention is used without assuming a permanent Release ledger.
- An absent registry coordinate is legitimate initial-publication state and
  requires no retained Intent lineage, tag witness, or binding index. Live
  publication depends on atomic non-overwriting creation and durable exact-state
  observation from the destination.
- The first vertical slice is `hcoona-release-smoke-npm`: CI Qualification,
  live Buddy publication to GitHub Packages, and Official npmjs dry-run.
- The Release MLD identity decision was reopened and reconfirmed before LLD on
  2026-08-05.
- The first-slice Buddy trust-risk exception was reopened and reconfirmed before
  LLD on 2026-08-06.

## Implementation Direction

v3 will be built on a clean implementation line.

- Do not evolve the v2 control architecture in place.
- Preserve v2 at its immutable commit as a design and mechanism archive.
- Port only reviewed mechanism assets behind v3 adapters.
- Rewrite requirements, architecture layers, contracts, runbooks, and rollout
  plans for v3.
- Start with one end-to-end vertical slice before expanding across ecosystems
  and destinations.
- Keep v1 Official and CI as the production compatibility baseline. The
  first-slice direct cutover removes, disables, and drains legacy Buddy before
  acceptance; an intentional Buddy outage is allowed, and failed acceptance
  leaves Buddy publication disabled rather than restoring v1 Buddy.

Parallel implementation is acceptable. Parallel authoritative CI decisions or
parallel live publishers are not.

## Documentation Boundary

New normative delivery pages belong under
`docs/wiki/analyses/workflow-delivery/v3/`.

The v2 normative corpus remains in the archived v2 commit and must not be copied
into the v3 line. Platform experiments may be extracted only after separating
observed facts from v2 design conclusions and revalidating assumptions that may
have changed.

## Current Delivery Boundaries

1. PR #552 merged as `5a84bebd`; normal Live remains disabled.
2. The direct Buddy cutover is closed: both legacy workflow identities are
   `disabled_manually`, no nonterminal legacy executions remain, and old-ref
   dispatch is rejected.
3. PR #573 merged the protected acceptance finalization as `d36e5a68`.
   Attempt-1 run `32769435970` observed absent pre-state, started the mutation,
   and observed exact post-state for the fixed
   `0.0.0-wdv3-acceptance.1` package version, but retained an incomplete
   mutation classification. Terminal Governance evidence admission failed
   because the runner response contradicted the recorded mutation startedness,
   and the second probe was skipped. Cleanup PR #575 merged as `274d81fd`.
   Workflow ID `340952168` is `disabled_manually`; old-ref dispatch is rejected
   with HTTP 422; the workflow file, transition ref, and acceptance Environment
   are absent; and all related runs are terminal.
4. Authenticated reconciliation confirmed the fixed version and tag remain
   exact, including repository association, SHA-1, SHA-512, manifest, and
   target witness. The Adapter distinguishes pre-action, post-action, and
   post-mutation-start failures across returned and exception paths while
   retaining incomplete classification; this does not retroactively make
   acceptance successful. Do not reuse the invocation, review, or coordinate.
5. Retry-2 run `32805739095` is consumed and unsuccessful. It observed `.5`
   exact after mutation startedness but retained incomplete runner evidence;
   the second probe was skipped. Workflow ID `341728447` is disabled, the
   workflow and Environment are absent, and cleanup PR #584 is merged. Cleanup
   probe run `32809578776` is now terminal as `completed` / `cancelled`, with
   zero jobs or pending deployments. Do not dispatch or recreate a ref.
   Authenticated read-only reconciliation confirms exact `.5` version, tag,
   repository, SHA-1, SHA-512, and target witness. PR #590 was closed unmerged;
   no Platform-Orphan authority or result entered `main`. Do not reuse
   `.5`-`.8`. Keep `live_enabled: false`; acceptance remains unsuccessful and
   normal Live activation remains unauthorized.
6. Retry-3 run `33032171094` is consumed and unsuccessful. Preserve its exact
   artifacts and `.9` package reconciliation evidence. Cleanup PR #600 removed
   the temporary source and contract; workflow ID `343371046` is `deleted`,
   and the Environment and acceptance refs are absent. Do not retry or reuse
   `.9`-`.12`. Live activation remains unauthorized.
7. Retry-3 documentation closure PR #601 merged as `ad70a879`. PR #603 merged
   the bounded upstream-diagnostic and expected-one request-reservation repair
   as `bf174897`. Diagnostics remain observability only and cannot establish
   execution, mutation completion, or Governance acceptance. No acceptance
   invocation followed the repair before this documentation update.
8. Preparation PR #608 rebase-merged without bypass as `835b81be`. Its closed
   fourth profile binds absent/exact `.13`, identical-race `.14`,
   differing-race `.15`, and lost-response `.16`; the retry-4 workflow,
   Environment identity, and confirmation remain fixed. Fresh authenticated
   preflight confirmed exact `main`, active ruleset and workflow identity, no
   nonterminal or retry-4 runs, deployments, acceptance refs, `.13`-`.16`
   versions, or retry-4 npm tags. Environment ID `20772100445` has sole
   required reviewer `hcoona`, self-review permitted, and sole deployment
   branch `main`. Protected finalization binds the workflow and Governance
   profile to the preparation merge SHA, not a finalization commit, and must
   merge without bypass. A fresh exact preflight must then pass before any
   acceptance ref is created or the single `run_attempt == 1` attempt is
   dispatched. No workflow dispatch, deployment, acceptance ref, package, or
   tag mutation has occurred. Subsequent explicit user authorization covers
   the bounded acceptance-only repair/retry loop through genuine success,
   reconciliation, cleanup, and closure. It does not authorize normal Live
   activation or `live_enabled: true`.

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Workflow Delivery Architecture Versions](./analyses/workflow-delivery/README.md)
