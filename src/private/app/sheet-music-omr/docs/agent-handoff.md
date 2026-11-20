# Sheet Music Recognition Agent Handoff

## Authority and purpose

Read this page before changing `src/private/app/sheet-music-omr/` or running its
measurement harness. It owns change routing, prior formal-run and external-state
boundaries, and ordered execution gates; it is not a second requirements record,
format specification, comparison protocol, or work-authorization file. The
[project portal](../README.md) owns purpose, user context, command-line usage,
and top-level record routing.

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md) is the sole
positive work authority ([Issue #686](https://github.com/hcoona/three/issues/686)
for sheet-music recognition planning and
[Issue #1488](https://github.com/hcoona/three/issues/1488) for W1 text
representation). A pull request, local run record, or issue comment cannot
enlarge that authorization or waive an accepted gate. Chronological progress,
reviews, and findings remain in those Issues and their linked pull requests.

## Change routing

Read the rows that match the task; when several rows apply, read their union.
Before architecture, abstraction, security/recovery, or test-strategy decisions,
also apply [shared engineering guidance](../../../../../.github/skills/engineering-guidance/SKILL.md).

| Change                                                         | Read                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Text Score 2 lexical rules, grammar, or canonical form         | [Text Score 2 format](text-format.md) ([Lexical rules](text-format.md#lexical-rules-and-preprocessing), [Grammar](text-format.md#grammar), [Canonical form](text-format.md#canonical-form)); [OMR-REQ-005](requirements.md#omr-req-005-canonical-form); `lexical.py`, `parser.py`, `writer.py`, `tests/test_documents.py`, `tests/test_lexical.py`                                                                                                                                                                                                |
| Semantic validation, carry state, or system assembly           | [Text Score 2 format](text-format.md) ([Semantics](text-format.md#semantics), [Carry state and assembly](text-format.md#carry-state-and-assembly)); [OMR-REQ-004 through OMR-REQ-007](requirements.md#representation-invariants); `semantics.py`, `carry.py`, `assembly.py`, `relations.py`, `timeline.py`, `review.py`                                                                                                                                                                                                                           |
| Forward or reverse MusicXML conversion                         | [Text Score 2 format](text-format.md) ([Transcription rules](text-format.md#transcription-rules), [Category encodings](text-format.md#category-encodings), [Layout, unsupported content and open points](text-format.md#layout-unsupported-content-and-open-points)); [OMR-REQ-008](requirements.md#omr-req-008-forward-and-reverse-conversion), [Unsupported content](requirements.md#unsupported-content); `musicxml_forward.py`, `musicxml_reverse.py`, `musicxml_tree.py`, `musicxml_events.py`, `beams.py`, `pitches.py`, `measures.py`      |
| Round-trip comparator, pairing rules, or supplementary checks  | [Comparison protocol](comparison-protocol.md) ([Comparison instrument](comparison-protocol.md#comparison-instrument), [Category assignment](comparison-protocol.md#category-assignment), [Calibration](comparison-protocol.md#calibration), [Layout-fidelity score](comparison-protocol.md#layout-fidelity-score)); [OMR-REQ-009](requirements.md#omr-req-009-comparison-protocol); `comparison.py`, `supplementary.py`, `tests/test_comparison.py`                                                                                               |
| Corpus screening, MuseScore isolation, runner, or reporting    | [Comparison protocol](comparison-protocol.md) ([Tools and environment](comparison-protocol.md#tools-and-environment), [Inputs and score identities](comparison-protocol.md#inputs-and-score-identities), [Execution and bounds](comparison-protocol.md#execution-and-bounds), [Stop conditions](comparison-protocol.md#stop-conditions), [Evidence, publication and cleanup](comparison-protocol.md#evidence-publication-and-cleanup)); [OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance); `src/sheet_music_omr/harness/` |
| Acceptance claims, ABC capability mapping, or tokenizer counts | [Acceptance](requirements.md#acceptance), [Evidence and boundaries](requirements.md#evidence-and-boundaries), [Validation basis](requirements.md#validation-basis); [research-evidence review](../../../../../.github/skills/research-evidence-review/SKILL.md)                                                                                                                                                                                                                                                                                   |
| Project records or navigation only                             | [Project portal](../README.md), [record policy](../../../../../docs/governance/record-system.md), [family catalog](../../../../../docs/governance/record-families.yaml), [record-system review](../../../../../.github/skills/record-system-review/SKILL.md)                                                                                                                                                                                                                                                                                      |

## Accepted W1 baseline on main

All completed W1 specifications, converters, comparison logic, and execution
harness components are merged on `main` (source findings from repository
history):

- **Authorization and normative records:** Delivery Wave entries (`#1504`,
  amended in `#1525` to replace the ABC round-trip gate with ABC capability
  mapping), [requirements](requirements.md) and
  [Text Score 2 format](text-format.md) (`#1537`, `#1547`, `#1559`, `#1564`,
  `#1569`), and the [comparison protocol](comparison-protocol.md) with its
  accepted calibration, voice-position, and octave-shift amendments (`#1571`,
  `#1591`, `#1598`, `#1605`, `#1608`).
- **Core Python package (`sheet_music_omr`):** parser and canonical writer
  (`#1570`), semantic validator and carry-out derivation (`#1572`), system
  assembler (`#1577`), forward MusicXML 4.0 converter (`#1578`), reverse
  MusicXML converter (`#1579`), and calibrated round-trip comparator (`#1607`).
- **Measurement run harness (`sheet_music_omr.harness`):** preflight,
  byte-bounded download ledger, structural scope screen, deterministic PDMX and
  OpenScore Lieder selection, isolated MuseScore invocation, bounded worker pool,
  read-only private-gold execution, reporting, measure-level `view`, and
  repository-fixture `pilot` (`#1609`, merged at
  `397b306f27cf51e15f58d44f5b67c1c0626f1ade`).

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

## Pre-run baselines and open W1 evidence items

The following pre-execution artifacts and open obligations are recorded on
[Issue #1488](https://github.com/hcoona/three/issues/1488):

- **Source triage method (ready):** fixed before formal execution in
  [#1488 comment 6721091552](https://github.com/hcoona/three/issues/1488#issuecomment-6721091552)
  (source finding). Every counted difference or failure type in a formal run is
  attributed to one of five sources: converter defect, format gap or ambiguity,
  MuseScore export or import, comparator/screen/harness defect, or unsupported
  content in gold.
- **Structural-screen allowlist review (passed):** independent Round 2 review
  passed across all eight calibration pair tables (`322` texts, `71` screen
  tests) before `#1609` merged, recorded in
  [#1488 comment 6721238428](https://github.com/hcoona/three/issues/1488#issuecomment-6721238428)
  (source finding).
- **Pilot and probe limits:** the final single-fixture harness pilot on the
  rebased `#1609` branch
  ([#1488 comment 6728291229](https://github.com/hcoona/three/issues/1488#issuecomment-6728291229))
  is a runtime observation on repository fixtures only, not corpus or gold
  acceptance evidence. Earlier pilots and local gold probes
  ([#1488 comment 6722886585](https://github.com/hcoona/three/issues/1488#issuecomment-6722886585))
  ran before `#1608` was accepted and do not count as formal protocol runs.
- **ABC 2.1 capability mapping (completed):** the earlier `xml2abc`/`abc2xml`
  execution
  ([#1488 comment 6614290511](https://github.com/hcoona/three/issues/1488#issuecomment-6614290511))
  is retained only as an incomplete toolchain runtime observation (`#1525`).
  After `#1547` (`eb4986a5`) clarified in
  [Validation basis](requirements.md#validation-basis) that source-only
  standards comparisons do not require an experiment protocol, the desk ABC 2.1
  capability mapping
  ([#1488 comment 6639491389](https://github.com/hcoona/three/issues/1488#issuecomment-6639491389))
  completed independent `research-evidence-review`
  ([#1488 comment 6642511739](https://github.com/hcoona/three/issues/1488#issuecomment-6642511739)),
  and its `#1569` (`6bc166ac`) addendum covering `heading`, review markers, and
  heading `carry-in` also completed independent `research-evidence-review` and
  GOV-011 triage
  ([#1488 comment 6672103690](https://github.com/hcoona/three/issues/1488#issuecomment-6672103690))
  (source findings; each mapping cell is a source-backed inference). Revisit
  this mapping only if a later accepted change modifies the categories or
  representation invariants in `requirements.md` or `text-format.md`.
- **Unstarted W1 acceptance and auxiliary evidence:** no formal corpus or gold
  round-trip measurement has completed (`OMR-REQ-010`, `OMR-REQ-011`, and the
  non-gating layout score in `OMR-REQ-013`), the `OMR-REQ-012` toolchain-class
  finding set is not yet frozen, and W1 tokenizer counts
  ([Delivery Wave](../../../../../docs/delivery-wave.md) `#1488`,
  [OMR-REQ-014](requirements.md#omr-req-014-evidence-separation), and
  [Validation basis](requirements.md#validation-basis)) have not yet been
  protocol-reviewed or executed.

## Prior formal-run state and resumption boundary

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
4. **Resumed execution status, preserved external runs, and protocol routing:**
   W1 formal execution was paused in
   [#1488 comment 6729151812](https://github.com/hcoona/three/issues/1488#issuecomment-6729151812)
   during workstation transition and resumed by the repository owner in
   [#1488 comment 7029347075](https://github.com/hcoona/three/issues/1488#issuecomment-7029347075).
   When executing on the active Windows + WSL2 workstation:
    - preserve the external `formal-1` and `formal-2` run directories and their
      partial selection and ledger artifacts intact (on the prior workstation or
      in their archived copies); never overwrite them or manually edit run
      artifacts to skip a stopping file;
    - follow the [comparison protocol](comparison-protocol.md) for the updated
      [MuseScore 4.x provenance and WSL2/host isolation](comparison-protocol.md#tools-and-environment),
      [octave-shift span screening](comparison-protocol.md#structural-scope-screen),
      [code-tree binding, shared ledger, resumable selection, and split baseline steps](comparison-protocol.md#execution-and-bounds),
      and [stop conditions](comparison-protocol.md#stop-conditions);
    - run formal corpus/gold execution (`formal-3`) only from a clean
      `origin/main` checkout after the accepted protocol and Wave baseline are
      present on `main`; read-only local preparation of the `OMR-REQ-012`
      toolchain-class finding set may proceed in parallel under the accepted W1
      Wave entry.

## External state, privacy and worktree hygiene

- **External run and download directories:** `harness/records.py:check_outside_repository`
  enforces that `RUN` and `DOWNLOADS` lie outside every local git worktree, and
  `cli.py` serializes mutating commands per run directory via `RUN/.run.lock`
  ([Execution and bounds](comparison-protocol.md#execution-and-bounds)). Never
  commit downloaded corpora, MuseScore scratch files, or run outputs
  ([OMR-REQ-015](requirements.md#omr-req-015-privacy-and-fixture-provenance)).
  When initializing a newly created `DOWNLOADS` directory that has no shared
  `download-ledger.json` yet, pass `--prior-total 2343244452` (plus any later W1
  downloads) and `--basis` on the first `preflight` or `download`.
- **Code-tree and MuseScore provenance binding:** `harness/cli.py:_require_preflight`
  and `harness/run.py:begin_run` enforce the clean project code-tree (`CODE_PATHS`)
  and per-run MuseScore identity invariants defined in
  [Tools and environment](comparison-protocol.md#tools-and-environment) and
  [Execution and bounds](comparison-protocol.md#execution-and-bounds). Start
  resumed formal runs in a fresh external run directory (`formal-3` or later)
  while keeping `formal-1` and `formal-2` untouched.
- **Private gold boundary (`OMR-REQ-015`):** read private gold inputs locally
  and read-only. Keep `RUN/gold/mapping.json` (`harness/privacy.py:GOLD_MAPPING`)
  confined to the local external run directory. Never commit, log, or publish
  private file paths, filenames, piece identities, or score content; public
  records may report only aggregate counts and anonymized batch numbers.
- **Worktree hygiene:** create new branches from a freshly fetched, clean
  `origin/main` worktree. Do not stage or revive superseded local prototypes
  left in historical developer worktrees—specifically any early
  `w1/omr-abc-roundtrip` checkout holding uncommitted
  `experiments/abc_roundtrip/` files (superseded by `#1525`) or any early C#
  `TextScore2.csproj` normalizer prototype (superseded by the Python package in
  `#1570`–`#1609`).

## Execution gates when external artifacts are staged

With out-of-repository `GOLD` (`--gold-status` and `--gold-cache`), `DOWNLOADS`
(restored from the previous machine or initialized with `--prior-total
2343244452`), and the `OMR-REQ-012` desk-study archive staged outside OneDrive
and outside all repository worktrees (resumed in
[#1488 comment 7029347075](https://github.com/hcoona/three/issues/1488#issuecomment-7029347075)),
execute these steps in order:

1. **Confirm clean code baseline on `main`:** ensure the protocol and harness
   updates are merged to `origin/main` and `src/private/app/sheet-music-omr/`
   has a clean code tree. Never bypass a stop by editing run artifacts or
   skipping files manually.
2. **Verify host MuseScore 4 availability (Windows or WSL2):** confirm Windows
   host `MuseScore4.exe` (`4.x`) is reachable (default
   `C:\Program Files\MuseScore 4\bin\MuseScore4.exe` or
   `/mnt/c/Program Files/MuseScore 4/bin/MuseScore4.exe`, or pass `--musescore`
   / `--settings`). An absent `MuseScore4.ini` on a fresh installation is
   valid and uses the eight source defaults.
3. **Run preflight in a fresh external run directory:**

    ```powershell
    uv run --package sheet-music-omr --group dev `
      python -m sheet_music_omr.harness preflight `
      --run-dir RUN_FORMAL_3 --downloads DOWNLOADS `
      --prior-total 2343244452 `
      --basis "Carried forward from formal-1/formal-2 (#1488 comment 6728507174)"
    ```

    (Omit `--prior-total` and `--basis` if `DOWNLOADS/download-ledger.json` is
    already present.)

4. **Complete selection and publish the manifest before conversion:** run
   `download` (reusing verified local archives at 0 network bytes when copied
   from the prior machine) and `select` (resumable across interruptions via
   `selection/*.progress.json`), then post the complete protocol-conforming
   selection list (up to 200 PDMX files and 50 Lieder extracts, or all passing
   candidates with the smaller count reported when fewer pass;
   [Selection before conversion](comparison-protocol.md#selection-before-conversion))
   to `#1488` before invoking `run`. Before relying on heading results, also
   settle any open check in
   [Evidence limits and open checks](comparison-protocol.md#evidence-limits-and-open-checks)
   (such as `credit` elements without a `credit-type` on the selected files).
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
   and GOV-011 triage, use local `harness view` for the owner's spot-check, and
   post sanitized results to `#1488`.
7. **Complete remaining W1 exit gates:** iterate reviewed converter or format
   changes until one converter commit satisfies `OMR-REQ-010` and `OMR-REQ-011`
   (reporting the non-gating `OMR-REQ-013` layout score alongside them), freeze
   and replay the `OMR-REQ-012` toolchain-class set, complete protocol-reviewed
   tokenizer counts ([Delivery Wave](../../../../../docs/delivery-wave.md)
   `#1488`, [OMR-REQ-014](requirements.md#omr-req-014-evidence-separation), and
   [Validation basis](requirements.md#validation-basis)), and obtain owner W1
   acceptance before retiring the `#1488` Delivery Wave entry.
