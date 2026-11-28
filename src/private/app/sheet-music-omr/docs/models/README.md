# OMR: paper, readings and editable meaning

## Layer 0: what can a reading establish about the paper?

[ScoreKnowledge](ScoreKnowledge.tla) relates three **values**, not three
successive job phases:

```text
I (paper) --lossy observation--> visible cues --constrained choices--> T (reading)
   |                                                                     |
   +--interpret what was printed--> M (editable meaning) <--convert if complete--+
```

`I` is the ordered printed systems with their physical extents. `T` is one
possible report: accepted system documents (possibly with `?`) and the crops
without a reading. `M` keeps written accidentals, sounding pitches, fingering,
roles and typed spans. Scores differing only in normalized beaming (`B`) or
layout (`C`) have the same `M`. These are domain-model layers, not the evaluation
protocol's [normalization layers](../evaluation-protocol.md#normalization-and-mask-layers).

**When do the two paths agree?** `PaperBridge` in [ReadingLaws](ReadingLaws.tla)
checks actual `Readings(cues)` against an independently interpreted paper score.
`OrderedCover` requires ordered one-to-one coverage; `NoSilentLoss` checks the
marks, roles and anchors against the ink within the retained extents. Together
they keep the paper meaning **among** the possible readings and resolutions.
If the cues are also unambiguous (`Clear`) and the reading is closed and
unblocked (`Complete`), its actual conversion **equals** that meaning. These
are sufficient assumptions, not a certificate available to the recognizer.
The bounded reader takes a clear glyph as shown; that is not an accuracy
claim for a real recognizer. Even with perfect coverage, a soft accidental
offers two legal closed readings; one can be wrong.

**What cannot self-check?** A clipped fingering and a page with no fingering
have different `M` but identical cues and possible blind reports. So do
blurred sharp and flat pages. The same test compares an unanchored omission
with a genuinely shorter score made from the surviving printed systems, not
from the prediction. Even an omitted key change can pass. A printed key, bar
number or an unmatched tie can instead expose the gap. A parseable merge or
split need not cover the page one-to-one. Only the evaluator sees provenance.

| Human decision       | Guarantee and limit                                                                                                                                                                            |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Must resolve         | A `?` (prediction event) or an abstention and its downstream gap (crop ordinals) blocks delivery. Dependent pitches stay provisional; no optional budget is spent to record the block.         |
| Suggested for review | With an existing draft `D`, the blind reader flags at most `floor(len(D) / 5)` **draft measures** in the modeled split. A flag is suspicion, not an error. Without a draft there are no flags. |
| Not flagged          | A closed, supported, unblocked reading can be delivered with its meaning preserved. An unflagged measure is not certified against the paper: the clipped fingering escapes both checks.        |

These are not three disjoint sets of measure numbers. Mandatory review lives
on `T`, optional selection on `D`, and only later is a finding attributed to
a gold measure in `G`.

## Layer 1: which interfaces deliver those guarantees?

**Division and assembly** ([ScoreObservations](ScoreObservations.tla)).
`Readings(cues)` is a set, not a scheduler. At a system boundary the modeled
carry is `[pos, ctx, acc, edge]`: position (bar and offset), key context,
in-measure accidental, and open relation with its source and pitch. The
printed anchor is separate evidence. `PrefixOK` checks carry continuity,
anchors and the music already observed. At the first contradiction the crop
is `abstained`; all later ones are `upstreamGap`, even if locally readable.
`PrefixReading` independently reconstructs the header and tests the same crop
both as a prefix and as a final cut. A hole inside observed time fails
immediately; an unfinished bar or still-open arc may wait, but not at the end.
An arc's endpoint types its earlier start. A second-ending `{tie-in}` needs
the whole score's source before the first ending; it does not add a carry field.

**Symbolic meaning and interchange** ([ScoreMeaning](ScoreMeaning.tla)).
`ScoreGraph` looks up written sources by position; `Forward` interprets in
order. A tie across a bar inherits pitch without changing the next untied
note. After a first-beat `?`, the next system's unmarked F is provisionally
natural but may be flat, natural or sharp. Resolving it can invalidate the
next header. Conversely, restoring a flat late in bar three leaves **every
header valid** and changes the second ending in bar five. Rebind and check
the whole edition, not just the edited system.

Text Score 2 and MusicXML are semantic inverses **on the supported, closed
domain**, not a byte-level bijection: an untyped arc may return as a typed
tie or slur. Besides text round trips, the model checks independently supplied
native notation and native edits. Unknown constructs and inconsistent printed
and sounding pitch must not yield a successful import; unsupported text
must not yield an export.
See the [carry and assembly](../text-format.md#carry-state-and-assembly),
[review markers](../text-format.md#review-markers),
[constrained decoding](../training-protocol.md#training-detector-and-constrained-decoding-contracts)
and [conversion contract](../requirements.md#omr-req-008-forward-and-reverse-conversion).

## Text Score 2: do the transcription rules determine a reading?

The [decision audit](printed-decisions.md) starts **before** normalization.
It checks existence and uniqueness separately for ordering, anchor selection,
ownership and simultaneous completion, including cases with no answer or
several answers. Its inputs are invented positions and notation facts, not
preselected canonical documents.

## Text Score 2: one target for one determined reading

The original [W1 design rationale](https://github.com/hcoona/three/issues/1488)
calls the format the canonical target trained readers emit. The accepted
[canonical form](../requirements.md#omr-req-005-canonical-form) and
[crop/target pairing](../requirements.md#omr-req-017-overlay-qa-normalized-system-crops-and-printed-system-slices)
make the distinction concrete: two transcribers who read the same marks alike
should not supply different target sequences just because they list voices in
a different order. A grammar can accept both sequences; it cannot choose the
training label. The [synthetic renderer](../../src/sheet_music_omr/synthetic/renderers.py)
writes each system with `write_document`; the [packager](../../src/sheet_music_omr/training/packaging.py)
records `target_text_v2`, and the [Arm B training sequence](../../src/sheet_music_omr/training/models.py)
tokenizes that field after the prompt. Canonicalization removes an arbitrary
choice from supervision; this is not a measured accuracy improvement.

[TextScoreTarget](TextScoreTarget.als) distinguishes two decisions:

- **Transcription from paper.** For a fixed header, carry and crop, one
  determined printed event belongs to voice 1 regardless of stem; for two
  simultaneous events on this staff, the up-stem belongs to voice 1 and the
  down-stem to voice 2. A target exists for each modeled reading and every
  spelling that transcribes it yields that target (`OneTargetPerDeterminedReading`).
  Valid text alone cannot enforce the [paper-dependent choice](../text-format.md#transcription-rules).
- **Writing a valid document.** `OneLosslessSpelling` checks that normalization
  is idempotent, preserves the reading, and gives equal targets **if and only
  if** the voice-keyed written notes and encoded stem hints agree. The writer
  sorts the actual line slots and omits the redundant `from=0` option. Two
  reversed line orders with and without that option produce the same target;
  `C4` and `Cn4` with the same voice and stem do not, although both sound C
  natural when no alteration is in effect (`SoundFixesTarget`).

The target here is an ordered token template, not Python output or tokenizer
IDs. Each line contains one fixed-duration note; modeled pitches are `C4`,
`Cn4`, `D4`. Different system cuts or incoming context are not interchangeable
training targets. Undetermined paper uses the [review-marker rules](../text-format.md#undetermined-content);
it is outside this uniqueness claim.

## Text Score 2: does a system cut preserve the whole score?

[TextScoreAssembly](TextScoreAssembly.als) models the
[assembly contract](../requirements.md#omr-req-007-system-carry-state-and-assembly)
for an unsplit measure and an interior cut common to all voices; no note
crosses the cut. In the split view, the right fragment has _local_ event
indices. Assembly adds each voice's own count from the left fragment and
joins its local relation labels through
the carried start and kind, not the pitch. The unsplit reference independently
numbers events over the whole voice and counts spans by kind and endpoint pair.
Two slurs with identical endpoints count twice. Label atoms abstract the names,
not the format's lexical `rN` numbering algorithm.

`LegalCutPreservesGraph` compares the two results. `PitchAndClosureSuffice`
deliberately exchanges the destinations of two cross-cut unison ties: both
pairings meet the local pitch, staff, timing and closure checks, but they
connect different voices. Both are legal scores; only one preserves the input.
Witnesses also exercise different per-voice carry counts, interchangeable
parallel slurs and a closed relation's label reused in the next document.
The abstraction keeps each single-note event's payload intact; it does not
parse text, compute pitch inheritance, model chords or decide a pending arc's
kind.

### Canonical labels across two consecutive cuts

[TextScoreLabels](TextScoreLabels.als) connects the two questions above. Two
slurs start in system one, at the same or at different events; one ends in
system two and the other in system three. The model compares the **visible**
start marks, carry entries and end marks for both traversal orders, not the
identity of an unprinted slur. It independently rebuilds the two endpoint
pairs from those marks. All other tokens and both cuts are fixed.

On Linux x64 with Alloy 6.2.0 and SAT4J, `OneTextAndGraph` found no
counterexample. Both negative controls found one: freezing working names for
identical carry entries can change the middle target; renumbering entries
with different starts can attach the middle ending to the wrong start.
The [Python split and assembly scenarios](../../tests/test_harness_systems.py)
exercise both start arrangements at barlines: changing working names leaves
all three serialized documents identical, and parsing and reassembling them
returns the original canonical whole text. This does not cover other relation
kinds, more simultaneous slurs, or arbitrary chains of systems.

### What the Python implementation actually writes

The [document scenarios](../../tests/test_documents.py) compare canonical output
with expected text: voice-line order and `from=0` disappear; written naturals
and stem directions survive. A local enumeration used one fixed `2/4` system,
one note per line, voice IDs `{1}`, `{2}` or `{1,2}`, and the target model's
three note spellings and two stems. Both `from=0` choices and every row order
gave 168 valid inputs and exactly 48 distinct Arm B target sequences. Decoding
each target returned its expected canonical text. This checks valid document
spellings, not which voices a recognizer should infer from paper.

The [assembly scenarios](../../tests/test_assembly.py) join two system documents
at `1/4` with different incoming voice counts (2 and 1). Two cross-staff
unison tie pairings both succeed but yield different, independently specified
whole-score texts. These cuts are supplied to assembly, not inferred by the
barline-only harness splitter. The [packaging scenario](../../tests/test_w6_packaging_ledgers_copista.py)
follows the synthetic renderer's system-text producer through the two on-disk
crop rows and decodes each Arm B target after its prompt. It uses fixture
geometry, no external rasterizer, model weights, training or private scores.

### Reproduce the Alloy checks

With the [locked Analyzer](../../../../../../docs/engineering/workspaces.md#alloy),
run from the repository root:

```shell
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/TextScoreTarget.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/TextScoreAssembly.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/TextScoreLabels.als
```

For the target and assembly models, Alloy 6.2.0 with bundled SAT4J on Linux
x64 found no counterexample (UNSAT)
to the two target laws or the assembly law. Both deliberately false assertions
produced counterexamples, and all five `run` witnesses were SAT. As a sensitivity
check, changing the writer to emit only up-stems produced counterexamples to
both target laws. The target checks compare up to two spellings and two printed
heads. The main assembly check uses four time positions, up to two voices, two
staves, two pitches, six notes and three spans; the separate label-reuse witness
uses five positions and one voice. These are bounded design checks, not a proof
for arbitrary scores, a training run, or implementation conformance evidence.

## Independent clients of the first reading

`P0` is the actual first `T`, with no draft, gold, budget or score. The
product can finish without an observer. A new observation of missing ink by
the editor is not the blind second reading of somebody else's draft.

**Product** ([OMR](OMR.tla)). The [publication contract](ScorePublication.tla)
observes the first reading, working edition and ordered successful deliveries. An actual second observation can
replace a gapped report; an answer can replace an existing marker. Accepting
the answer also rebinds the score. The refinement mapping reconstructs the
expected edition from the **submitted mark and pre-edit text**, not from
the converter's output. A reported export must carry the actual whole-score
graph. A successful reverse conversion delivers the returned text and must
honor a submitted fingering edit; an unsupported import cannot retract an
earlier export. Identical deliveries still count twice. Safety admits
stuttering; `AnsweredProgress` separately checks that an answered score is
eventually exported if a continuously ready export is serviced. Nobody is
assumed to answer or to read the paper correctly.

**Read-only evaluation** ([Evaluation](Evaluation.tla)). The alignment,
calibration and attribution in [ReviewEvidence](ReviewEvidence.tla) are
functions, not editing phases. `FreezeFlags` consumes `P0`, an independent
draft and a frozen development calibrator. `Reveal` consumes that sealed report before using the
gold and pre-registered mask. `EvidenceHistory` checks the **whole** flag
report against blind inputs on first issuance, and the score against `P0` on
reveal; neither may change thereafter. Checking just the copied input and
immutability would miss a mask-dependent choice of initial flags. With the
observer attached, every product step is available with the observer unchanged;
every observer step leaves the product unchanged. TLC also checks the product
specification and the publication refinement on their interleavings. Refinement
alone would not detect a new observer prerequisite that removed product paths.

In the `reviewAndGap` witness, two crops initially yield one marked measure;
four subsequent crops have no reading. With **no flags and no gold result
yet**, the editor reobserves, resolves the marker, exports and returns editable
text with no gaps. Only then does the observer flag draft measure 1 and
score the original: four gold measures still have no physical match. The
repair is useful to the editor without becoming evidence of first-pass accuracy.

**What the flags mean.** `needsPerson` preserves a timed placeholder;
mandatory abstentions are outside the optional `floor(M_draft / 5)` budget
on the modeled one-piece split. The blind comparison aligns `P0` to `D`;
only after freeze does alignment from `D` to `G` attribute draft errors.
In `miss`, draft flag **2** catches unmatched gold bar **3** through its
preceding anchor; in `duplicate`, draft flag **3** catches gold error **2**.
`A` is a content discrepancy, `B` a normalized representation difference,
`C` layout, and `R` a retracted allegation, not a way to erase a scored
`A`. Physical mismatch remains recorded in `forced` while `denominator`
and scored `errors` stay restricted to unmasked eligible measures; an
empty denominator is unavailable, not zero. Alignment costs one per
differing category and four per gap: keeping the slightly different bar
costs **5** with the spurious neighbor dropped, rather than **7** for
keeping the wholly different bar.

Fold exclusion and calibration are value checks, not steps each user runs.
The development fit excludes its target from the contributing folds and
their weights; the production fit uses out-of-fold development evidence.
Two synthetic confidence bands do not measure calibration quality. The
[evaluation](../evaluation-protocol.md#arm-s0-b-stock-copista-28m-drafter-and-blind-second-reader-modes)
and [calibration protocols](../training-protocol.md#pre-registered-4-fold-train_dev-split-calibration-w7-sidecar-and-slice-b-gates)
remain authoritative for the full metrics and split-level budgets.

## Experiment control: the one shared-resource state machine

[Pilot](Pilot.tla) knows no notes, drafts or editor. For an already frozen
evaluation plan, two contenders may see room. `Issue`
must recheck and record one irrevocable lease **before** `ReadInput` by its
holder. Recording a derived view grants no access. There is one slot per
modeled stage and at most three cumulative pilot touches; the current final
also requires no prior touches. `otherStage` exercises this bound, not
permission for another pass. The model assumes an atomic issue decision; it
does not prove a lock implementation or that a real caller uses this boundary.

A known completion still needs `Settle`. A crash before or after the first
read keeps the charge. With only a caller receipt, losing the handoff can
block other work even after stoppage is verified. In the ordinary source
path, the [reservation](../../src/sheet_music_omr/training/slice_b_ledgers.py)
records `started` before returning a token; the
[runner](../../src/sheet_music_omr/training/slice_b_runner.py) acquires the
token before its settlement `try`/`finally`. This is source inspection,
not a crash experiment or a claim that manual recovery is impossible.
`byLease` models a candidate: once stoppage is verified, reconcile by lease
and run other work without refunding access. Settlement, reconciliation and
dispatch have weak fairness; verification does not. This does not change
the [accepted pilot ceilings](../requirements.md#omr-req-019-test-set-touch-governance-and-stage-s0-baselines).

## Training labels: renumber notes without reconnecting beams

[TrainingVoiceLabels](TrainingVoiceLabels.tla) checks one staff, two bars,
two source lines and two unit or double-length slots per line per bar. Each
bar chooses a stem assignment and overlapping or disjoint rhythms. Each
temporally adjacent pair on a line can be joined by a beam, including one
whose last event before the barline is a hidden rest. The model derives beam
flags from those edges, applies a numbering policy and reconstructs the
edges on the output lines. It compares **event endpoints**, not beam counts.

A determined local pair takes its printed numbers; a crossing beam can move
with both bars if their permutations agree. Otherwise preserve source lines
rather than infer a crossing assignment. The normal configuration exhausts
1,152 valuations. To reproduce it:

```shell
mise run tla:tlc -- -workers 1 -config src/private/app/sheet-music-omr/docs/models/TrainingVoiceLabels.cfg src/private/app/sheet-music-omr/docs/models/TrainingVoiceLabels.tla
```

In a temporary copy of the configuration, replace `Safe` with `Blanket`,
`Independent`, `NotesOnly`, `Freeze` or `Guess`. Each **must fail**: respectively a
local beam retains source IDs, per-bar permutation changes a crossing edge,
ignoring a hidden rest loses an endpoint, an agreed cross-bar permutation
is unnecessarily blocked, or both conflicting bars are swapped without
changing a single beam edge. This is not a model of OCR, MusicXML parsing,
multi-level beams, or all voice assignment. The producer uses normalized
MusicXML beam states at every level; fixture tests cover repeated `begin`,
the hidden endpoint, decoded supervision, tie endpoint identity and marker
rejection separately.

## Reproduce the bounded checks

From the repository root, substitute the configuration and **its module**:

```shell
mise run tla:tlc -- -workers 1 -config src/private/app/sheet-music-omr/docs/models/Publication.cfg src/private/app/sheet-music-omr/docs/models/OMR.tla
```

Local breadth-first TLC 2.19 (`5a47802`, tools `1.7.4`) exhausted these
configurations with exit **0**. Counts are distinct valuations or states,
not counts of pieces. The photographic domain has 20 existing cases, five
or six measures, one voice and one displayed pitch, at most one ambiguous
event and one open arc at a time. No private scores, model weights, training,
inference or actual `TEST` reads were used.

| Configuration                      | Module        |     Distinct | Obligation                                                                                                                                                                                |
| ---------------------------------- | ------------- | -----------: | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [ReadingLaws.cfg](ReadingLaws.cfg) | `ReadingLaws` | 84 per meter | Paper bridge, review limits, prefix/final cuts, blind pairs, round trips, alignment and calibration. Four beats; repeat with `Beats = 3` in a temporary copy. All are initial valuations. |
| [OMR.cfg](OMR.cfg)                 | `OMR`         |          393 | Product alone, including independent native and unsupported text inputs. First report stays fixed; nonterminal deadlocks and endless state changes are ruled out.                         |
| [Publication.cfg](Publication.cfg) | `OMR`         |          382 | All four-beat photographic cases without an evaluator: refinement and conditional progress after an answer.                                                                               |
| [Evaluation.cfg](Evaluation.cfg)   | `Evaluation`  |        7,320 | Same product with optional blind flagging, retraction and scoring, including delayed attachment and reveal.                                                                               |
| [Pilot.cfg](Pilot.cfg)             | `Pilot`       |          600 | Two contenders, zero to three prior touches, normal settlement and recovery after verified stoppage.                                                                                      |

| Deliberately false assertion     | Configuration / module                                    | What the counterexample establishes                                                         |
| -------------------------------- | --------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `LocalReviewCertificateSurvives` | [RevisionWitness](RevisionWitness.cfg) / `ReadingLaws`    | The printed correction invalidates a local header.                                          |
| `SameHeadersSameEnding`          | [EndingWitness](EndingWitness.cfg) / `ReadingLaws`        | All headers still match but the remote pitch changes.                                       |
| `StructurallyCertifiedInk`       | [ClippedWitness](ClippedWitness.cfg) / `ReadingLaws`      | A clean round trip misses a mark that the blind budget does not catch.                      |
| `OrdinalFlagsTransfer`           | [PairingWitness](PairingWitness.cfg) / `ReadingLaws`      | Draft and gold ordinals are not interchangeable.                                            |
| `NoLateAssessment`               | [ReviewLoopWitness](ReviewLoopWitness.cfg) / `Evaluation` | Publication can finish before the first flag; the late observer still grades `P0`.          |
| `HandoffProgress`                | [HandoffWitness](HandoffWitness.cfg) / `Pilot`            | Receipt-only recovery can stutter forever after a lost pre-read handoff.                    |
| `NoRecoveredContestedRead`       | [AdmissionWitness](AdmissionWitness.cfg) / `Pilot`        | After a contested read and crash, other work can resume without refund or a published view. |

The four static witnesses fail in an initial valuation, not at the end of a
workflow. Invariant witnesses exit **12**; `NoLateAssessment` and
`HandoffProgress` are temporal counterexamples and exit **13**.

Removing one premise at a time from the bridge changes the result: an
omitted system defeats it without coverage, a clipped fingering without
signal fidelity, and a wrong soft accidental without clarity. Deliberately
dropping a clear fingering in the reader also breaks the bridge on intact
paper. Thus the agreement is not assumed by selecting only correct outputs.

Two negative controls distinguish independence from mere sequencing. In a
temporary copy, require `audit # Absent` for any product step after `P0`:
`NoLateAssessment` then passes (exit **0**) because the desired path is gone.
Separately, clear flags at `FreezeFlags` in `miss` whenever the hidden mask is
nonempty. A check of the copied `P0` and immutability alone passed that fault;
`EvidenceHistory` rejects it **at flag formation**, before reveal. Substituting
the current working reading for `P0` instead fails at `FreezeFlags` or `Reveal`,
whichever consumes it. These are deliberately injected modeling faults, not
observations about the application.
