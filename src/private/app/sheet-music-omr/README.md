# Sheet Music Recognition

This project defines how photographed pages of printed piano music become
editable notation. A recognizer reads one printed grand-staff system at a time
into text, and deterministic tools validate, assemble and convert that text to
MusicXML. The records here define the text representation and the evidence its
tools must meet.

**Current state:** experimental; W1 text representation
([Issue #1488](https://github.com/hcoona/three/issues/1488)) is accepted and
closed on `formal-5` (`main` commit `9171f870684a37e0ca5a526387ea69d92e740882`;
results in
[#1488 comment 7083248609](https://github.com/hcoona/three/issues/1488#issuecomment-7083248609),
owner acceptance disposition in
[#1488 comment 7083509900](https://github.com/hcoona/three/issues/1488#issuecomment-7083509900)).
A Python package parses Text Score 2 documents, validates their grammar and
semantics, reports each error with its rule class and location, lists review
markers, writes canonical text, derives the carry-out state of a system
document and assembles system documents into one score. A forward converter
turns a valid assembled score without review markers into MusicXML 4.0, and a
reverse converter turns a MusicXML piano part on two staves back into a valid
assembled score. A round-trip comparison instrument and execution harness run
the [comparison protocol](docs/comparison-protocol.md) and the auxiliary
protocols in the [validation basis](docs/requirements.md#validation-basis).
On `formal-5` (`MuseScore Studio 4.7.7.261109195`), the accepted private gold
cohort (with `gold-01` evaluated on the one-time owner-authorized staged
tie-endpoint repair of [PR #1705](https://github.com/hcoona/three/pull/1705)
and the original private repository and remaining `22` gold pieces kept
read-only; see [Agent handoff](docs/agent-handoff.md)) met `OMR-REQ-010` at
`100.0000%` (`30,716 / 30,716`, `0` counted differences, `23 / 23` import
checks passed, `23 / 23` text-space consistent, `87.8583%` layout fidelity) and
`23 / 23` equal system-assembly pieces (`493` systems), all four public corpus
gating pools met `OMR-REQ-011` (`pdmx-x` `99.6360%`, `pdmx-rx` `93.8568%` raw
and `99.6995%` across the `197` files not solely corrupted by upstream
MuseScore 4.7.7 export defects, `lieder-x` `99.8140%`, `lieder-rx` `99.8435%`),
the frozen `OMR-REQ-012` replay reproduced `327 / 327` deduplicated loci (`356`
raw entries, `0` failed), and `Qwen3-VL-2B-Instruct` tokenizer counts were
recorded alongside the completed ABC 2.1 capability mapping. With W1's
[Delivery Wave](../../../../docs/delivery-wave.md) entry retired on acceptance,
[PR #1734](https://github.com/hcoona/three/pull/1734) authorized W2
([Issue #1489](https://github.com/hcoona/three/issues/1489)) and W3
([Issue #1490](https://github.com/hcoona/three/issues/1490)) alongside design
and planning under [Issue #686](https://github.com/hcoona/three/issues/686),
and the [evaluation and real-data alignment protocol](docs/evaluation-protocol.md)
defines the W3 real-data split/alignment schema and W2 Stage S0 baseline
protocol (`#1739`, implemented in `#1746`–`#1749`). W2
([Issue #1489](https://github.com/hcoona/three/issues/1489)) and W3
([Issue #1490](https://github.com/hcoona/three/issues/1490)) are accepted and
closed on `w2-s0-formal-3` (`main` commit
`385aa460d2f7634696799ecae5c4961c8709ca6d`; runtime observations in
[#1490 comment 7108804779](https://github.com/hcoona/three/issues/1490#issuecomment-7108804779)
and
[#1489 comment 7108805391](https://github.com/hcoona/three/issues/1489#issuecomment-7108805391),
owner acceptance dispositions in
[#1490 comment 7109703011](https://github.com/hcoona/three/issues/1490#issuecomment-7109703011)
and
[#1489 comment 7109703768](https://github.com/hcoona/three/issues/1489#issuecomment-7109703768)):
W3 completed the unified
`23`-piece alignment/split manifest (`7`-piece `TEST` `432` measures / `111`
printed systems vs. `16`-piece `TRAIN_DEV` `1,601` measures / `323` printed
systems), `92 / 92` page overlay approvals (`434` `14.0 px` normalized system
crops), and `23 / 23` zero-difference printed-system `Text Score 2` slice
round-trips on the tested pieces, while W2 recorded fail-closed end-to-end Stage S0 drafter and blind
second-reader baselines (`TEST` Layer-1 `A`-error rate `0.458333` for
`agent-build-01` vs. `0.979167` for stock `copista-28m@44464d1` where `20 / 23`
`TEST` page outputs failed validation and were scored fail-closed, ratio
`2.136364`, `95%` CI `1.651282..2.835939`; `TRAIN_DEV` `0.463460` vs.
`0.896315` where `49 / 69` `TRAIN_DEV` page outputs failed validation and were
scored fail-closed, ratio `1.933962`), the `225`-row `T01` review-pass model
re-fit, and `4,070` schema-validated `omr-discrepancy-log-v1` records (`6 / 6`
S0 `TEST` touches spent, `480,830,451 / 10,000,000,000` download bytes). Under
[PR #1752](https://github.com/hcoona/three/pull/1752) and the
[synthetic data and system-detection protocol](docs/synthetic-protocol.md)
([PR #1755](https://github.com/hcoona/three/pull/1755), implemented in `#1757`,
`#1760` and `#1761`), the W4a–W4d synthetic
engine & Stage S3 proof
([Issue #1491](https://github.com/hcoona/three/issues/1491); W4a
[Issue #1493](https://github.com/hcoona/three/issues/1493) closed, `#1491` and
`#1494`–`#1496` retaining the deferred `20k`–`50k`-page bulk pilot and W6
ablation) and W5
([Issue #1497](https://github.com/hcoona/three/issues/1497), accepted and
closed) were accepted at `main` commit
`abef22536dbd82ca654fa7cd599204c4e59efd62` (runtime observations in
[#1491 comment 7130836402](https://github.com/hcoona/three/issues/1491#issuecomment-7130836402)
and
[#1497 comment 7130837035](https://github.com/hcoona/three/issues/1497#issuecomment-7130837035),
owner acceptance dispositions in
[#1491 comment 7138762309](https://github.com/hcoona/three/issues/1491#issuecomment-7138762309)
and
[#1497 comment 7138763006](https://github.com/hcoona/three/issues/1497#issuecomment-7138763006)):
W4a admitted `440` `permissive` piano scores (`380` `PDMX` + `60` `OpenScore
Lieder`, `14,907` measures, `0` `TEST` overlap), Stage S3 (`w4-w5-formal-2`)
evaluated `84` distinct round-trip scores per engine (`MuseScore Studio
4.7.7.261109195`: `37` passed / `47` dropped, `105` `Tier R` pages; `Verovio
6.4.1`: `54` passed / `30` dropped / `213` pre-excluded,
`disabled_categories = ['clef', 'cross_staff', 'grace', 'hairpin', 'ottava']`,
`141` `Tier R` pages, `50` verified overlay pages/engine, and `6 / 6`
sim-to-real crop descriptors enclosing the real `TRAIN_DEV` median on both
engines), and W5 (`w4-w5-formal-w5-traindev` and `w4-w5-formal-w5-locked92`)
recorded blind system-detection counts (`comb_dp_blind` on `TRAIN_DEV`:
`318 / 323 = 98.45%` exact 2-staff matches, `0` merged, `0` split/truncated,
and `4` ink-clipped matches driving `recommend_training = true` from
`TRAIN_DEV` alone; descriptive `TEST` geometry pass: `107 / 111 = 96.40%` exact
2-staff matches, `0` merged, `0` split/truncated, `3` ink-clipped;
`copisteria_front` `89 / 434` merged and `343 / 434` split/truncated; offline
`homr_cli` `92 / 92` `unscorable_page_frame`) and `16 / 16` (`323 / 323`
systems) zero-difference `TRAIN_DEV` slice assembly plus `5 / 5` fault-class
rejections (`341,067,483 / 15,000,000,000` download bytes, `889,390,301 /
50,000,000,000` proof disk bytes, `1,215 / 25,000` formal pages, `1 / 1` locked
`92`-page pass spent). With the W1, W2/W3 and W4/W5 execution
[Delivery Wave](../../../../docs/delivery-wave.md) entries retired on
acceptance, current work authorization and routine-maintenance boundaries
follow the [Delivery Wave](../../../../docs/delivery-wave.md) and
[record policy](../../../../docs/governance/record-system.md#work-authorization-and-concurrency)
([Issue #686](https://github.com/hcoona/three/issues/686) is the overall
planning work carrier; see [Agent handoff](docs/agent-handoff.md)). There is no
trained recognizer, model packaging or release.

## Usage

From the repository root:

```shell
uv run --package sheet-music-omr sheet-music-omr validate FILE...
uv run --package sheet-music-omr sheet-music-omr canonicalize FILE
uv run --package sheet-music-omr sheet-music-omr assemble FILE... [-o OUTPUT]
uv run --package sheet-music-omr sheet-music-omr to-musicxml FILE [-o OUT]
uv run --package sheet-music-omr sheet-music-omr from-musicxml FILE [-o OUT]
uv run --package sheet-music-omr --group dev pytest src/private/app/sheet-music-omr/tests
```

`validate` prints one `path:line:column: class/rule: message` line for each
error and one `review:` line for each review marker, and exits with 1 when a
file is invalid. `canonicalize` writes the canonical text of one valid file.
`assemble` writes the assembled score to standard output or `OUTPUT`, and
reports errors and review items on standard error. It exits with 0 on
success, 1 on failure and 3 when review markers or extended trills need a
person.
`to-musicxml` writes the MusicXML 4.0 of one valid assembled score to standard
output or to `OUT`. It exits with 1 and writes no MusicXML when the score is
invalid, holds review markers or holds content that the converter cannot
convert faithfully.
`from-musicxml` reads a `.musicxml` or compressed `.mxl` file and writes the
canonical text of the assembled score to standard output or to `OUT`. It exits
with 1 and writes no score when the file is unreadable, is not one part on two
staves or holds content that the converter cannot convert faithfully.

The W1 round-trip measurement harness runs the
[comparison protocol](docs/comparison-protocol.md) on Windows or from WSL2
using the Windows host's MuseScore Studio 4 installation. Its run and download
directories must be outside every local repository worktree:

```powershell
uv run --package sheet-music-omr --group dev `
  python -m sheet_music_omr.harness COMMAND --run-dir RUN --downloads DOWNLOADS [options]
```

`preflight` and `pilot` invoke `pytest` in the active interpreter, so include
`--group dev` when running the harness.
The commands, in order, are `preflight` (with `--prior-total` and `--basis`
when initializing a fresh shared `DOWNLOADS/download-ledger.json`), `download`,
`select`, `run` (with `--gold-status`, `--gold-cache` and `--jobs`), `report`,
`replay-req012` (with `--manifest` for the frozen `OMR-REQ-012` replay) and
`tokenizer-counts` (for the W1 tokenizer-counts protocol). `preflight`,
`select`, `run` and `pilot` accept optional
`--musescore` and `--settings` path overrides. `view --id ID [--measure N]`
shows one file's differences by measure, and `pilot [--fixtures FILE...]` runs
every phase on repository fixtures. A stop condition is recorded in
`RUN/run.json` and exits with 2.

The W4/W5 [synthetic and detector CLI](docs/synthetic-protocol.md) exposes its
arguments through:

```shell
uv run --package sheet-music-omr python -m sheet_music_omr.synthetic --help
```

Use `setup-w4-w5` to provision the metered external tools, `intake-w4a` to
build the audited permissive source partition, `run-s3` for the dual-renderer
proof, `run-w5-detectors` (`--mode traindev` or `--mode locked92 --formal`) for
the system-detector benchmark and assembly checks, and
`validate-assembly --expected-systems K FILE...` to validate ordered system
documents without starting a proof run. Directory confinement, formal execution
bounds, isolation rules, and single-pass gates are specified in the
[synthetic data and system-detection protocol](docs/synthetic-protocol.md#environment-tool-isolation-and-mutable-source-rechecks);
external provisioning, batch rendering and private-page runs require an
accepted [Delivery Wave](../../../../docs/delivery-wave.md) entry.

Each worker call has one 300-second bound, which includes starting a fresh
worker. Some calls hold more than one of the protocol's steps: the content
pass with the supplementary checks, the text-space check's two reverse
conversions, and each gold piece's whole system-assembly test (the baseline's
content and layout passes run in separate 300-second worker calls). A timeout
of one of these calls is counted as a failure under the protocol's failure
rule, but the bound is stricter than the protocol's per-step bound, so the
results carrier lists any such timeout for the owner to review before its
figures are used.

The W2/W3 evaluation and real-data alignment CLI runs the
[evaluation and real-data alignment protocol](docs/evaluation-protocol.md)
with external `--run-dir` and `--downloads` directories:

```powershell
uv run --package sheet-music-omr `
  python -m sheet_music_omr.evaluation COMMAND --run-dir RUN --downloads DOWNLOADS [options]
```

The sequence is `setup-copista`, `align`, visual inspection of all 92
`RUN/overlays/<gold_id>-p<NN>.png` images (updating
`RUN/alignment/box-overrides.json` and re-running `align` if any box clips
notation), `approve-overlays --overlays-sha256 SHA256 --note NOTE`,
`run-copista` and `score-s0` (with `--formal` on `align`, `run-copista` and
`score-s0` for clean-tree formal runs).

## User and primary journey

The user is the repository owner, who turns photographed pages of piano scores
into editable notation and reviews the result against the page. The intended
journey is:

1. A page photograph is divided into grand-staff systems.
2. A recognizer writes each system as a system document of the text format.
3. Tools validate the documents, assemble them into one score and convert it to
   MusicXML for editing.
4. The owner reviews the score against the page and corrects it.

The records here cover the text format, real-data alignment and splits, and the
requirements and evaluation protocols its tools and candidate recognizers must
meet. Recognizer training and workflow integration are later workstreams. Their
direction and research context are tracked in
[Issue #686](https://github.com/hcoona/three/issues/686), W1 text
representation in [Issue #1488](https://github.com/hcoona/three/issues/1488), W2
evaluation and baselines in
[Issue #1489](https://github.com/hcoona/three/issues/1489), W3 real-data
inventory and splits in
[Issue #1490](https://github.com/hcoona/three/issues/1490), W4 synthetic
training-data engine in
[Issue #1491](https://github.com/hcoona/three/issues/1491) (`#1493`–`#1496`),
and W5 system detection and assembly in
[Issue #1497](https://github.com/hcoona/three/issues/1497).

Text Score 2 is derived from the owner's as-printed text convention but is
specified anew, with a versioned normative grammar. It is the pilot baseline
representation. It is not a finding that it outperforms ABC, MusicXML or
another representation. That comparison needs model evidence that this project
does not yet produce.

## Records and authority

| Reader question                                                                 | Authority                                                                  |
| ------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| What must the format, its tools and their acceptance/baseline evidence achieve? | [Requirements](docs/requirements.md)                                       |
| What exactly is a valid system document or assembled score?                     | [Text Score 2 format](docs/text-format.md)                                 |
| How is a converter round trip compared and scored?                              | [Comparison protocol](docs/comparison-protocol.md)                         |
| How are real-data splits, system crops and recognizer baselines evaluated?      | [Evaluation and real-data alignment protocol](docs/evaluation-protocol.md) |
| How are synthetic training pairs, Stage S3 gates and W5 detectors evaluated?    | [Synthetic data and system-detection protocol](docs/synthetic-protocol.md) |
| How do I route a change or operate formal/baseline runs safely?                 | [Agent handoff](docs/agent-handoff.md)                                     |
| What work is currently authorized?                                              | Repository [Delivery Wave](../../../../docs/delivery-wave.md)              |
| Where are progress, reviews and findings?                                       | The Issues above and their linked pull requests                            |

The README owns purpose, user context, current status and routing. The
requirements own the obligations of the format, its tools and their acceptance
evidence. The format owns the grammar and semantics. None of these grants work
or replaces the accepted repository policies. The private inputs and accepted
transcriptions that acceptance uses stay private. The requirements state that
boundary in OMR-REQ-015.

## Record admission and maintenance

The project authors produce these records, and the repository owner maintains
them with the authors. Converter, validator and harness implementers and their
independent reviewers consume the requirements before choosing behavior or an
acceptance measure. Without them each implementation would invent its own
coverage and its own meaning of "no difference", and a result could be judged
against the tool that produced it. The requirements need their own carrier
because the Delivery Wave authorizes work only for a bounded time and is
deleted when accepted, and Issues carry progress rather than durable
obligations.

The same implementers and reviewers consume the format as the single
definition of a valid document. Without it the validator, the converters and any
recognizer training data would disagree about validity, and round-trip results
would not be comparable. The format needs a separate carrier because it changes
through versioned grammar and semantic decisions that a reviewer checks against
the grammar, while the requirements change with acceptance obligations.
Merging them would bury the obligations in a reference text.

The round-trip harness implementers, their independent reviewers and W1
acceptance consume the comparison protocol, which fixes the comparison
dependencies, MuseScore 4 provenance bounds, inputs, element measure and
category assignment of every round-trip result. Without it
the harness would choose its own measure after seeing results. It needs its
own carrier because an accepted protocol must be fixed before formal acceptance
execution and changes under research-evidence review before formal acceptance
runs rely on it, independently of format and requirement changes. Issues carry
its results, not the protocol.

W2 and W3 implementers, their independent reviewers, and downstream W4 through
W8 workstreams consume the
[evaluation and real-data alignment protocol](docs/evaluation-protocol.md),
which fixes the unified `23`-piece alignment/split manifest schema, `92`-page
overlay QA and `14.0 px` system-crop rules, `B`-normalized per-category
recognition (`A`-class) and blind second-reader metrics, test-set touch ledger,
isolated `copista-28m` installation/execution bounds, and discrepancy logging
schema for formal baseline evidence and private-input operations. Without it,
splits, masks or thresholds could shift after inspecting recognizer outputs,
and noisy OMR predictions would collide with the W1 converter protocol's
fail-stop rules.

W4 and W5 implementers, their independent reviewers, and downstream W6 through
W8 training consumers use the
[synthetic data and system-detection protocol](docs/synthetic-protocol.md)
before installing external rendering/detector tools, fetching optional corpora,
running Stage S3 synthetic batches, or benchmarking blind system detectors on
staged facsimile pages. It needs a carrier distinct from `requirements.md` and
the closed W1/W2-W3 protocols because it binds engine-specific coordinate
scales, MEI/SVG round-trip gates, `rights_variant` and `TEST`-exclusion digest
and work-identity checks, offline `AGPL` CLI isolation, and the dedicated W4/W5
download and formal-run ledgers without altering the retired Stage S0
recognizer rules.

Replacement coding agents and contributors consume the
[agent handoff](docs/agent-handoff.md) before modifying project files or
operating the harness across sessions. It owns change routing, prior formal-run
and external-state/ledger invariants, and ordered execution gates. Without it a
fresh session must reconstruct operational hazards and stop boundaries from
dozens of Issue comments and risks repeating environment stops, disturbing
preserved external runs, or bypassing required pre-conversion gates. It needs a
distinct carrier so this README stays a concise project overview while normative
requirements, grammar and comparison rules remain separate from operational
handoff routing.

Review execution, findings and chronological progress stay in Issues and pull
requests. When implementation or execution status changes, update this README
and the handoff rather than creating a progress ledger.
