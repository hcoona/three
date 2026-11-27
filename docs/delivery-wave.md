# Current Delivery Wave

Current bounded work grants accepted on `main`. See
[work authorization](governance/record-system.md#work-authorization-and-concurrency)
for when a grant is needed and how it ends.

## Authorized Advancements

### Run and repair the LiteAgensh 32-peer user audit

- **Work carrier:** [Issue #1552](https://github.com/hcoona/three/issues/1552)
  and its linked protocol, repair, and outcome pull requests, retaining the
  accepted equal-peer single-machine boundary from closed
  [Issue #1542](https://github.com/hcoona/three/issues/1542) and
  [PR #1530](https://github.com/hcoona/three/pull/1530).
- **Accepted inputs:** the repository governance, record, and
  [engineering principles](engineering/engineering-principles.md) authorities,
  and LiteAgensh's [portal](../src/private/app/lite-agensh/README.md),
  [requirements](../src/private/app/lite-agensh/docs/requirements.md),
  [architecture](../src/private/app/lite-agensh/docs/architecture.md),
  [implementation contracts](../src/private/app/lite-agensh/docs/implementation-contracts.md),
  [migration map](../src/private/app/lite-agensh/docs/upstream-migration.md),
  [validation basis](../src/private/app/lite-agensh/docs/validation.md), and
  [user-audit protocol](../src/private/app/lite-agensh/docs/user-audit-protocol.md).
- **Advancement and outcome:** execute the read-only migration review with 32
  equal peers and one shared Chinese goal in a foreground `psmux` coordinator,
  diagnose retained failures, and apply bounded, reviewed source/record repairs
  within LiteAgensh's accepted single-machine peer-cooperation model (no
  permanent worker role assignment or central intelligent scheduler) until the
  real user journey succeeds—stabilizing first on small-peer Luna runs (startup,
  peer cooperation, post-quiescence report convergence, and complete
  process/usage settlement) before advancing to GPT-6.1 Terra, and delivering
  substantive source-backed findings with a Chinese final summary in team state
  and owned private output.
- **Permitted external and paid effects:** paid use of the existing Windows
  Copilot SDK and installed Copilot CLI (without CLI version gates; record
  observed CLI version and verify required interfaces before dependent native
  work; disabled runtime acquisition and auto-updates) using Luna for
  stabilization and GPT-6.1 Terra thereafter via exact native identifiers
  without fallback or provider substitution; owned worktree/state/process
  creation, worker-bound local MCP cooperation, ordinary dependency preparation
  per [user-audit protocol](../src/private/app/lite-agensh/docs/user-audit-protocol.md#dependency-preparation),
  read-only public upstream inspection, private audit output, and owned
  process/aggregate host memory counter inspection. Ceilings: `32` workers,
  `1,800`-second per-run deadline, `20,000` AI Credits per initialized attempt,
  and `100,000` AI Credits cumulatively across all trials (counting calibration,
  failed attempts, repetitions, and diagnostics) in the single authorized
  forward-only ledger while preserving the prior ledger and its unknown
  reservations unchanged; apply the accepted
  [verified pre-dispatch cancellation](../src/private/app/lite-agensh/docs/implementation-contracts.md#verified-pre-dispatch-cancellation)
  (`actual08 worker28` per
  [protocol](../src/private/app/lite-agensh/docs/user-audit-protocol.md#verified-pre-dispatch-accounting)
  plus qualifying fresh sessions) and
  [SDK-native recovery boundary](../src/private/app/lite-agensh/docs/implementation-contracts.md#sdk-native-recovery-boundary)
  only after their owning records and implementation are accepted on `main`.
- **Exclusions and closure:** workers must not edit protected repository/workflow
  inputs, commit/push, mutate GitHub, invoke nested agents/models, or inspect
  unrelated host/credential/account/session data; no reopening the former
  development campaign, general 32-worker support claim, provider substitution,
  authentication/access/host-setting change, release/publication dispatch, or
  second ledger reset. Retain applicable independent reviews, `GOV-011`
  disposition, and `Validate` CI; close by merged Wave deletion after owner
  acceptance or explicit stop.

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

### Repair Workflow Delivery dependency cohorts and SDK host compatibility

- **Work carrier:** [Issue #1679](https://github.com/hcoona/three/issues/1679)
  and its linked repair pull requests (including
  [#1658](https://github.com/hcoona/three/pull/1658) and
  [#1652](https://github.com/hcoona/three/pull/1652)).
- **Accepted inputs:** [engineering principles](engineering/engineering-principles.md),
  root `Directory.Packages.props`, `global.json`, and `renovate.json`, and
  Workflow Delivery's [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [.NET integration](../src/private/app/workflow-delivery/docs/integration/dotnet.md),
  [NuGet publisher source basis](../src/private/app/workflow-delivery/docs/research/nuget-native-publisher.md),
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
- **Advancement and outcome:** reconcile the seven directly referenced official
  NuGet clients as a coherent version-8 cohort (CPM, locks, Renovate grouping,
  and owning source/selection records) and repair declared SDK 11 compilation
  and mixed-endpoint task execution without coupling the controller's target
  runtime to whichever SDK supplies native MSBuild, keeping tool selection
  derived from committed configuration while preserving product target
  frameworks, native MSBuild responsibilities, publisher behavior, and
  transport/trust boundaries.
- **Exclusions and closure:** no campaign replay, new proving generation,
  release/proving dispatch, publication, credential/permission/access change, repository-wide TFM migration, or new runtime matrix. Qualify on actual source with applicable
  independent reviews, `GOV-011` disposition, and ordinary checks; delete this
  entry when the bounded repair is accepted.

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
