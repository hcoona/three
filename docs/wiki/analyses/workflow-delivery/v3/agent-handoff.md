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
- Implementation commits 1 through 11 of the approved first-slice LLD are
  delivered. Commit 10 was pushed at `e69675be`, and commit 11 retired the
  legacy Buddy entry workflows at `f0f81d52`. The branch then integrated
  current `main` at `e6482727`, repaired live qualification authority and
  runtime boundaries at `2ddeeed1`, and committed durable unsuccessful
  qualification Attempt Outcomes at `1e742b29`. The current published
  implementation and validation closure ends at `4fac140d`. It follows the
  cancellation-finalization runtime hardening at `646060e5`, closes the
  independently adjudicated final-review and re-review test gaps, keys Buddy
  caller-held concurrency by canonical Release Execution identity at
  `3a2df043`, integrates `origin/main` at `3cc079ee` through non-rewriting merge
  commit `e4dfea3d`, and explicitly preserves Ruff 0.14.4 until a separately
  scoped Ruff 0.16 migration. PR validation then skips Git LFS smudge only for
  Provider Git subprocesses at `2c0c1c24`, hardens the acceptance proxy at
  `08bde53f`, replaces the consumer-policy tokenizer with a linear scan at
  `db2e60a3`, binds live checkouts and admission to the caller revision at
  `627c5b6e`, removes the superseded release-build-variant workflow at
  `2c4ec0eb`, and records the CodeQL closure at `116b84d4`. Non-rewriting merge
  commit `4fac140d` integrates `origin/main` at `191abc82`. The validated
  implementation baseline is published at
  `dev/shuaizhang/design-workflows`. The bounded pre-coexistence CI bootstrap
  design is committed at `7c457b7c`, and its implementation, tests, and review
  closure are committed at `f0535989`. That implementation preserves the
  canonical failed CI Decision and projects only the enclosing
  non-authoritative pull-request check conclusion when the exact base tree
  lacks the canonical v3 CI workflow. The exception self-disables after merge
  because later bases contain that workflow. The 14-file committed range from
  the published baseline through `f0535989` passes the managed gates with
  3,234 v3 tests and 1,257 workflow-release control tests. All three original
  policy, CLI, and workflow reviewers report no findings after independent
  adjudication and repair. The branch backs
  [PR #552](https://github.com/hcoona/three/pull/552), which is open against
  `main`. Documentation closure is committed at `a9e8cbfa`. Non-rewriting
  merge commit `30b793be` then integrates the latest `origin/main` at
  `7f8f41c2`, containing only the upstream Biome 2.5.9 and Asciidoctor 4.0.10
  dependency updates; the frozen PNPM and UV lock checks pass. Nothing from
  PR #552 has merged.
  Commit 3 includes target-tree-bound
  Release Unit/Quality authoring validation, duplicate Release Unit rejection,
  exact-target Repository Model compilation, static catalogs, Node/NBGV
  Provider contracts, fixed Governance-source fields and attestation schema,
  and the exact-target Live Eligibility Decision. Commit 4 adds the first-slice
  project test, canonical in-tarball Package Target Witness, isolated Node
  Build and Quality Adapters, immutable single-read source staging, frozen
  credential-free runtime execution, deterministic cross-umask packaging, and
  a closed ordinary-file USTAR artifact profile. Commit 5 adds exact
  current-candidate CI planning, static lane Evidence, required
  non-authoritative finalization, the shadow pull-request/manual
  `slice-validation` workflow, typed retained npm artifact Evidence, blocked
  semantic-model closure, trusted pull-request SLO classification, and the
  permanent repository-wide smoke-package consumer-policy gate, including
  bounded local workflow and composite-action consumer discovery. Commit 6
  adds Release Intent and simulation identities, complete compiled Release
  policy closure in the Repository Model, exact four-obligation Qualification
  Snapshots and Decisions, two-stage npm build/upload Artifact formation,
  guarded live-only Publication Snapshot contracts with exact materialized
  Publication Actions, strict current-attempt Release transport, and the
  12-job Official simulation workflow. Commit 7 adds credential-free public
  npmjs observation for Official simulation, digest-bound observation/action
  transport bundles, SHA-512 exact-state classification, and hypothetical
  action reporting without live Publication Snapshot, capability, receipt, or
  mutation lineage.
- Commit 8 adds strict GitHub Actions history discovery/admission, the live CLI
  chain, immutable reviewer payload and credential-free exact-SHA
  Authorization formatting, Governance freshness and Capability admission,
  GitHub Packages publication/result/Receipt finalization, and the disabled
  Buddy caller/reusable workflows. At the commit-8 boundary the protected
  Governance document was absent, so normal live execution failed closed
  before Attempt creation.
- Commit 9 adds final-match `@hcoona` CODEOWNERS coverage for every governed v3
  package, engineering, descriptor, Governance, HK, root Python, workflow,
  action, and directly invoked script surface. Contracts evaluate the real
  ordered CODEOWNERS rules, future descriptor/workflow/action layouts,
  missing and later-overridden rules, arbitrary-ref Buddy separation, and
  real HK add/modify/delete/rename trigger behavior.
- Commit 10 is delivered and pushed at `e69675be`; it adds the
  temporary five-job protected destination-acceptance
  workflow, with a 40-zero target sentinel that blocks before Environment
  review or mutation until a later protected finalization commit fixes the true
  target SHA. It adds the canonical protected attestation with
  `live_enabled: false`, strict Governance Acceptance Evidence and fixed
  GitHub Packages probe contracts, first-attempt-only guards, terminal
  incomplete/unknown reconciliation evidence, and optional on-demand read-only
  reviewer recovery. The second package-write job runs the fixed exact,
  identical-race, differing-race, and lost-response suite at reviewed internal
  `.1` through `.4` coordinates; authenticated GitHub REST package and version
  enumeration proves absence, ownership, repository association, and the exact
  version before strict npm/tarball readback validates tag, bytes, and witness.
  Reviewer identity is unavailable in workflow job context;
  a missing reviewer alone does not downgrade otherwise complete Evidence, and
  recovery is uniquely scoped by run plus Environment because only
  `acceptance-review` declares it; recovery remains diagnostic-only and
  retention-dependent. The Environment and reviewer configuration are pending
  protected finalization, not asserted to exist at this boundary. Normal live remains
  blocked. The real bounded probe orchestration is present but unreachable
  behind the zero-SHA sentinel. Commit 11 retired the legacy
  `.github/workflows/buddy.yml` and `.github/workflows/release-buddy.yml`
  entries with no `legacy-buddy.yml`, dispatch, or caller-compatibility
  route while preserving v1 Official/CI, generic/v2 behavior, normal v3 Buddy
  workflows, and the live-attempt/acceptance sentinel.
- Commit 10's local acceptance boundary is closed around a real npm 11.9.0
  request captured with Node 24.14.0 from a disposable package against a
  loopback-only registry. The proxy strictly validates the emitted CouchDB
  body before replacing the dummy authorization for a mocked upstream, and
  classification consumes an immutable proof of those validated request,
  tarball, response, and response-identity bytes rather than reconstructing a
  synthetic body. One monotonic deadline spans observation, process, proxy,
  upstream, and cleanup boundaries. Missing, malformed, or contradictory
  runner facts remain incomplete, and complete Governance Acceptance Evidence
  rejects zero target and workflow SHAs while incomplete sentinel semantics
  remain available.
- With `GIT_LFS_SKIP_SMUDGE=1`, the current Workflow Delivery v3 package
  validation passes 3,234 tests, and the workflow-release control suite passes
  1,257 tests. The complete workspace gate and authoritative 573-file
  `origin/main..HEAD` gate pass at `4fac140d`; the later 14-file bootstrap
  committed range also passes through `f0535989`. Remote run
  [`32346356010`](https://github.com/hcoona/three/actions/runs/32346356010)
  passes general CI and CodeQL. All 20 targeted PR #552 CodeQL findings
  (alerts 45-46 and 54-71) are fixed in the PR analysis without dismissal or
  suppression. For the broad implementation diff, the required
  non-authoritative shadow finalizer still returns the canonical
  `incomplete-model-plan` failure class and `fix-model-plan-and-rerun` next
  action. The new pre-coexistence projection does not rewrite that Decision;
  it can conclude only the enclosing pull-request check successfully after
  exact record re-admission, exact event-identity comparison, and an exact
  base-tree proof that the v3 CI workflow is absent. Manual validation, lane
  failures, mixed diagnostics, malformed records, explicit supersession, and
  post-coexistence pull requests remain nonzero.
  Published head `9b7b7d2c` passes every PR check. Workflow Delivery v3 run
  [`32440545037`](https://github.com/hcoona/three/actions/runs/32440545037)
  completes successfully, as do general CI run
  [`32440545005`](https://github.com/hcoona/three/actions/runs/32440545005)
  and CodeQL run
  [`32440545090`](https://github.com/hcoona/three/actions/runs/32440545090).
  The exact retained Plan and lane-result artifacts replay to Finalizer exit
  `1`, terminal failure `incomplete-model-plan`, next action
  `fix-model-plan-and-rerun`, 283 exclusively unclassified-path diagnostics,
  and no admitted Evidence or artifacts. The exact base/head/tested-merge
  projection replay succeeds and emits the explicit note that the canonical
  Decision remains failure.
  The protected acceptance Environment and reviewer configuration are still
  pending; no live acceptance dispatch or package mutation has run.
- Durable qualification terminalization now permits exact failed or incomplete
  Qualification Decisions to produce publication-free Attempt Outcomes. The
  `finalize-live` CLI replays the exact retained Qualification Snapshot,
  Evidence, Release Artifact, and Decision before accepting that outcome.
- The publication-preparation interruption slice is implemented and committed:
  `62ac4bb2` records the confirmed design, `fca9862d` adds the canonical
  Outcome/Finalizer/CLI contract, and `8377343b` adds direct GitHub job facts,
  retained diagnostics, and fail-after-retention workflow behavior.
  `14b40c75` closes the first runtime review, `5f8449d7` reconciles the
  Snapshot/reviewer documentation, `91deece4` closes the executable
  classifier, cancellation, lifecycle, retention, and reviewer-link review
  gaps, and `297d5adf` records that test evidence. `c295f612` makes finalizer
  acquisition cancellation-admitting, preserves qualification-only outcomes
  under cancellation, and rejects partial optional CLI transports.
  `6857617d` replaces the stale aggregate control-bundle requirement with the
  implemented separate-artifact Publication Control Closure, and `2b573fe4`
  records that regression evidence. Subsequent review closure through
  `8ef930fe` makes acquisition cancellation-admitting and preserves exact
  qualification-only finalization. `646060e5` adds the job-level inherited
  cancellation witness, nonempty mandatory artifact-ID guards, independent
  propagation and unsuccessful-Qualification operand coverage, and one-hot
  CLI platform-fact forwarding. `1daf3202` locks exact producer-specific
  Qualification digest forwarding and the valid cancellation combination
  where Observation succeeded before materialization was skipped and the
  publisher was cancelled. `b5c4b38e` extends that closure to every retained
  Qualification artifact ID and name producer and to the exact retained job
  diagnostics for that cancellation state. The slice uses terminal phase
  `publication-preparation`, result `incomplete`, uncertainty, no possible
  mutation, and next action `new-attempt`. Whole-workflow cancellation may
  report an unstarted publisher as `cancelled` only when no Snapshot or
  downstream lineage exists; that state is not also platform termination.
  Failed or incomplete Qualification retains its qualification-only Outcome
  under the corresponding cancellation-owned no-lineage state. It adds no
  Evidence type, fabricated Snapshot, aggregate control artifact, artifact API
  lookup, or watchdog. A durable Publication Snapshot remains the boundary
  into the existing Snapshot-bound lifecycle.
- `3a2df043` completes Buddy caller-held Release Execution concurrency. The
  real model compiler emits the canonical SHA-256 key for channel, Release
  Unit, and immutable target only. Request ID and workflow-run identity remain
  bound Attempt transport but cannot partition the Execution group. The
  caller holds that group across the complete reusable live Attempt with
  `cancel-in-progress: false`; different targets retain distinct groups.
- `e4dfea3d` completes the non-rewriting integration of `origin/main` at
  `3cc079ee`. Conflict resolution preserves the branch's deliberate retirement
  of the misleading legacy `validation` and `dotnet-tests` jobs, retains both
  active CI PNPM 11.22.0 pins, and regenerates the root PNPM, standalone Hexo,
  and UV locks from merged manifests. The standalone Hexo lock retains its
  `hexo@<7.2.0` override. `f3eb3b81` makes the resulting lint compatibility
  boundary explicit by pinning Ruff 0.14.4 until a separately approved Ruff
  0.16 migration. Independent merge and tooling re-reviews report no findings.
- `4fac140d` completes the later non-rewriting integration of `origin/main` at
  `191abc82` after the bounded LFS and CodeQL repairs. It preserves upstream
  open-code-review 1.9.5 lock data exactly and closes the remote implementation
  validation described above.
- The first vertical slice is `hcoona-release-smoke-npm`.
- The slice has one Node project, one package variant, and one npm artifact.
- It covers CI Qualification, live Buddy publication to GitHub Packages, and
  Official npmjs dry-run.
- The Release MLD identity decision was reopened and reconfirmed before LLD on
  2026-08-05.
- Buddy npm uses the frozen native NBGV `npmPackageVersion` unchanged. Separate
  manual requests retain separate request and Intent records. Each admitted,
  non-coalesced request creates a distinct Attempt in the same Release Execution
  when it names the same Buddy Release Execution Identity: channel, Release
  Unit, and target. Official Product Identity is channel, Release Unit, and
  canonical NBGV version; Official Release Execution Identity adds target.
  Different targets create separate Executions. No permanent Product
  Identity-to-target ledger is required. A replaced pending dispatch creates no
  Attempt.
- An absent coordinate is legitimate initial-publication state and is not
  reserved by Intent. Live Buddy requires atomic non-overwriting creation and
  durable exact-state observation from GitHub Packages. Exact state requires
  coordinate, ownership, byte-identical tarball, and the canonical immutable
  target witness extracted from inside that tarball; otherwise the destination
  is unsupported or blocked. Isolated npm staging deterministically updates and
  verifies only the staged manifest `files` allowlist to include
  `workflow-delivery/provenance.json` alongside existing intended files. The
  source manifest remains unchanged, and qualification inspects exact tar entry
  `package/workflow-delivery/provenance.json` and requires canonical bytes equal
  to the frozen witness input.
- First-slice npm publication explicitly uses
  `buddy-sha-<40-lowercase-target-sha>`, never implicit `latest` or a shared
  moving Buddy tag. The tag is routing, not provenance. Exact projection state
  requires both exact package bytes/witness and the tag mapped to the frozen
  native version. Publication is one compound action keyed by coordinate and
  destination/package/tag; absent or mismatched tag state requires
  reconciliation.
- Repository Model compilation emits authoritative target-bound canonical and
  native NBGV projections, including `npmPackageVersion`. Plans and Build
  Requests freeze the exact required projection; Build Adapters apply and
  verify it without recomputing NBGV, deriving another version, or using
  fallback fields. The NBGV Provider remains pinned to the exact target while
  fetching complete ancestry and tags through `fetch-depth: 0` or an equivalent
  guarantee and blocks shallow or incomplete history before compiling facts.
  Official Product Identity uses the canonical NBGV version, while Official
  ecosystem publication and dry-run use the exact frozen native projection
  unchanged.
- Every candidate run attempt branches to live Release or release simulation
  before live eligibility, identity lookup, coalescing, or admission. Each
  branch compiles exactly one same-revision, purpose-bound Repository Model
  Snapshot and reuses it throughout the resulting live Attempt or simulation
  pass. A new run attempt compiles a new Snapshot. Simulation uses separately
  namespaced request-scoped identity derived only after its Snapshot validates;
  the Snapshot never binds a future Simulation Identity. Simulation cannot
  acquire live Product, Execution, Attempt, authorization, capability, Receipt,
  or mutation lineage. Cross-purpose records are rejected. Actual live actions,
  inputs, and complete mutation key sets are frozen only in the Publication
  Snapshot after build, qualification, and observation.
- Successful approval produces the only Authorization Record accepted by
  capability groups. Terminal denial Evidence requires exact attempt-bound
  platform proof; GitHub Environment Deployment Review lacks it, so first-slice
  rejection is unknown, replayable incomplete, diagnostic-only, and
  non-authorizing. Workflow Delivery adds no approval watchdog. GitHub
  cancellation or platform expiry
  while approval is pending may end the run before a separate record or
  Finalizer outcome exists. If no capability group started, the platform
  conclusion proves no side effect and leaves a replayable incomplete Attempt.
  If capability may have started, the Attempt is incomplete and possibly
  mutated and replay must reobserve.
- Existing `approval-finalizer` is the credential-free Capability Admission
  Gate. It validates Authorization Record, Snapshot, summary, actions, artifacts,
  resource keys, group manifest, and immediate Governance freshness. Using
  `contents: read`, it freshly resolves the policy-fixed protected ref and reads
  the attestation document, requires `live_enabled: true`, current validity, and
  provenance/content identity with the admitted Live Eligibility Decision, and
  blocks the current Attempt on disablement, expiry, change, or invalidation.
  Governance restoration requires a new Attempt. Only success may schedule the
  package-write publisher. Publisher repetition is optional architecture-wide
  defense in depth, but this slice LLD elects and requires it immediately
  before npm mutation; it creates no malicious-writer boundary, credential, or
  service.
- The approval job remains `permissions: {}` and credential-free. Because jobs
  do not share workspaces, it anonymously fetches the exact selected
  40-character target SHA from the public `hcoona/three` Git repository,
  verifies detached `HEAD`, and executes that same-revision Authorization
  formatter. It does not use `GITHUB_TOKEN`, Actions artifact credentials,
  `actions/checkout`, a moving ref, or fallback revision.
- Qualification declares Capability requirements but cannot request, approve,
  or create live Capability. The normal v3 live path requests destination
  Capability only in an authorized side-effect capability group after
  validating exact authorization and Publication Snapshot/action bindings.
- Every live Destination Adapter mutating action declares complete
  deterministic mutable-resource keys. Publication Snapshots and action
  manifests bind them, overlapping actions serialize, and remediation reuses
  exactly the complete frozen original action key set without Product or
  Execution Identity derivation. Package keys include the exact External
  Package Coordinate plus any additional Adapter-required keys; non-package
  keys are Adapter-defined. GitHub exposes equality concurrency groups rather
  than arbitrary set-overlap locking. The first-slice GitHub Packages Adapter
  therefore uses a conservative physical-destination-plus-package group that
  serializes different versions and target-derived tags for the same package.
  This intentionally over-serializes but does not replace the complete frozen
  coordinate-plus-tag key set in Snapshots, actions, Receipts, validation,
  remediation, or future abstract overlap semantics. Missing or unenforceable
  required keys or serialization projections block live publication.
- GitHub Release publication and live Official npmjs publication are outside
  this slice.
- The first live Buddy GitHub Packages slice has an explicitly accepted bounded
  risk exception reconfirmed before LLD on 2026-08-06. Any same-repository ref
  selected by `workflow_dispatch` may supply the same-revision workflow,
  Planner, Finalizer, Providers, Adapters, compiler, clients, catalogs,
  capability declarations, and publisher without protected-ref or CODEOWNERS
  eligibility. Every Attempt still requires dedicated protected Buddy
  Environment approval after Publication Snapshot creation. The normal live
  workflow keeps workflow-level permissions empty or read-only. It declares
  `packages: write` only on the `run-live-attempt` `uses`-only caller job as the
  reusable-workflow ceiling and on the called Environment-referencing publisher
  job as effective capability, with no PAT and no `id-token: write`.
  `evaluate-live-eligibility` receives only `contents: read`; effective
  `actions: read` is confined to history admission and explicit
  `packages: read` to the observer. Every other job is explicitly
  least-privilege, and the callee cannot elevate beyond the caller-job ceiling.
- Approval is not cryptographic or independent semantic validation. Approved
  malicious target code may publish arbitrary bytes or abuse reachable package
  operations. Every repository Write/Maintain/Admin actor is in the slice
  publisher TCB and can author alternate write-capable workflow jobs;
  Environment approval controls mistakes and the normal process, not that
  adversary. The exception is bounded by the dedicated disposable package,
  isolated destination and Environment, minimum normal-flow permissions,
  reviewer-visible target/coordinate/artifact/lifecycle/action details, no
  normal consumers, forbidden ordinary admin actions, and Break-Glass
  delete/restore handling. An untrusted Write/Maintain/Admin actor blocks the
  slice until that actor's access is reduced below those roles or an
  independently enforced publisher boundary makes package-write Capability and
  destination access unavailable to writer-authored workflows. Ref narrowing,
  Environment branch restrictions, CODEOWNERS, and workflow-execution
  protections are insufficient remediation by themselves. Official and future
  Buddy destinations or production packages do not inherit the exception.
- The existing v2 `three.release.yml`, workflows, and control types are
  reference material only. The v3 slice must define its own contracts and must
  not inherit the v2 profile model.
- The first brief
  [`hcoona-release-smoke-npm` LLD](./hcoona-release-smoke-npm-lld.md)
  was approved for implementation on 2026-08-06. It defines one clean Python v3
  control package; shadow pull-request and manual `slice-validation` CI; live
  Buddy caller-held Execution concurrency around a same-revision reusable
  Attempt workflow; Official simulation; strict record and capability-group
  bundle bindings; job-scoped reusable-workflow permission ceilings with no
  workflow-wide package write; isolated npm staging with staged-manifest
  `files` allowlist enforcement and exact packed-witness checks; distinct
  tarball-content/install-import Evidence; immutable
  reviewer-summary artifact linkage; SHA-512 remote exact proof; truthful
  cancellation/expiry handling; history-only same-Execution admission,
  including earlier attempts of the same run without artifact-to-attempt claims;
  ID-only artifact transport; exact path-triggered root-HK v3 tests;
  credential-free capability admission; diagnostic-only rejection handling;
  caller-selected current/history admission modes; full-SHA action pin policy
  using the current Renovate-selected Node-24-compatible major; permanent
  consumer policy plus current-attempt Release-owned Live Eligibility Decision;
  fixed-source protected-ref non-executable human writer-TCB/access attestation
  with exact `hcoona/three` + `refs/heads/main` +
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` source
  fields and bounded staleness; pre-Capability freshness revalidation; physical
  artifact names unique across reruns through `github.run_attempt`; embedded npm
  target witness; target-specific dist-tag projection; platform-aware history
  attribution; complete CODEOWNERS
  triggers/final-match tests, including the exact protected Governance document;
  post-activation Governance re-attestation; legacy
  Buddy test/topology cleanup; exact-target full-history/tag NBGV checkout with
  shallow-history rejection; a removable protected fixed-coordinate acceptance
  bootstrap after direct legacy Buddy retirement whose probes independently
  require `github.run_attempt == 1`, whose terminal evidence capture uses
  `always() && github.run_attempt == 1` to retain dependency failures and
  ambiguous mutation state for incomplete/unknown reconciliation, and whose
  retry requires a newly reviewed invocation and disposable coordinate/version;
  45-day Release retention; acceptance scenarios; and dependency-ordered
  commits.
- The implementation PR merge is the direct v1 Buddy-to-v3 Buddy cutover and
  lands with the protected attestation's `live_enabled` field false and removes
  both legacy Buddy workflow files. No `legacy-buddy.yml` or unrelated
  compatibility route is created. Governance freezes dispatch, disables both
  `buddy.yml` and `release-buddy.yml`, cancels or drains all old executions,
  verifies disabled state, removal, and old-ref dispatch rejection before
  acceptance, runs every probe only on `github.run_attempt == 1`, always captures
  first-attempt evidence after failed dependencies, removes and verifies the
  bootstrap, and only then may an authorized protected commit set
  `live_enabled` true for the named smoke package. Partial reruns are rejected;
  retry requires a new reviewed invocation and disposable coordinate/version.
  Failed or ambiguous acceptance enters incomplete/unknown reconciliation,
  leaves all Buddy publication disabled, and keeps legacy Buddy retired.
  Restoring it requires a separate user-approved rollback PR; an intentional
  brief Buddy outage is expected. v1
  Official and CI assets remain unchanged; legacy Buddy workflows,
  Buddy-specific tests/matrices, and Buddy docs are excluded from that
  preservation and are retired or rewritten.
- After activation, relevant role/team/permission or
  package/repository/Manage Actions access changes require immediate manual
  response by an authorized human who promptly commits `live_enabled: false` to
  the protected source, followed by inspection, update, and re-attestation
  before re-enabling. Review, merge, and fresh-read latency make this bounded
  operational response rather than instantaneous platform disablement;
  at-most-90-day expiry bounds normal-flow staleness. Runtime does not claim
  complete writer or GitHub Packages grant enumeration. Every live request uses
  `contents: read` to freshly resolve and read the exact policy-fixed
  protected-ref attestation and validates repository/ref/resolved
  commit/path/blob/content provenance, schema, bindings, expiry, limitations,
  and `live_enabled: true` before Execution lookup or Attempt creation.
  Immediately before Capability Admission it repeats that fresh source read and
  exact provenance/content comparison. Missing, unreadable, malformed, expired,
  provenance-mismatched, changed, disabled, invalidated, or consumer-positive
  state blocks; no repository variable, PAT, App, service, ledger, OIDC, or
  additional token permission is added. Permanent root HK policy remains the
  repository-wide dependency gate.
- Final local v3 validation and independent review are complete through
  bootstrap implementation commit `f0535989`. The canonical shadow Decision
  remains the expected non-authoritative fail-closed record for the broad
  implementation diff; only the one-time pre-coexistence check conclusion may
  project success, and the exact base-tree marker disables that route after
  merge. Current-head PR #552 checks and exact artifact replay now pass. The
  immediate boundary is human review, followed by separate explicit
  authorization to merge. Merge starts the direct cutover and intentional
  Buddy outage, so the operator must be ready to execute the immediate
  post-merge legacy drain and old-ref rejection proof. Do not merge, run real
  acceptance probes, finalize the sentinel target, activate normal live, or
  begin later scopes without a separate explicit user task.
- Implementation must preserve the approved commit boundaries and keep live
  activation disabled until acceptance and the separate activation approval.

## Required Reading Order

1. This handoff and the [v3 entry point](./README.md).
2. [Requirements](./requirements.md).
3. [High-Level Design](./high-level-design.md).
4. [Architecture Glossary](./architecture-glossary.md).
5. The five MLDs linked from the v3 entry point.
6. [Migration and Document Policy](./migration-strategy.md).
7. Current repository code only for implementation facts.

v1 remains the production compatibility baseline for CI and Official. Its Buddy
routes retire at the confirmed first-slice cutover. v2 is an archived prototype
and mechanism source. Neither is normative for v3.

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
- Control code is same-revision. Target-controlled execution does not share a
  trust boundary with publication capability except for the explicitly accepted
  first-slice Buddy target-revision publisher after Environment approval and
  credential-free Capability Admission.
- Planning closes each execution boundary before it runs. The Qualification
  Snapshot closes build and qualification scope and deterministic
  pre-observation publication basis before build; after qualification and
  observation, the Publication Snapshot closes exact publication and
  authorization inputs before side effects. Unknown, incomplete, conflicting,
  or unprovable required state fails closed.
- NBGV is the sole canonical and published product-version authority. Release
  must not append Intent-derived version components. Release rebuilds and
  qualifies the complete Release Unit variant set.
- Buddy and Official isolation is defined by the complete channel,
  destination, package-coordinate, and Capability boundary, not necessarily by
  different version strings.
- Buddy Release Execution Identity and request coalescing use channel, Release
  Unit, and immutable target. Official request coalescing uses Official Product
  Identity plus immutable target. Every live mutating action uses complete
  Adapter-declared mutable-resource keys without joining distinct Executions.
  Package keys include the exact External Package Coordinate: channel,
  destination, package, and version, excluding Release Unit and target, plus any
  additional Adapter-required keys.
- Do not add a tag witness, binding index, application-level destination lock,
  or permanent Release ledger.
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
