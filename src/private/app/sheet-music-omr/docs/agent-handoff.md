# Sheet Music Recognition Agent Handoff

## Authority and purpose

Read this page before changing `src/private/app/sheet-music-omr/` or running its
measurement, evaluation, synthetic, or training harnesses. The
[project portal](../README.md) owns purpose, CLI usage, and top-level record
routing; [`record-system.md`](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency)
and [`docs/delivery-wave.md`](../../../../../docs/delivery-wave.md) govern work
authorization ([Issue #686](https://github.com/hcoona/three/issues/686) covers
design and planning, and the active
[W6 Slice B Delivery Wave entry](../../../../../docs/delivery-wave.md#execute-omr-gpu-training-and-evaluation-on-local-rtx-4090-w6-slice-b)
governs W6 GPU training and evaluation on the local RTX 4090 workstation for
[Issue #1498](https://github.com/hcoona/three/issues/1498) and
[Issue #1491](https://github.com/hcoona/three/issues/1491) (`#1494`–`#1496`),
while the earlier W1 `#1488`, W2/W3 `#1489`/`#1490`, W4/W5 `#1491`/`#1497`, and
W6 CPU Slice A `#1766`/`#1769` execution Wave grants are retired on completion).

Routine credential-free local development and debugging (`pytest`, `ruff`,
`pyrefly`, `harness view` on scratch runs, and repository/synthetic-fixture
`pilot` or smoke runs in out-of-worktree temporary directories without network
downloads or private-gold access) run freely without `main`-merge preconditions
or attempt quotas
([project namespaces](../../../../../docs/governance/record-system.md#project-namespaces)).
Any external package/model/corpus download, campaign synthetic rendering,
campaign GPU training, or private-gold execution outside active Wave bounds
requires its own accepted `Delivery Wave` entry and protocol on `main`.

## Change routing

Read the union of applicable rows below, and apply
[shared engineering guidance](../../../../../.github/skills/engineering-guidance/SKILL.md)
for architecture, abstraction, security/recovery, or test-strategy decisions:

| Change                                                               | Read                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Text Score 2 lexical rules, grammar, or canonical form               | [Text Score 2 format](text-format.md) ([Lexical rules](text-format.md#lexical-rules-and-preprocessing), [Grammar](text-format.md#grammar), [Canonical form](text-format.md#canonical-form)); [OMR-REQ-005](requirements.md#omr-req-005-canonical-form); `lexical.py`, `parser.py`, `writer.py`, `tests/test_documents.py`, `tests/test_lexical.py`                                                                                                                                                                                                                  |
| Semantic validation, carry state, or system assembly                 | [Text Score 2 format](text-format.md) ([Semantics](text-format.md#semantics), [Carry state and assembly](text-format.md#carry-state-and-assembly)); [OMR-REQ-004 through OMR-REQ-007](requirements.md#representation-invariants); `semantics.py`, `carry.py`, `assembly.py`, `relations.py`, `timeline.py`, `review.py`                                                                                                                                                                                                                                             |
| Forward or reverse MusicXML conversion                               | [Text Score 2 format](text-format.md) ([Transcription rules](text-format.md#transcription-rules), [Category encodings](text-format.md#category-encodings), [Layout, unsupported content and open points](text-format.md#layout-unsupported-content-and-open-points)); [OMR-REQ-008](requirements.md#omr-req-008-forward-and-reverse-conversion), [Unsupported content](requirements.md#unsupported-content); `musicxml_forward.py`, `musicxml_reverse.py`, `musicxml_tree.py`, `musicxml_events.py`, `beams.py`, `pitches.py`, `measures.py`                        |
| Round-trip comparator, pairing rules, or supplementary checks        | [Comparison protocol](comparison-protocol.md) ([Comparison instrument](comparison-protocol.md#comparison-instrument), [Category assignment](comparison-protocol.md#category-assignment), [Calibration](comparison-protocol.md#calibration), [Layout-fidelity score](comparison-protocol.md#layout-fidelity-score)); [OMR-REQ-009](requirements.md#omr-req-009-comparison-protocol); `comparison.py`, `supplementary.py`, `tests/test_comparison.py`                                                                                                                 |
| Corpus screening, MuseScore isolation, runner, or reporting          | [Comparison protocol](comparison-protocol.md) ([Tools and environment](comparison-protocol.md#tools-and-environment), [Inputs and score identities](comparison-protocol.md#inputs-and-score-identities), [Execution and bounds](comparison-protocol.md#execution-and-bounds), [Stop conditions](comparison-protocol.md#stop-conditions), [Evidence, publication and cleanup](comparison-protocol.md#evidence-publication-and-cleanup)); [OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance); `src/sheet_music_omr/harness/`                   |
| Acceptance claims, ABC capability mapping, or tokenizer counts       | [Acceptance](requirements.md#acceptance), [Evidence and boundaries](requirements.md#evidence-and-boundaries), [Validation basis](requirements.md#validation-basis); [research-evidence review](../../../../../.github/skills/research-evidence-review/SKILL.md)                                                                                                                                                                                                                                                                                                     |
| Real-data alignment, overlay QA, system crops, or split rules        | [OMR-REQ-016 and OMR-REQ-017](requirements.md#real-data-splits-and-recognizer-evaluation), [Evaluation and real-data alignment protocol](evaluation-protocol.md) ([Real-data alignment, overlay QA and system crops](evaluation-protocol.md#real-data-alignment-overlay-qa-and-system-crops)); [OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance); `src/sheet_music_omr/evaluation/alignment.py`, `tests/test_evaluation.py`                                                                                                                 |
| Recognizer scorer, Stage S0 baselines, touch log, or `copista`       | [OMR-REQ-018 through OMR-REQ-020](requirements.md#real-data-splits-and-recognizer-evaluation), [Evaluation and real-data alignment protocol](evaluation-protocol.md) ([Recognizer comparison and B-normalization](evaluation-protocol.md#recognizer-comparison-and-b-normalization), [Stage S0 baselines and isolated copista-28m execution](evaluation-protocol.md#stage-s0-baselines-and-isolated-copista-28m-execution), [Discrepancy and round-logging schema](evaluation-protocol.md#discrepancy-and-round-logging-schema)); `src/sheet_music_omr/evaluation/` |
| Synthetic data engine, Stage S3 proof, W5 detectors, or W5 assembly  | [OMR-REQ-021 through OMR-REQ-025](requirements.md#synthetic-training-data-engine-and-system-detection-benchmark), [Synthetic data and system-detection protocol](synthetic-protocol.md); [OMR-REQ-014 and OMR-REQ-015](requirements.md#evidence-and-boundaries); `src/sheet_music_omr/synthetic/`, `src/sheet_music_omr/evaluation/w5_detectors.py`, `tests/test_synthetic_intake_injector.py`, `tests/test_w5_and_degrade.py`, `tests/test_synthetic_ledgers_cli.py`                                                                                               |
| W6 bulk crop packaging, training stack, detector, or Slice B GPU run | [OMR-REQ-026 through OMR-REQ-031](requirements.md#pilot-dataset-packaging-clean-training-stack-and-per-system-baseline), [Training, bulk-packaging and page-detector protocol](training-protocol.md); [OMR-REQ-014 and OMR-REQ-015](requirements.md#evidence-and-boundaries); `src/sheet_music_omr/training/`, `src/sheet_music_omr/synthetic/`, `src/sheet_music_omr/evaluation/`, `tests/test_w6_packaging_ledgers_copista.py`, `tests/test_w6_models_decoding_calibration.py`, `tests/test_w6_slice_b.py`                                                        |
| Project records or navigation only                                   | [Project portal](../README.md), [record policy](../../../../../docs/governance/record-system.md), [record bindings](../../../../../docs/governance/record-families.yaml), [record-system review](../../../../../.github/skills/record-system-review/SKILL.md)                                                                                                                                                                                                                                                                                                       |

Run local package checks from the repository root (always include `--group dev`
when invoking `sheet_music_omr.harness preflight` or `pilot`, which execute
`pytest` via `sys.executable`):

```shell
uv run --package sheet-music-omr --group dev pytest src/private/app/sheet-music-omr/tests
uv run ruff check src/private/app/sheet-music-omr
uv run ruff format --check src/private/app/sheet-music-omr
uv run pyrefly check src/private/app/sheet-music-omr
```

## Settled baselines, closed campaign ledgers and active W6 Slice B routing

All completed formal baselines, stopped-run attributions, and review receipts
are retained in GitHub Issues, and their former Wave allowances ended on
retirement (ledger and run-directory retention follow
[External state, privacy and protocol execution boundaries](#external-state-privacy-and-protocol-execution-boundaries)):

- **W1 (`#1488`, accepted on `formal-5` at `9171f870`):** [triage method](https://github.com/hcoona/three/issues/1488#issuecomment-6721091552),
  [Round 2 screen review](https://github.com/hcoona/three/issues/1488#issuecomment-6721238428),
  [Phase 1 screen allowlist review](https://github.com/hcoona/three/issues/1488#issuecomment-7069648364),
  ABC 2.1 desk mappings
  ([base](https://github.com/hcoona/three/issues/1488#issuecomment-6639491389) /
  [review](https://github.com/hcoona/three/issues/1488#issuecomment-6642511739),
  [heading addendum](https://github.com/hcoona/three/issues/1488#issuecomment-6672103690),
  [Phase 1 addendum](https://github.com/hcoona/three/issues/1488#issuecomment-7058919271);
  earlier `xml2abc`/`abc2xml` probe in
  [`6614290511`](https://github.com/hcoona/three/issues/1488#issuecomment-6614290511)
  is a toolchain observation only, and pre-`#1608` local probes in
  [`6722886585`](https://github.com/hcoona/three/issues/1488#issuecomment-6722886585)
  / fixture pilot [`6728291229`](https://github.com/hcoona/three/issues/1488#issuecomment-6728291229)
  are non-formal), stopped `formal-1`–`formal-4` history
  ([`formal-2`](https://github.com/hcoona/three/issues/1488#issuecomment-6728507174) /
  [`6728532773`](https://github.com/hcoona/three/issues/1488#issuecomment-6728532773),
  workstation pause [`6729151812`](https://github.com/hcoona/three/issues/1488#issuecomment-6729151812) /
  resume [`7029347075`](https://github.com/hcoona/three/issues/1488#issuecomment-7029347075),
  `formal-3` selection [`7030522396`](https://github.com/hcoona/three/issues/1488#issuecomment-7030522396))
  and `F1` `GOV-011` `false positive` triage
  ([`6728641537`](https://github.com/hcoona/three/issues/1488#issuecomment-6728641537)),
  and accepted `formal-5` report
  ([PR #1716](https://github.com/hcoona/three/pull/1716), selection
  [`7075296838`](https://github.com/hcoona/three/issues/1488#issuecomment-7075296838),
  [`7083248609`](https://github.com/hcoona/three/issues/1488#issuecomment-7083248609),
  owner disposition [`7083509900`](https://github.com/hcoona/three/issues/1488#issuecomment-7083509900);
  one-time `gold-01` staged tie repair from [`7067355696`](https://github.com/hcoona/three/issues/1488#issuecomment-7067355696)
  / `#1705` is spent). Settled `download-ledger.json`: `2,350,276,855` bytes
  (pass `--prior-total 2350276855` with `--basis` if initializing a fresh
  authorized `DOWNLOADS` root).
- **W2 & W3 (`#1489` / `#1490`, accepted on `w2-s0-formal-3` at `385aa460`, grant retired in [PR #1751](https://github.com/hcoona/three/pull/1751)):**
  results and owner acceptance in
  [#1490 comment 7108804779](https://github.com/hcoona/three/issues/1490#issuecomment-7108804779)
  / [`7109703011`](https://github.com/hcoona/three/issues/1490#issuecomment-7109703011)
  and [#1489 comment 7108805391](https://github.com/hcoona/three/issues/1489#issuecomment-7108805391)
  / [`7109703768`](https://github.com/hcoona/three/issues/1489#issuecomment-7109703768).
  Settled `w2-download-ledger.json`: `480,830,451` bytes; `w2-touch-log.jsonl`:
  `6 / 6` S0 `TEST` touches (`S0_TEST_TOUCH_CAP` exhausted).
- **W4 Stage S3 & W5 (`#1491` / `#1493` / `#1497`, accepted at `abef2253`, grant retired in [PR #1762](https://github.com/hcoona/three/pull/1762)):**
  results and owner acceptance in
  [#1491 comment 7130836402](https://github.com/hcoona/three/issues/1491#issuecomment-7130836402)
  / [`7138762309`](https://github.com/hcoona/three/issues/1491#issuecomment-7138762309)
  and [#1497 comment 7130837035](https://github.com/hcoona/three/issues/1497#issuecomment-7130837035)
  / [`7138763006`](https://github.com/hcoona/three/issues/1497#issuecomment-7138763006).
  Settled `w4-w5-download-ledger.json`: `341,067,483` bytes (`204` entries);
  `w4-w5-run-ledger.jsonl`: `1,215` settled pages, `1 / 20` `TRAIN_DEV` W5
  starts, and `1 / 1` locked `92`-page pass spent (`889,390,301` disk bytes).
- **W6 CPU Slice A (`#1498` / `#1491`, completed through `w6-slice-a-package-formal-6` at `bf1aedf4`, `#1766`/`#1769` grant retired):**
  non-formal fixture smoke `w6-slice-a-smoke-2` (`smoke-report.json`
  `116d2cca857af4082cb72363ee9123a19fe073b16ac51049288e3f14f67de907`),
  `TRAIN_DEV`-only per-system `copista-28m` baseline `w6-slice-a-copista-formal-2`
  at `01c5acce` (`copista-per-system-traindev.json`
  `fb804747aebc0e591fa04672e9f6361f6d06578834cb843e43c1795912a1ea74`, `0`
  `TEST` touches), base packaging `w6-slice-a-package-formal-4` at `e7250dca`
  (`w6-packaging.json`
  `260b461d20c7647415d60f9fb3f025fc33b290cf7a92ef7c4d7db85bcc9f22c3`;
  [#1498 comment 7173082854](https://github.com/hcoona/three/issues/1498#issuecomment-7173082854),
  receipt [`7173281357`](https://github.com/hcoona/three/issues/1498#issuecomment-7173281357);
  [#1491 comment 7173083668](https://github.com/hcoona/three/issues/1491#issuecomment-7173083668);
  [#686 comment 7173099158](https://github.com/hcoona/three/issues/686#issuecomment-7173099158)),
  and full-`27`-category `MuseScore Studio 4.7.7.261109195` supplement
  `w6-slice-a-package-formal-6` at `bf1aedf4` (`w6-packaging.json`
  `77b8c24212118c8707b164eae2e46b3c668400ff644b443d09bbb7c4c0be0331`,
  refreshed `w6-crops-20k/summary.json`
  `5ded55ce63fb1f2591529e65a5c605cffc77c4b4d770d4090a524db356e1e495` at
  `18,000` `synth_train` + `2,005` `synth_val` crops, and refreshed
  `w6-crops-50k/summary.json`
  `b0f3cf6ef51973e07c9aa8f4914b14f9d23d9ed392699207c10d8aaa0df2756f` at
  `45,004` `synth_train` + `5,001` `synth_val` crops; recorded in
  [#1498 comment 7185613521](https://github.com/hcoona/three/issues/1498#issuecomment-7185613521)
  with receipt [`7185614009`](https://github.com/hcoona/three/issues/1498#issuecomment-7185614009),
  [#1491 comment 7185614792](https://github.com/hcoona/three/issues/1491#issuecomment-7185614792),
  and [#686 comment 7185615455](https://github.com/hcoona/three/issues/686#issuecomment-7185615455)).
  Settled Slice A ledgers: `w6-slice-a-download-ledger.json` at `443,814,298`
  bytes across `108` entries (`SHA-256`
  `9a4c84f60f73155a4f5169f367e8a5ebca51cf5c0c2ab0758c6c7b37a6331711`),
  `w6-slice-a-run-ledger.jsonl` at `18,150` settled charged pages (`18,149`
  actual rendered pages plus `1` failed-start minimum charge) and `3 / 10`
  private-input `copista-28m` invocations (`SHA-256`
  `e2120f815812a8ac181fc671cef40af1446f2cc27688aa0244282d2c70dc9004`, `0`
  `TEST` touches), and `56,639,196,692` unique-inode bytes across
  `runs/w6-slice-a-*`.
- **Active W6 GPU Slice B (`#1498` / `#1491` on local RTX 4090 `24 GB`):**
  follow the ordered accepted-protocol and CUDA/`.safetensors` lock
  prerequisites, persistent ledger/import bounds, offline fixture-smoke
  exemption, and pre-read `w2-touch-log.jsonl` + `w6-slice-b-run-ledger.jsonl`
  frozen-`TEST` touch governance owned by the active
  [W6 Slice B Delivery Wave entry](../../../../../docs/delivery-wave.md#execute-omr-gpu-training-and-evaluation-on-local-rtx-4090-w6-slice-b)
  and the [training, bulk-packaging and page-detector protocol](training-protocol.md).

## External state, privacy and protocol execution boundaries

- **Out-of-worktree state & ledger preservation:** Keep `RUN` and `DOWNLOADS`
  outside every Git worktree (`harness/records.py:check_outside_repository`).
  Carry forward prior download, run, and `TEST`-touch ledger history without
  reset or deletion (updating active download ledgers only through their owning
  protocol's reservation/settlement flow, while Slice B imports W1–Slice A
  ledgers read-only), and never delete, reset, or overwrite prior `formal-*`,
  `w2-s0-*`, `w4-w5-*`, or `w6-slice-a-*` run directories without explicit owner
  deletion after closure.
- **Privacy (`OMR-REQ-015`):** Access private gold inputs locally and read-only,
  confine `RUN/gold/mapping.json` to external run state, and never commit, log,
  or publish private paths, piece/book identities, or score content (report only
  aggregate counts and anonymized batch numbers, verified by `privacy.scan`).
- **Clean baselines & owning protocols:** Branch from clean `origin/main` (do
  not stage superseded `experiments/abc_roundtrip/` or `TextScore2.csproj`
  worktree prototypes). Execute Wave-authorized formal runs strictly under the
  preconditions, preflight/code-tree bindings, and stop rules in the owning
  protocol ([comparison](comparison-protocol.md),
  [evaluation](evaluation-protocol.md), [synthetic](synthetic-protocol.md), or
  [training](training-protocol.md)).
