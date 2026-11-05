# Sheet Music Recognition Requirements

These are required behaviors for the text representation of printed piano
music, its converters and its acceptance evidence, not claims about an existing
implementation. The [README](../README.md) supplies context and routing. The
[Text Score 2 format](text-format.md) is the normative grammar and semantics;
this record states what that format and its tools must achieve.

The format defines _system document_, _assembled score_, _content_ and
_layout_. An _in-scope element_ is a score element that belongs to one of the
content categories in OMR-REQ-002 and is not unsupported content; the
comparison protocol of OMR-REQ-009 makes the definition exact. An _accepted
private gold piece_ is a transcription that the repository owner's review
process has accepted. Its identity and content stay private.

## Scope

### OMR-REQ-001: Recognition target

The target shall be the printed content of one grand-staff system of a piano
score, written as one system document. System documents of consecutive systems
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
- text and structure: `text`, `tempo_rehearsal`, `repeat_ending`

A required printed distinction that the format cannot encode shall appear under
Unsupported content below instead of being omitted silently. The format
enumerates the variants within each category, and an unenumerated variant is
unsupported, not absent.

### OMR-REQ-003: Content and layout

The format's definitions of content and layout apply. Group membership and
symbol identity are content even when a group is drawn graphically, while curve
shape, spacing and font metrics are layout. The format shall encode the layout
hints that its specification lists, and a layout hint shall never count as a
content difference.

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

Comparing a source score with its round trip shall follow a protocol that is
accepted under research-evidence review before it executes. The protocol shall
fix the tools and their versions, define an in-scope element and _identical_,
state the denominator of every percentage and say how generated identifiers are
treated, since an identifier difference alone is not a content difference. It
shall assign every difference to exactly one of the 26 content categories or to
layout. A difference that cannot be assigned is a protocol defect, never
agreement. Missing content and extra content both count as differences.

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
round trip. The comparison protocol fixes the files, the selection rule and the
denominator before execution. This result measures the converters and the
importer on those files. It does not show that the format is complete or that
any model can learn it.

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
inputs. This work makes no claim about model accuracy or review-effort
savings.

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
- segno and coda signs, and extension lines of words other than a dashed line
  after expression text;
- breves, microtonal pitches, percussion, tablature and historical notation;
- barline types other than ordinary, final, double and repeat, and
  multi-number endings;
- clefs other than G, F and C on lines 1 to 5, with or without a change of one
  octave up or down;
- more than four voices on a staff, mixed-staff chords and duplicate chord
  members;
- unmeasured tremolo, and fingering other than one digit from 1 to 5 per pitch
  or two different such digits on a pitch of a trill;
- a whole-measure rest in a split measure, and beams or tuplet groups across a
  barline or a fragment boundary;
- notes that fill their measure only in tuplet groups with no printed tuplet
  number, when the transcription rules of the format leave no grouping or more
  than one;
- metronome units other than quarter, half, eighth, dotted quarter and dotted
  half;
- printed content that the source image shows only in part, such as a mark cut
  off at the edge of a scan, when its visible part and its context leave more
  than one reading; it is reported for a person to resolve and not guessed.

Playback-only data and engraving coordinates are neither content nor unsupported
content. A reverse converter ignores them.

## Validation basis

No acceptance measure has been run yet. The evidence has three parts:

- Protocols. Every experiment or runtime observation (including the comparison
  protocol, tokenizer counts, MuseScore import checks and any capability
  mapping that uses tool or runtime observations) needs a protocol accepted
  under research-evidence review before execution. A capability mapping done
  solely by comparing accepted project requirements and format records with
  published standards, without executing software or measuring data/model
  outputs, is source-based desk research and does not need an experiment
  protocol. It still needs recoverable source citations and independent
  research-evidence review before serving as a gate result. A protocol names
  its exact subject, environment, isolation, expected observations, evidence
  limits, bounds and cleanup.
- Inputs. Accepted private gold pieces stay private and are counted only in
  aggregate. Public corpus inputs and committed fixtures need a license that
  permits the use and a recorded provenance.
- The frozen toolchain-class set. The set of OMR-REQ-012 is not frozen yet.
  Freezing it needs private read-only inspection, public output limited as
  OMR-REQ-015 allows and an independent attestation of how the set was derived.
  A later reviewed change records it before the replay and before acceptance.

Evidence statements are classified as source finding, runtime observation,
inference or hypothesis, and no conclusion exceeds the environment, files and
tools that were actually tested.

## Relation to the existing category vocabulary

This section is informative and adds no requirement. The recognition workflow's
category identifiers, as [Issue #1489](https://github.com/hcoona/three/issues/1489)
lists them when this record was written, relate to the 26 content categories as
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
- `N9` title and footer text is supplied by OCR or a user. The format records
  an optional title and composer only in an assembled score, and it records no
  footer.
- `Y1` layout is the layout hints, which are never scored as content.
