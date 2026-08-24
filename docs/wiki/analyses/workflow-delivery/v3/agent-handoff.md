# Workflow Delivery v3 AI Agent Handoff

## Purpose and Authority

Read this page before acting on any Workflow Delivery v3 request.

This page is an AI operating handoff, not a second architecture specification.
If it conflicts with the
[requirements](./requirements.md),
[HLD](./high-level-design.md),
[glossary](./architecture-glossary.md), or an MLD, the normative document wins.

## Current Checkpoint

- Phase 1 clean-scope repair restores production `ci.yml` byte-for-byte from
  base `7f8f41c2` and restores the v1 Official and reusable non-Buddy release
  stack from that base, with only the explicit
  fail-closed Buddy-channel rejection in the orchestrator. The inherited
  pre-v3 control plane, obsolete smoke projects, legacy descriptors, related
  tests and fixtures, and old design-history pages are removed. UV, PNPM, and
  Mise locks are regenerated for the retained v3/first-slice scope. The repair
  is published at `50e4463a`. This is not RC-001 final validation evidence.
- Phase 2 consumer and Live Eligibility admission repair is published at
  `bfb41525`. It closes RC-013 by rejecting matrix base-product or include
  overflow before materializing state 257, RC-033 with a pinned Tree-sitter
  scanner limited to direct and shallow statically resolvable package-manager
  use, and RC-016 with strict canonical Consumer Policy and Live Eligibility
  binding. Relevant unsupported JavaScript flow fails closed; the scanner does
  not model arbitrary JavaScript execution. Parseable approved-exception drift
  remains consumer-positive, while a missing approved path remains a scan
  error. Buddy workflows retain immutable blocked Decisions before propagating
  domain status. The complete v3 suite passes 3,535 tests, the repository
  Consumer Policy is clean with zero consumers, three admitted exceptions,
  and 142 scanned surfaces, and the full HK and staged pre-commit gates pass.
  The three original finding reviewers and the independent holistic reviewer
  report no findings after atomic adjudication and repair.
- Phase 3 execution-history integrity repair is published at `e92f4fe5`. It
  closes RC-011 by deriving complete history Snapshot authority from an
  admitted Intent, RC-017 by admitting the raw artifact archive digest before
  payload extraction, RC-028 by excluding exactly expired artifacts before
  download, RC-026 by exhausting exact prior-attempt and job facts and
  requiring recent expected bindings, and RC-027 by independently retaining
  canonical raw Outcome and summary artifacts. The implementation does not
  fabricate artifact-to-attempt or artifact-to-job provenance. The complete
  v3 suite passes 3,581 tests, the repository Consumer Policy is clean with
  zero consumers, three admitted exceptions, and 145 scanned surfaces, and
  root UV lock, scoped Ruff and Pyrefly, Actionlint, full HK, and staged
  pre-commit gates pass. All five original finding reviewers and the
  independent holistic reviewer report no findings after atomic adjudication
  and repair.
- Phase 4 destination-observation repair is published at `722f7783`. It closes
  RC-019 with the step-local effective observer token, RC-020 with unscoped
  GitHub Package REST resource naming while preserving scoped npm coordinates,
  RC-021 with exact Package Version API owner authority, and RC-030 with
  retain-before-propagate blocking Observations and pre-Snapshot Outcome
  binding. RC-030 direct admission reuses the existing frozen
  projection/qualified-artifact validator. RC-022 is closed as a freshly
  adjudicated false positive: mutable package repository association remains
  acceptance-bootstrap evidence and is not added to normal exact-state
  authority. The complete v3 suite passes 3,613 tests; Consumer Policy is
  clean with zero consumers, three admitted exceptions, and 142 scanned
  surfaces; scoped Ruff and Pyrefly, Actionlint, full HK, and staged
  pre-commit gates pass. The four original finding reviewers and the
  independent holistic reviewer report no findings after atomic adjudication
  and repair.
- Phase 5 acceptance-evidence repair is published at `37ce8d64`. It closes
  RC-023 by terminalizing the fixed `exact` scenario after one pre-observation
  unless the expected exact state already exists, with no mutation runner
  invocation for absent, conflicting, or unknown state. It closes RC-025 by
  retaining every canonically reconstructed probe suite across later failure,
  cancellation, or monotone post-upload downgrade while keeping the separate
  job conclusion fail-closed in terminal Governance Evidence. Missing artifact
  bindings remain incomplete; failed or cancelled probe jobs make aggregate
  state unknown; empty facts are used only when no valid record formed. The
  complete v3 suite passes 3,615 tests; Consumer Policy is clean with zero
  consumers, three admitted exceptions, and 142 scanned surfaces; Ruff and
  Pyrefly, Actionlint, full HK, and staged pre-commit gates pass. Both original
  finding reviewers and the independent holistic reviewer report no findings
  after atomic adjudication and repair.
- Phase 6 approval, capability, and finalization repair is published at
  `bd9b2318`. It closes RC-031 by binding the selected ref to the admitted
  Release Intent and rendering reviewer-visible target, coordinate, artifact
  hashes and manifest, lifecycle scripts, canonical action details, or an
  explicit exact-satisfied no-action disposition. Live Eligibility and
  Governance duplication were freshly narrowed out of the required reviewer
  payload correction. It closes RC-032 by preventing Capability Decision
  persistence until Authorization has a successful immutable upload identity,
  while retaining blocking Decisions before failure propagation. It closes
  RC-018 by treating group bundles, Receipts, and Receipt transports as
  capability-start evidence and strictly admitting safe replay without
  rejecting the admission-only pre-publisher cancellation window. The complete
  v3 suite passes 3,629 tests; Consumer Policy is clean with zero consumers,
  three admitted exceptions, and 142 scanned surfaces; Ruff and Pyrefly,
  Actionlint, full HK, and staged pre-commit gates pass. All three original
  finding reviewers and the independent holistic reviewer report no findings
  after atomic adjudication and repair.
- Phase 7 canonical-result repair is published at `a89d2986`. It closes RC-014
  by retaining the canonical non-authoritative CI Slice Decision and Summary
  as run/attempt-qualified raw artifacts for 45 days, including failed and
  incomplete finalization, before the terminal no-Decision guard. It closes
  RC-035 by rejecting every nested Hypothetical Action whose Simulation
  Identity or Qualification Snapshot/Decision digests differ from its
  enclosing Simulation Outcome. The complete v3 suite passes 3,633 tests;
  Consumer Policy is clean with zero consumers, three admitted exceptions, and
  142 scanned surfaces; scoped Ruff and Pyrefly, Actionlint, full HK, and
  staged pre-commit gates pass. Both original finding reviewers and the
  independent holistic reviewer report no findings after atomic adjudication
  and repair.
- Phase 8 and the PR-comment follow-up are complete at final behavior commit
  `9f97ef09`; the prior `e9d812b2` RC-001 boundary is retained as superseded
  evidence. Every review thread is resolved. PR #552 merged as `5a84bebd` on
  2026-08-24 after separate explicit authorization. Normal Live remains
  disabled.
- The immediate post-merge cutover is complete. Workflow IDs `216311758`
  (`buddy.yml`) and `269749708` (`release-buddy.yml`) are
  `disabled_manually`; all legacy nonterminal execution counts are zero; both
  files are absent from `main`; and complete dispatch requests against a real
  old branch are rejected with HTTP 422 because the workflows are disabled.
  Transition runs `32693641797` and `32693679161` completed without publication:
  the first had zero jobs, and the second executed only the read-only
  default-branch refusal stub.
- Destination acceptance was separately authorized. PR #573 merged protected
  finalization as `d36e5a68`, pinning the one-time workflow to implementation
  merge `5a84bebd`. Attempt-1 run `32769435970` passed fixed-input review,
  observed absent pre-state, started the mutation, and observed exact post-state
  for version `0.0.0-wdv3-acceptance.1`, but retained an incomplete mutation
  classification. Terminal Governance evidence failed admission because the
  response claimed pre-request failure while the canonical action facts
  recorded mutation startedness. The second probe never ran. Governance
  immediately converted the workflow to `disabled_manually` and removed the
  acceptance Environment. Protected cleanup must remove the workflow file and
  verify the workflow identity, any temporary bypass, and the Environment
  absent before reconciliation. The consumed run, review, and coordinate must
  not be reused, and no retry is currently authorized. Acceptance does not
  authorize normal Live activation; `live_enabled` remains false.
- Requirements, HLD, and all five MLDs are confirmed.
- Implementation commits 1 through 11 of the approved first-slice LLD are
  delivered. Commit 10 was pushed at `e69675be`, and commit 11 retired the
  legacy Buddy entry workflows at `f0f81d52`. The branch then integrated
  current `main` at `e6482727`, repaired live qualification authority and
  runtime boundaries at `2ddeeed1`, and committed durable unsuccessful
  Qualification Attempt Outcomes at `1e742b29`. The historical pre-cleanup
  implementation and validation closure ended at `4fac140d`. It follows the
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
  because later bases contain that workflow. Before Phase 1 scope cleanup, the
  14-file committed range from the published baseline through `f0535989`
  passed the managed gates with 3,234 v3 tests and 1,257 workflow-release
  control tests. The inherited workflow-release suite is not retained, so
  those counts are historical rather than current repair evidence. All three
  original policy, CLI, and workflow reviewers report no findings after
  independent adjudication and repair. The branch backs
  [PR #552](https://github.com/hcoona/three/pull/552), which merged as
  `5a84bebd` on 2026-08-24. Documentation closure is committed at `a9e8cbfa`.
  Non-rewriting
  merge commit `30b793be` then integrates the latest `origin/main` at
  `7f8f41c2`, containing only the upstream Biome 2.5.9 and Asciidoctor 4.0.10
  dependency updates; the frozen PNPM and UV lock checks pass.
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
- At the pre-cleanup reviewed head, `GIT_LFS_SKIP_SMUDGE=1` Workflow Delivery
  v3 package validation passed 3,234 tests and the now-removed workflow-release
  control suite passed 1,257 tests. The complete workspace gate and
  authoritative 573-file
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
- The v2 `three.release.yml` descriptors, workflows, and control projects are
  not retained in this tree. Their immutable archive commit is mechanism
  reference material only. The v3 slice defines its own contracts and does not
  inherit the v2 profile model.
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
- Phase 8 review repair commit
  `adb70177d2edf74a6ca1b2079121cd1cfa0913d3` closes RC-039 by making the
  ignored generated Node distribution optional only in the isolated source
  fixture, RC-002 by removing generated `.testagent/plan.md` and timestamp
  coupling, RC-005 by returning implementation mechanisms to the design layers
  while preserving observable fail-closed requirements, and RC-006 by
  replacing obsolete pre-LLD steps with the actual review, merge, cutover,
  acceptance, and activation boundaries.
- Final evidence gathering did not treat a green dedicated v3 workflow as
  closure. General CI exposed five independently adjudicated prerequisite
  defects in successive published candidates: floating Node/npm at
  `adb70177` in run
  [`32641986429`](https://github.com/hcoona/three/actions/runs/32641986429),
  non-idempotent cached Mise links at `635ce5cb` in run
  [`32644327603`](https://github.com/hcoona/three/actions/runs/32644327603),
  PNPM fixture path-depth coupling at `5feaacad` in run
  [`32646068126`](https://github.com/hcoona/three/actions/runs/32646068126),
  missing Node/PNPM prerequisites in the Python 3.14 workspace job at
  `e06dcfcc` in run
  [`32648886474`](https://github.com/hcoona/three/actions/runs/32648886474),
  and missing locked Mise/HK provisioning at `3eb4ee35` in run
  [`32651937348`](https://github.com/hcoona/three/actions/runs/32651937348).
  Each repair was narrow, independently re-reviewed, normally published, and
  retained as superseded evidence rather than rewritten.
- Non-rewriting integration commit
  `f594db2ee85dd398255f1bc16b5f3d1d40544bc6` incorporates the then-current
  `origin/main` commit `62ffb59bcfbe7845e580d7aea5337afafc88bdf8`.
  The prior RC-001 behavior boundary was commit
  `e9d812b28f940ba64e83478d950f266077876859`, tree
  `b36b79262260b0e76e494198e5c63dbe74e16c4a`, with tested merge
  `34dc8fe660c82a28de44dee19e519287aa321581`. General CI run
  [`32655841248`](https://github.com/hcoona/three/actions/runs/32655841248),
  CodeQL run
  [`32655841242`](https://github.com/hcoona/three/actions/runs/32655841242),
  and dedicated v3 run
  [`32655841197`](https://github.com/hcoona/three/actions/runs/32655841197)
  all passed on that exact head and attempt 1. That immutable evidence remains
  below as a superseded ledger.
- PR-comment repair commits `83287129`, `bcd84a55`, and `9f97ef09` close the
  ten adjudicated true positives plus two later holistic test-integration
  gaps. Four original comments were adjudicated false positives with executable
  or type-contract evidence. A later comment claiming that
  `PurePosixPath.full_match()` is unavailable was independently adjudicated as
  a fifth false positive: the package requires Python 3.13 or later, the
  dedicated workflow runs Python 3.13, and Python 3.13.12 executes the required
  whole-path recursive match. Fresh same-scope rereviews and the complete
  holistic rereview report no findings. The prior RC-001 evidence reviewer
  could not be resumed; a fresh independent evidence and documentation reviewer
  reports no findings. Every PR review thread is resolved.
- The current final behavior boundary is commit
  `9f97ef091e8a831f73d81fe91b441aa6ee0520c3`, tree
  `69bec461fcb1047e7beb2ce13a9e9192e5cdf056`, with exact base
  `62ffb59bcfbe7845e580d7aea5337afafc88bdf8` and tested merge
  `59ad1ef2bd9277dc6cc35f897d8230dcf807ecdb`. General CI run
  [`32669623270`](https://github.com/hcoona/three/actions/runs/32669623270),
  CodeQL run
  [`32669623284`](https://github.com/hcoona/three/actions/runs/32669623284),
  and dedicated v3 run
  [`32669623261`](https://github.com/hcoona/three/actions/runs/32669623261)
  all passed on that exact head and attempt 1.
- Pre-cleanup local evidence included the complete Python 3.14 workspace with
  3,790 passing tests, the complete v3 suite with 3,638 passing tests, root
  PNPM tests and builds, scoped Ruff and Ruff format, scoped Pyrefly over every
  changed Python file, Biome, Actionlint, Shellcheck, shfmt, Markdown checks,
  diff checks, full all-profile HK, and the staged pre-commit gate. The broader
  configured-project Pyrefly probe still reports 16 unrelated pre-existing
  diagnostics and is not represented as a green full gate.
- Cleanup-tree evidence includes the complete v3 suite with 3,533 tests
  passing, including 408 isolated consumer-policy cases after clearing the
  exhausted shared `/tmp` inode pool, plus the complete staged HK gate. An
  earlier nonstandard workspace `TMPDIR` run produced 12 PNPM fixture-path
  failures and is not represented as product evidence.
- Prior local evidence included 63 isolated real-HK integration/scenario
  cases under a Mise root containing only locked HK 1.53.0, the complete Python
  3.14 workspace with 3,782 passing tests, the complete v3 suite with 3,632
  passing tests, Ruff, Ruff format, Pyrefly, Actionlint, diff checks, full HK,
  and the staged pre-commit gate.
- RC-001 current closure records the exact final behavior identity and
  immutable evidence in a strict documentation-only child of `9f97ef09`. The
  child names the behavior commit and tree but not itself. Any behavior change
  or new base integration invalidates this evidence and restarts the cycle.
  Checks on the documentation child are external evidence and must not be
  recursively added to that child.
- PR [#552](https://github.com/hcoona/three/pull/552) merged as `5a84bebd`.
  The immediate legacy drain and old-ref rejection proof are complete.
  Acceptance run `32769435970` failed with incomplete mutation evidence after
  exact `.1` post-state observation. The workflow is `disabled_manually`, the
  Environment is absent, and workflow-file cleanup must merge and be followed
  by API verification that the workflow identity, bypass, and Environment are
  absent before reconciliation. Do not retry with the same invocation, review,
  or coordinate; no retry is currently authorized. Do not activate normal Live
  or begin later scopes without a separate explicit user task.
- Implementation must preserve the approved commit boundaries and keep live
  activation disabled until acceptance and the separate activation approval.

The retained superseded v3 artifact inventory for run `32655841197`, attempt
1, is:

| Artifact             |           ID |  Bytes | SHA-256                                                            |
| -------------------- | -----------: | -----: | ------------------------------------------------------------------ |
| Request              | `9497411391` | 20,553 | `7b53e760ddd1e212f95eb4840fe771847d9aeccbc829b5fb384650260962a06d` |
| Provider             | `9497430708` |  3,156 | `e5a5b95659630d807df113f40ae97d0490369d131e2208f1d0c63f65ca636489` |
| Plan                 | `9497437095` | 30,386 | `6b207ea6d6edd046bd77efe41a00a4fd1fb7db84167055efdd7a295bca07abfa` |
| `project-test` lane  | `9497442930` |    887 | `271698c8beef2f05c6dcaad248a821b6a788ab2f132a3e9b14eb5e04deba328f` |
| `project-build` lane | `9497443148` |    889 | `de77e953abf5cf9b3774338e96cdf708aeccba7c6aee97bdfc768e1aefc2ecb5` |
| `npm` lane           | `9497443195` |    899 | `6164326cb3028e8e157a0ddb614490b43282b18f3ff617381002414ddfee0705` |
| `root-HK` lane       | `9497445406` |    877 | `f7bb765fcf4d6afbcb7b4aef815a675126ec8665d37ec32b222789843c2b6536` |
| Decision             | `9497449966` | 58,917 | `04a44f17c903b55542463ae830484f31c1e110f97c20a3048ba6dd151425020f` |
| Summary              | `9497450107` | 27,549 | `338b81882105d603ff61deba4aaedd34294d0d6122ec67cdb26817dd0e3f6b58` |

All nine downloaded archives match GitHub's byte counts and SHA-256 digests,
and every payload passes canonical admission. Authenticated exact-identity
replay at the recorded 193-second clock reproduces the remote Decision and
Summary byte-for-byte, exits `1`, and preserves the expected
non-authoritative `failure` /
`incomplete-model-plan` /
`fix-model-plan-and-rerun` result with 292 changed paths, 79 exclusively
unclassified-path diagnostics, four empty lane results, and no admitted
Evidence or artifact digests. The exact bootstrap projection exits `0` while
explicitly retaining that canonical failure. Supersession is
`not-superseded`, and the broad-change PR is excluded from the SLO.

The superseded General CI run `32655841248` retained three unrelated
AzureAuth build artifacts:

| Artifact                                                     |           ID |   Bytes | SHA-256                                                            |
| ------------------------------------------------------------ | -----------: | ------: | ------------------------------------------------------------------ |
| `azureauth-credprovider-foundation-internal-Windows-win-x64` | `9497595744` | 830,976 | `48631938c88290c8bf6c07412a4b029f8bba812e32af6ae1b6b7c443194b9348` |
| `azureauth-credprovider-foundation-internal-Linux-linux-x64` | `9497592310` | 829,562 | `6ba23573dbc9daaa63ba73badd1918b7885e0e4cb90e5b1fad0741224b9059eb` |
| `azureauth-credprovider-foundation-internal-macOS-osx-x64`   | `9497590302` | 829,575 | `5e3409158861c22e803882cb35d8b7bb57c23499b21929391c505a2d2ab887bf` |

CodeQL run `32655841242` retained zero GitHub Actions artifacts.

The retained current v3 artifact inventory for run `32669623261`, attempt 1,
is:

| Artifact             |           ID |  Bytes | SHA-256                                                            |
| -------------------- | -----------: | -----: | ------------------------------------------------------------------ |
| Request              | `9500994349` | 20,759 | `5c477df8015c37b3c3264148a18f27059350f22e13b22a9ddd93fc06a2e513c9` |
| Provider             | `9501006339` |  3,156 | `5d844562c21896c54c72b57014739b92a3a95e3170f95543d6d9b0e543c5c03d` |
| Plan                 | `9501016405` | 30,616 | `9c5f3123eab1b679608934eece126c286dfc429ab0cab3f32b062ac71d643ad4` |
| `project-test` lane  | `9501026242` |    887 | `0b06b9626ae2d5c78e1a085f9314bac5975a5cacf2a0fdb31a9c28ee67168bd8` |
| `project-build` lane | `9501026706` |    889 | `7032b253c8624fcb7d8fe623b3f7efaf461bcc9be6ad7a141ee8138c71a01ada` |
| `npm` lane           | `9501024069` |    899 | `d21ae1ccebe61bc48bf1ff4323feebb94ebd02a865252a55c086469f4cdf8396` |
| `root-HK` lane       | `9501025800` |    877 | `d0759826723e1e9624730535ae3e8e4813355a82244f13a94eed9a2f224b167e` |
| Decision             | `9501031958` | 59,373 | `372be7fff99f7a72f2d5fa722d90aac8e725ac0cbd807dd501e9df5ae54070ba` |
| Summary              | `9501032181` | 27,775 | `74fbe29ebba16363e2d4ae3e4978ae2a0e556d63094faeaef5e77e8b8ce81888` |

All nine downloaded raw payloads match GitHub's byte counts and SHA-256
digests and pass canonical admission. Authenticated exact-identity replay at
the recorded 171-second clock reproduces the remote Decision and Summary
byte-for-byte, exits `1`, and preserves the expected non-authoritative
`failure` / `incomplete-model-plan` / `fix-model-plan-and-rerun` result with
295 changed paths, 78 exclusively unclassified-path diagnostics, four empty
lane results, and no admitted Evidence or artifact digests. The exact
bootstrap projection exits `0` while explicitly retaining that canonical
failure. Supersession is `not-superseded`, and the broad-change PR is excluded
from the SLO.

General CI run `32669623270` retained three unrelated AzureAuth build artifacts:

| Artifact                                                     |           ID |   Bytes | SHA-256                                                            |
| ------------------------------------------------------------ | -----------: | ------: | ------------------------------------------------------------------ |
| `azureauth-credprovider-foundation-internal-Windows-win-x64` | `9501199320` | 830,990 | `a9f65c3acc8f3bbdf229710567ccb1793e0ce1c9dc13e43acf5bf165e604e5d6` |
| `azureauth-credprovider-foundation-internal-Linux-linux-x64` | `9501202559` | 829,578 | `d18127ddab8f0de4d791732d6627faa394d5f29b842c56dbca4aa4fd4509ae42` |
| `azureauth-credprovider-foundation-internal-macOS-osx-x64`   | `9501197154` | 829,585 | `b061b9f7a535aea215bbc45b11cdf017a0a3167fe74778a40d6a40369f4545e0` |

CodeQL run `32669623284` retained zero GitHub Actions artifacts.

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
