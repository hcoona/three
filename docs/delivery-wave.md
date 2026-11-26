# Current Delivery Wave

This record is the sole positive work-authorization authority for feature or
capability advancements, architectural or public-contract changes, and
non-routine external or risk-bearing operations under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency);
contract-preserving routine maintenance and owner-governed release operations
defined there execute without a Wave entry. Only `main`'s accepted version
grants Wave authorization. Within an authorized workstream, routine local
editing, builds, lint, tests, scratch harnesses, read-only inspection,
independent review, and ordinary Issue/PR/CI validation (including failed-job
reruns) are permitted by default with no attempt quota, and implementation may
evolve concurrently with its owning records so long as each merge into `main`
stays self-consistent. Entries declare bounded scope and outcome, exclusions,
and any explicitly permitted external or risk-bearing effects. Merge changes
authorization; deletion ends a grant. Git and the proposing PR retain the reason
and history.

## Authorized Advancements

### Contract repository governance surface and retire defensive campaign scars

- **Work carrier:** [Issue #1896](https://github.com/hcoona/three/issues/1896)
  and its linked stacked root-governance and subproject pull requests.
- **Accepted inputs:** [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [checker contract](governance/checker-contract.md),
  [engineering principles](engineering/engineering-principles.md),
  `docs/governance/*.yaml`, `schemas/governance/*.schema.json`, `AGENTS.md`,
  `CONTRIBUTING.md`, `.github/skills/**`, `hk.pkl`, `mise.toml`,
  `eng/scripts/check_repository_records.py`,
  `src/private/app/workflow-delivery/Repository/CiScope.cs`, and the handoff and
  entry records of `workflow-delivery`, `sheet-music-omr`, and `lite-agensh`.
- **Advancement and outcome:** (1) retire `docs/governance/controls.yaml`,
  `schemas/governance/controls.schema.json`,
  `docs/governance/checker-contract.md`, `docs/AGENTS.md`, and
  `.github/skills/{record-system,research-evidence}-review/evals/*.md`;
  (2) replace per-project `record-families.yaml` enumeration and `families`
  metadata with concise wildcard path/namespace bindings (`schema_version: 4`),
  aligning `CiScope.cs` without hard-coding document lists in C#; (3) adopt
  `lychee` (`mise.toml`, `mise.lock`, `.lychee.toml`, `hk.pkl`,
  `hk-scope-copies.json`) for offline Markdown link and anchor checks and shrink
  `check_repository_records.py` (with `tests/eng/test_repository_records.py`) to
  an automated HK/CI record-binding and `REQ`/`FR`/`NFR`/`AC` identifier check;
  (4) consolidate `record-system.md` (narrowing Wave triggers so contract-safe
  internal refactoring, CI/tooling consolidation, dead-route retirement, and
  owner-merged governance/doc cleanup use ordinary PRs, fixing a thin Wave entry
  contract, and tightening grant closure), deduplicate `AGENTS.md`,
  `CONTRIBUTING.md`, and the review `SKILL.md` files, move `Workflow Delivery
v3` instructions to `src/private/app/workflow-delivery/AGENTS.md`, and trim
  `docs/engineering/{hk-execution,agent-execution}.md`; and (5) in separate
  per-subproject pull requests, retire closed-campaign defensive scar prose in
  `workflow-delivery`, `sheet-music-omr`, and `lite-agensh` handoff/entry docs
  while preserving change routing, active protocols, and historical links.
- **Ordering and gates:** merge this authorization before the newly proposed
  substantive work. Review the root governance amendment under `main`'s
  currently accepted policies and `record-system-review` (plus applicable
  `research-evidence-review` and code review), apply `GOV-011` finding
  disposition, and keep each merged PR self-consistent with passing `hk` and
  `Validate` CI.
- **Exclusions and closure:** no product runtime behavior change, new
  external/paid/credentialed execution, release/proving dispatch, publication,
  ruleset/Environment/permission change, or deletion of active protocols,
  human-source inputs, legal notices, or historical evidence links. Close this
  entry atomically in the final subproject cleanup PR (or a Wave-only closure
  PR).

### Unify Workflow Delivery release kernel and decouple release units

- **Work carrier:** [Issue #1899](https://github.com/hcoona/three/issues/1899)
  and its linked authorization and stacked implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  `.github/workflows/workflow-delivery-v3-*-release.yml`,
  `eng/scripts/release_triage.py`,
  `src/public/*/*/workflow-delivery.release-unit.yml`,
  `tests/private/app/workflow-delivery/fixtures/products/*/workflow-delivery.release-unit.yml`,
  and Workflow Delivery's [application interface](../src/private/app/workflow-delivery/README.md),
  [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [HLD](../src/private/app/workflow-delivery/docs/high-level-design.md),
  [MLD and scoped designs](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [integration contracts](../src/private/app/workflow-delivery/docs/integration/common.md),
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
- **Advancement and outcome:** (1) consolidate the six Release destination
  families (`NuGet`, `npm`, `Python`, `Ruby`, `GitHub Releases`, and Browser
  Stores) onto a unified port-adapter `ReleaseEngine` (`IDestinationAdapter`),
  unified stage transfer envelopes, and parameterized CLI routing while
  preserving every destination's trust, redirect, non-overwrite, settle, and
  `WD-REL-008` boundaries, backed by zero-dependency native C# explicit-state
  BFS model-checking tests across reachable multi-artifact, transport-drop,
  post-commit timeout, visibility-lag, contention, and recovery transitions;
  (2) evolve `workflow-delivery/v3/release-unit` (`workflow-delivery.release-unit.yml`)
  and dynamic repository discovery so release units self-declare channel
  eligibility and project-local consumer/payload probes while deriving package
  identity, target frameworks, and companion build dependencies from native
  manifests and project graphs, retiring hardcoded product registries
  (`*ReleaseUnits.cs` and Python `PRODUCTS`), changing release workflow `unit`
  dispatch inputs from hardcoded `choice` lists to catalog-validated `string`
  inputs, and aligning `eng/scripts/release_triage.py`; and (3) contract
  duplicated product/path ownership rules across `CiScope.cs` and
  `NativePreparationQualification.cs` onto discovered release units and native
  graphs without modifying `record-families.yaml` bindings, backed by finite DAG
  partition-closure exhaustion tests, updating owning Workflow Delivery records
  atomically.
- **Ordering and gates:** merge this authorization before merging substantive
  implementation pull requests; implementation and owning records may evolve
  across stacked pull requests self-consistent at each merge into `main`. Retain
  applicable independent `engineering-guidance`, `record-system-review`, OCR and
  code review, `GOV-011` finding disposition, explicit-base `records:check`, HK
  conformance, and `Validate` CI.
- **Exclusions and closure:** no new external NuGet or tool dependencies,
  release or proving workflow dispatch, package/store/release publication,
  reopening of closed proving campaigns or smoke-fixture live operations,
  admitting new public release units or Official refs beyond the currently
  admitted set, editing `Platform/NativeProcess.cs` or
  `tests/private/app/workflow-delivery/Release/NuGetWorkflowCallerTests.cs` while
  owned by active sibling repair work, or ruleset/Environment/credential/access
  changes; TLA+ / `TLC` specification remains tracked separately under
  [Issue #1897](https://github.com/hcoona/three/issues/1897) (blocked by
  [Issue #1890](https://github.com/hcoona/three/issues/1890)). Close this entry
  atomically by deleting it in the final implementation pull request (or an
  explicit Wave-only closure pull request).

### Verify Hcoona.Nanopass formal specification and algebraic laws

- **Work carrier:** [Issue #1779](https://github.com/hcoona/three/issues/1779)
  and its linked authorization and implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  `Hcoona.Nanopass`'s accepted [package portal](../src/public/lib/Nanopass/README.md),
  [architecture](../src/public/lib/Nanopass/docs/architecture.md),
  [validation strategy](../src/public/lib/Nanopass/docs/validation.md), and
  [third-party notices](../src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt), and
  the public Nanopass Framework specification ([Keep & Dybvig, ICFP 2013](https://doi.org/10.1145/2500365.2500618),
  [`nanopass-framework-racket`](https://github.com/nanopass/nanopass-framework-racket),
  and [`nanopass-framework-scheme`](https://github.com/nanopass/nanopass-framework-scheme),
  MIT License).
- **Owner decisions and boundaries:**
    - Add `CsCheck` in `Directory.Packages.props` scoped to
      `tests/public/lib/Nanopass.Tests/Hcoona.Nanopass.Tests.csproj`
      (`Condition="'$(MSBuildProjectName)' == 'Hcoona.Nanopass.Tests'"`) and
      refresh affected NuGet lockfiles.
    - Author an independent, clean-room PEP 723 Python formal specification
      script (`src/public/lib/Nanopass/scripts/verify_formal_spec.py` with
      pinned `z3-solver`) directly from the upstream Nanopass specification
      without reading `Hcoona.Nanopass` implementation files during model
      construction, and register an on-demand `mise.toml` task (`uv run --script`)
      without adding a standalone `.github/workflows/*.yml` pipeline or daily
      Python CI suite.
    - Do not introduce TLA+, F\*, Lean, Rocq, or `FsCheck` / `FSharp.Core`
      dependencies, and do not expand `Hcoona.Nanopass`'s public API surface
      beyond contract-preserving fixes for discrepancies uncovered by
      differential verification.
- **Advancement and outcome:**
    - Formalize and check mathematical self-consistency of the core Nanopass
      relational and inductive invariants in Z3 (incremental grammar algebra and
      entry-reachability pruning fixed point, nonterminal subtype inclusion
      shortest-path uniqueness versus `NP1003` ambiguity, processor and clause
      conditional auto-synthesis fixed point versus `NP1001`/`NP1002`, and
      4-phase S-expression dispatch priority), execute a model-driven
      differential audit against `Hcoona.Nanopass`, and remediate any uncovered
      semantic or diagnostic defects.
    - Add a deterministic, shrinking-enabled `CsCheck` algebraic property test
      suite in `tests/public/lib/Nanopass.Tests/` covering the Identity Pass
      Law, Metadata and S-Expression Round-Trip Law, and Analyzer–Compiler
      Soundness Alignment Law (zero leaked `CSxxxx` compiler errors when
      `NPxxxx` diagnostics are absent), and update
      `src/public/lib/Nanopass/docs/architecture.md`,
      `src/public/lib/Nanopass/docs/validation.md`, and `mise.toml` atomically.
- **Ordering and gates:** merge this authorization before substantive
  implementation. Keep implementation, dependency manifests and lockfiles,
  `mise.toml`, and owning Nanopass records self-consistent at each merge into
  `main` across ordinary or stacked pull requests. Retain applicable independent
  `engineering-guidance`, `record-system-review`, code review, `GOV-011` finding
  disposition, explicit-base `records:check`, HK conformance, and `Validate` CI.
  Material scope or semantic contract conflicts return to the repository owner.
- **Acceptance and closure:** the Z3 formal specification script, `CsCheck`
  algebraic property suite, and any differential-audit fixes pass explicit local
  verification, `records:check`, HK conformance, and `Validate` CI with no
  unresolved material findings after final review and contraction. Close this
  entry by deleting it in the final implementation pull request (or an explicit
  Wave-only closure pull request).
- **Exclusions:** no TLA+, F\*, Lean, Rocq, or `FSharp.Core` toolchains; no new
  GitHub Actions workflow files, package or release publication, rulesets,
  Environments, credentials, authentication, or access changes; no change to
  `CurrentTargetFramework` (`net10.0`) for unrelated projects.

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

- **Work carrier:** [Issue #1498](https://github.com/hcoona/three/issues/1498)
  (W6 recognizer and page-detector pilot) and
  [Issue #1491](https://github.com/hcoona/three/issues/1491) (W4 packaged
  system-crop and page-box milestones, with sub-issues
  [Issue #1494](https://github.com/hcoona/three/issues/1494),
  [Issue #1495](https://github.com/hcoona/three/issues/1495) and
  [Issue #1496](https://github.com/hcoona/three/issues/1496)), sub-issues of
  [Issue #686](https://github.com/hcoona/three/issues/686), and their linked
  pull requests. Following completion of CPU Slice A
  ([#1498](https://github.com/hcoona/three/issues/1498#issuecomment-7173082854),
  [#1498](https://github.com/hcoona/three/issues/1498#issuecomment-7185613521),
  [#1491](https://github.com/hcoona/three/issues/1491#issuecomment-7185614792)),
  the repository owner approved transitioning to the local RTX 4090 (`24 GB`)
  workstation to execute **W6 GPU Slice B**.
- **Accepted inputs:** those of the sheet-music design and planning entry, the
  accepted project records under `src/private/app/sheet-music-omr/`
  ([portal](../src/private/app/sheet-music-omr/README.md),
  [requirements](../src/private/app/sheet-music-omr/docs/requirements.md),
  [Text Score 2 format](../src/private/app/sheet-music-omr/docs/text-format.md),
  [comparison protocol](../src/private/app/sheet-music-omr/docs/comparison-protocol.md),
  [evaluation and real-data alignment protocol](../src/private/app/sheet-music-omr/docs/evaluation-protocol.md),
  [synthetic data and system-detection protocol](../src/private/app/sheet-music-omr/docs/synthetic-protocol.md),
  [training, bulk-packaging and page-detector protocol](../src/private/app/sheet-music-omr/docs/training-protocol.md)
  and [handoff](../src/private/app/sheet-music-omr/docs/agent-handoff.md)), and
  the reviewed W6 Slice A readiness, full-category supplement and per-system
  baseline reports recorded on #1498, #1491 and #686. Unreviewed findings in
  #686, #1491, #1494 through #1496 and #1498 through #1500 remain research
  inputs; any repository record that relies on one rechecks it under
  research-evidence review.
- **Owner decisions:**
    - Execute all GPU provisioning, `E0` zero-shot inference, Arm A / Arm B /
      page-detector training, `4`-fold `TRAIN_DEV` selection and calibration,
      matched-step local `TRAIN_DEV` replay ablation and the single final `TEST`
      evaluation strictly locally inside WSL2 on the repository owner's
      Windows/WSL2 physical workstation equipped with an NVIDIA GeForce RTX 4090
      (`24 GB` VRAM; `local_gpu_only`), with zero cloud-compute dispatch and
      zero upload of private or synthetic score material, crops, checkpoints or
      diagnostics to external services (`OMR-REQ-015`).
    - Support cross-host migration from the Slice A CPU workstation to the RTX
      4090 workstation by verifying immutable `SHA-256` digests of the `7`
      carried-forward campaign ledgers (`download-ledger.json`,
      `w2-download-ledger.json`, `w2-touch-log.jsonl`,
      `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
      `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`), the
      self-contained `w6-slice-a-package-formal-6/datasets/` tree (`w6-crops-20k`,
      `w6-crops-50k` and shared `assets/` PNGs), and the `16`-piece
      `TRAIN_DEV` starting-state directory (`102` leaves verified against
      committed `EXPECTED_TRAINDEV_PORTABLE_SHA256`), re-sealing (`0o500`
      directories, `0o400` leaves owned by `os.geteuid()`) and re-anchoring
      local `(st_dev, st_ino)` identities in Slice B's run ledger without
      scanning mixed `23`-piece directories or touching `TEST` during setup.
    - Treat installed host Windows `MuseScore 4.x` CLI versions and executable
      `SHA-256` digests as per-run provenance recorded in `RUN/environment.json`
      rather than gating execution on a committed `ACCEPTED_MUSESCORE_IDENTITIES`
      binary-hash allowlist: retire `ACCEPTED_MUSESCORE_IDENTITIES` blocking
      across W4/W6 while retaining `4.x` version verification, the `8` MusicXML
      preference checks (`importInferTextType=false`), automatic `1`-page
      `.mpos / 12.0 <= 2.0 px` pixel-scale calibration, mid-run executable and
      preference immutability checks, and per-sample round-trip gates, keeping
      public `permissive` score rendering on the host Windows `MuseScore 4.x`
      CLI under its local lock without host firewall or user-preference
      mutations while private-input and Linux worker subprocesses retain
      `unshare -rn` offline isolation.
    - Retain Slice A's strictly permissive (`Apache-2.0`, `MIT`, `BSD-3-Clause`)
      trainable model and weight matrix (`trust_remote_code=False`, zero remote
      `.py` fetches; keeping `AGPL`, `CC-BY-NC` and custom revenue-threshold
      model/code exclusions unchanged, while permitting standard out-of-worktree
      PyTorch CUDA and `nvidia-*` / `triton` runtime wheels in
      `DOWNLOADS/w6-gpu-venv` for local GPU execution without redistribution):
      run `E0` zero-shot `Text Score 2` baselines on the `69` `TRAIN_DEV` pages /
      `323` systems (`0` `TEST` touches) across the pinned permissive VLM
      foundation checkpoints (`Qwen/Qwen3-VL-2B-Instruct@89644892e4`,
      `Qwen/Qwen3.5-2B@15852e8c16`, `PaddlePaddle/PaddleOCR-VL-1.6@132c45f52c`)
      alongside Slice A's completed `copista-28m@44464d1` per-system crop
      baseline before comparing fine-tuned Arm A (`bf16` SFT/LoRA on
      `w6-crops-20k`, scaling the top candidate to `w6-crops-50k` and optionally
      testing `Qwen/Qwen3-VL-4B-Instruct@ebb281ec70` capacity), Arm B
      (`timm/convnext_tiny.fb_in22k_ft_in1k@bc48a87f11` `stride 16` vs.
      `stride 8+16` and `zenosai/MonkeyOCRv2-S@8efa4f856a` on `w6-crops-50k`)
      and the 2-class (`system`, `measure`) page detector
      (`ustc-community/dfine-small-coco@f79e65b5fb` at `L = 1024` vs. `1280` and
      `PekingU/rtdetr_v2_r18vd@5650961749` on `w6-crops-50k` `page-boxes.jsonl`).
    - Select the synthetic-only recognizer and page-detector finalists, fit
      nested out-of-fold temperature/Platt calibrators, and execute the
      matched-step local `TRAIN_DEV` replay ablation (`synthetic continuation`
      vs. `REAL_TRAIN + synth_train replay` with `open_reading_masks` loss
      masking) strictly across the pre-registered `4` plate-group-disjoint
      `TRAIN_DEV` folds (`0` `TEST` touches, labeled as cross-validated
      selection / conditional replay evidence), then execute **at most `1`**
      pre-registered `"W6-pilot-final"` evaluation pass on frozen `TEST` (`7`
      pieces, `111` systems, `432` measures, `23` pages), appending `1`
      immutable record to `DOWNLOADS/w2-touch-log.jsonl` (`1 / 3` cumulative
      `OMR-REQ-019` training-pilot `TEST` touches beyond the `6` preserved
      `S0-baseline` records).
- **Advancement and outcome:**
    - Amend the OMR synthetic and training protocols and `sheet_music_omr`
      harnesses to: (a) retire `ACCEPTED_MUSESCORE_IDENTITIES` binary-hash
      allowlist gating in favor of `MuseScore 4.x` interface/preference/pixel
      validation plus per-run provenance recording; and (b) commit the Slice B
      CUDA wheel and pinned permissive `.safetensors` checkpoint lock manifest,
      cross-host `SHA-256`-verified ledger/dataset/`--traindev-root` import and
      local `(st_dev, st_ino)` re-anchoring (`setup-w6-slice-b`), `E0`
      zero-shot runner, GPU training/evaluation/checkpoint runners for Arm A,
      Arm B and `D-FINE` / `RT-DETRv2`, `4`-fold calibration + matched-step
      `TRAIN_DEV` replay runners, and the single-pass `"W6-pilot-final"`
      frozen-`TEST` evaluation runner with persistent Slice B download, disk and
      run ledgers.
    - Provision `DOWNLOADS/w6-gpu-venv` and pinned permissive `.safetensors`
      foundation checkpoints on the local RTX 4090 (`24 GB`) WSL2 host, execute
      `E0` zero-shot `TRAIN_DEV` baselines, Arm A / Arm B / `D-FINE` &
      `RT-DETRv2` GPU training, `4`-fold `TRAIN_DEV` selection, nested
      calibration and matched-step `TRAIN_DEV` replay ablation (`0` `TEST`
      touches), and execute the single pre-registered `"W6-pilot-final"` frozen-
      `TEST` evaluation pass (`1` pilot touch in `w2-touch-log.jsonl`),
      publishing independently reviewed sanitized Slice B evidence reports and
      validated `omr-w7-sidecar-v1` / `omr-discrepancy-log-v1` handoff summaries
      on #1498, #1491 (#1494 through #1496) and #686.
- **Ordering and gates:** merge this entry before any Slice B protocol or
  implementation change, cross-host `setup-w6-slice-b` / private
  `--traindev-root` import or local `(st_dev, st_ino)` re-anchoring, CUDA wheel
  or foundation-checkpoint fetch, campaign diagnostic rendering, `E0` zero-shot
  inference, campaign GPU training, `TRAIN_DEV` replay or `"W6-pilot-final"`
  `TEST` execution. Keep project records and implementation self-consistent at
  each merge into `main`, and accept the Slice B protocol amendments and
  committed CUDA wheel + `.safetensors` checkpoint locks on `main` under
  record-system review, research-evidence review and shared engineering guidance
  before any cross-host `setup-w6-slice-b` / `--traindev-root` import or
  re-anchoring, any `DOWNLOADS/w6-gpu-venv` or `.safetensors` download, any
  campaign diagnostic synthetic rendering, any campaign GPU training or `E0`
  inference run, any `TRAIN_DEV` replay run, or the `"W6-pilot-final"` `TEST`
  pass (offline repository-fixture and synthetic-fixture unit/scenario/smoke
  checks with zero network fetches, zero staged-corpus batch rendering and zero
  private-input access remain unconstrained; formal campaign execution fails
  closed on any drifted wheel or model checkpoint digest until its license and
  interface mutable-source recheck is reviewed and accepted on `main`). Complete
  all `E0` baselines, synthetic-only `4`-fold `TRAIN_DEV` selection,
  matched-step `TRAIN_DEV` replay, out-of-fold production calibrator fitting,
  and pre-registration of the single `"W6-pilot-final"` model/detector/threshold
  configuration before reading or scoring frozen `TEST`. Apply `GOV-011` finding
  disposition. New product, scope or feasibility tradeoffs return to the owner.
- **Acceptance and closure:** the tested Slice B protocol amendments and GPU
  training/evaluation harnesses are merged on `main`, and independently reviewed
  sanitized Slice B reports (`E0` zero-shot vs. fine-tuned Arm A / Arm B
  results and `20k`->`50k` scaling, 2-class `D-FINE` / `RT-DETRv2` vs.
  `comb_dp_blind` page-detector metrics and downstream system assembly, nested
  out-of-fold calibration + `omr-w7-sidecar-v1` / `omr-discrepancy-log-v1`
  validation, matched-step `TRAIN_DEV` replay ablation, and the single
  `"W6-pilot-final"` frozen-`TEST` drafter and blind second-reader comparison
  against Stage S0 with `1 / 3` cumulative pilot `TEST` touches recorded) are
  published on #1498, #1491 (#1494 through #1496) and #686, with project
  portal/handoff records updated for W7 (#1499) and W8 (#1500). Close this entry
  by deleting it in the final PR that completes acceptance, or through an
  explicit Wave-only closure PR after owner acceptance or owner stop.
- **External effects:** permit local private-archive inspection and Issue
  effects as in #686 (including `formal-5/gold/mapping.json` read-only solely
  for `scan-public-report` `23`-identity `OMR-REQ-015` privacy checks);
  read-only cross-host import and `SHA-256` verification of preserved campaign
  ledgers (`download-ledger.json`, `w2-download-ledger.json`,
  `w2-touch-log.jsonl`, `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
  `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`),
  self-contained `w6-slice-a-package-formal-6/datasets/` (`w6-crops-20k`,
  `w6-crops-50k` and shared `assets/` PNGs), staged public archives if
  present, sealed `16`-piece `--traindev-root` (re-anchoring local `(st_dev,
st_ino)` after `102`-leaf `SHA-256` + `EXPECTED_TRAINDEV_PORTABLE_SHA256`
  verification once the Slice B protocol is accepted), and cached
  `w2-copista-venv` / `w4-synth-venv` when
  transferred; metered out-of-worktree installation into `DOWNLOADS/w6-gpu-venv`
  of locked CUDA PyTorch/vision/training wheels (from PyPI and PyTorch's
  official CUDA wheel index, including required `nvidia-*` / `triton` CUDA
  runtime wheels and locked transitive Python/native wheel dependencies) plus
  metered download into `DOWNLOADS/w6-model-weights/<slug>@<sha10>/` of pinned
  permissive `.safetensors` checkpoints and metadata for the approved W6 model
  matrix (`trust_remote_code=False`, zero remote `.py` files); local RTX 4090
  (`24 GB`) campaign CUDA training and offline `unshare -rn` `TRAIN_DEV`
  inference, calibration and replay runs; host Windows `MuseScore 4.x` CLI (on
  public `permissive` inputs only, recorded by version and `SHA-256`) and
  offline `Verovio` if bounded campaign diagnostic rendering is invoked; and **at
  most `1`** offline `unshare -rn` frozen-`TEST` evaluation pass
  (`purpose: "W6-pilot-final"`) writing and fsyncing **both** its pre-start
  `DOWNLOADS/w6-slice-b-run-ledger.jsonl` reservation **and** its immutable
  `DOWNLOADS/w2-touch-log.jsonl` record (`1 / 3` cumulative training-pilot
  `TEST` touches, retained on any subsequent failure or abort; never opening
  `TEST` if either write fails) **before** reading the `7` frozen `TEST` pieces
  (`23` restored facsimile pages, `111` systems, `432` measures, via the
  approved `w2-s0-formal-3` alignment/split/overlay manifests, `gold-cache`,
  `open-reading-masks.json`, `desk-study` and `req012-private-manifest.json`
  strictly read-only for that single pass), stopping to return to the owner
  before cumulative Slice B network downloads exceed `60,000,000,000` bytes
  (`60 GB`) in persistent `DOWNLOADS/w6-slice-b-download-ledger.json`, before
  generated Slice B checkpoints, predictions and run outputs (`RUNS/w6-slice-b-*`)
  exceed `150,000,000,000` bytes (`150 GB`) on disk, or before cumulative
  campaign runs in persistent append-only `DOWNLOADS/w6-slice-b-run-ledger.jsonl`
  (writing atomic pre-start reservations before every campaign GPU training run,
  every private-input `TRAIN_DEV` evaluation or replay run regardless of
  `--formal` flag or outcome, any optional campaign diagnostic rendering run,
  and the single `TEST` pass; only repository-fixture and synthetic-fixture
  unit/scenario/smoke checks with zero external downloads, zero staged-corpus
  batch rendering and zero private-input access are exempt) exceed `40` campaign
  GPU training runs (`8`-hour per-run wall-clock timeout), `30` private-input
  `TRAIN_DEV` evaluation runs (`300`-second per-page/system worker deadline),
  `5,000` optional campaign diagnostic synthetic pages, or `1` frozen-`TEST`
  evaluation pass (all Slice B ledgers and `w2-touch-log.jsonl` are permanently
  preserved and never deleted or reset).
- **Exclusions:** no paid or cloud GPU/compute dispatch, and no upload of
  private or synthetic score material, crops, checkpoints or diagnostics to
  external OMR, cloud-compute, hosting or storage services (`local_gpu_only`);
  no second Slice B `TEST` evaluation pass (`> 1` new `w2-touch-log.jsonl`
  pilot record) and no hyperparameter, architecture, prompt, calibrator or
  threshold tuning on `TEST`; no edit, reset or deletion of preserved W1,
  W2/W3, W4/W5 or W6 Slice A ledgers; no full `20k`–`50k`-page #1491 corpus
  scale-up, live transcription-workflow integration (W7, #1499), or multi-round
  flywheel deployment (W8, #1500) under this entry; no `AGPL`, `CC-BY-NC` or
  custom revenue-threshold code or model weights (`homr`, `transcoda`,
  `ultralytics`, `rokot-omr-2b`, `ConvNeXtV2` NC weights, `OXR-1.0`, `dinov3`,
  `aimv2`, `C-RADIOv3`), and no ingestion or training on `TEST` pieces or
  unreviewed public image+label targets (`GrandStaff`, `OLiMPiC`,
  `FP-GrandStaff`, `Polish-scores`, `SMB`); no committed fixture other than
  self-authored, public-domain or `CC0` material with provenance; no commit or
  public upload of private paths, piece/book identities or copyrighted score
  content; no modification to the private transcription repository or staged
  gold files; no unreviewed change to repository-locked tool/runtime versions or
  other projects' behavior; and no publication, release, credential or access
  change.

### Import AzureAuth Unofficial V2 into the monorepo

- **Work carrier:** [Issue #1892](https://github.com/hcoona/three/issues/1892)
  and its linked authorization and implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  [workspace setup](engineering/workspaces.md),
  [public release triage](engineering/public-release-triage.md), and the pinned
  source snapshot
  [`hcoona/microsoft-authentication-cli` `main-v2` at `f509276cad32c49b5764af906e8374f3f9b182ae`](https://github.com/hcoona/microsoft-authentication-cli/tree/f509276cad32c49b5764af906e8374f3f9b182ae).
- **Owner decisions and boundaries:**
    - Import `main-v2@f509276cad32c49b5764af906e8374f3f9b182ae` via a linear
      verbatim snapshot commit followed by monorepo adaptation commits under
      `src/public/app/azureauth-unofficial/` and
      `tests/public/app/azureauth-unofficial/` as an independent namespace
      separate from `src/private/app/azureauth-credprovider/`, dropping
      superseded repository-level governance, hook, toolchain, and workflow
      shells (`GOV-002`).
    - Register `src/public/app/azureauth-unofficial` in
      `NON_RELEASE_PUBLIC_DIRS` (`eng/scripts/release_triage.py` and
      [public release triage](engineering/public-release-triage.md)) while its
      portable-plus-symbols Windows prerelease archive shape remains outside
      Workflow Delivery v3 GitHub Release recipes; preserve monotonic
      `0.1.0-alpha.*` SemVer2 ordering beyond `0.1.0-alpha.37` via NBGV
      `versionHeightOffset` with `^refs/heads/main$` as `publicReleaseRefSpec`.
    - Relicense the sole-authored `main-v2` codebase and project records to the
      repository default `LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`,
      retaining only the embedded Microsoft `default.tb7` MIT notice inside
      `authentication-engine.tm7` and the bundled runtime/MSAL third-party MIT
      notices in `THIRD-PARTY-NOTICES.header.txt`.
- **Advancement and outcome:** place `Authentication.Core`,
  `Authentication.Windows`, `Authentication.Cli`, public JSON schemas, Windows
  packaging scripts, structured-record schemas, and project records under
  `src/public/app/azureauth-unofficial/` and scenario test suites plus
  `ResultValidation` under `tests/public/app/azureauth-unofficial/`; integrate
  root CPM (`Directory.Packages.props`), `dirs.proj`
  (`WindowsOnlyProjectReference` across `src/` and `tests/`), refreshed NuGet
  lockfiles, CI `WindowsHostTiming` test filtering, and per-process test scratch
  isolation; rehome project-scoped identity, `operational-identities.yaml`, and
  `rechecks.yaml` records, bind project records in
  `docs/governance/record-families.yaml`, and route from root `README.md` and
  `docs/README.md`.
- **Ordering and gates:** merge this authorization before merging substantive
  implementation into `main`. Keep implementation, lockfiles, inventory
  metadata, and owning records/routing self-consistent at merge into `main`.
  Retain applicable independent `engineering-guidance`, `record-system-review`,
  `research-evidence-review`, code review, `GOV-011` finding disposition,
  explicit-base `records:check`, HK conformance, and `Validate` CI. Material
  scope or feasibility tradeoffs return to the repository owner.
- **Acceptance and closure:** the imported projects, lockfiles, scenario tests,
  record bindings, and `public-release-inventory` check pass local validation
  and `Validate` CI with no unresolved material findings after review and
  contraction. Close this entry by deleting it in the implementation pull
  request (or an explicit Wave-only closure pull request).
- **Exclusions:** no Workflow Delivery v3 release-unit onboarding, live or
  dry-run package/release publication, build-provenance attestation,
  release/proving dispatch, code signing, real-account or WAM host
  authentication experiment, or `azureauth-credprovider` provider migration; no
  ruleset, Environment, credential, or access change.
