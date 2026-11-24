# Sheet Music Recognition Agent Handoff

## Authority and purpose

Read this page before changing `src/private/app/sheet-music-omr/` or running its
measurement harness. It owns change routing, prior formal-run and external-state
boundaries, and ordered execution gates; it is not a second requirements record,
format specification, comparison protocol, or work-authorization file. The
[project portal](../README.md) owns purpose, user context, command-line usage,
and top-level record routing.

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md) governs
feature/capability advancements, architectural or public-contract changes, and
external/privacy effects ([Issue #686](https://github.com/hcoona/three/issues/686)
for sheet-music recognition design and planning; the W1 text-representation
grant for [Issue #1488](https://github.com/hcoona/three/issues/1488) was
retired after owner acceptance of `formal-5` in
[#1488 comment 7083509900](https://github.com/hcoona/three/issues/1488#issuecomment-7083509900),
the W2/W3 grant for [Issue #1489](https://github.com/hcoona/three/issues/1489)
and [Issue #1490](https://github.com/hcoona/three/issues/1490) was retired in
[PR #1751](https://github.com/hcoona/three/pull/1751) after owner acceptance of
`w2-s0-formal-3`, and the [PR #1752](https://github.com/hcoona/three/pull/1752)
W4/W5 Stage S3 and system-detection grant for
[Issue #1491](https://github.com/hcoona/three/issues/1491) (`#1493`–`#1496`)
and [Issue #1497](https://github.com/hcoona/three/issues/1497) was retired after
owner acceptance of `w4-w5-formal-2`, `w4-w5-formal-w5-traindev` and
`w4-w5-formal-w5-locked92` in
[#1491 comment 7138762309](https://github.com/hcoona/three/issues/1491#issuecomment-7138762309)
and
[#1497 comment 7138763006](https://github.com/hcoona/three/issues/1497#issuecomment-7138763006)),
while contract-preserving routine maintenance without new external, paid, or
privacy effects follows the
[record policy](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency).
Any new workstream/feature implementation, empirical experiment, external or
host tool installation outside locked repository toolchains, dataset download,
or private-gold execution beyond active Delivery Wave entries requires its own
accepted Delivery Wave entry; evidence acceptance does not retain or revive
retired execution grants. A pull request, local run record, or issue
comment cannot enlarge authorization or waive an accepted gate. Chronological
progress, reviews, and findings remain in those Issues and their linked pull
requests.

## Change routing

Read the rows that match the task; when several rows apply, read their union.
Before architecture, abstraction, security/recovery, or test-strategy decisions,
also apply [shared engineering guidance](../../../../../.github/skills/engineering-guidance/SKILL.md).

| Change                                                              | Read                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Text Score 2 lexical rules, grammar, or canonical form              | [Text Score 2 format](text-format.md) ([Lexical rules](text-format.md#lexical-rules-and-preprocessing), [Grammar](text-format.md#grammar), [Canonical form](text-format.md#canonical-form)); [OMR-REQ-005](requirements.md#omr-req-005-canonical-form); `lexical.py`, `parser.py`, `writer.py`, `tests/test_documents.py`, `tests/test_lexical.py`                                                                                                                                                                                                                  |
| Semantic validation, carry state, or system assembly                | [Text Score 2 format](text-format.md) ([Semantics](text-format.md#semantics), [Carry state and assembly](text-format.md#carry-state-and-assembly)); [OMR-REQ-004 through OMR-REQ-007](requirements.md#representation-invariants); `semantics.py`, `carry.py`, `assembly.py`, `relations.py`, `timeline.py`, `review.py`                                                                                                                                                                                                                                             |
| Forward or reverse MusicXML conversion                              | [Text Score 2 format](text-format.md) ([Transcription rules](text-format.md#transcription-rules), [Category encodings](text-format.md#category-encodings), [Layout, unsupported content and open points](text-format.md#layout-unsupported-content-and-open-points)); [OMR-REQ-008](requirements.md#omr-req-008-forward-and-reverse-conversion), [Unsupported content](requirements.md#unsupported-content); `musicxml_forward.py`, `musicxml_reverse.py`, `musicxml_tree.py`, `musicxml_events.py`, `beams.py`, `pitches.py`, `measures.py`                        |
| Round-trip comparator, pairing rules, or supplementary checks       | [Comparison protocol](comparison-protocol.md) ([Comparison instrument](comparison-protocol.md#comparison-instrument), [Category assignment](comparison-protocol.md#category-assignment), [Calibration](comparison-protocol.md#calibration), [Layout-fidelity score](comparison-protocol.md#layout-fidelity-score)); [OMR-REQ-009](requirements.md#omr-req-009-comparison-protocol); `comparison.py`, `supplementary.py`, `tests/test_comparison.py`                                                                                                                 |
| Corpus screening, MuseScore isolation, runner, or reporting         | [Comparison protocol](comparison-protocol.md) ([Tools and environment](comparison-protocol.md#tools-and-environment), [Inputs and score identities](comparison-protocol.md#inputs-and-score-identities), [Execution and bounds](comparison-protocol.md#execution-and-bounds), [Stop conditions](comparison-protocol.md#stop-conditions), [Evidence, publication and cleanup](comparison-protocol.md#evidence-publication-and-cleanup)); [OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance); `src/sheet_music_omr/harness/`                   |
| Acceptance claims, ABC capability mapping, or tokenizer counts      | [Acceptance](requirements.md#acceptance), [Evidence and boundaries](requirements.md#evidence-and-boundaries), [Validation basis](requirements.md#validation-basis); [research-evidence review](../../../../../.github/skills/research-evidence-review/SKILL.md)                                                                                                                                                                                                                                                                                                     |
| Real-data alignment, overlay QA, system crops, or split rules       | [OMR-REQ-016 and OMR-REQ-017](requirements.md#real-data-splits-and-recognizer-evaluation), [Evaluation and real-data alignment protocol](evaluation-protocol.md) ([Real-data alignment, overlay QA and system crops](evaluation-protocol.md#real-data-alignment-overlay-qa-and-system-crops)); [OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance); `src/sheet_music_omr/evaluation/alignment.py`, `tests/test_evaluation.py`                                                                                                                 |
| Recognizer scorer, Stage S0 baselines, touch log, or `copista`      | [OMR-REQ-018 through OMR-REQ-020](requirements.md#real-data-splits-and-recognizer-evaluation), [Evaluation and real-data alignment protocol](evaluation-protocol.md) ([Recognizer comparison and B-normalization](evaluation-protocol.md#recognizer-comparison-and-b-normalization), [Stage S0 baselines and isolated copista-28m execution](evaluation-protocol.md#stage-s0-baselines-and-isolated-copista-28m-execution), [Discrepancy and round-logging schema](evaluation-protocol.md#discrepancy-and-round-logging-schema)); `src/sheet_music_omr/evaluation/` |
| Synthetic data engine, Stage S3 proof, W5 detectors, or W5 assembly | [OMR-REQ-021 through OMR-REQ-025](requirements.md#synthetic-training-data-engine-and-system-detection-benchmark), [Synthetic data and system-detection protocol](synthetic-protocol.md); [OMR-REQ-014 and OMR-REQ-015](requirements.md#evidence-and-boundaries); `src/sheet_music_omr/synthetic/`, `src/sheet_music_omr/evaluation/w5_detectors.py`, `tests/test_synthetic_intake_injector.py`, `tests/test_w5_and_degrade.py`, `tests/test_synthetic_ledgers_cli.py`                                                                                               |
| Project records or navigation only                                  | [Project portal](../README.md), [record policy](../../../../../docs/governance/record-system.md), [family catalog](../../../../../docs/governance/record-families.yaml), [record-system review](../../../../../.github/skills/record-system-review/SKILL.md)                                                                                                                                                                                                                                                                                                        |

## Accepted W1 baseline on main

All completed W1 specifications, converters, comparison logic, and execution
harness components are merged on `main` (source findings from repository
history):

- **Authorization and normative records:** Delivery Wave entries (`#1504`,
  amended in `#1525`, `#1678`, `#1705`, and `#1711`, and retired on W1
  acceptance), [requirements](requirements.md) and
  [Text Score 2 format](text-format.md) (`#1537`, `#1547`, `#1559`, `#1564`,
  `#1569`, `#1685`, `#1716`), and the
  [comparison protocol](comparison-protocol.md) with its accepted calibration,
  voice-position, octave-shift, Phase 1, and re-export attribution amendments
  (`#1571`, `#1591`, `#1598`, `#1605`, `#1608`, `#1678`, `#1685`, `#1716`).
- **Core Python package (`sheet_music_omr`):** parser and canonical writer
  (`#1570`), semantic validator and carry-out derivation (`#1572`), system
  assembler (`#1577`), forward MusicXML 4.0 converter (`#1578`), reverse
  MusicXML converter (`#1579`), and calibrated round-trip comparator (`#1607`),
  together with the Phase 1 and post-`formal-4` converter and comparator updates
  (`#1704`, `#1716`, merged at `9171f870684a37e0ca5a526387ea69d92e740882`).
- **Measurement run harness (`sheet_music_omr.harness`):** preflight,
  byte-bounded download ledger, structural scope screen, deterministic PDMX and
  OpenScore Lieder selection, isolated MuseScore invocation, bounded worker pool,
  read-only private-gold execution, attributed-`R(X)` reporting, frozen
  `OMR-REQ-012` replay (`replay-req012`), `Qwen3-VL-2B-Instruct` tokenizer
  counting (`tokenizer-counts`), measure-level `view`, and repository-fixture
  `pilot` (`#1609`, `#1678`, `#1704`, `#1716`).

Run local package checks from the repository root:

```shell
uv run --package sheet-music-omr --group dev pytest src/private/app/sheet-music-omr/tests
uv run ruff check src/private/app/sheet-music-omr
uv run ruff format --check src/private/app/sheet-music-omr
uv run pyrefly check src/private/app/sheet-music-omr
```

`sheet_music_omr.harness preflight` and `pilot` run `pytest` via
`sys.executable` (`src/sheet_music_omr/harness/preflight.py`). Always include
`--group dev` in `uv run` when invoking the harness so `pytest` is present in
the active environment.

## Completed W1 evidence baselines

The following W1 pre-execution baselines and acceptance artifacts are recorded
on [Issue #1488](https://github.com/hcoona/three/issues/1488):

- **Source triage method (ready):** fixed before formal execution in
  [#1488 comment 6721091552](https://github.com/hcoona/three/issues/1488#issuecomment-6721091552)
  (source finding). Every counted difference or failure type in a formal run is
  attributed to one of five sources: converter defect, format gap or ambiguity,
  MuseScore export or import, comparator/screen/harness defect, or unsupported
  content in gold.
- **Structural-screen allowlist review (completed for Phase 1 in `#1704`):**
  independent Round 2 review passed before `#1609` merged
  ([#1488 comment 6721238428](https://github.com/hcoona/three/issues/1488#issuecomment-6721238428)),
  and the updated independent allowlist review across all eight expanded Phase 1
  calibration tables (`191` pairs, `382` texts, `0` violations) passed and is
  recorded in
  [#1488 comment 7069648364](https://github.com/hcoona/three/issues/1488#issuecomment-7069648364)
  (source findings; no `screen.py` changes have been made since `#1704`).
- **Pilot and probe limits:** the final single-fixture harness pilot on the
  rebased `#1609` branch
  ([#1488 comment 6728291229](https://github.com/hcoona/three/issues/1488#issuecomment-6728291229))
  is a runtime observation on repository fixtures only, not corpus or gold
  acceptance evidence. Earlier pilots and local gold probes
  ([#1488 comment 6722886585](https://github.com/hcoona/three/issues/1488#issuecomment-6722886585))
  ran before `#1608` was accepted and do not count as formal protocol runs.
- **ABC 2.1 capability mapping (completed, including Phase 1 addendum):** the
  earlier `xml2abc`/`abc2xml` execution
  ([#1488 comment 6614290511](https://github.com/hcoona/three/issues/1488#issuecomment-6614290511))
  is retained only as an incomplete toolchain runtime observation (`#1525`).
  After `#1547` (`eb4986a5`) clarified in
  [Validation basis](requirements.md#validation-basis) that source-only
  standards comparisons do not require an experiment protocol, the desk ABC 2.1
  capability mapping for `be64962a`
  ([#1488 comment 6639491389](https://github.com/hcoona/three/issues/1488#issuecomment-6639491389))
  completed independent `research-evidence-review`
  ([#1488 comment 6642511739](https://github.com/hcoona/three/issues/1488#issuecomment-6642511739)),
  its `#1569` (`6bc166ac`) addendum covering `heading`, review markers, and
  heading `carry-in` completed independent `research-evidence-review` and
  GOV-011 triage
  ([#1488 comment 6672103690](https://github.com/hcoona/three/issues/1488#issuecomment-6672103690)),
  and its `#1685` (`1573289e`) Phase 1 addendum covering all eight Phase 1
  notation extensions and invariants completed independent
  `research-evidence-review` in
  [#1488 comment 7058919271](https://github.com/hcoona/three/issues/1488#issuecomment-7058919271)
  (source findings; each mapping cell is a source-backed inference).
- **Completed W1 acceptance run (`formal-5`) and owner acceptance:** after
  `formal-4` stopped on `p0098/rx` on `5956790d`
  ([#1488 comment 7071662321](https://github.com/hcoona/three/issues/1488#issuecomment-7071662321))
  and the post-`formal-4` owner dispositions and implementations landed in
  [#1488 comment 7071944465](https://github.com/hcoona/three/issues/1488#issuecomment-7071944465),
  [PR #1711](https://github.com/hcoona/three/pull/1711), and
  [PR #1716](https://github.com/hcoona/three/pull/1716), `formal-5` ran to 100%
  completion on clean `main` commit `9171f870684a37e0ca5a526387ea69d92e740882`
  with `MuseScore Studio 4.7.7.261109195` (`executable_sha256`
  `89d9145261eb2bec6aedc208b5234b92c01fe6bc5485fec7e2ce28ec05819f31`;
  pre-conversion selection manifest in
  [#1488 comment 7075296838](https://github.com/hcoona/three/issues/1488#issuecomment-7075296838),
  independently reviewed results report in
  [#1488 comment 7083248609](https://github.com/hcoona/three/issues/1488#issuecomment-7083248609),
  and owner acceptance disposition in
  [#1488 comment 7083509900](https://github.com/hcoona/three/issues/1488#issuecomment-7083509900);
  source findings over runtime observations): `gold-x` `100.0000%`
  (`30,716 / 30,716`, `0` differences, `23 / 23` import checks passed, `23 / 23`
  text-space consistent, `87.8583%` layout fidelity, `6` `excluded_voices`;
  `gold-01` evaluated on the one-time owner-authorized staged tie-endpoint
  repair of [PR #1705](https://github.com/hcoona/three/pull/1705) /
  [#1488 comment 7067355696](https://github.com/hcoona/three/issues/1488#issuecomment-7067355696)
  item 4, recording prior and repaired SHA-256 digests locally while keeping the
  original private transcription repository and the other `22` gold pieces
  read-only),
  system assembly `23 / 23` `equal` (`493` systems, `0` `excluded_voices`),
  `pdmx-x` `99.6360%` (`94,696 / 95,042`), `pdmx-rx` `93.8568%` raw
  (`89,255 / 95,097`) and `99.6995%` (`89,255 / 89,524`) across the `197` files
  not solely corrupted by upstream MuseScore 4.7.7 export defects
  ([musescore/MuseScore#22974](https://github.com/musescore/MuseScore/issues/22974),
  [#35740](https://github.com/musescore/MuseScore/issues/35740),
  [#35741](https://github.com/musescore/MuseScore/issues/35741)), `lieder-x`
  `99.8140%` (`41,323 / 41,400`), `lieder-rx` `99.8435%` (`41,463 / 41,528`),
  `OMR-REQ-012` frozen replay `327 / 327` deduplicated loci (`356` raw entries,
  `0` failed), and `Qwen3-VL-2B-Instruct` tokenizer counts recorded (`2,350,276,855`
  cumulative ledger bytes against the `20,000,000,000`-byte W1 cap).

## Formal-run history and closed-grant boundary

Two formal harness attempts ran on Windows x64 at clean `main` commit
`397b306f27cf51e15f58d44f5b67c1c0626f1ade` before workstation transition:

1. **`formal-1` (stopped in `preflight`):** runtime observation. `preflight`
   stopped with `PreflightError: calibration failed` (`No module named pytest`)
   because `--group dev` was omitted from `uv run`. Before stopping, `formal-1`
   recorded the initial `download-ledger.json` total of `2,343,244,452` bytes
   against the `20,000,000,000`-byte W1 cap (`200,000,000`-byte conservative
   prior allowance plus `PDMX.csv` `225,399,738` bytes, `mxl.tar.gz`
   `1,894,335,797` bytes, and `OpenScore/Lieder-v3.0.0.zip` `23,508,917` bytes).
2. **`formal-2` (passed `preflight`, stopped in `select`):** runtime
   observations recorded in
   [#1488 comment 6728507174](https://github.com/hcoona/three/issues/1488#issuecomment-6728507174)
   and
   [#1488 comment 6728532773](https://github.com/hcoona/three/issues/1488#issuecomment-6728532773).
   `preflight` passed on clean commit `397b306f27cf51e15f58d44f5b67c1c0626f1ade`
   with MuseScore `4.7.6.261022093`, baseline settings SHA-256
   `12e9296df54dd73c0a8820e7264257ec0553cb9d73b95e0e8431f2687274deb3`, `511`
   calibration tests passed, and carried-forward ledger total `2,343,244,452`
   bytes (`0` new bytes downloaded). During `select` (`parse_check`), public
   PDMX archive entry
   `./mxl/12/23/QmUhRXt7YGTniz62wPpVGpCGtvFa1tfdHjrndu5bL2Ftj6.mxl` stopped the
   run with `StepStopError` wrapping
   `ProtocolDefectError: music21 reads an octave shift in measure 1 other than MusicXML does`
   on two `octave-shift` spans overlapping on the same staff. Selection halted before completing PDMX and Lieder sample selection, and no
   selected-file round-trip conversion ran.
3. **GOV-011 triage of `F1`:** independent triage in
   [#1488 comment 6728641537](https://github.com/hcoona/three/issues/1488#issuecomment-6728641537)
   classified an alleged structural-screen implementation omission (`F1`) as
   `false positive` (confidence `9/10`, source finding + inference):
   [Text Score 2](text-format.md#relations) forbids overlapping same-staff
   `8va`/`8vb` spans (`open_conflict`), and the accepted
   [comparison protocol](comparison-protocol.md#category-assignment) requires
   `ProtocolDefectError` when `music21`'s octave-shift reading disagrees with
   MusicXML after annotation. Excluding overlapping same-staff octave shifts in
   the screen changes the accepted sample rule and requires an owner-approved,
   independently reviewed protocol amendment with calibration coverage.
4. **Resumed execution status, `formal-3` stop, and protocol routing:**
   W1 formal execution was paused in
   [#1488 comment 6729151812](https://github.com/hcoona/three/issues/1488#issuecomment-6729151812)
   during workstation transition and resumed by the repository owner in
   [#1488 comment 7029347075](https://github.com/hcoona/three/issues/1488#issuecomment-7029347075).
   Runtime observation: formal attempt `formal-3` passed `preflight` and
   `select` (`200` PDMX + `50` Lieder;
   [#1488 comment 7030522396](https://github.com/hcoona/three/issues/1488#issuecomment-7030522396))
   on clean commit `6f3c38f280264969b4afc00e970223e2a27726fb` (`MuseScore Studio 4.7.7.261109195`,
   `executable_sha256` `89d9145261eb2bec6aedc208b5234b92c01fe6bc5485fec7e2ce28ec05819f31`)
   and stopped in `baseline_content` on `l0033`
   ([#1488 comment 7031008701](https://github.com/hcoona/three/issues/1488#issuecomment-7031008701)),
   followed by the five-source triage, 23-piece gold audit, Phase 1 ABC 2.1
   addendum, and repository-owner Phase 1 dispositions recorded in
   [#1488 comment 7034443356](https://github.com/hcoona/three/issues/1488#issuecomment-7034443356),
   [#1488 comment 7058188830](https://github.com/hcoona/three/issues/1488#issuecomment-7058188830),
   [#1488 comment 7058919271](https://github.com/hcoona/three/issues/1488#issuecomment-7058919271)
   and
   [#1488 comment 7067355696](https://github.com/hcoona/three/issues/1488#issuecomment-7067355696).
5. **`formal-4` stop and `formal-5` completion:** `formal-4` (`5956790d`)
   completed `pdmx-x` (`200 / 200`) and stopped on `p0098/rx`
   ([#1488 comment 7071662321](https://github.com/hcoona/three/issues/1488#issuecomment-7071662321)).
   After `#1711` and `#1716` (`9171f870`), `formal-5` completed all arms,
   `replay-req012`, and `tokenizer-counts` with zero stops and was accepted by
   the repository owner
   ([#1488 comment 7083248609](https://github.com/hcoona/three/issues/1488#issuecomment-7083248609),
   [#1488 comment 7083509900](https://github.com/hcoona/three/issues/1488#issuecomment-7083509900)).
   No further W1 execution is authorized without a new accepted
   [Delivery Wave](../../../../../docs/delivery-wave.md) entry. If a future
   Delivery Wave entry authorizes follow-on formal runs on a Windows or Windows +
   WSL2 workstation:
    - preserve external `formal-1` through `formal-5` run directories and their
      selection and ledger artifacts until the repository owner deletes them
      after results/closure merge; never overwrite prior run directories or
      manually edit run artifacts to skip a stopping file;
    - follow the [comparison protocol](comparison-protocol.md) for
      [MuseScore 4.x provenance and WSL2/host isolation](comparison-protocol.md#tools-and-environment),
      [structural scope screening](comparison-protocol.md#structural-scope-screen),
      [failures and attributed upstream MuseScore re-export breakdown](comparison-protocol.md#failures),
      [supplementary checks](comparison-protocol.md#supplementary-checks),
      [code-tree binding, shared ledger, resumable selection, and split baseline steps](comparison-protocol.md#execution-and-bounds),
      and [stop conditions](comparison-protocol.md#stop-conditions), and
      follow [Validation basis](requirements.md#validation-basis) for
      `replay-req012` and `tokenizer-counts`;
    - for formal acceptance evidence, run corpus/gold execution,
      `replay-req012`, and `tokenizer-counts` from a clean `origin/main`
      checkout after any newly authorized protocol and converter changes are
      merged.

## External state, privacy and worktree hygiene

- **External run and download directories:** `harness/records.py:check_outside_repository`
  enforces that `RUN` and `DOWNLOADS` lie outside every local git worktree, and
  `cli.py` serializes mutating commands per run directory via `RUN/.run.lock`
  ([Execution and bounds](comparison-protocol.md#execution-and-bounds)). Never
  commit downloaded corpora, MuseScore scratch files, or run outputs
  ([OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance)).
  W1's `20,000,000,000`-byte download allowance ended when `#1488` was retired
  at a completed cumulative total of `2,350,276,855` bytes, W2/W3's
  `10,000,000,000`-byte download allowance ended when `#1489` and `#1490` were
  retired at `480,830,451` bytes in `w2-download-ledger.json` (`6 / 6` S0
  `TEST` touches in `w2-touch-log.jsonl`), and W4/W5's `15,000,000,000`-byte
  download allowance (`341,067,483` bytes across `204` entries in
  `w4-w5-download-ledger.json`), `50,000,000,000`-byte proof-output disk fuse
  (`889,390,301` bytes across `runs/w4-w5-*`), and `25,000`-page formal ledger
  (`1,215` settled pages, `1 / 20` `TRAIN_DEV` W5 starts, and `1 / 3` starts
  with `1 / 1` completed and locked `92`-page pass in
  `w4-w5-run-ledger.jsonl`) ended when the `PR #1752` Wave entry was retired;
  any new download, batch synthetic rendering, or private-page execution
  requires explicit bounds in a future Delivery Wave entry, and initializing a
  fresh authorized `DOWNLOADS` directory without `download-ledger.json` must
  pass `--prior-total 2350276855` and `--basis` on the first `preflight` or
  `download`.
- **Code-tree and MuseScore provenance binding:** `harness/cli.py:_require_preflight`
  and `harness/run.py:begin_run` enforce the clean project code-tree (`CODE_PATHS`)
  on formal runs (`pilot` permits uncommitted edits in a fresh `--run-dir`,
  recording `dirty: true` provenance) and per-run MuseScore identity invariants
  defined in
  [Tools and environment](comparison-protocol.md#tools-and-environment) and
  [Execution and bounds](comparison-protocol.md#execution-and-bounds). Start
  any newly authorized formal run in a fresh external run directory while
  keeping prior `formal-1` through `formal-5` directories untouched.
- **Private gold boundary (`OMR-REQ-015`):** read private gold inputs locally
  and read-only (`PR #1705`'s one-time staged `gold-01` tie-endpoint repair
  grant from [#1488 comment 7067355696](https://github.com/hcoona/three/issues/1488#issuecomment-7067355696)
  item 4 is spent and permits no further gold-file edits). Keep
  `RUN/gold/mapping.json` (`harness/privacy.py:GOLD_MAPPING`) confined to the
  local external run directory. Never commit, log, or publish private file
  paths, filenames, piece identities, or score content; public records may
  report only aggregate counts and anonymized batch numbers.
- **Worktree hygiene:** create new branches from a freshly fetched, clean
  `origin/main` worktree. Do not stage or revive superseded local prototypes
  left in historical developer worktrees—specifically any early
  `w1/omr-abc-roundtrip` checkout holding uncommitted
  `experiments/abc_roundtrip/` files (superseded by `#1525`) or any early C#
  `TextScore2.csproj` normalizer prototype (superseded by the Python package in
  `#1570`–`#1609`).

## Formal acceptance gates for future authorized harness runs

`#1488`'s W1 grant is closed; `#686` authorizes design and planning, while
contract-preserving routine maintenance without new external, paid, or privacy
effects follows the
[record policy](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency).
Within authorized `sheet-music-omr` code work, local harmless debugging on
working branches—including `pytest`, `ruff`, `pyrefly`, `harness view` on
scratch runs, and `harness pilot` on repository/synthetic fixtures in an
out-of-worktree temporary directory without network downloads or private-gold
access—runs freely without `origin/main`-merge preconditions or attempt quotas
([project namespaces](../../../../../docs/governance/record-system.md#project-namespaces)).
Dataset downloads, private-gold access
([OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance)), and
formal acceptance runs (`preflight`, `download`, `select`, `run`, `report`,
`replay-req012`, and `tokenizer-counts` reported as acceptance evidence)
require explicit Wave authorization for those external/privacy effects. With
out-of-repository `GOLD` (`--gold-status` and `--gold-cache`), `DOWNLOADS`
(carrying `download-ledger.json`, or initialized with `--prior-total
2350276855`), and the `OMR-REQ-012` desk-study archive staged outside OneDrive
and outside all repository worktrees, execute formal acceptance runs in order:

1. **Confirm clean code baseline on `main` for formal acceptance:** ensure the
   authorized record, protocol, converter, and comparator updates being
   evaluated for formal acceptance are merged on `origin/main` and
   `src/private/app/sheet-music-omr/` has a clean code tree. Never bypass a
   stop by editing run artifacts or skipping files manually.
2. **Verify host MuseScore 4 availability (Windows or WSL2):** confirm Windows
   host `MuseScore4.exe` (`4.x`) is reachable (default
   `C:\Program Files\MuseScore 4\bin\MuseScore4.exe` or
   `/mnt/c/Program Files/MuseScore 4/bin/MuseScore4.exe`, or pass `--musescore`
   / `--settings`). An absent `MuseScore4.ini` on a fresh installation is
   valid and uses the eight source defaults (rechecking source defaults per
   [Source basis](comparison-protocol.md#source-basis) if `MuseScore4.exe` is
   outside `4.7.6`/`4.7.7`).
3. **Run preflight in a fresh external run directory:**

    ```powershell
    uv run --package sheet-music-omr --group dev `
      python -m sheet_music_omr.harness preflight `
      --run-dir RUN_DIR --downloads DOWNLOADS `
      --prior-total 2350276855 `
      --basis "Carried forward from completed W1 formal-5 (#1488 comment 7083248609)"
    ```

    (Omit `--prior-total` and `--basis` if `DOWNLOADS/download-ledger.json` is
    already present.)

4. **Complete selection and publish the manifest before conversion:** run
   `download` and `select` (resumable across interruptions via
   `selection/*.progress.json`), then post the complete protocol-conforming
   selection list (up to 200 PDMX files and 50 Lieder extracts, or all passing
   candidates with the smaller count reported when fewer pass;
   [Selection before conversion](comparison-protocol.md#selection-before-conversion))
   to the governing Issue before invoking `run`. Before relying on results, also
   settle any remaining open check in
   [Evidence limits and open checks](comparison-protocol.md#evidence-limits-and-open-checks).
5. **Run all arms on the calibrated code tree and MuseScore build:** execute
   `run` and `report` across PDMX, Lieder, their MuseScore `R(X)` re-exports,
   the `X`/`R(X)` baselines, and read-only local gold (including the gold
   system-assembly test). Confirm the eight MusicXML preferences and MuseScore
   binary hash remain unchanged and surface any 300-second worker timeouts or
   excluded reference voices for owner review.
6. **Triage differences, review independently, and support owner spot-check:**
   classify every counted difference and failure type under the pre-posted
   [source triage method](https://github.com/hcoona/three/issues/1488#issuecomment-6721091552)
   with explicit evidence labels, complete independent `research-evidence-review`
   and applicable GOV-011 finding disposition, use local `harness view` for the
   owner's spot-check, and post sanitized results to the governing Issue.
7. **Complete auxiliary validation-basis protocols when required:** execute
   `replay-req012 --manifest` and `tokenizer-counts` on the completed formal run
   per [Validation basis](requirements.md#validation-basis)
   ([OMR-REQ-012](requirements.md#omr-req-012-toolchain-class-findings) and
   [OMR-REQ-014](requirements.md#omr-req-014-evidence-separation)), post the
   sanitized reports, and obtain owner acceptance before retiring the governing
   Delivery Wave entry.

## Formal baseline gates for future authorized W2 and W3 runs

`#1489` (W2) and `#1490` (W3) are accepted and closed on `w2-s0-formal-3`
([#1490 comment 7108804779](https://github.com/hcoona/three/issues/1490#issuecomment-7108804779),
[#1489 comment 7108805391](https://github.com/hcoona/three/issues/1489#issuecomment-7108805391),
owner acceptance dispositions in
[#1490 comment 7109703011](https://github.com/hcoona/three/issues/1490#issuecomment-7109703011)
and
[#1489 comment 7109703768](https://github.com/hcoona/three/issues/1489#issuecomment-7109703768)),
with their [Delivery Wave](../../../../../docs/delivery-wave.md) entry retired;
no further W2/W3 external downloads, private-gold alignment/inference, or `TEST`
scoring is authorized without a new accepted Delivery Wave entry. Within
contract-preserving routine maintenance, local unit tests and synthetic-fixture
debugging without external package installation, network downloads or
private-gold access run freely on working branches; if a future Delivery Wave
entry authorizes follow-on W2/W3 or recognizer-evaluation runs, execute
external and private-gold steps in order:

1. **Accept protocol on `main` before external or private-input effects:**
   ensure [OMR-REQ-016 through OMR-REQ-020](requirements.md#real-data-splits-and-recognizer-evaluation)
   and the [evaluation and real-data alignment protocol](evaluation-protocol.md)
   (including any required protocol amendment) are accepted on `origin/main`
   before installing external reader packages (`kobimusic/copisteria`,
   `w2-copista-venv`), downloading weights, generating real-page overlays or
   crops from private inputs, or running formal baseline measurements.
2. **Keep external installations and ledgers out of worktrees:** install
   `kobimusic/copisteria` and CPU PyTorch wheels only into
   `DOWNLOADS/w2-copista-venv`, track network bytes in
   `DOWNLOADS/w2-download-ledger.json` against an explicitly authorized Wave
   cap, and verify pinned commits and `SHA-256` digests before running
   inference.
3. **Complete W3 alignment, `92`-page overlay QA, and `0`-diff slice
   verification before scoring:** approve all `92 / 92` page overlays in
   `overlay-qa.json`, verify `0` content differences on re-assembled
   printed-system `Text Score 2` slices across all `23` pieces, and write
   `RUN/splits/test-exclusion.json`.
4. **Log `TEST` touches and privacy-scan outputs:** append to
   `DOWNLOADS/w2-touch-log.jsonl` before scoring `TEST`, perform zero tuning on
   `TEST`, validate `omr-discrepancy-log-v1` records, and pass the
   `OMR-REQ-015` privacy scan and applicable `research-evidence-review` before
   posting results to the governing Issue.
5. **Preserved `w2-s0-formal-3` baseline and exhausted S0 `TEST` touch ceiling:**
   runtime observation: on clean `origin/main` commit
   `385aa460d2f7634696799ecae5c4961c8709ca6d`, `w2-s0-formal-3` completed W3
   alignment (`92 / 92` approved overlays, `434` exported `14.0 px` crops,
   `23 / 23` zero-difference printed-system `Text Score 2` slice round-trips on
   the tested pieces) and W2 Stage S0 scoring (`4,070` validated
   `omr-discrepancy-log-v1` rows; `480,830,451` bytes in
   `w2-download-ledger.json`). `DOWNLOADS/w2-touch-log.jsonl` is at `6 / 6`
   recorded `"S0-baseline"` `TEST` touches (`S0_TEST_TOUCH_CAP = 6`), reaching
   the accepted protocol's S0 stop condition: no further S0 `TEST` evaluation
   may run without accepted Delivery Wave authorization together with an
   accepted protocol amendment establishing a new finite bound (downstream W6
   training pilots carry their own separate `3`-touch-per-pilot limit in
   [Test-set touch governance](evaluation-protocol.md#test-set-touch-governance-bounds-stops-and-cleanup)
   when authorized by a W6 Delivery Wave entry). Do not delete or edit prior
   `w2-touch-log.jsonl` lines or overwrite preserved `w2-s0-1` and
   `w2-s0-formal-1` through `w2-s0-formal-3` external run directories until the
   repository owner deletes them after closure merge.

## Formal proof and detector gates for future authorized W4 and W5 runs

W4a ([Issue #1493](https://github.com/hcoona/three/issues/1493)) and W5
([Issue #1497](https://github.com/hcoona/three/issues/1497)) are accepted and
closed, and the W4a–W4d synthetic engine and Stage S3 proof on
[Issue #1491](https://github.com/hcoona/three/issues/1491) are accepted at
`main` commit `abef22536dbd82ca654fa7cd599204c4e59efd62`
([#1491 comment 7130836402](https://github.com/hcoona/three/issues/1491#issuecomment-7130836402),
[#1497 comment 7130837035](https://github.com/hcoona/three/issues/1497#issuecomment-7130837035),
owner acceptance dispositions in
[#1491 comment 7138762309](https://github.com/hcoona/three/issues/1491#issuecomment-7138762309)
and
[#1497 comment 7138763006](https://github.com/hcoona/three/issues/1497#issuecomment-7138763006)),
with the `PR #1752` [Delivery Wave](../../../../../docs/delivery-wave.md) entry
retired (`#1491` and `#1494`–`#1496` remain open solely for the deferred
`20k`–`50k`-page bulk pilot generation and W6 augmentation ablation). No
further external tool/corpus download, batch synthetic rendering, detector
training, or private-page execution is authorized without a new accepted
Delivery Wave entry (and the single zero-tuning `92`-page W5 box-geometry count
report in `DOWNLOADS/w4-w5-run-ledger.jsonl` is spent and locked against rerun
absent an accepted protocol amendment). Keep `DOWNLOADS/w4-w5-run-ledger.jsonl` permanently preserved and append-only,
and preserve settled download history in `DOWNLOADS/w4-w5-download-ledger.json`
(permitting only normal locked reservation and settlement updates under future
Wave authorization) — never delete or reset either ledger, and carry both
forward into any future Wave-authorized `DOWNLOADS` root (starting in a fresh
or missing `DOWNLOADS` root would hide the recorded `locked92` single-pass stop
and is prohibited). Do not overwrite
preserved external `w4-w5-formal-1`, `w4-w5-formal-2`,
`w4-w5-formal-w5-traindev` and `w4-w5-formal-w5-locked92` run directories
unless the repository owner deletes those run directories after closure merge.
