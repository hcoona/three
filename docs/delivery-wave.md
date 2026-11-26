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
    - Support portable cross-host migration from the Slice A CPU workstation to
      the RTX 4090 workstation by verifying immutable content `SHA-256` digests
      of the `7` carried-forward campaign ledgers (`download-ledger.json`,
      `w2-download-ledger.json`, `w2-touch-log.jsonl`,
      `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
      `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`), the
      self-contained `w6-slice-a-package-formal-6/datasets/` tree (`w6-crops-20k`,
      `w6-crops-50k` and shared `assets/` PNGs), and the `16`-piece
      `TRAIN_DEV` starting-state directory (`102` leaves under a dedicated
      `TRAIN_DEV`-only staging directory verified via its `traindev-manifest.json`,
      symlink-free worktree-disjoint path confinement and exact `102`-relpath
      allowlist before hashing payload leaves against committed
      `EXPECTED_TRAINDEV_PORTABLE_SHA256`, without scanning mixed `23`-piece
      directories or touching `TEST` during setup). Retire hidden
      `~/.local/state` host anchors, `(st_dev, st_ino, st_mtime_ns)` filesystem
      inode/mtime pinning, `0o500`/`0o400` permission sealing, `dir_fd`
      `O_NOFOLLOW` walk boilerplate, sticky unreaped-worker fences, custom
      PID-namespace watchdog scripts, custom raw-socket HTTP wire-byte wrappers,
      `unshare` wrapping on public-score `Verovio` rendering, and exact
      `producer_git_commit == HEAD` equality locks across W2 through W6
      (including atomic simplification of `OMR-REQ-029` while preserving its
      requirement ID and Slice A historical evidence) in favor of self-contained
      `DOWNLOADS/` ledgers, portable content `SHA-256` verification, direct OS
      `unshare -rn` network isolation (`HF_HUB_OFFLINE=1`,
      `TRANSFORMERS_OFFLINE=1`, `PIP_NO_INDEX=1`, `UV_OFFLINE=1`) with standard
      process-group timeouts on Linux private-input subprocesses, and recorded
      git provenance.
    - Treat installed host Windows `MuseScore 4.x` CLI versions and executable
      `SHA-256` digests as per-run provenance recorded at formal run start and
      verified unchanged at run end in `RUN/environment.json` rather than gating
      execution on a committed `ACCEPTED_MUSESCORE_IDENTITIES` binary-hash
      allowlist: retire `ACCEPTED_MUSESCORE_IDENTITIES` blocking,
      binary/portable-layout heuristics, per-job executable re-hashing, and
      inline PowerShell C# JobObject compilation wrappers across W4/W6 while
      retaining `4.x` version and `8`-preference (`importInferTextType=false`)
      verification, serialized shared-profile host execution under
      `.musescore.lock` with confirmed native process exit before reuse (or
      stopping further MuseScore dispatches if native exit cannot be confirmed
      on timeout, unless a supported per-worker profile-isolation mechanism is
      verified on the target build, since Qt Windows INI settings resolve
      `SHGetKnownFolderPath`), optional style-compatible `-j` batching, a
      focused `.mpos / 12.0 <= 2.0 px` geometry check on any `4.x` executable
      build not yet covered by source verification or prior cached calibration,
      and per-sample `.mpos` / MusicXML round-trip gates.
    - Retain Slice A's strictly permissive (`Apache-2.0`, `MIT`, `BSD-3-Clause`)
      trainable model and weight matrix (`trust_remote_code=False`, zero remote
      `.py` fetches; keeping `AGPL`, `CC-BY-NC` and custom revenue-threshold
      model/code exclusions unchanged, while permitting offline `uv`/`pip`
      `--no-index` installation into `DOWNLOADS/w6-gpu-venv` from locally
      fetched, hash-locked PyTorch CUDA, vision, training and `nvidia-*` /
      `triton` runtime wheels for local GPU execution without redistribution):
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
    - Simplify and amend `OMR-REQ-029` in `requirements.md`, the OMR evaluation,
      synthetic and training protocols, and `sheet_music_omr` harnesses to:
      (a) retire `ACCEPTED_MUSESCORE_IDENTITIES` and the over-defensive
      host-anchor, inode/permission-sealing, custom PID-watchdog, raw-socket
      wire-metering, PowerShell JobObject and exact-commit restrictions above in
      favor of portable content-`SHA-256` verification, direct WSL2/Windows
      `MuseScore 4.x` provenance + style-compatible `-j` batching, and standard
      process-group timeouts; and (b) commit the Slice B hash-locked CUDA wheel
      and pinned permissive `.safetensors` checkpoint lock manifests, portable
      `setup-w6-slice-b` verification, `E0` zero-shot runner, GPU
      training/evaluation/checkpoint runners for Arm A, Arm B and `D-FINE` /
      `RT-DETRv2`, `4`-fold calibration + matched-step `TRAIN_DEV` replay
      runners, and the single-pass `"W6-pilot-final"` frozen-`TEST` evaluation
      runner with persistent Slice B download, disk and run ledgers.
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
- **Ordering and gates:** merge this entry before any `OMR-REQ-029`, protocol or
  harness simplification/amendment, cross-host `setup-w6-slice-b` / private
  `--traindev-root` import, CUDA wheel or foundation-checkpoint fetch, campaign
  diagnostic rendering, `E0` zero-shot inference, campaign GPU training,
  `TRAIN_DEV` replay or `"W6-pilot-final"` `TEST` execution. Keep project
  records and implementation self-consistent at each merge into `main`, and
  accept the Slice B protocol amendments and committed hash-locked CUDA wheel +
  `.safetensors` checkpoint manifests on `main` under record-system review,
  research-evidence review and shared engineering guidance before any cross-host
  `setup-w6-slice-b` / private `--traindev-root` import, any
  `DOWNLOADS/w6-gpu-venv` or `.safetensors` download, any campaign diagnostic
  synthetic rendering, any campaign GPU training or `E0` inference run, any
  `TRAIN_DEV` replay run, or the `"W6-pilot-final"` `TEST` pass (offline
  repository-fixture and synthetic-fixture unit/scenario/smoke checks with zero
  network fetches, zero staged-corpus batch rendering and zero private-input
  access remain unconstrained; formal campaign execution fails closed on any
  drifted wheel or model checkpoint digest until its license and interface
  mutable-source recheck is reviewed and accepted on `main`). Complete all `E0`
  baselines, synthetic-only `4`-fold `TRAIN_DEV` selection, matched-step
  `TRAIN_DEV` replay, out-of-fold production calibrator fitting, and
  pre-registration of the single `"W6-pilot-final"` model/detector/threshold
  configuration before reading or scoring frozen `TEST`. Apply `GOV-011` finding
  disposition. New product, scope or feasibility tradeoffs return to the owner.
- **Acceptance and closure:** the tested Slice B requirement/protocol amendments
  and GPU training/evaluation harnesses are merged on `main`, and independently
  reviewed sanitized Slice B reports (`E0` zero-shot vs. fine-tuned Arm A / Arm
  B results and `20k`->`50k` scaling, 2-class `D-FINE` / `RT-DETRv2` vs.
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
  read-only cross-host import and content-`SHA-256` verification (after the
  Slice B protocol update merges) of preserved campaign ledgers
  (`download-ledger.json`, `w2-download-ledger.json`, `w2-touch-log.jsonl`,
  `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
  `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`),
  self-contained `w6-slice-a-package-formal-6/datasets/` (`w6-crops-20k`,
  `w6-crops-50k` and shared `assets/` PNGs), staged public archives if present,
  portable `16`-piece `--traindev-root` (`102`-leaf `SHA-256` +
  `EXPECTED_TRAINDEV_PORTABLE_SHA256` verification), and cached
  `w2-copista-venv` / `w4-synth-venv` when transferred; metered local download
  of hash-locked PyTorch CUDA, vision, training and runtime (`nvidia-*` /
  `triton`) wheels (from PyPI and PyTorch's official CUDA wheel index, installed
  offline via `uv`/`pip` `--no-index` into `DOWNLOADS/w6-gpu-venv`) plus pinned
  permissive `.safetensors` checkpoints and metadata into
  `DOWNLOADS/w6-model-weights/<slug>@<sha10>/` for the approved W6 model matrix
  (`trust_remote_code=False`, zero remote `.py` files); local RTX 4090 (`24 GB`)
  campaign CUDA training and offline `unshare -rn` `TRAIN_DEV` inference,
  calibration and replay runs; host Windows `MuseScore 4.x` CLI (on public
  `permissive` inputs only, recorded by version and `SHA-256`) and offline
  `Verovio` if bounded campaign diagnostic rendering is invoked; and **at most
  `1`** offline `unshare -rn` frozen-`TEST` evaluation pass
  (`purpose: "W6-pilot-final"`) writing and fsyncing **both** its pre-start
  `DOWNLOADS/w6-slice-b-run-ledger.jsonl` reservation **and** its immutable
  `DOWNLOADS/w2-touch-log.jsonl` record (`1 / 3` cumulative training-pilot
  `TEST` touches, retained on any subsequent failure or abort; never opening
  `TEST` if either write fails) **before** reading the `7` frozen `TEST` pieces
  (`23` restored facsimile pages, `111` systems, `432` measures, via the
  approved `w2-s0-formal-3` alignment/split/overlay manifests, `gold-cache`,
  `open-reading-masks.json`, `desk-study` and `req012-private-manifest.json`
  strictly read-only for that single pass). Frozen `TEST` access (`<= 1` pass)
  is gated strictly before reading any `TEST` file; every non-exempt download
  reserves its pinned artifact payload size before transfer and bounds
  application payload reads to the remaining `60,000,000,000`-byte (`60 GB`)
  artifact-payload ceiling in persistent
  `DOWNLOADS/w6-slice-b-download-ledger.json` (charging partial or failed
  payload reads without custom raw-socket wrappers or lower-layer HTTP/TLS/TCP
  framing accounting), records every
  non-exempt campaign run attempt before launch in persistent append-only
  `DOWNLOADS/w6-slice-b-run-ledger.jsonl` (`40` GPU training starts [`8`-hour
  per-run timeout], `30` private-input `TRAIN_DEV` evaluation starts
  [`300`-second per-page/system timeout], and `5,000` optional diagnostic
  synthetic pages [`<= 200` validated canonical measures per job, charging
  `max(actual_emitted_pages, 1)` on completion, failure or abort]), and treats
  the `150,000,000,000`-byte (`150 GB`) generated `RUNS/w6-slice-b-*` local
  disk ceiling and `5,000`-page optional diagnostic ceiling as **soft local
  runaway-prevention caps** checked before each dispatch and settled serially
  before another campaign job or run draws against the same soft cap (halting
  further dispatches that consume an exhausted budget and returning to the
  owner, with that single-job or single-run in-flight completion overshoot
  explicitly accepted and recorded by the owner rather than requiring
  pre-syscall worst-case byte or page estimation; all Slice B ledgers and
  `w2-touch-log.jsonl` are permanently preserved and never deleted or reset).
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

### Provision native TLA+ toolchain and HK SANY source conformance

- **Work carrier:** [Issue #1890](https://github.com/hcoona/three/issues/1890)
  and its linked pull requests (prerequisite for
  [Issue #1884](https://github.com/hcoona/three/issues/1884) and
  [Issue #1897](https://github.com/hcoona/three/issues/1897)).
- **Accepted inputs:** [governance](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  [workspaces](engineering/workspaces.md),
  [HK execution](engineering/hk-execution.md), root `mise.toml`, `mise.lock`,
  `hk.pkl`, `renovate.json`, `.github/workflows/ci.yml`, and Workflow Delivery
  [repository selection](../src/private/app/workflow-delivery/docs/design/repository-and-selection.md).
- **Advancement and outcome:** lock immutable upstream `tlaplus/tlaplus`
  `tla2tools.jar` release assets (`linux-x64`, `windows-x64`) alongside locked
  Temurin Java in `mise` with stable-only Renovate tracking; add fast `SANY`
  syntax/semantic conformance for `*.tla` files in `hk.pkl` (with Java enabled
  in CI `conformance` and HK lock-widening integration); provide an explicit
  non-HK `mise` `TLC` runner using isolated temporary `-metadir` directories;
  and document the `SANY` vs. `TLC` boundary in shared engineering guides.
- **Exclusions and closure:** no MSBuild wrappers; keep `TLC` and `PlusCal`
  outside HK hooks and `large` profiles; defer project `.tla`/`.cfg` `CiScope`
  model-checking jobs, `Apalache`, `TLAPS`, `CommunityModules`, `tlafmt`, and
  experimental native checkers until concrete specifications justify them; no
  paid, credentialed, release, or repository-setting effects. Retain applicable
  independent reviews and `GOV-011` disposition; delete this entry atomically in
  the delivery PR that closes #1890.
