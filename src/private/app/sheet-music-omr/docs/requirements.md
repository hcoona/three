# Sheet Music Recognition Requirements

These are required behaviors for the text representation of printed piano
music, its converters, its real-data splits and recognizer evaluation tools,
and their acceptance and baseline evidence, not claims about an existing
implementation. The [README](../README.md) supplies context and routing. The
[Text Score 2 format](text-format.md) is the normative grammar and semantics;
this record states what that format, its tools and the evaluation harness must
achieve.

The format defines _system document_, _assembled score_, _content_ and
_layout_. An _in-scope element_ is a score element that belongs to one of the
content categories in OMR-REQ-002 and is not unsupported content; the
comparison protocol of OMR-REQ-009 makes the definition exact. An _accepted
private gold piece_ is a transcription that the repository owner's review
process has accepted. Its identity and content stay private.

## Scope

### OMR-REQ-001: Recognition target

The target shall be the printed content of one grand-staff system of a piano
score, with the heading text printed above it, written as one system document. System documents of consecutive systems
shall assemble into one assembled score, which deterministic converters turn
into MusicXML for the existing editable-score workflow. The format is for piano
music on two staves. It is not a general music language and not a replacement
for MusicXML. Handwritten additions to a printed copy, such as pencil
annotations, are not part of that content, and a transcription neither encodes
nor reports them.

Which header values of a system document (key, meter, clefs, carry mode,
position and carry state) a recognizer is given instead of producing is outside
this record.

### OMR-REQ-002: Notation coverage

The format shall encode every content category below, with the encoding that the
Category encodings section of the format defines:

- pitch and rhythm: `pitch`, `rhythm_rest`, `chord_membership`, `accidental`,
  `voice`, `cross_staff`, `tuplet`, `grace`
- staff context: `clef`, `key_signature`, `time_signature`, `ottava`
- grouping and relations: `beam`, `tie`, `slur`, `arpeggio`, `tremolo`,
  `ornament`
- marks: `articulation_fermata`, `fingering`, `dynamic`, `hairpin`, `pedal`
- text and structure: `text`, `tempo_rehearsal`, `repeat_ending`, `heading`

A required printed distinction that the format cannot encode shall appear under
Unsupported content below instead of being omitted silently. The format
enumerates the variants within each category, and an unenumerated variant is
unsupported, not absent.

### OMR-REQ-003: Content and layout

The format's definitions of content and layout apply. Group membership and
symbol identity are content even when a group is drawn graphically, while curve
shape, spacing and font metrics are layout. The format shall encode the layout
hints that its specification lists, and a layout hint shall never count as a
content difference. The `up` and `down` stem hints themselves are layout;
the voice-line assignment determined by the format's voice-order rules is
group-membership content.

## Representation invariants

### OMR-REQ-004: As-printed observations

The text shall record what the page shows, and derived readings shall follow
from it by rule. In particular:

- pitch is the written pitch, which includes the octave change of a transposing
  clef and excludes an ottava line, and sounding pitch is derived from it;
- a printed accidental, including a redundant or parenthesized one, is
  recorded, and no unprinted accidental is added;
- common-time and cut-time symbols stay distinguishable from the numeric meter;
- explicit beam grouping is recorded instead of derived from durations;
- a tie, a slur, an arpeggio and the extent of an ornament or tremolo keep
  their endpoints and membership.

### OMR-REQ-005: Canonical form

Every valid document shall have one canonical spelling that a deterministic
normalizer produces. Documents that differ only in spelling (for example
ordering, defaults, reprints, escapes and whitespace) shall have the same
canonical text. Normalization shall never erase a content distinction or an
encoded layout hint.

### OMR-REQ-006: Explicit failure

A validator shall accept every valid document and reject every other document
with its rule class and location. A tool that meets invalid or unsupported
input shall report the item with its location and shall not report success. A
converter may still return the result for the in-scope content. Conversion and
assembly shall never invent a rest, infer an unprinted accidental or drop a
mark while reporting success.

Assembly shall report an unfilled range of a measure instead of inventing rests,
and shall report an extended trill whose wavy line continues into a later
system and stops there before any note for a person to resolve instead of
completing it.

Printed content whose reading the page leaves undetermined, as the format's
transcription rules define it, shall be held in the document by a review marker
instead of being guessed, and the marker may carry candidate readings. A
validator shall accept such a document and list each marker with its location
and candidates. Assembly and the converters shall report each marker in the
same way and shall not report success.

Finer coverage or confidence information (a recognition sidecar) is not part of
this format and needs its own record admission.

### OMR-REQ-007: System carry state and assembly

A system document shall carry the context needed to interpret it: key, meter,
clefs, the accidental carry mode, the position within a measure that is split
across systems, the events already written there and the relationships that are
still open. The carry state at the end of one system shall equal the carry state
at the start of the next. A split shall not reset timing or accidental state.

Except where OMR-REQ-006 requires assembly to report an extended trill, assembly
shall restore one score with no duplicated or lost event or relationship, and
with the same content as an unsplit transcription.

## Conversion and comparison

### OMR-REQ-008: Forward and reverse conversion

Converters shall be written in this repository and shall not import the private
workflow's converter source. The forward converter shall turn a valid assembled
score into MusicXML that a MuseScore import accepts and that carries every
in-scope content category. The reverse converter shall turn the in-scope content
of a MusicXML score, including MuseScore, PDMX and OpenScore exports, into an
assembled score, shall report each omitted construct that is unsupported content
and shall not report success when it omits one. A grammar validator shall
implement the grammar and semantic rules of the format.

### OMR-REQ-009: Comparison protocol

Comparing a source score with its round trip for formal acceptance evidence
shall follow a protocol accepted under research-evidence review before that
formal acceptance run executes (local in-scope unit tests and repository- or
synthetic-fixture debugging without network downloads or private-gold access
under OMR-REQ-015 remain unconstrained by pre-execution protocol merge under
the
[repository record policy](../../../../../docs/governance/record-system.md#project-namespaces)).
The protocol shall fix the in-repository comparison dependencies and their
versions, bound external desktop score editors by major version while recording
each run's exact version string and executable hash as provenance, define an
in-scope element and _identical_, state the denominator of every percentage and
say how generated identifiers are treated, since an identifier difference alone
is not a content difference. It
shall assign every difference to exactly one of the 27 content categories or to
layout. A difference that cannot be assigned is a protocol defect, never
agreement. Missing content and extra content both count as differences.
The [comparison protocol](comparison-protocol.md) is that protocol.

## Acceptance

### OMR-REQ-010: Private gold

On every accepted private gold piece, the round trip (reverse converter, then
forward converter) shall show zero in-scope content differences from the source
score. Splitting the piece at its printed system breaks into system documents
and assembling them shall also reproduce the whole-piece encoding with zero
in-scope content differences.

### OMR-REQ-011: Public corpus fidelity

On public corpus files whose licenses permit the use, and on their MuseScore
re-exports, at least 99% of in-scope elements shall be identical after the
round trip. Raw MuseScore re-export (`R(X)`) arm scores shall keep every failed
file's reference elements counted as non-identical in the denominator without
converter workarounds; when `X` passes the structural scope screen and converts
without failure and `R(X)` fails whole-file conversion or `musicdiff` parsing
solely because of one of the three minimal-reproduced, upstream-reported
MuseScore 4 MusicXML export defects recorded in the comparison protocol, the
report shall list those files, their element counts and their upstream issues
separately alongside the raw `R(X)` score, and the 99% re-export acceptance
threshold shall be evaluated across the remaining `R(X)` files not corrupted by
those three upstream export defects. The comparison protocol fixes the files,
the selection rule and the denominator before execution. This result measures
the converters and the importer on those files. It does not show that the
format is complete or that any model can learn it.

### OMR-REQ-012: Toolchain-class findings

The validation basis shall freeze the set of recorded review findings that are
_toolchain-class_: a recorded review finding of an error that arose from the
format, converter, importer or exporter toolchain rather than from reading the
printed page. Each member of the frozen set shall be reproduced correctly from
the text without score-file edits. Public records report the set and its replay
results only as OMR-REQ-015 allows.

### OMR-REQ-013: Layout fidelity

A layout-fidelity score, where higher is better, shall be reported for the gold
pieces and the public corpus files. Its definition and comparison basis belong
to the comparison protocol and shall be recorded before the score is reported.
The score shall not gate acceptance.

## Real-data splits and recognizer evaluation

### OMR-REQ-016: Unified real-data alignment and piece-level split manifest

A single versioned local manifest shall inventory all `23` accepted private
gold pieces (`2,033` measures across `92` restored US-Letter facsimile pages)
at piece, page and physical printed grand-staff system levels, recording each
system's facsimile-page bounding box, local staff-space pixels, measure-ordinal
range, barline coordinate status, box trust, overlay-QA approval status,
book/plate group (`G1` through `G7`), label trust tier (`L0` all five review
aspects converged versus `L1` per-aspect converged with explicit aspect masks),
and documented open-reading measure-category masks. Splits shall never divide a
piece: `7` pieces (`gold-01` through `gold-06` in Book 1 plus `gold-18` in Book
2; `432` measures, `23` pages) form the frozen `TEST` set, and the remaining
`16` accepted model-draft-free pieces (`1,601` measures, `69` pages) form the
non-test `TRAIN/DEV` pool. An explicit `TEST` exclusion manifest (`SHA-256`
digests and anonymized batch identifiers) shall exclude `TEST` pieces from
training, prompt or threshold tuning, and synthetic re-rendering.

### OMR-REQ-017: Overlay QA, normalized system crops and printed-system slices

Every one of the `92` restored facsimile pages shall complete visual bounding-box
overlay QA before its system crops enter baseline evaluation or downstream
datasets (with raw `.heic` captures warped through stored page quads into the
same schema if later staged locally). Each approved printed system shall yield
a staff-space-normalized system crop (`14.0` pixels per staff space with padded
margins preserving ledger lines, pedals, dynamics, hairpins, ottavas and slurs)
paired with its canonical per-system `Text Score 2` target slice derived along
the physical printed-system measure-ordinal boundaries. Re-assembling all
printed-system slices of each of the `23` pieces and converting forward to
MusicXML shall reproduce the accepted gold score with zero counted in-scope
content differences (`23 / 23` equal under OMR-REQ-009).

### OMR-REQ-018: B-normalized per-category recognition and second-reader scorer

For formal Stage S0 baseline and recognizer evaluation evidence, evaluation
rules, `B`-normalization, open-reading masks, candidate parse-failure
accounting and statistical estimators shall follow the
[evaluation and real-data alignment protocol](evaluation-protocol.md) accepted
under research-evidence review before external reader installation, weight
downloads, private-input crop/overlay runs or formal S0 baseline execution
(local in-scope unit tests and synthetic-fixture debugging without external
installs, network downloads or private-gold access remain unconstrained under
[project namespaces](../../../../../docs/governance/record-system.md#project-namespaces)).
In drafter mode, the primary metric shall be the paired measure-level
recognition (`A`-class) error rate (share of reference measures with at least
one unmasked `A`-class content difference after normalizing
representation/toolchain `B`-class artifacts and separating layout `C`-class
differences), reported overall, per review aspect and per category with
piece-cluster bootstrap `95%` confidence intervals alongside `musicdiff` `6.0`
normalized edit distance; unparsable or wrong-staff-count candidate outputs
shall fail closed by counting all unmasked reference measures and symbols of
the failed unit as non-identical rather than aborting evaluation. In blind
second-reader mode, the scorer shall report recall of baseline draft `A`-error
measures (and late-round survivor measures) within a `<= 20%` flagged-measure
budget, error-correlation rate, per-measure confidence calibration (`AUROC`,
`ECE`, `AURC`) where posteriors exist, and expected review-pass projections.

### OMR-REQ-019: Test-set touch governance and Stage S0 baselines

Every evaluation run that reads or scores `TEST` predictions shall append an
immutable record to a local touch ledger (`w2-touch-log.jsonl`) naming the UTC
timestamp, code commit and tree digest, reader and weight SHA-256 digests,
split subset and command; training pilots shall touch `TEST` at most `3` times
with all touches reported, and Stage S0 baselines shall perform zero parameter
or threshold tuning on `TEST` within a finite cumulative S0 `TEST`-touch fuse.
Stage S0 shall measure and report (a) the initial agent drafts (`build-01` or
`draft.musicxml` against accepted gold on `TEST` and `TRAIN/DEV` separately,
re-fitting the review-pass model parameters against the historical review
archive) and (b) stock `copista-28m` (`Apache-2.0`, executed locally in an
isolated out-of-worktree environment) in both drafter and blind second-reader
modes on the staged restored facsimile pages.

### OMR-REQ-020: Discrepancy, sidecar and round-logging schema

The evaluation harness shall emit and validate a versioned JSON schema
(`omr-discrepancy-log-v1`) capturing per-measure candidate discrepancies,
declared reader capability masks, raw and non-`TEST`-fitted calibrated
confidence, flag decisions, and `A/B/C/R` by category review-round records for
downstream training, workflow integration and flywheel workstreams, subject to
OMR-REQ-015 privacy scanning before any public summary is written.

## Synthetic training-data engine and system-detection benchmark

### OMR-REQ-021: Rights-segregated symbolic intake and TEST exclusion

Every ingested symbolic score and derived synthetic sample shall carry an
explicit `rights_variant` (`permissive`, `nc_sa` or `private_traindev`) stored
in physically separated manifest and output partitions; sources with unverified
or non-permitted licenses and public image-plus-partial-label sets (`GrandStaff`,
`Camera-GrandStaff`, `FP-GrandStaff`, `OLiMPiC`, `Polish-scores`, `SMB`) shall
be rejected fail-closed from training manifests. Every retained source file
shall be filtered or extracted to a single two-staff piano part, pass the
structural scope screen (`OMR-REQ-001` through `OMR-REQ-008` and
[Unsupported content](#unsupported-content)), parse in `music21`, convert
cleanly to canonical `Text Score 2` and forward MusicXML 4.0, record per-measure
category densities and coverage gaps relative to real-book targets, and pass
fail-closed `TEST`-exclusion verification against `test-exclusion.json`,
raw-file `SHA-256` digests and authorized non-notation work-identity metadata
(rejecting missing or uninformative composer/title metadata as well as any
batch-ID, `SHA-256` digest or normalized composer/work/catalogue match, without
parsing `TEST` gold notation).

### OMR-REQ-022: Labeled annotation injection and unlabeled clutter separation

A deterministic, seeded annotation injector shall augment canonical symbolic
piano scores toward real-book category densities across `N6` fingering (digits
`1` through `5`, chord-stacked, placed above or below across a mixture spanning
fingering-free through dense pieces), `N1` dynamics, `N2` hairpins, `N3/N4`
expression and tempo text, `N5` pedal (both text-and-star and bracket-line
styles), `L2` slurs (including overlapping, cross-staff and explicit-side
slurs), `L3` articulations and fermatas, `L4` arpeggios and `P3` ottavas,
emitting an explicit per-sample injection log while weighting source selection
toward `D2` tuplets, `D4` cross-staff notation, `P2` clef changes and `N8`
barlines, repeats and voltas. Every injected musical notation mark shall be
present in the rendered sample's ground-truth `Text Score 2` label; non-notation
visual clutter (pencil marks, handwritten digits, show-through and gutter
shading) shall be composited strictly on rasterized page images after label
derivation so labels with and without clutter remain byte-identical.

### OMR-REQ-023: Dual-engine layout rendering and round-trip label gates

Batch rendering drivers for host `MuseScore 4.x` CLI (primary) and isolated
`Verovio 6.x` (secondary) shall sample style/font parameters and system/page
break densities spanning real plate groups (`G1` through `G7`), emitting
`~300 dpi` untrimmed page rasters, verified `1:1` rendered-measure-to-block
ordinals, system and measure bounding boxes mapped to page pixels (accounting
for MuseScore's `1 / 12` `.mpos`/`.spos`-to-PNG-pixel scale and `0`-based
sidecar page index, and Verovio's nested SVG `viewBox`/margin transform,
expanded vertically to enclose all assigned staff and annotation ink), and
per-system `Text Score 2` slices with explicit carry-in headers. every rendered
sample shall pass a fail-closed round-trip label gate (verifying MuseScore's
MusicXML re-export via `compare_file` and `musicdiff 6.0`, and verifying
Verovio's MEI export via `music21` + `musicdiff 6.0` plus direct MEI checks for
facets `musicdiff` omits before slicing canonical `Text Score 2`), dropping any
failed sample, excluding any disabled category or unchecked W1 supplementary
facet from emitted samples, and reporting per-category drop rates across at
least `50` pieces per engine before Stage S3 statistics are accepted. Per
[OMR-REQ-014](#omr-req-014-evidence-separation), the re-export and `Text Score
2` round-trip gates verify symbolic preservation of the checked facets (trusting
the renderer to paint them rather than verifying each mark's pixel legibility in
the PNG), and diagnostic Verovio SVG glyph checks establish SVG element presence
only without waiving a failed symbolic gate.

### OMR-REQ-024: Facsimile-residue degradation, warped crops and statistics

With the default recognizer input domain locked to restored US-Letter facsimile
PDFs (`~303 dpi`), `Tier R` degradation shall apply reproducible seeded
page-level restoration-residue transforms (mild residual mesh warp,
resampling/deblur blur, illumination and paper-tint variation, mirrored
show-through, gutter shading and unlabeled pencil/handwritten clutter),
co-warping system and measure bounding boxes through the forward geometric map
to emit YOLO-format page box labels and `14.0`-pixel-per-staff-space
grand-staff system crops paired with canonical `Text Score 2` targets and
carry-in state. Stage S3 shall record `>= 100`-page per-engine throughput, peak
memory and bytes per page, verify YOLO system overlays on `>= 50` pages per
engine (`0` missed or merged systems), and compare synthetic `Tier R` crops
against W3's `TRAIN_DEV` real crops across staff-space, staff-line thickness,
blur, noise, contrast and residual warp slope.

### OMR-REQ-025: Blind system-detection benchmark and assembly validator

Formal W4 Stage S3 and W5 evaluations shall follow the
[synthetic data and system-detection protocol](synthetic-protocol.md) accepted
under research-evidence review before external tool/package installation, tool
or corpus network fetches, batch synthetic rendering or private-page detector
benchmarking (routine local unit/scenario tests and repository-fixture runs
without network fetches or private-gold access remain unconstrained under
[project namespaces](../../../../../docs/governance/record-system.md#project-namespaces)).
W5 shall benchmark blind off-the-shelf system detectors (repository `K`-free
`comb + DP`, `kobimusic/copisteria` layout front-end, and offline external
`homr` CLI) against W3's pixel-approved grand-staff boxes (`69` `TRAIN_DEV`
pages with zero `TEST` parameter tuning, plus at most one completed locked
zero-tuning `92`-page per-book-group box-geometry count report that never reads
`TEST` notation labels), reporting missed, merged, split, spurious
false-positive and wrong-staff-count systems (or `unscorable_page_frame` when
exact page-coordinate mapping cannot be verified) per book group (`G1` through
`G7`) to decide whether a trained YOLO detector is warranted. A deterministic
candidate system-assembly validator shall check the exact expected `(page,
system)` tuple sequence and reuse `assemble` and forward MusicXML 4.0 conversion
to verify carry links, measure continuity, spanner closure and `music21` parse
validity supporting gate `G3 >= 99%`, returning structured diagnostics without
silently repairing corrupt musical content.

## Pilot dataset packaging, clean training stack and per-system baseline

### OMR-REQ-026: Expanded intake, work split and crop packaging

Bulk synthetic dataset generation for W6
([Issue #1498](https://github.com/hcoona/three/issues/1498)) and W4
([Issue #1491](https://github.com/hcoona/three/issues/1491)) shall reuse the
`60` already-admitted `OpenScore Lieder` `v3.0.0` (`CC0-1.0`) scores and expand
newly admitted `PDMX` (`no_license_conflict` `CC0-1.0` and Public Domain Mark)
two-staff piano scores from staged local archives (`0` new corpus download
bytes) toward `2,500`–`5,000` verified `permissive` piano scores under
[OMR-REQ-021](#omr-req-021-rights-segregated-symbolic-intake-and-test-exclusion),
applying reviewed composer transliteration alias canonicalization **both**
during `TEST`-exclusion matching/auditing and before split assignment while
rejecting uninformative or contradictory composer-and-catalogue identities
fail-closed **before** admission into either split. Admitted scores shall be
grouped by canonicalized composer identity (scoping ambiguous and composer-local
catalogue prefixes including `K.`, `L.`, `D.`, `S.`, `Op.`, `WoO` and `No.`
strictly to canonical composer, requiring corroborating composer metadata for
single-composer prefixes, and stopping fail-closed if validation-quota minimums
cannot be met) into disjoint `synth_train` (target `90%`) and `synth_val`
(target `10%`) quotas **before** annotation injection or rendering variants are
created, slicing packaged datasets only at complete rendered-variant boundaries
(`variant_id`, `system_index`, `expected_systems`, `is_final_system`). Packaged
`permissive` `Tier R` `14.0 px` grand-staff crop datasets at **`20,000` crops**
(Arm A milestone) and **`50,000` crops** (Arm B and page-detector milestone,
paired with co-warped page `system` and `measure` boxes) shall route
`Verovio`-disabled categories (`clef` mid-score changes, `cross_staff`, `grace`,
`hairpin`, `ottava`) and unchecked W1 supplementary facets to `MuseScore 4.x`,
record an explicit per-sample `category_supervision_mask` that masks
disabled-category token logits out of training softmax denominators (or excludes
Verovio rows when subwords overlap) and excludes Verovio rows from those
categories' validation denominators so disabled categories are never supervised
or scored as negatives, and report realized component/work/score split counts,
unique source-measure coverage and per-measure repetition quantiles (`p50`,
`p90`, `p99`, `max`).

### OMR-REQ-027: Clean permissive recognizer and page-detector model stack

All W6 model definitions, metadata files and checkpoints in
`DOWNLOADS/w6-model-meta` and `DOWNLOADS/w6-model-weights` shall carry strictly
`Apache-2.0`, `MIT` or `BSD-3-Clause` licenses (with `trust_remote_code=False`,
zero remote Python code fetches, and strictly `.safetensors` /
`.safetensors.index.json` weight files), and supporting Python library wheels
installed into isolated out-of-worktree `DOWNLOADS/w6-train-venv` (from PyPI
and PyTorch's official CPU wheel index) or `DOWNLOADS/w6-gpu-venv` (from PyPI
and PyTorch's official CUDA `cu132` wheel index for local non-redistributed
RTX 4090 GPU execution) shall carry permissive,
non-strong-copyleft, non-NC, non-revenue-restricted licenses (`Apache-2.0`,
`MIT`, `MIT-CMU`, `BSD-2-Clause`, `BSD-3-Clause`, `PSF-2.0` / `Python-2.0`,
`ISC`, `Unlicense`, unmodified transitive `MPL-2.0 AND MIT` on `tqdm`, PyTorch
CUDA `Apache-2.0` / `BSD` / `LLVM-exception` / `MIT` / `Zlib` third-party
notices, and local non-redistributed `LicenseRef-NVIDIA-Proprietary` solely on
`cuda-toolkit` and `nvidia-*` GPU runtime library wheels in `w6-gpu-venv`,
failing closed on `GPL`/`AGPL` wheel packages, `CC-BY-NC` or revenue-threshold
external assets while keeping the first-party repository package under its
existing license and leaving W4/W5 external CLI renderer rechecks under
[synthetic-protocol.md](synthetic-protocol.md)). The trainable
matrix shall comprise: (a) **Arm A** small VLMs emitting `Text Score 2`
(`Qwen/Qwen3-VL-2B-Instruct` control plus `4B` capacity option,
`Qwen/Qwen3.5-2B` non-thinking mode, and `PaddlePaddle/PaddleOCR-VL-1.6` native
`28x28` `px/token` grid); (b) **Arm B** `~30M`–`60M` rectangular vision encoders
(`timm/convnext_tiny.fb_in22k_ft_in1k` fusing `stride 8` and `stride 16`
features, and `zenosai/MonkeyOCRv2-S` `384`-dim native `patch-14` trunk
unpermuted to raster order) paired with an autoregressive `Text Score 2`
Transformer decoder; and (c) **2-class (`system`, `measure`) page detectors**
(`ustc-community/dfine-small-coco` primary and `PekingU/rtdetr_v2_r18vd` control
with `do_pad=False` stride-`32`-aligned aspect-preserving resize `(W_32, H_32)`,
stock labeled `model(pixel_values=..., labels=...)` training forward preserving
`num_denoising` and auxiliary loss terms, and post-processed pixel coordinates
clamped to `[0, W_orig] x [0, H_orig]`), evaluating `measure` boxes on
`synth_val` and `system` boxes on both `synth_val` and held-out `TRAIN_DEV`
folds (`AP75` on scored detectors, matched `IoU >= 0.85`, ink containment,
optional blind-comb two-staff W5 matching and downstream assembly integrity)
against `comb_dp_blind`. In CPU Slice A, every dataset loader, labeled
forward/backward training step (asserting active denoising loss terms on both
detectors), checkpoint save/resume path, processor token-grid audit and
inference entry point shall pass deterministic tiny-model CPU smoke tests
without downloading multi-gigabyte foundation weights on the CPU host.

### OMR-REQ-028: Constrained decoding, 4-fold real validation and W7 sidecar

Blind held-out recognizer inference shall propagate predicted `carry_out` and
end `(key, meter, clefs)` header state sequentially from system `0` (failing
remaining systems of a piece closed with `"upstream_carry_gap"` after an
abstention rather than guessing unreprinted meter, key, clef or bar-number state
across a gap), enforce both an online
**per-measure** token cap and a system-total token cap, and validate candidate
`Text Score 2` system outputs through the parser, semantic validator,
measure-duration check and a shared `assembly.py` prefix validator within a
bounded beam/retry budget (`<= 3` attempts), recording an explicit abstention
reason and system-level abstention sidecar row (separating mandatory abstention
review from the `20%` calibrated measure-flag budget) rather than silently
repairing invalid notation. Across W3's `16` accepted `TRAIN_DEV` pieces (`323`
printed systems, `1,601` measures, `69` pages), the protocol shall pre-register
`4` plate-group-disjoint validation folds (keeping every plate group `G1`, `G2`,
`G4`, `G5`, `G6`, `G7` intact so each training fold retains `>= 236` real
systems) for real-crop model selection, leak-free out-of-fold temperature or
Platt confidence calibration (using two-fold-held-out `{0, 1, 2, 3} \ {f, g}`
inner predictions when calibrating replay branches), W7 sidecar emission
(`omr-w7-sidecar-v1` as defined in
[training-protocol.md](training-protocol.md#pre-registered-4-fold-train_dev-split-calibration-w7-sidecar-and-slice-b-gates),
emitting `omr-discrepancy-log-v1` strictly on aligned `TRAIN_DEV` and `TEST`
evaluations), and Slice B's paired local-only `synthetic continuation vs.
synthetic + TRAIN_DEV replay` ablation.

### OMR-REQ-029: Offline per-system copista-28m TRAIN_DEV baseline

Before GPU recognizer training, Slice A (`w6-slice-a-copista-formal-2`, now
completed and retired with its `DOWNLOADS/w6-slice-a-run-ledger.jsonl`
preserved read-only for Slice B) shall measure a zero-training
per-system crop baseline reserving `1` ledger slot **before** opening any
locator or private file, resolving inputs strictly beneath a dedicated
symlink-free, worktree-disjoint `TRAIN_DEV`-only directory root
(`--traindev-root`, containing `69` `TRAIN_DEV` page PNGs, `16` accepted
`accepted.musicxml` files, `16` Stage S0 initial `draft.musicxml` files, and a
`TRAIN_DEV`-only `traindev-manifest.json`, verified against the exact `102`-leaf
relative-path allowlist and `asset_locks.py`'s committed portable aggregate
`SHA-256` digest without host-inode or permission sealing — never opening
`batch-status.json`, `test-exclusion.json` or the `23`-piece W3 manifest, and
never committing per-file private hashes or boxes), and feeding W5's blind
`comb_dp_blind` 2-staff crops from the `69` `TRAIN_DEV` pages (`0` `TEST`
touches) into a single persistent offline `unshare -rn` worker with a
`300`-second per-page heartbeat timeout that terminates the worker process group
cleanly on stall and settles without spawning a replacement worker. Piece scoring shall preserve `(page, system)` boundaries, restrict
candidate-to-reference measure alignment strictly to each crop's `1:1`-matched
oracle system range, force unmatched spurious crops to incur W2 extra-measure
penalties, and score all measures of missed, merged, split or unparsed oracle
systems fail-closed under
[OMR-REQ-018](#omr-req-018-b-normalized-per-category-recognition-and-second-reader-scorer),
reporting the measured `TRAIN_DEV` parse-survival, Layer-1 `A`-error and blind
second-reader baseline alongside whole-page Stage S0 `copista-28m`.

### OMR-REQ-030: Slice A execution governance, ledgers and zero-TEST-touch isolation

All historical W6 Slice A external installations, expanded W4a intake runs, bulk
`20k`/`50k`-crop synthetic rendering runs and private-input `copista-28m`
per-system runs (completed and retired on `main`; Slice B imports Slice A's
settled `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`
read-only and routes active GPU training/evaluation to W6 Slice B) shall follow
the
[W6 training, bulk-packaging and page-detector protocol](training-protocol.md)
accepted on `main` under record-system review, research-evidence review and
shared engineering guidance (routine local unit, scenario and
repository/synthetic-fixture CPU smoke tests without network fetches or
private-input access remain unconstrained). Slice A shall enforce a committed
wheel and file-digest lock manifest alongside persistent non-resettable
`DOWNLOADS/w6-slice-a-download-ledger.json` (`15,000,000,000`-byte cap),
`RUNS/w6-slice-a-*` (`100,000,000,000`-byte disk cap and `15,000` rendered pages
per run), and append-only `DOWNLOADS/w6-slice-a-run-ledger.jsonl` pre-start
reservations (`35,000` cumulative rendered pages across all non-exempt rendering
runs, and `10` `1:1` single-worker private-input `copista-28m` per-system
invocations charged regardless of purpose, page count, `--formal` flag or
outcome), attesting the `TRAIN_DEV` input allowlist before opening files and
keeping frozen `TEST` (`7` pieces, `111` systems) at strictly `0` touches
throughout Slice A while preserving prior W1, W2/W3 and W4/W5 ledgers unchanged.

### OMR-REQ-031: Slice B GPU training, replay and single TEST pass

All W6 Slice B cross-host imports, CUDA wheel and `.safetensors` checkpoint
provisioning (`setup-w6-slice-b`), `E0` zero-shot baselines, Arm A / Arm B /
page-detector GPU training (`train-w6-gpu`), `4`-fold `TRAIN_DEV` evaluation
(`eval-w6-traindev`), matched-step local `TRAIN_DEV` replay and production
calibrator pre-registration (`run-w6-replay`), and the single frozen-`TEST`
evaluation pass (`run-w6-pilot-final`) shall execute strictly locally inside
WSL2 on the repository owner's RTX 4090 (`24 GB`, `local_gpu_only`) workstation
under the accepted
[W6 training, bulk-packaging and page-detector protocol](training-protocol.md)
(keeping routine offline repository- and synthetic-fixture tests unconstrained).
Slice B shall: (a) verify all `7` carried-forward W1 through Slice A campaign
ledgers read-only by `SHA-256`, the packaged `w6-slice-a-package-formal-6`
dataset digests (`w6-crops-20k` and `w6-crops-50k`), and the portable `102`-leaf
`--traindev-root` allowlist (`EXPECTED_TRAINDEV_PORTABLE_SHA256`) without
scanning mixed `23`-piece directories during setup or `TRAIN_DEV` runs; (b)
reserve exact locked artifact payload bytes before HTTPS transfer in persistent
`DOWNLOADS/w6-slice-b-download-ledger.json` within a `60,000,000,000`-byte
(`60 GB`) ceiling (reusing verified cached wheels/metadata/weights at `0` new
bytes and charging partial/failed payload reads); (c) enforce persistent
append-only `DOWNLOADS/w6-slice-b-run-ledger.jsonl` pre-start reservations
(`<= 40` GPU training starts at an `8`-hour timeout, `<= 30` offline
`unshare -rn` `TRAIN_DEV` evaluation starts at a `300`-second per-page
heartbeat timeout, and serialized local soft caps of `150,000,000,000` bytes
across `RUNS/w6-slice-b-*` and `5,000` optional diagnostic synthetic pages); and
(d) complete `E0` baselines, synthetic-only `4`-fold `TRAIN_DEV` selection,
matched-step `TRAIN_DEV` replay (`open_reading_masks` loss masking and
two-fold-held-out `{0, 1, 2, 3} \ {f, g}` inner replay calibration), out-of-fold
production calibrator fitting, and pre-registration
(`omr-w6-pilot-final-preregistration-v1`) before executing **at most `1`**
offline `unshare -rn` `"W6-pilot-final"` pass on frozen `TEST`, atomically
reserving and `fsync`ing both `w6-slice-b-run-ledger.jsonl` and line `7` of
`w2-touch-log.jsonl` (`1 / 3` cumulative pilot `TEST` touches under
[OMR-REQ-019](#omr-req-019-test-set-touch-governance-and-stage-s0-baselines))
**before** opening any `TEST` file.

## Evidence and boundaries

### OMR-REQ-014: Evidence separation

Representation capability, converter fidelity, tokenizer counts and model
accuracy are separate claims, and each result shall be reported as the claim
its method supports. A converter round trip tests one implementation on the
files it exercises. It neither shows that the format can express every required
distinction nor that another representation, such as a named ABC profile or
direct MusicXML, cannot. A comparison with another representation shall be
recorded under the claim its method supports: a capability mapping against the
content categories supports a capability claim, and a round trip supports a
fidelity claim. Tokenizer counts report counts for named tokenizers on named
inputs. Converter round trips and tokenizer counts make no claim about model
accuracy or review-effort savings; recognizer baseline measurements report
observed error rates and fitted pass-model projections only on the named
inputs, reader versions and protocol.

### OMR-REQ-015: Privacy and fixture provenance

Private inputs shall be read locally and read-only. No private path, piece
identity or private content, and nothing derived from them except aggregate
results and anonymized batch numbers, shall be committed or published. Every
committed fixture shall be self-authored, public domain or CC0, and shall
record its provenance. Downloaded public corpora stay outside the repository.

## Unsupported content

The format does not encode the following, and this list is the boundary. A
validator rejects a document that attempts to carry such content, and where a
rule of the format rejects a listed item, the format names the rule class. A
converter reports such content as OMR-REQ-006 and OMR-REQ-008 state.

- general MusicXML coverage, instruments other than piano on two staves,
  lyrics, chord symbols, figured bass and cue notes;
- multi-measure rests, measure-repeat signs, additive and unmetered meters, and
  key or meter changes inside a measure;
- segno and coda signs, brackets with end hooks, and extension lines other than
  a dashed line after expression text or an unhooked solid voice-leading line;
- breves, microtonal pitches, percussion, tablature and historical notation;
- barline types other than ordinary, final, double and repeat, and
  multi-number endings;
- clefs other than G, F and C on lines 1 to 5, with or without a change of one
  octave up or down;
- more than four voices on a staff, mixed-staff chords and duplicate chord
  members;
- unmeasured tremolo, ornament accidental marks other than one non-microtonal
  accidental mark on a trill, and fingering other than one digit from 1 to 5
  per pitch or two different such digits, adjacent or as a substitution, on a
  pitch;
- a whole-measure rest in a split measure, beams across a system or fragment
  boundary, and tuplet groups across a barline or a fragment boundary;
- heading text above a system that starts inside a measure;
- metronome units other than quarter, half, eighth, dotted quarter and dotted
  half.

Undetermined content is not unsupported content: a review marker holds it as
OMR-REQ-006 states. Playback-only data and engraving coordinates are neither
content nor unsupported content. A reverse converter ignores them.

## Validation basis

The evidence has three parts:

- Protocols. Formal acceptance runs and empirical experiments with external or
  private-gold effects (including formal comparison runs, tokenizer-count
  fetches, formal MuseScore import checks and any capability mapping reported
  from empirical corpus/gold runs) require a protocol accepted under
  research-evidence review before execution; routine in-scope local unit tests
  and repository- or synthetic-fixture debugging without network downloads or
  private-gold access under OMR-REQ-015 require no pre-execution protocol merge
  or attempt quota
  ([project namespaces](../../../../../docs/governance/record-system.md#project-namespaces)).
  A capability mapping done solely by comparing accepted project requirements
  and format records with published standards, without executing software or
  measuring data/model outputs, is source-based desk research and does not need
  an experiment protocol. It still needs recoverable source citations and
  independent research-evidence review before serving as a gate result. A
  protocol names its exact subject, environment, isolation, expected
  observations, evidence limits, bounds and cleanup.
- Inputs. Accepted private gold pieces stay private and are counted only in
  aggregate and by anonymized batch numbers. Public corpus inputs and committed
  fixtures need a license that permits the use and a recorded provenance.
- The frozen toolchain-class set. The set of OMR-REQ-012 is frozen below from
  read-only inspection of the private review archive, with public output
  limited as OMR-REQ-015 allows, independent attestation of its derivation and
  explicit owner approval of the full-archive union scope.

Evidence statements are classified as source finding, runtime observation,
inference or hypothesis, and no conclusion exceeds the environment, files and
tools that were actually tested.

### Frozen toolchain-class set and replay protocol

- **Derivation provenance (local read-only runtime observations over the
  private archive):**
  deterministic derivation (`derive_req012_set.py`, SHA-256
  `3f7653709ead11f270e8c2d2ab8960be1ae72d2d006d5838ad8578a01e40d5d5`) over all
  `1,591` historical `findings.json` review files across the `23` accepted
  private gold pieces (`gold-01` through `gold-23`; review-archive
  `findings.json` tree SHA-256
  `de7a5e56a8618b150646a9443db71c854c3267f6e84e837a87667a4d8d1c6fa4`),
  producing the local read-only private manifest `req012-private-manifest.json`
  (SHA-256 `968fcdac6c0ecf5c1f8d720f954a29bbf52ca88cf548d812b4f8b01f7c4b8f55`),
  verified by an independent attestation pass (exact hash reproducibility,
  keyword negative-class audit over non-`B` records, resolution of every locus
  to valid measure ordinals in the `23` gold scores, and
  OMR-REQ-015 privacy check; sanitized summary recorded in
  [Issue #1488](https://github.com/hcoona/three/issues/1488#issuecomment-7071662321)).
- **Relation to the 34-entry desk-study estimate (local read-only runtime
  observation + owner decision):** at the desk-study (`T01`) snapshot (`96` of
  `1,591` `findings.json` files, when `14` of the `23` accepted gold pieces had
  zero review files; `T01-findings-classified.json` SHA-256
  `5bea8318d14f5380a110cc8b49c0ae5903e6f7dbedefd5c3b9180dfce1bb480d` with `29`
  Class-`B` rows and `T01-findings-addendum.json` SHA-256
  `456d206af33dbb2f0a739aca2a91652d285134494de2a84bdd136099fbc09531` with `5`
  Class-`B` rows), `34` raw entries (`29` deduplicated `(gold_id, measure,
w1_category, mechanism)` loci: `28` confirmed toolchain-class `B` plus `1`
  `gold-03` duplicate-slur draft entry reclassified to Class `A` on draft
  inspection) were recorded (`Set A`). The `1,495` post-`T01` review files added `322` raw
  confirmed toolchain-class (`B`) entries (`298` net-new deduplicated loci;
  full-archive `Set B` = `355` raw / `326` deduplicated loci). Following the
  Delivery Wave return-to-owner check (source findings:
  [Issue #1488](https://github.com/hcoona/three/issues/1488#issuecomment-7071944465),
  [PR #1711](https://github.com/hcoona/three/pull/1711)), the frozen set is the
  complete union **`Set A union Set B`: `327` deduplicated loci (`356` raw
  entries)** across `13` anonymized gold batches (`gold-02`: 14, `gold-03`: 13,
  `gold-07`: 4, `gold-09`: 51, `gold-10`: 46, `gold-11`: 7, `gold-13`: 26,
  `gold-14`: 35, `gold-15`: 25, `gold-17`: 29, `gold-18`: 7, `gold-22`: 54,
  `gold-23`: 16; `10` gold batches have `0` loci) and `13` W1 categories
  (`beam`: 233, `pedal`: 29, `repeat_ending`: 17, `rhythm_rest`: 16,
  `ornament`: 15, `fingering`: 4, `tuplet`: 4, `time_signature`: 3,
  `arpeggio`: 2, `grace`: 1, `ottava`: 1, `slur`: 1, `tremolo`: 1).
- **Replay subject, environment and isolation:** `sheet_music_omr.harness
replay-req012` evaluates the `327` frozen loci on a completed formal run of
  the [comparison protocol](comparison-protocol.md) in the same preflight-pinned
  Python 3.14 / `musicdiff 6.0` / MuseScore Studio 4 environment, clean project
  code tree and external confinement (`RUN` and `DOWNLOADS` outside every git
  worktree). It reads the local private manifest, `RUN/gold/mapping.json`, on-disk
  gold score files and completed `files/gold/` artifacts read-only and writes
  `report/req012-replay.json` and `report/req012-replay.md` only after passing
  the harness's OMR-REQ-015 privacy scan.
- **Expected observations, evidence limits, bounds, stops and cleanup:** a
  frozen locus passes if and only if its gold piece completes `reverse(X)` to
  canonical Text Score 2 text and `forward` back to `T(X)` with zero score-file
  edits, passes the MuseScore 4 import check and text-space consistency check,
  and has zero counted in-scope differences in `compare_file(T(X), X)` at its
  target measure ordinals and category (with any file-level supplementary-check
  difference or unlocated global difference in a category failing every locus of
  that category in that piece; `327 / 327` required for OMR-REQ-012).
  The replay measures deterministic format and converter fidelity on gold
  scores only, not recognizer accuracy (OMR-REQ-014). Execution is bounded to
  one read-only verification pass per completed formal run (zero network bytes);
  it stops immediately with exit status 2 if preflight/code-tree/version checks
  fail, if the formal run is unfinished, if `RUN/gold/mapping.json` is absent,
  if the manifest SHA-256 differs from
  `968fcdac6c0ecf5c1f8d720f954a29bbf52ca88cf548d812b4f8b01f7c4b8f55`, if any
  mapped gold score SHA-256 on disk differs from the frozen run plan or
  manifest, or if any output fails the privacy scan, and creates no files
  outside `RUN/report/`.

### Tokenizer-counts protocol

- **Subject:** subword token counts produced by the Hugging Face
  `Qwen/Qwen3-VL-2B-Instruct` tokenizer (`Apache-2.0`, immutable revision
  `89644892e4d85e24eaac8bacfd4f463576704203`, file `tokenizer.json`, published
  size `7,032,403` bytes, Git blob SHA-1 OID
  `c6cc1014128b19d1fc46b1d30a23e3b1d35db421`; source finding from Hugging Face
  repository metadata), evaluated via `tokenizers` `0.23.3`
  (`Tokenizer.from_file`, `add_special_tokens=False`) with no model weights
  downloaded and no base model selected, across matched representations on a
  completed formal run's three input cohorts (`23` accepted private gold
  pieces, selected PDMX `X` files, and selected OpenScore Lieder `X` extracts):
  canonical Text Score 2 (`v2`, whole-piece `reverse.txt`, plus per-system
  documents split via `split_score` on every gold piece including single-system
  pieces), ABC produced deterministically by `converter21` `4.0.2`'s bundled
  `xml2abc.vertaal` (`u=0, b=0, n=0, c=0, v=0, d=0, m=0, x=0, t=False`), source
  MusicXML (`X`), and forward MusicXML (`T(X)`).
- **Environment and isolation:** runs locally via `sheet_music_omr.harness
tokenizer-counts` in the repository's preflight-verified Python 3.14 `uv`
  workspace (`converter21 4.0.2`, `tokenizers 0.23.3`), verifying run
  completion, `RUN/gold/mapping.json`, presence of all planned `result.json`
  files and on-disk gold score SHA-256 digests before opening the ledger or
  downloading any bytes, storing `Qwen3-VL-2B-Instruct-tokenizer.json` in the
  external `--downloads` directory under the shared `download-ledger.json`, and
  writing `report/tokenizer-counts.json` and `report/tokenizer-counts.md` only
  after passing the OMR-REQ-015 privacy scan.
- **Expected observations and evidence limits:** reports resolved Python,
  `converter21` and `tokenizers` versions, the exact SHA-256 of the fetched
  `tokenizer.json`, explicit per-reason skip counts (such as round-trip failure
  on `X` or `xml2abc` failure), and aggregate and summary-distribution counts
  (`count`, `total`, `mean`, `median`, `p90`, `min`, `max`, pooled tokens per
  measure, per-system gold `v2` statistics, and `v2 / ABC` and `MusicXML / v2`
  length ratios). Because `xml2abc` omits notation categories outside its
  coverage, `abc_xml2abc` counts reflect that lossy projection rather than
  lossless semantic equivalence; per OMR-REQ-014, token counts make no claim
  about model accuracy, learnability, review-effort savings or base-model
  choice.
- **Bounds, stop conditions and cleanup:** bounded to one HTTPS fetch of
  `tokenizer.json` (`7,032,403` bytes, checked before fetch against the
  `20,000,000,000`-byte W1 cumulative download limit and verified by byte size
  and Git blob OID before acceptance, with no retry) and single-process CPU
  counting; stops immediately with exit status 2 if preflight/code-tree/version
  checks fail, if the formal run is unfinished or missing planned results, if
  any gold hash on disk differs from the frozen run plan, if the download would
  exceed the 20 GB cap, or if any report output fails the OMR-REQ-015 privacy
  scan, leaving no partial or temporary files behind.

## Relation to the existing category vocabulary

This section is informative and adds no requirement. The recognition workflow's
category identifiers, as [Issue #1489](https://github.com/hcoona/three/issues/1489)
lists them when this record was written, relate to the 27 content categories as
follows, so that later scoring work can keep its per-category vocabulary:

- `P1` pitch and chord membership: `pitch` and `chord_membership`
- `P2` clef: `clef`; `P3` ottava: `ottava`; `P4` key: `key_signature`
- `D1` value, dots and rests: `rhythm_rest`; `D2` tuplet groups: `tuplet`;
  `D3` voices: `voice`; `D4` cross-staff: `cross_staff`; `D5` grace notes:
  `grace`; `D6` time signature and pickup: `time_signature`; `D7` tremolo:
  `tremolo`
- `X1` printed accidentals: `accidental`
- `L1` ties: `tie`; `L2` slurs: `slur`; `L3` articulations and fermatas:
  `articulation_fermata`; `L4` arpeggio: `arpeggio`; `L5` ornaments:
  `ornament`
- `L6` beams, flags and stems: `beam`; duration flags belong to `rhythm_rest`,
  and stem direction is a layout hint
- `N1` dynamics: `dynamic`; `N2` hairpins: `hairpin`; `N3` expression text:
  `text`; `N4` tempo and `N7` rehearsal marks: `tempo_rehearsal`; `N5` pedal:
  `pedal`; `N6` fingering: `fingering`; `N8` barlines, repeats and voltas:
  `repeat_ending`
- `N9` title and footer text: title text printed above a system is `heading`,
  and the format records no footer. The optional title and composer lines of
  an assembled score are metadata that OCR or a user supplies.
- `Y1` layout is the layout hints, which are never scored as content.
