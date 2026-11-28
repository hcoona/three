# Current Delivery Wave

Current bounded work grants accepted on `main`. See
[work authorization](governance/record-system.md#work-authorization-and-concurrency)
for when a grant is needed and how it ends.

## Authorized Advancements

### Remodel LiteAgensh coordination kernel from TLA+ specifications

- **Work carrier:** [Issue #1943](https://github.com/hcoona/three/issues/1943)
  and its linked authorization and stacked specification/implementation pull
  requests (superseding the suspended
  [Issue #1552](https://github.com/hcoona/three/issues/1552) audit grant until
  the remodeled kernel and a refreshed audit protocol are accepted).
- **Accepted inputs and scope/outcome:** Under
  [governance](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md), and
  LiteAgensh's [portal](../src/private/app/lite-agensh/README.md),
  [requirements](../src/private/app/lite-agensh/docs/requirements.md),
  [architecture](../src/private/app/lite-agensh/docs/architecture.md),
  [implementation contracts](../src/private/app/lite-agensh/docs/implementation-contracts.md),
  [migration map](../src/private/app/lite-agensh/docs/upstream-migration.md),
  and [validation basis](../src/private/app/lite-agensh/docs/validation.md):
  (1) specify and `TLC`-verify LiteAgensh's concurrent kernel in TLA+
  (`src/private/app/lite-agensh/specs/`) across per-worker event dispatch and
  tool-boundary notice piggybacking, optimistic Git worktree CAS integration,
  and single-coordinator run/cohort lifecycle; (2) contract LiteAgensh's CLI,
  configuration schema, and owning records onto equal-peer core primitives
  (`Tasks`, `Messages`, `Typed Shared Board`, and `Git Worktree CAS Integration`
  with configured checks and optional peer/protected-path review) plus per-run
  wall-clock and native SDK soft-credit guardrails—retiring `campaignLedgerPath`,
  `calibrate`, `reconcile`, pre-dispatch cancellation proofs, compaction-fenced
  credit windows, SQLite contention sidecars, `planningSeconds`, `HealthGate`,
  and superseded operational protocols; and (3) rewrite the C# runtime and
  controlled scenario/invariant suite from the verified model across
  self-consistent stacked pull requests.
- **Exclusions and closure:** No paid native model calls, live audit execution
  under [Issue #1552](https://github.com/hcoona/three/issues/1552), reopening or
  deletion of retained historical ledgers, new external dependencies, central
  task schedulers, alternate agent backends, or release/credential/access
  changes. Merge this authorization before substantive contract or code pull
  requests; retain applicable independent reviews, `GOV-011` disposition,
  `records:check`, HK conformance, and `Validate` CI, and delete this entry
  atomically in the final remodel pull request.

### Reconcile Workflow V1, V2 and V3 documentation

- **Work carrier:** [Issue #1406](https://github.com/hcoona/three/issues/1406)
  and its linked documentation pull requests.
- **Accepted inputs:** repository governance/record policies,
  [documentation portal](README.md), and Workflow Delivery's
  [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md), and
  [historical readers](../src/private/app/workflow-delivery/docs/validation/historical-readers.md).
- **Advancement and outcome:** audit and reconcile Workflow V1, V2, and V3
  documentation and directly affected routing bindings against current
  implementation and accepted authorities; delete superseded records atomically
  after migrating consumers, links, and unique current rationale/evidence, using
  immutable Git references for historical bodies while preserving native
  findings, rechecks, provenance, evidence limits, requirement IDs, legal
  notices, and human-source inputs.
- **Exclusions and closure:** no runtime, dependency, workflow-execution, or
  product-behavior change; no new empirical experiments, support claims,
  release/proving dispatch, publication, or credential/Environment/access
  change; closed-campaign limits remain unchanged. Retain applicable independent
  reviews and `GOV-011` disposition; close only on an explicit further
  repository-owner request.

### Prepare the Release Plan 2A public NuGet cohort

- **Work carrier:** [Issue #1978](https://github.com/hcoona/three/issues/1978) and
  linked readiness pull requests, retaining [Issue #1679](https://github.com/hcoona/three/issues/1679).
- **Accepted inputs:** repository governance and
  [engineering principles](engineering/engineering-principles.md), root
  `Directory.Packages.props`, `global.json`, and `renovate.json`, the
  [release triage policy](engineering/public-release-triage.md), Workflow
  Delivery's [.NET](../src/private/app/workflow-delivery/docs/integration/dotnet.md)
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md)
  records, plus unit-owned records linked from #1978.
- **Advancement and outcome:** prepare the nine admitted NuGet libraries: finish
  the scoped outstanding behavior/API repairs and shared target/build work,
  reconcile the seven official NuGet clients as a version-8 cohort, and repair
  SDK 11 compilation/mixed-endpoint execution without coupling the controller's
  target runtime to native MSBuild. Add embedded symbols, bounded release notes,
  metadata/license payloads, consumer presentation, qualification, and a
  credential-free coordinated dry run. Limit Nanopass to the accepted completed
  implementation's package, presentation, and qualification.
- **Permitted external and paid effects:** routine credential-free local/CI only.
- **Exclusions and closure:** no proving, publication dispatch, credential/access
  change, artifact promotion, architecture rewrite, new runtime matrix or release
  unit, or Nanopass implementation/API expansion. Delete after source, package,
  presentation, qualification, and dry-run acceptance; version transition and
  Official dispatch remain owner directed. Retain reviews, `GOV-011`, and `Validate`.

### Design and plan sheet-music recognition

- **Work carrier:** [Issue #686](https://github.com/hcoona/three/issues/686),
  its sub-issues, and their linked design/planning pull requests.
- **Accepted inputs:** repository governance, record, and
  [engineering principles](engineering/engineering-principles.md) authorities,
  [documentation portal](README.md), and accepted records under
  `src/private/app/sheet-music-omr/` (findings in #686 and its sub-issues remain
  unreviewed inputs until checked under `research-evidence-review`).
- **Advancement and permitted effects:** refine the design, workstream
  decomposition, and admitted project records under
  `src/private/app/sheet-music-omr/` (product language and packaging remain
  undecided) for recognizing photographed/scanned piano scores into editable
  notation; permit read-only local inspection of the owner's private
  transcription repository and desk-study archive (model-service processing of
  agent-read content is accepted by the owner, while public records/Issues/PRs
  exclude private paths, piece identities, and score content in favor of
  anonymized batch numbers and aggregate counts) plus Issue/sub-issue creation
  and editing.
- **Exclusions and closure:** no new workstream implementation, runtime
  experiment, external tool/package installation, dataset/model download,
  training, inference, paid compute, external score upload, private-repository
  mutation, or publication/credential/access change without its own accepted
  Wave entry. Close only on an explicit further repository-owner request.

### Inspect four TRAIN_DEV fragments for Text Score 2 representation gaps

- **Carrier and outcome:** [#686](https://github.com/hcoona/three/issues/686)
  and its linked pilot PR. Check whether a determined printed distinction is
  missing, ambiguous in the specification, or lost in transcription or tooling.
  Report observations per fragment privately, not an estimated coverage rate.
- **Starting state:** owner-supplied, read-only `TRAIN_DEV` staging only. Check
  the manifest against the accepted inventory digest and selected files against
  their manifest hashes before use. Apply [OMR privacy](../src/private/app/sheet-music-omr/docs/requirements.md#omr-req-015-privacy-and-fixture-provenance).
- **Bounds:** one lead-agent desk review, at most four source pages, four
  fragments and four corresponding accepted gold files cumulatively, including
  failed selections; about 50,000 tokens is the first stop. Select pages by
  anonymous ordinal before reading gold; choose two ordinary and two
  notation-dense fragments from those pages where available. Record what the
  ink requires before opening gold, then compare with its Text Score 2 rendering.
  Reproduce suspected format gaps only with independently invented examples.
- **Effects and closure:** allow owner-approved model-service viewing in this
  session only; no subagents receiving private material, separate uploads,
  private-input mutation, W6 runner/ledger use, training, GPU or paid jobs,
  or `TEST` reads. Use owner-only temporary storage; remove derived crops after
  review. Stop on access, privacy, digest or scope mismatch, or at the cap.
  Keep all fragment-level observations private. Public reporting is aggregate
  only, without score content or identities. Retire on delivery or owner stop;
  expansion needs separate authorization. Other grants and limits are unchanged.

### Introduce native Alloy tooling and model Text Score 2 assembly

- **Work carrier:** [Issue #1991](https://github.com/hcoona/three/issues/1991)
  and its linked PRs; the first model serves the
  [sheet-music design work](#design-and-plan-sheet-music-recognition) in #686.
- **Scope and outcome:** follow the [native TLA+ toolchain](engineering/workspaces.md#tla)
  and [HK execution boundary](engineering/hk-execution.md#ci-execution-contract):
  use the upstream Alloy Analyzer for fast syntax and type checking in HK;
  run bounded model checks explicitly outside hooks. Model the
  [Text Score 2 assembly contract](../src/private/app/sheet-music-omr/docs/text-format.md#assembly)
  on invented scores, comparing legal cuts with the unsplit event and relation
  graph. Include a multi-voice cross-staff witness and a swapped-tie-endpoint
  counterexample; state the bounds without claiming Python or MusicXML correctness.
- **Permitted effects:** provision one official Alloy 6 release JAR, locked by
  SHA-256 through `mise` on `linux-x64` and `windows-x64`, using the existing
  Java runtime and bundled solver. Only routine local and CI checks are in scope.
- **Exclusions and closure:** no external solver service, private scores, OMR
  application changes, training, paid runs, release or access changes. Preserve
  the existing OMR grants and their limits. Remove this entry with the accepted
  tooling and bounded model checks, or on an explicit owner stop.

### Execute OMR GPU training and evaluation on local RTX 4090 (W6 Slice B)

- **Carrier and scope:** [#1498](https://github.com/hcoona/three/issues/1498)
  and [#1491](https://github.com/hcoona/three/issues/1491) (#1494–#1496), under
  [#686 design and privacy bounds](#design-and-plan-sheet-music-recognition).
  Implement and evaluate W6 Slice B only: portable Slice A handoff and related
  harness simplifications, Arm A/B and two-class detector pilot on the owner's local WSL2 RTX 4090 (24 GB).
- **Before any campaign effect:** accept the Slice B protocol and hash-locked
  CUDA wheel and checkpoint manifests on `main` under record-system and
  research-evidence review and shared engineering guidance. Use only the
  [approved assets and inputs](../src/private/app/sheet-music-omr/docs/training-protocol.md#environment-isolated-provisioning-and-mutable-source-rechecks);
  protocol edits cannot expand this grant; digest drift stops execution until a license/interface recheck is accepted on `main`.
  This covers cross-host import, downloads, rendering and private/GPU runs;
  offline non-private fixture checks remain free of campaign quotas.
- **Permitted effects:** import the seven prior ledgers, packaged crops and isolated `TRAIN_DEV`
  staging read-only; staged public archives and cached `w2-copista-venv` / `w4-synth-venv`
  may also transfer read-only with SHA-256 verification, not fresh downloads.
  Verify input digests and use the private identity mapping only for report privacy scans.
  Provision only locked wheels and permissive model assets, with no remote code.
  Isolate every Linux private-input worker, including replay training, offline with `unshare -rn`.
  Run `E0`, training, four-fold
  selection, calibration and matched-step replay under the
  [Slice B protocol](../src/private/app/sheet-music-omr/docs/training-protocol.md#w6-gpu-slice-b-execution-replay-pre-registration-and-single-pass-test-protocol).
  Reserve at most 60 GB of download payload including failures, 40 GPU training starts
  (8 hours each) and 30 private `TRAIN_DEV` evaluation starts (300 s/page or system).
  Serialized soft caps: 150 GB generated disk and 5,000 diagnostic pages
  (at most 200 measures/job); only the in-flight job may overshoot. On exhaustion
  or overage stop dispatches consuming that budget and return to the owner.
- **Frozen `TEST`:** at most one preregistered offline `W6-pilot-final` pass after
  all `TRAIN_DEV` gates. Durably charge both run and touch ledgers before any
  `TEST` read (one of three pilot touches, beyond six spent S0 touches); failure
  does not refund it. Preserve all ledgers without reset. No `TEST` training or tuning.
- **Exclusions and closure:** no paid/cloud compute, external upload of private
  or synthetic score material, crops, checkpoints or diagnostics, private input mutation,
  unreviewed data or out-of-matrix models, full #1491 page scale-up, W7/W8 implementation,
  publication, release or credential/access changes. Committed fixtures must be self-authored, public-domain or CC0 with provenance.
  Merge the tested records/harnesses; publish independently reviewed, sanitized
  [Slice B results](../src/private/app/sheet-music-omr/docs/requirements.md#omr-req-031-slice-b-gpu-training-replay-and-single-test-pass)
  (zero-shot `E0` versus fine-tuned Arms A/B, scaling, detector/assembly, calibration/sidecars,
  replay and the final drafter and blind second-reader comparison to Stage S0)
  to #1498, #1491 (#1494–#1496) and #686, and update the project portal/handoff.
  Delete in the final acceptance PR or a Wave-only PR after owner acceptance or stop.
