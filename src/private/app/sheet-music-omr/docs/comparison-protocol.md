# Round-Trip Comparison Protocol

This record is the comparison protocol that
[OMR-REQ-009](requirements.md#omr-req-009-comparison-protocol) requires. Its
consumers are the implementers of the W1 round-trip harness, their
independent reviewers and the W1 acceptance decision in the repository
[Delivery Wave](../../../../../docs/delivery-wave.md). It fixes how a score and
its round trip are compared and scored before anything executes.

This record governs execution only once it is accepted under
[research-evidence review](../../../../../.github/skills/research-evidence-review/SKILL.md),
and every later change needs the same review before the changed part executes.
It is not a result report, and nothing in it has been run. Results go to the W1
work carrier, [Issue #1488](https://github.com/hcoona/three/issues/1488), and
its linked pull requests.

Statements about tools and datasets are source findings and cite their source
under [Source basis](#source-basis). Items that the sources do not settle are
listed under [Evidence limits and open checks](#evidence-limits-and-open-checks)
and are not relied on as facts.

## Subject and scope

The subject is the experimental forward converter (text to MusicXML) and
reverse converter (MusicXML to text) of this project, at one recorded commit,
together with MuseScore Studio 4.7.6 as importer and exporter. The protocol
covers four measures:

1. the round-trip comparison of
   [OMR-REQ-009](requirements.md#omr-req-009-comparison-protocol),
   [OMR-REQ-010](requirements.md#omr-req-010-private-gold) (first sentence) and
   [OMR-REQ-011](requirements.md#omr-req-011-public-corpus-fidelity);
2. the MuseScore import check of forward output that
   [OMR-REQ-008](requirements.md#omr-req-008-forward-and-reverse-conversion)
   requires;
3. the system-assembly test of OMR-REQ-010 (second sentence);
4. the layout-fidelity score of
   [OMR-REQ-013](requirements.md#omr-req-013-layout-fidelity).

It does not cover tokenizer counts, the ABC capability mapping or the replay of
the toolchain-class findings of
[OMR-REQ-012](requirements.md#omr-req-012-toolchain-class-findings). Each of
those needs its own accepted protocol or record.

## Tools and environment

- **Host:** the repository owner's Windows host, in the owner's interactive
  desktop session. Runs are local. Operation without a display or in a
  service session is not verified and is not used.
- **Python:** Python 3.14 from the repository's uv workspace.
- **musicdiff:** version `6.0` from PyPI, the distribution of GitHub tag
  `v6.0.0` at commit `0368856e8561ba655d990c00c7ae46a2408f071c`. The Wave's
  "musicdiff 6.0.0" names this release. musicdiff states only minimum versions
  for music21 and converter21 and no version for numpy. The versions that the
  repository lockfile resolves for them are the ones used.
- **MuseScore:** MuseScore Studio 4.7.6, build `4.7.6.261022093`, already
  installed at its default location. If it is not installed there when a run
  starts, the run stops before any MuseScore conversion. It is GitHub tag `v4.7.6` at commit
  `96480d58445e3b24b230a3fe7d4725d639aa2eb3`. Preflight records the
  executable path and version string.
- **Run record:** every run records the converter commit, the harness commit,
  the Python version, the resolved versions of musicdiff, music21,
  converter21 and numpy, the MuseScore executable path and version string, and
  the MuseScore preference values below.

### MuseScore isolation

MuseScore 4.7.6 has no option that moves its configuration folder, and its
`-F` and `-R` options both reset the user's settings. The harness therefore:

- never passes `-F` or `-R`, and does not rely on a configuration-folder
  option;
- invokes MuseScore only for single conversions with `-o` or conversion jobs
  with `-j`, and never passes `--force` (`-f`);
- before the first conversion, copies the user settings file
  `%APPDATA%\MuseScore\MuseScore4.ini` into the run directory so that the owner
  can restore it, and never restores it automatically;
- reads that file read-only and records its SHA-256 before and after each
  batch of MuseScore conversions;
- records the MusicXML import and export preferences in effect, read
  read-only from that file. A preference that the file does not set is
  recorded with its 4.7.6 source default: import breaks `true`, import layout
  `true`, export layout `true`, export compatibility with MuseScore 3 `false`,
  export breaks `All`, export invisible elements `false`, apply default font
  `false`, infer text type `false`.

The path follows from the pinned source: outside macOS a non-portable build
stores its settings in INI format under organization `MuseScore` and
application `MuseScore4`, which Qt places at
`%APPDATA%\MuseScore\MuseScore4.ini` for the user scope on Windows. Preflight
checks it. If the file does not exist at that path when a run starts, the run
stops before any MuseScore conversion.

Runs use the existing installation and user settings unchanged. These guards,
with the harness's confined writes, are what this protocol means by an
isolated local run.

## Inputs and score identities

### Identities

The protocol keeps these scores separate:

- `X`: a source score, in MusicXML.
- `R(X)`: the MuseScore re-export of `X`, which is MuseScore's import of `X`
  followed by MusicXML export (`-o` to a `.musicxml` file).
- `T(Y)`: the round trip of a score `Y`, which is
  `forward(reverse(Y))`. `Y` is `X` or `R(X)`.

The comparison of `X` with `R(X)` is reported as a MuseScore-only baseline. It
is never counted as converter error.

An _arm_ is one set of reference scores `Y`. The gating arms are PDMX `X`,
PDMX `R(X)`, Lieder `X` and Lieder `R(X)`, and the gold arm `X`.

### Private gold

The gold inputs are the MusicXML files of every accepted private gold piece,
read locally and read-only. The set is fixed when a run starts and only its
count is recorded publicly. Gold pieces are not screened. Their `R(X)` is not
run.

### PDMX sample

- **Source:** PDMX v9, version DOI
  [10.5281/zenodo.15571083](https://doi.org/10.5281/zenodo.15571083). Only
  `PDMX.csv` (225,399,738 bytes) and `mxl.tar.gz` (1,894,335,797 bytes) are
  downloaded.
- **Prefilter on `PDMX.csv`:** `subset:no_license_conflict` is true,
  `subset:all_valid` is true, `license_url` is exactly
  `https://creativecommons.org/publicdomain/zero/1.0/` or
  `https://creativecommons.org/publicdomain/mark/1.0/`, `n_tracks` is `1`,
  `tracks` is exactly `0` and `has_lyrics` is false.
- **Source file:** `X` is the root MusicXML document of the candidate's
  compressed MusicXML file, which the harness extracts as the container's
  `META-INF/container.xml` names it.
- **Order:** each candidate's key is its compressed MusicXML path as
  `PDMX.csv` records it. Candidates are ordered by the lowercase hexadecimal
  SHA-256 of the UTF-8 bytes of `w1-pdmx-v1:` followed by the key, ascending,
  with ties broken by the key.
- **Sample:** the first 200 candidates in that order that pass the
  [structural scope screen](#structural-scope-screen). If fewer pass, all that
  pass are taken and the count is reported.

### OpenScore Lieder sample

- **Source:** OpenScore Lieder release `v3.0.0`, commit
  `f44e962a1ff85ddcc2f6f42bed3b0c529555ee15`, version DOI
  [10.5281/zenodo.15450144](https://doi.org/10.5281/zenodo.15450144). The
  scores are CC0.
- **Export:** MuseScore 4.7.6 exports each candidate `.mscx` file to MusicXML.
  A candidate whose export fails is excluded with the reason "export failed".
- **Piano extraction:** before any reference is defined, a deterministic
  transform keeps the one part whose `score-part` names a piano instrument
  (its `instrument-sound` starts with `keyboard.piano`), its `score-part`
  entry and the score's credits, and drops every other part, `score-part` and
  `part-group`. A score without exactly one such part is excluded with the
  reason "no unique piano part". The transform is part of the harness, and its
  commit is recorded with the run.
- **Label:** the result is a piano-accompaniment extract, not an original
  piano work, and it is reported so. It is `X`, and `R(X)` is made from it.
- **Order and sample:** each candidate's key is the repository-relative path
  of its `.mscx` file at the pinned commit. Ordering is as for PDMX with the
  prefix `w1-lieder-v1:`. The sample is the first 50 extracts in that order
  that pass the structural scope screen. If fewer pass, all that pass are
  taken and the count is reported.

### Structural scope screen

The screen applies to every PDMX and Lieder candidate `X`. It fails closed: a
file is excluded when any check fails or cannot be decided, and the exclusion
is counted by its first failing reason. The screen is a selection rule. Passing
it is not evidence that a file's other content is supported.

A file passes only if all of the following hold:

- it has exactly one part, and that part has two staves throughout, each with
  five lines and no percussion or tablature;
- every `clef` has `sign` `G`, `F` or `C`, a `line` from 1 to 5 and a
  `clef-octave-change` that is absent, `-1` or `1`;
- no `clef` restates an earlier `clef` of its staff in the same measure with
  no different `clef` of that staff between them;
- no staff has notes of more than four `voice` values in one measure;
- it has no `lyric`, `harmony` or `figured-bass` element, no `cue` element and
  no note `type` with `size="cue"`;
- it has no `multiple-rest` or `measure-repeat`, no `segno` or `coda`, no note
  `type` of `breve`, `long` or `maxima`, no `unpitched` note and no `fret` or
  `string` element;
- every `ending` has a single number;
- every `key` and `time` element appears before the first note, rest or
  `forward` with a duration in its measure;
- every `time` has one `beats` and `beat-type` pair with a whole-number
  `beats`, and no `senza-misura`;
- every `alter` is a whole number, and no `accidental` value is a quarter-tone
  or other microtonal value;
- every barline is ordinary, final, double or repeat: a `bar-style` that is
  absent, `regular`, `light-light` or `light-heavy`, or `heavy-light` with a
  backward or forward `repeat`;
- no `chord` note has a `staff` different from the first note of its chord,
  and no chord repeats a pitch;
- no `tremolo` has `type="unmeasured"` or a stroke count of 0;
- every note has at most one `fingering` with one digit from 1 to 5, except
  that a note with a `trill-mark` may have two different such digits;
- no `bracket` direction appears, and every `dashes` direction follows a
  `words` direction;
- every `metronome` has one `beat-unit` of quarter, half or eighth, with a
  dot only on a quarter or half, and a `per-minute` value;
- every beam level and every tuplet that starts in a measure stops in it;
- every notation, ornament, articulation, technical mark, dynamic and
  direction type belongs to a variant that the
  [Text Score 2 format](text-format.md#category-encodings) enumerates. The
  harness implements this as an allowlist written from the format, reviewed
  with the calibration set, independently of the converters;
- musicdiff 6.0 parses it in the content pass within 300 seconds.

These checks follow the
[Unsupported content](requirements.md#unsupported-content) list. Its items
about split measures and heading text above a system that starts inside a
measure do not arise in whole-piece files, whose system breaks fall at measure
boundaries.

### Selection before conversion

The screen and sample are evaluated in candidate order until the sample is
full. Exclusion counts therefore cover the screened prefix of each order, and
the report says so. Selection finishes, and the list of selected public files
is posted to the results carrier, before any round-trip conversion of a
selected file. The sample is fixed by the seed prefixes and is never
reselected after results are seen. A defect in the screen that changes the
selection is corrected only by a reviewed change to this protocol.

### Downloads

Downloads and their extracted files go to one download directory outside the
repository, which is recorded with the run and reused by reruns. A download
ledger in the run
directory records every download made for this protocol with its byte count,
and starts from the W1 cumulative download total so far. Before each download,
the harness checks that the total plus the download's published or estimated
size stays at or below 20,000,000,000 bytes. Otherwise the run stops and
returns to the owner.

## Comparison instrument

The instrument has three parts: musicdiff 6.0 through its library API, the
category assignment below and the supplementary checks below. Each score
`Y` is compared with `T(Y)` as `T(Y)` first and `Y` second, so that musicdiff
treats `Y` as the reference. Whether an operation is missing content or extra
content is decided by which side of the operation holds an object, never by
the operation's name.

The harness builds musicdiff's annotated scores and calls
`Comparison.annotated_scores_diff` directly, as the high-level `diff` function
does at the pinned commit, so that it keeps every raw operation. It does not
use musicdiff's batch mode, which skips files whose parsing fails.

At the pinned commit, `Comparison._voices_coupling_recursive` adds the cost
of pairing the first voice of the first list with a voice `c` of the second
only when the first voices of both lists differ, so an unequal pairing can
cost nothing. The harness replaces that condition,
`compare_to[0] != original[0]`, with `c != original[0]`, and changes nothing
else in musicdiff.

### Detail levels

- **Content pass:**
  `(DetailLevel.AllObjects | DetailLevel.Voicing) & ~(DetailLevel.Lyrics | DetailLevel.LyricIdentifiers | DetailLevel.ChordSymbols)`.
  `Style`, `Metadata` and `NoteStaffPosition` are off.
- **Layout pass:** the content pass with `DetailLevel.Style` added.

For each pass and file the harness retains every raw operation (name, kinds of
both objects, cost and location), the OMR edit distance, both symbol counts
and the normalized OMR edit distance. These are reported as secondary figures.
The normalized distance divides by the symbol counts of both scores and is not
a symbol error rate. Neither it nor the measure below is reported as one.

### Elements and the fidelity measure

- An _in-scope element_ of `Y` is an annotated object of `Y` in the content
  pass (a note, rest or chord, or an extra such as a clef or dynamic) whose
  category is one of the 27 content categories, plus each element that a
  supplementary check below defines.
- A reference element is _identical_ when no counted difference touches it.
  A difference is counted unless the assignment below disregards it.
- Each counted difference that holds an object only in `T(Y)` adds one _extra
  element_.
- For an arm, the fidelity figure is the number of identical reference
  elements divided by the number of reference elements plus extra elements,
  pooled over all files of the arm. The per-file distribution and the mean of
  the per-file figures are reported as secondary figures.

Missing and extra content both count, as OMR-REQ-009 requires: a round trip
that drops or invents a mark on a reference note, rest or chord makes it
non-identical, and one that invents a note, rest, chord or extra adds an extra
element. OMR-REQ-011 is met when each of the four public arms reaches at least
99%. OMR-REQ-010 is met when every gold piece completes every step with no
failure status and the gold arm has no counted difference.

### Identifiers and other data that are not compared

Generated identifiers are not differences. musicdiff compares notes and staff
groups without encoding identifiers. The following are not compared:

- XML `id` attributes, `part` and `score-part` identifiers (parts are compared
  by position) and the `number` attributes that link starts and stops of
  slurs, tuplet groups, wedges and other spanners. A beam's `number` is its
  level, which musicdiff and the beam checks compare;
- raw MusicXML `voice` numbers. The order of a staff's voices is content,
  because the format gives voice numbers effects that depend on that order,
  and the voice-order check below compares it. Renumbering that keeps the
  order is not a difference;
- `divisions` values, since durations are compared in quarter lengths;
- the `identification` element, including software, encoding dates and
  `supports`, and other metadata such as work title and creator, since the
  `Metadata` detail level is off. A title or composer line of an assembled
  score is metadata, not content;
- engraving coordinates and other properties that the layout pass does not
  compare, and playback-only data.

### Failures

A file's status is recorded for every step. A file counts all of its in-scope
reference elements as non-identical, and is listed, when its `R(X)` export
fails, its reverse conversion fails, reports unsupported content or a review
marker, or its forward conversion fails, or musicdiff cannot parse `R(X)` or
`T(Y)`, or a step exceeds the 300-second timeout. Such a file is never dropped
from the denominator. Its element count is that of `Y` alone, or of `X` when
`R(X)` is missing or cannot be parsed: the in-scope elements of its content
pass plus the supplementary elements computed on that score alone, where each
voice, each slur, each note with a `tie` of type `stop`, each tremolo of type
`single` or `start`, each `beam` with value `forward hook` or `backward hook`
on a note that is not a rest, each beam on a rest as defined by the Rest beams
check, and each note, rest or chord with a `time-modification` counts as an
element.

A gold piece whose reverse conversion reports unsupported content does not
pass OMR-REQ-010, and the count of such pieces returns to the owner, because
the requirements do not say how unsupported content in gold is treated. A
gold piece whose `X` musicdiff cannot parse also does not pass OMR-REQ-010,
and the count of such pieces returns to the owner.

## Category assignment

Every counted difference is assigned to exactly one of the 27 content
categories of the [format](text-format.md#category-encodings), or to `layout`.
A difference assigned to `layout` is never a content difference: the content
pass disregards it, and the layout pass counts it. A difference that the table
and rules below cannot assign is a protocol defect. The run stops, and the
difference is never counted as agreement.

| musicdiff 6.0 operation                                                                                                                                  | Assignment                                                                                                                                                                                                                                                                 |
| -------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `pitchnameedit`                                                                                                                                          | `pitch`; `layout` when both objects are rests, since only the layout pass compares a rest's staff position                                                                                                                                                                 |
| `pitchtypeedit`                                                                                                                                          | `rhythm_rest` (note against rest)                                                                                                                                                                                                                                          |
| `inspitch`, `delpitch`                                                                                                                                   | `chord_membership`                                                                                                                                                                                                                                                         |
| `accidentins`, `accidentdel`, `accidentedit`                                                                                                             | `accidental`                                                                                                                                                                                                                                                               |
| `headedit`, `dotins`, `dotdel`                                                                                                                           | `rhythm_rest`                                                                                                                                                                                                                                                              |
| `insspace`, `delspace`, `editspace`                                                                                                                      | `rhythm_rest` (unprinted spacers and gaps in time)                                                                                                                                                                                                                         |
| `graceedit`                                                                                                                                              | `grace`                                                                                                                                                                                                                                                                    |
| `insbeam`, `delbeam`, `editbeam`                                                                                                                         | `beam`; `rhythm_rest` when the two notes differ in duration type, since musicdiff 6.0 gives every value from a quarter down the same notehead and encodes its flags only as beam levels                                                                                    |
| `instuplet`, `deltuplet`, `edittuplet`                                                                                                                   | `tuplet`; `layout` when the two tuplet groups differ only in printed number or bracket                                                                                                                                                                                     |
| `tieins`, `tiedel`                                                                                                                                       | `tie`                                                                                                                                                                                                                                                                      |
| `insarticulation`, `delarticulation`, `editarticulation`                                                                                                 | `articulation_fermata`; `layout` when only the placement differs; disregarded when the object is a fingering                                                                                                                                                               |
| `insexpression`, `delexpression`, `editexpression`                                                                                                       | by expression type: fermata `articulation_fermata` (`layout` when only its shape or placement differs); trill, mordent, inverted mordent, turn and inverted turn `ornament` (`layout` when only its placement differs); single-note tremolo `tremolo`; arpeggio `arpeggio` |
| `extrains`, `extradel`, `extracontentedit`, `extrasymboledit`, `extrainfoedit`, `extraoffsetedit`, `extradurationedit`                                   | by the object's `kind`, below; `layout` when only the placement differs                                                                                                                                                                                                    |
| `extrastyleedit`, `editnoteshape`, `editnoteheadfill`, `editstemdirection`, `editstyle`                                                                  | `layout`                                                                                                                                                                                                                                                                   |
| `editnoteheadparenthesis`                                                                                                                                | disregarded; the parenthesis check scores it                                                                                                                                                                                                                               |
| `staffgrpins`, `staffgrpdel`, `staffgrpnameedit`, `staffgrpabbreviationedit`, `staffgrpsymboledit`, `staffgrpbartogetheredit`, `staffgrppartindicesedit` | `layout`                                                                                                                                                                                                                                                                   |
| `noteins`, `notedel`, `voiceins`, `voicedel`, `insbar`, `delbar`, `inspart`, `delpart`                                                                   | decomposed, below                                                                                                                                                                                                                                                          |
| `lyricins`, `lyricdel`, `lyricedit`, `lyricidedit`, `lyricoffsetedit`, `lyricstyleedit`, `mditemins`, `mditemdel`, `mditemvalueedit`                     | not expected, since their detail levels are off; any occurrence is a protocol defect                                                                                                                                                                                       |

The `kind` of an extra assigns it as follows:

| `kind`                                                     | Assignment        |
| ---------------------------------------------------------- | ----------------- |
| `clef`                                                     | `clef`            |
| `keysig`                                                   | `key_signature`   |
| `timesig`                                                  | `time_signature`  |
| `ottava`                                                   | `ottava`          |
| `slur`                                                     | `slur`            |
| `arpeggio`                                                 | `arpeggio`        |
| `tremolo`                                                  | `tremolo`         |
| `dynamic`                                                  | `dynamic`         |
| `crescendo`, `diminuendo`                                  | `hairpin`         |
| `pedalmark`, `pedalbounce`, `pedalgapstart`, `pedalgapend` | `pedal`           |
| `barline`, `repeat`, `ending`                              | `repeat_ending`   |
| `direction`                                                | `text`            |
| `tempo`, `rehearsalmark`                                   | `tempo_rehearsal` |
| `staffinfo`, `systembreak`, `pagebreak`                    | `layout`          |
| `chordsym`, or a kind not listed                           | protocol defect   |

In each score, a key signature that restates the key in effect on its staff,
a clef at the start of a system that restates the clef in effect on its
staff, or a numeric time signature that restates the meter in effect, is a
reprint, which the format does not encode. So is a trill mark or
a `words` direction on the first event of a system on a staff where a
`wavy-line` or `dashes` line that started in an earlier system is open, when
it repeats the trill mark or the words that start that line. The harness
removes reprints from both annotated scores before comparison, so a reprint
is neither an element nor part of any operation. The first key, time
signature and clef of each staff, any change, and a `common` or `cut` time
signature are never reprints.

An operation that touches a whole object is decomposed. Each note, rest,
chord or extra that a note, voice, measure or part insertion or deletion holds
is one difference for element counting. For the per-category counts, each of
its symbols is assigned by the tables above as if inserted or deleted alone:
its pitches to `pitch`, further chord members to `chord_membership`, its
duration, dots and rest state to `rhythm_rest`, and so on. The container
operation itself adds nothing.

Staff groups and staff details are assigned to `layout` because the format
encodes neither: the piano grand staff is implied, and the screen fixes the
staff count and lines.

### Pairing rules

These rules apply before counting, in this order:

1. A deleted note of `Y` and an inserted note of `T(Y)` in the same measure,
   with the same onset, pitches, duration and attached symbols, on different
   staves, are one `cross_staff` difference. The pair is disregarded here
   when the cross-staff check has an element for either note. Otherwise, as
   when a whole voice changes staff, the reference note is non-identical and
   no extra element is added.
2. Such a pair on the same staff but in different canonical voice positions,
   as defined by the format's
   [voice-order rules](text-format.md#voices-and-staves), is one `voice`
   difference: the reference note is non-identical and no extra element is
   added. Raw MusicXML voice numbers alone do not identify a voice for this
   rule.

### Supplementary checks

musicdiff 6.0 does not establish some content. For these facets a
project-owned check compares the MusicXML of `Y` and `T(Y)` directly. Each
facet is scored by exactly one instrument, and the table above disregards the
musicdiff operations on a facet that a check owns. Notes are aligned by
measure index, staff, onset and written pitch. In every key, match and
identity condition below except those of the cross-staff and voice-order
checks, a note of `T(Y)` that pairing rule 1 pairs with a note of `Y` takes
that note's staff. Wherever a key below names an
onset, it also holds the note's grace position: 0 for a non-grace note, and
for a grace note its 1-based position, in written order, among the grace notes
of its voice at that onset.

- **Fingering** (`fingering`): each `technical/fingering` digit on a note is
  one element, keyed by measure, staff, onset, written pitch, written duration
  and digit.
  Elements of `Y` without a match in `T(Y)` are non-identical, and elements of
  `T(Y)` without a match are extra.
- **Cross-staff** (`cross_staff`): a voice's staff in a measure is the staff
  that holds most of that voice's notes and rests there, the lower number on a
  tie. Each note or rest on another staff is one cross-staff element, keyed by
  measure, onset, pitches and staff. Matching is as for fingering.
- **Voice order** (`voice`): in each measure, a voice's staff is defined as
  for the cross-staff check. The voices of a staff are ranked by their
  MusicXML `voice` number, and a voice is identified by its content: the
  sequence of onsets, durations, pitches and attached symbols of its notes and
  rests. Each voice
  of `Y` in each measure is one element. It is identical when `T(Y)` has a
  voice of the same content on the same staff in that measure with the same
  rank. A voice of `Y` whose content no voice of `T(Y)` on that staff in that
  measure matches is not a voice-order element: musicdiff and the pairing
  rules score its differences. The voice-order check adds no extra elements.
- **Heading** (`heading`): in the instrument, credits are the only carrier of
  heading text, and a `words` direction is always `text`, never `heading`.
  Whether the converters agree with that is what the round trip tests. Each
  line of `credit-words` text, after whitespace normalization, is one element.
  Credits whose `credit-type` is `title`,
  `subtitle`, `composer`, `arranger` or `lyricist` count. Credits whose
  `credit-type` is `page number`, `rights` or `part name` do not. The lines of
  a score are in reading order: by `credit` page, and on each page, when every
  counted credit of the page has `default-x` and `default-y`, by descending
  `default-y` and then ascending `default-x`, and otherwise in document order,
  with the lines of one `credit-words` in written order. The page orders the
  lines but is not compared, because an assembled score does not encode pages.
  The two sequences are matched by a longest common subsequence of texts: a
  line of `Y` outside it is non-identical, and a line of `T(Y)` outside it is
  extra.
- **Lines** (`ornament` and `text`): musicdiff 6.0 drops the music21
  spanners for `wavy-line` and `dashes`, though it still compares the trill
  mark and the words text. Each `wavy-line` from `start` to `stop` on a later
  note is one `ornament` element, keyed by the measure, staff, onset, written
  pitch and written duration of the notes that carry its start and stop. A
  `wavy-line` that starts and stops on one note is not counted, because the
  format encodes only that note's trill mark. Each `dashes` from `start` to
  `stop` is one `text` element, keyed by the measure, staff and onset of the
  directions that carry its start and stop and by the text of the `words`
  direction that its start follows. Matching is as for fingering.
- **Parentheses** (`pitch` and `accidental`): each `notehead` with
  `parentheses="yes"` is a `pitch` element and each `accidental` with
  `parentheses="yes"` is an `accidental` element, keyed by measure, staff,
  onset, written pitch and written duration. Matching is as for fingering. A
  musicdiff accidental operation whose only
  difference is the parentheses is disregarded.
- **Tremolo strokes** (`tremolo`): musicdiff 6.0 encodes a tremolo without
  its stroke count. Each `tremolo` of `Y` with type `single` or `start` is one
  element when `T(Y)` has a `tremolo` of the same type on a note with the same
  measure, staff, onset, written pitch and written duration. It is identical
  when the stroke counts are equal. musicdiff scores a tremolo that `T(Y)`
  lacks, and the check adds no extra elements.
- **Beam hooks** (`beam`): musicdiff 6.0 encodes a hook as `partial`,
  without its direction. Each `beam` of `Y` with value `forward hook` or
  `backward hook` is one element when `T(Y)` has a hook of the same beam
  `number` on a note with the same measure, staff, onset, written pitch and
  written duration. It is identical when the two values are equal. musicdiff
  scores a hook that `T(Y)` lacks, and the check adds no extra elements.
- **Rest beams** (`beam`): musicdiff 6.0 discards a rest's beams and rebuilds
  them from its duration and the next event. Each `beam` on a rest, printed or
  not, is one element, keyed by measure, staff, onset, written duration, beam
  `number` and value. Matching is as for fingering, except that an unmatched
  element is neither non-identical nor extra when a counted musicdiff
  difference touches its rest. A musicdiff beam operation between two rests is
  `rhythm_rest` when they differ in duration type, as for notes, and is
  otherwise disregarded. The beam-hook check covers only hooks on notes that
  are not rests.
- **Tuplet ratio** (`tuplet`): with `Style` off, musicdiff 6.0 records only a
  tuplet's actual number, and with `Voicing` it compares gaps between notes,
  not onsets. Each note, rest or chord of `Y` with a `time-modification` is
  one element when `T(Y)` has one at the same measure, staff, onset and
  written pitches and no counted musicdiff `tuplet` difference touches it. It
  is identical when the `actual-notes` and `normal-notes` values are equal.
  The check adds no extra elements.
- **Slur endpoints** (`slur`): musicdiff 6.0 compares a slur by its start
  offset and duration, not by the events it joins. Each slur of `Y` is one
  element when `T(Y)` has a slur whose start event has the same measure and
  onset, whatever its grace position, and whose stop event ends at the same
  time, no counted musicdiff
  `slur` difference touches it and no counted musicdiff difference touches its
  start or stop event. Slurs are matched one to one, preferring equal stop
  onsets, staffs, written pitches and durations, and the element is identical
  when the start events have the same grace position, the stop events also the
  same measure and onset, and the start and
  stop events the same staffs, written pitches and written durations. The
  check adds no extra elements.
- **Tie ends** (`tie`): musicdiff 6.0 marks a tie only on the note that
  starts or continues it, not on the note where it ends, including a `tie-in`
  pitch. Each `tie` of type `stop` on a note is one element, keyed by measure,
  staff, onset, written pitch and written duration. Matching is as for
  fingering. An unmatched stop is neither an element nor an extra element when
  a counted musicdiff difference touches its note, or touches the note of the
  same staff and written pitch that starts a tie and ends at its onset.

### Text-space consistency check

For each file whose conversions succeed, the canonical text of `reverse(Y)`
must equal the canonical text of `reverse(T(Y))`. A mismatch is reported as a
converter defect with its count per arm. This check is not part of the
fidelity figure.

## Calibration

Calibration runs before any corpus or gold run, and again after any change to
the instrument. Its fixtures are small self-authored MusicXML pairs, committed
with their provenance:

- for each of the 27 categories, at least one pair that differs in exactly one
  element of that category, built so that one edit gives one operation, and
  for `rhythm_rest` also a pair in which an unbeamed eighth note becomes a
  sixteenth note, one in which an unbeamed eighth rest becomes a sixteenth
  rest, and one in which a sixteenth rest in a beam group becomes an eighth
  rest;
- for each keyword value that the format's grammar admits for a note flag, a
  relation, a block option or a direction, other than a layout hint, and that
  has another admitted value of the same option, one pair that changes only
  that value to such a value, for example a `hook-backward` beam state that
  becomes `hook-forward`; a value without one has no value pair. Two
  keyword values belong to the same option when the
  [format](text-format.md#category-encodings) assigns them to the same
  category and one can replace the other at the same place in a valid text,
  at both ends of a relation together, as `stacc` and `acc`, `grace` and
  `grace/`, `cresc` and `dim` of a hairpin or `G` and `F` of a clef; the
  option's category is that category;
- for each layout hint that the format lists (stem direction, placement,
  inverted fermata, dynamic placement), one pair that differs only in it,
  one pair that differs only in a rest's staff position and one that differs
  only in the placement of a trill mark;
- pairs that differ only in generated identifiers or `divisions`, a pair whose
  voices are renumbered without a change of order (for example staff 2 voices
  5 and 6 against 1 and 2), and identical pairs;
- a pair in which the two voices of one staff in one measure swap their
  numbers, so that their order changes, and a pair in which two same-rhythm
  unison voices of one staff with different articulations swap their numbers;
- one pair that differs only in the end note of an extended trill, one in
  which an extended trill moves between simultaneous unison notes of different
  written durations in two voices of one staff that both carry a trill mark,
  one that differs only in the end of a dashes line, and one in which a
  dashes line moves between two `words` directions of one staff and onset;
- one pair for each pairing rule, with the rule-2 pair using raw MusicXML
  voice numbers that do not encode the canonical voice order; a second pair
  of pairing rule 1 in which the reference measure and the immediately
  following measure have identical content on the staff losing the moved
  note; a pair of pairing rule 1 whose moved note carries a fingering digit
  and a slur end; and a pair in which a one-note voice moves to the other
  staff, where it becomes that staff's highest-numbered voice;
- a pair in which the same-rhythm voices of one staff in one measure are a C4
  and an E4 in `Y` and two C4s in `T(Y)`;
- one pair that differs only in the stroke count of a single-note tremolo,
  one that differs only in that of a two-note tremolo, and a pair in which two
  simultaneous unison notes of different written durations in two voices of
  one staff swap their single-note tremolo stroke counts;
- a pair in which only the normal number of one tuplet group changes, for
  example from 3:2 to 3:1;
- a pair in which two consecutive grace notes of the same pitch swap their
  fingering digits, and a pair in which two simultaneous unison notes of
  different written durations in two voices of one staff swap their fingering
  digits;
- a pair in which the end of a slur moves to a simultaneous note of a
  different pitch and equal duration in another voice of the same staff, a
  pair in which the start of a
  slur moves to a simultaneous note of the same pitch and a different written
  duration in another voice of the same staff, a pair in which the start of a
  slur moves from the first to the second of two consecutive grace notes of the
  same pitch at one onset, a pair in which the end of a
  slur moves to a simultaneous note of equal written duration on the other
  staff, a pair in which the end of a slur moves to an earlier note of another
  voice of the same staff that ends at the same time, and a pair in which the
  end of
  a tie moves to a simultaneous note of the same pitch and a different written
  duration in another voice of the same staff;
- a pair in which `T(Y)` omits a second-ending `tie-in` whose source note also
  ties into the first ending, the reverse pair, in which `T(Y)` adds one, and a
  pair in which `T(Y)` drops an ordinary tie;
- a pair in which `T(Y)` drops the level 2 hook of a sixteenth rest in a beam
  group, and the reverse pair, in which `T(Y)` adds one;
- a pair in which the parentheses of a notehead move to a simultaneous unison
  notehead of a different written duration in another voice of the same staff;
- a pair in which `Y` reprints an unchanged key and an unchanged numeric time
  signature in a later measure and an unchanged clef at the start of a later
  system, and `T(Y)` omits them, and a pair in which `Y` reprints the trill
  mark of an extended trill and the
  words of a dashes line at the start of the system they continue into and
  `T(Y)` omits them, and a pair in which a wavy line of `Y` starts and stops on
  its trilled note and `T(Y)` has only the trill mark;
- a pair in which two heading lines of one page swap their reading order, and
  a pair in which the only change is that a page 2 heading line is moved to
  page 1 after the page 1 lines.

The instrument passes when it reports:

- exactly one difference, in the target category, for each category pair;
- at least one counted difference or non-identical or extra element of a
  supplementary check, and none outside the option's category, for each
  value pair;
- exactly one `layout` difference, exactly one non-identical or extra layout
  element and no content difference for each layout pair;
- exactly two non-identical `voice` elements and no other difference for each
  voice-swap pair, since both voices change rank;
- exactly one non-identical and one extra `ornament` element for the extended
  trill pair and for the extended-trill move pair, and exactly one
  non-identical and one extra `text` element for
  the dashes pair and for the dashes-text pair, each with no other difference;
- exactly one counted `cross_staff` difference and no other difference for
  the pair of pairing rule 1, the pair with identical adjacent measures, the
  marked pair and the voice that moves to the other staff;
- exactly one non-identical `voice` element and no other difference for the
  pair of pairing rule 2;
- exactly one counted `pitch` difference and no other difference for the
  voice-coupling pair;
- exactly one non-identical `tremolo` element for each tremolo-stroke pair,
  exactly two non-identical `tremolo` elements for the tremolo-swap pair,
  exactly one non-identical `beam` element for the hook value pair, and
  exactly one non-identical `tuplet` element for the tuplet-ratio pair, each
  with no other difference;
- exactly two non-identical and two extra `fingering` elements for the
  grace-note pair and for the unison-voice pair, exactly one non-identical
  `slur` element for each slur-endpoint pair, exactly one non-identical and one
  extra `tie` element for the tie-end pair, exactly one non-identical `tie`
  element for the tie-in deletion pair, exactly one extra `tie` element for the
  tie-in insertion pair, exactly one non-identical `beam` element for the
  rest-hook deletion pair, exactly one extra `beam` element for the rest-hook
  insertion pair, exactly one counted musicdiff `tie` difference for the
  ordinary-tie pair, exactly one non-identical and one extra `pitch`
  element for the parenthesis pair, and exactly one non-identical and
  one extra `heading` element for the heading-order pair, each with no other
  difference;
- no difference for the other pairs, including the reprint pairs.

The screen's allowlist is reviewed against the same fixtures. A
failure is a protocol defect: this record is revised under review before any
corpus or gold run.

As a preflight, one MuseScore conversion of a calibration fixture is run and
the settings-file hash is checked before any corpus export.

## MuseScore import check

Each forward output `T(Y)` of every arm is imported by MuseScore with `-o` to
a `.mscz` file in a fresh temporary directory. The import passes when the
exit status is 0 and the output file exists and was written by that
invocation. The run records the exit status, a sanitized log excerpt with no
private path or content, and whether the output exists. A pass shows only that
MuseScore accepted the file. It is not evidence of fidelity.

## System-assembly test

For each gold piece, the harness splits the whole-piece encoding, which is the
canonical text of `reverse(X)`, at the printed system breaks of `X`. A break is
a `print` element with `new-system="yes"` or `new-page="yes"`, and the first
system starts at measure 1. Each system document takes its carry-in from the
whole-piece encoding by the format's carry rules, and the heading lines of the
whole piece go to the system they precede. The documents are then assembled,
and the title and composer lines of the whole-piece encoding, if any, are
added to the assembled score as metadata that a user supplies.

The test passes for a piece when the canonical text of the assembled score
equals the whole-piece canonical text. Otherwise the forward converter
converts both texts, and the two results are compared with the content pass,
the category assignment, the pairing rules and the supplementary checks, with
the whole-piece result as the reference. The piece then passes only when
assembly and both conversions succeed and there are zero counted differences,
as OMR-REQ-010 requires. The number of pieces whose texts
differ is reported in either case, and the number of systems per piece is
reported as an aggregate.

A gold piece whose `X` has no `print` system or page break is not evidence
for assembly. It does not pass the test, and the count of such pieces returns
to the owner.

Gold system breaks fall at measure boundaries, so this test does not exercise
systems that start inside a measure.

## Layout-fidelity score

The layout pass gives the score, per arm and for gold. A _layout element_ of
`Y` is an annotated object, or an articulation, expression or tuplet group
attached to one, that carries a value other than musicdiff's default of a
property whose difference the operation table assigns to `layout`, such as a
non-empty style, a stem direction other than `unspecified`, a rest staff
position other than the default, a notehead other than `normal` or a set
notehead fill, a placement, or a tuplet's printed number or bracket; or that
is a system or page break, staff details or a staff group. It
is identical when no `layout` difference touches it. An articulation,
expression or tuplet operation touches only the attached element that it
changes, not the note that carries it, and another operation on a note does
not touch the elements attached to it. An object of `T(Y)` that
carries such a property without a counterpart that carries it is an extra
layout element. The score is the number of identical layout elements divided
by the number of layout elements plus extra layout elements, pooled per arm.
A file that the failure rules count as failed counts every layout element of
`Y`, or of `X` when `R(X)` is missing or cannot be parsed, as non-identical.
It is reported and never gates acceptance.

## Execution and bounds

1. Preflight: record versions, check the settings file and the download
   ledger, and run calibration.
2. Select the PDMX and Lieder samples and post the selected list.
3. Make `R(X)` for the selected public files.
4. For each arm, run reverse, forward, the import check, both passes, the
   supplementary checks and the text-space check on every file.
5. Compare `X` with `R(X)` for the baseline.
6. Run the system-assembly test on gold.

Each arm runs once per converter commit under test. Reruns after converter
fixes are allowed under this protocol, each with its commit recorded, and every
completed run is listed in the results carrier. The acceptance figures come
from one run in which every arm used the same converter commit. The bounds of
one run are 200 PDMX files, 50 Lieder extracts, the accepted gold pieces, at
most the 1,356 Lieder scores of the pinned release for selection exports, and
300 seconds per conversion or comparison step.

## Stop conditions

A run stops, and returns to the owner or to review, when:

- the settings-file hash changes, or the settings file is absent at start;
- MuseScore is not installed at its default location, or its version string
  is not 4.7.6, build `4.7.6.261022093`;
- a download would take the W1 cumulative total above 20,000,000,000 bytes;
- a difference cannot be assigned, or another protocol defect appears;
- calibration fails;
- the harness is about to write outside the run directory, its temporary
  directory or the download directory;
- private content, a private path or a gold piece identity is about to be
  written into the repository or a public carrier.

## Evidence, publication and cleanup

The run directory, downloads and all per-file results stay outside the
repository. Public carriers receive only:

- per-arm fidelity figures with their numerators and denominators, secondary
  figures and the layout-fidelity score;
- per-category difference counts, import-check counts, text-space mismatch
  counts and failure counts by status;
- exclusion counts by reason;
- tool versions, the converter and harness commits and the dataset versions,
  citing the PDMX deposit, which is licensed CC BY 4.0;
- the complete lists of selected PDMX and Lieder files, by PDMX path or
  Lieder path, and a short list of such files where a finding needs one.

Gold results appear only as aggregates and anonymized batch numbers. Each
reported statement is classified as a runtime observation, a source finding
or an inference.

Temporary MuseScore outputs are deleted after each file. The run directory is
kept until the pull request that reports the results is merged, and then the
owner may delete it. Downloaded corpora stay outside the repository.

## Evidence limits and open checks

Results measure these converters, MuseScore 4.7.6 and musicdiff 6.0 with its
coupling correction and the resolved dependencies on these files. They do not
show that the format is complete, that a model can learn it or that it is
better than ABC or MusicXML, as OMR-REQ-011 and
[OMR-REQ-014](requirements.md#omr-req-014-evidence-separation) state. A zero
musicdiff difference is not byte-identical MusicXML or identical engraving.
The layout score covers only the properties that musicdiff 6.0 compares with
`Style`, which exclude coordinates and font family. A layout element whose
object `T(Y)` deletes is touched by no `layout` difference and counts as
identical, so layout lost with deleted content, which the content pass counts,
does not lower the layout score. The heading check compares only the reading
order of heading lines, not which system or page a heading precedes; a move
between systems is not counted. It counts a credit that repeats an earlier
page's line as a heading line, though the format does not encode running
headers, so a conversion that omits one is counted. musicdiff 6.0 compares an
arpeggio across events by its type, member count and the onset of its highest
member, not by its members, so a regrouping that keeps these is not counted.
musicdiff 6.0 also drops a clef that restates an earlier clef of its staff in
the same measure; the screen excludes such clefs in `X`, but one that `T(Y)`
adds is not counted.
The fingering, parenthesis, tie-end, slur-endpoint, tremolo-stroke, beam-hook and
extended-trill checks identify a note by its written pitch and written
duration, not by its voice, so a mark or endpoint
that moves between simultaneous notes of the same pitch and written duration
in two voices of one staff, which usually share one notehead, is not counted.
The rest-beam check identifies a rest by its written duration, so a beam that
moves between simultaneous rests of one duration in two voices of one staff
is not counted.
The cross-staff check identifies a note by its pitches, not by its voice or
written duration, so simultaneous unison notes of two voices that exchange
staves are not counted when musicdiff reports the exchange only as notes that
pairing rule 1 disregards.
The text-space check shows such a loss only when the reverse conversion does
not make it in the same way on `Y` and on `T(Y)`, so a consistent loss can
escape every check.

These points are unverified. Calibration or a reviewed revision must settle
each before results depend on it:

- whether musicdiff 6.0 discriminates fingering digits, cross-staff voice and
  beam relations and nested tuplet groups; the supplementary checks cover the
  first two, the direction of beam hooks and beams on rests;
- whether `pitchtypeedit` means a change between note and rest, and how
  musicdiff represents a fermata's shape and a pickup measure;
- the `PDMX.csv` header, including the prefilter columns and the column that
  holds the compressed MusicXML path, the layout of
  `mxl.tar.gz`, and the Lieder file names at the pinned commit;
- whether MuseScore 4.7.6 exports `instrument-sound` for Lieder piano parts;
- whether a console conversion writes the settings file, which the preflight
  checks;
- the size of the Lieder download and the sizes of the eligible PDMX and
  Lieder sets;
- whether credits without a `credit-type` count as heading credits; this is
  settled on the calibration set and the selected files before results depend
  on it.

The harness confines its own writes. MuseScore's other writes in its own data
folders, such as logs, are not monitored beyond the settings-file hash.

## Source basis

Source findings, with the pinned sources they rely on:

- musicdiff `v6.0.0` is commit `0368856e`, distributed on PyPI as `6.0`, and
  requires `music21>=10.5.0`, `converter21>=4.0.2` and unversioned `numpy`.
  See the [release](https://github.com/gregchapman-dev/musicdiff/releases/tag/v6.0.0),
  [PyPI metadata](https://pypi.org/pypi/musicdiff/6.0.0/json) and
  [setup.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/setup.py#L63-L69).
- The detail-level flags, their defaults and their combination are in
  [detaillevel.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/detaillevel.py#L23-L117).
  With `Voicing`, voice identifiers and order are not compared.
- The high-level API passes the first score as predicted and the second as
  ground truth, and returns `None` when parsing fails. See
  [`__init__.py`](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/__init__.py#L51-L105)
  and [lines 229 to 234](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/__init__.py#L229-L234).
  Batch mode skips files whose parsing fails; see
  [lines 374 to 394](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/__init__.py#L374-L394).
- Raw operations are
  [`DiffOperation`](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L51-L78)
  objects, whose names are emitted between
  [lines 312 and 1515](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L312-L1515)
  of `comparison.py`, and the kinds of extras are in
  [m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L1057-L1115).
  musicdiff collects only the spanner classes in
  [m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L923-L960)
  and skips unrecognized kinds
  ([lines 898 to 901](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L898-L901)),
  and drops a clef that restates the last clef of its staff in the same measure
  ([lines 906 to 917](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L906-L917)).
  music21 10.5.0 imports `wavy-line` as `TrillExtension` and `dashes` as
  `Line`, neither of which is collected; see `music21/musicxml/xmlToM21.py`
  lines 3932 to 3934 and 4396 to 4404 in the
  [PyPI wheel](https://pypi.org/project/music21/10.5.0/#files)
  `music21-10.5.0-py3-none-any.whl`, SHA-256
  `9924eff5fbf58490e67cbf3b78a58ba53040e77c281930d354af32a747e94606`.
- The normalized OMR edit distance divides the OMR edit distance by the symbol
  counts of both scores; see
  [visualization.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/visualization.py#L1384-L1396).
  Note and staff-group comparison excludes encoding identifiers; see
  [annotation.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L645-L647)
  and
  [lines 1561 to 1562](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L1561-L1562).
  The `Style` level does not compare coordinates or font family; see
  [m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L2031-L2038)
  and
  [lines 1975 to 1977](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L1975-L1977).
- musicdiff encodes ties, slurs, tremolos, tuplet groups, arpeggios,
  durations and rests as the supplementary checks and the assignment table state. A note
  carries a tie flag only for `start` or `continue`
  ([m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L354-L359)).
  A slur has no string, symbol or information
  ([lines 2162 to 2183](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L2162-L2183)),
  and a spanner's offset and duration come from its primary and last elements
  ([lines 1340 to 1356](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L1340-L1356)),
  where an arpeggio's primary element is its highest note
  ([lines 757 to 766](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L757-L766))
  and an arpeggio keeps only its type and span length
  ([lines 2193 to 2211](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L2193-L2211)).
  A tremolo is only `bTrem` or `fTrem`
  ([lines 221 to 247](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L221-L247)).
  Without `Style`, a tuplet keeps only its actual number
  ([lines 612 to 644](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L612-L644)),
  and with `Voicing` a note keeps only its gap from the previous note: its
  offset is set only without `Voicing`
  ([annotation.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L110-L141)),
  voice annotation runs only with it
  ([lines 1196 to 1208](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L1196-L1208))
  and the gap is computed in
  [lines 1056 to 1069](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L1056-L1069).
  Every value from a quarter down has notehead `4`
  ([lines 220 to 228](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L220-L228)),
  and its flags are `partial` beam levels
  ([m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L483-L520)).
  It discards a rest's beams
  ([lines 57 to 59](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L57-L59))
  and rebuilds them from the rest's duration and the next event's beams
  ([lines 499 to 520](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L499-L520)).
  A rest's staff position is part of its name only with `Style`
  ([lines 290 to 301](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L290-L301)),
  and a change between two rests is a `pitchnameedit`
  ([comparison.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L349-L357)).
- At the pinned commit, voice coupling adds a pairing's cost only when the
  first voices of both lists differ; see
  [lines 1440 to 1460](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L1440-L1460)
  of `comparison.py`.
- MuseScore `v4.7.6` is commit `96480d58`, and its Windows build is
  `4.7.6.261022093`; see the
  [release](https://github.com/musescore/MuseScore/releases/tag/v4.7.6) and its
  `MuseScore-Studio-4.7.6.261022093-x86_64.msi` asset. Its
  `-F` and `-R` options both request a settings reset, which clears the
  settings store, and it registers no configuration-folder option. See
  [commandlineparser.cpp](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/app/internal/commandlineparser.cpp#L456-L462),
  [settings.cpp](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/framework/global/settings.cpp#L95-L117)
  and the [option list](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/app/internal/commandlineparser.cpp#L66-L190).
  The settings store uses INI format outside macOS and a custom path only in
  portable builds, under organization `MuseScore` and application
  `MuseScore4`; see
  [settings.cpp](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/framework/global/settings.cpp#L49-L61)
  and
  [main.cpp](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/app/main.cpp#L116-L123).
  Qt places such a user-scope INI file at
  `%APPDATA%\Organization\Application.ini` on Windows; see the Qt 6.8
  [QSettings documentation](https://github.com/qt/qtbase/blob/b839e9b36db3a4e50dfb34521d8ef8de1fd01969/src/corelib/io/qsettings.cpp#L2229-L2254).
  The MusicXML preference defaults are in
  [musicxmlconfiguration.cpp](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/importexport/musicxml/internal/musicxmlconfiguration.cpp#L33-L80).
  Conversion failures return nonzero codes; see
  [convertercodes.h](https://github.com/musescore/MuseScore/blob/96480d58445e3b24b230a3fe7d4725d639aa2eb3/src/converter/convertercodes.h#L27-L45).
- PDMX v9 has the files and sizes used above and describes the
  `no_license_conflict` and `all_valid` subsets; see the
  [deposit](https://zenodo.org/records/15571083) and its
  [metadata](https://zenodo.org/api/records/15571083). The column names come
  from the dataset code at `7abbd6d9`:
  [pdmx.py](https://github.com/pnlong/PDMX/blob/7abbd6d926d2df94129034eba81c10069c9dcbc4/wrangling/pdmx.py#L281-L287)
  and
  [full.py](https://github.com/pnlong/PDMX/blob/7abbd6d926d2df94129034eba81c10069c9dcbc4/wrangling/full.py#L424-L435).
  The accepted license URLs are in
  [full.py](https://github.com/pnlong/PDMX/blob/7abbd6d926d2df94129034eba81c10069c9dcbc4/wrangling/full.py#L52-L58),
  and `tracks` value `0` denotes piano in
  [instruments.py](https://github.com/pnlong/PDMX/blob/7abbd6d926d2df94129034eba81c10069c9dcbc4/wrangling/instruments.py#L102-L113).
- OpenScore Lieder `v3.0.0` is commit `f44e962a` with 1,356 scores, and its
  scores are CC0. See the
  [release](https://github.com/OpenScore/Lieder/releases/tag/v3.0.0), the
  [catalogue](https://github.com/OpenScore/Lieder/blob/f44e962a1ff85ddcc2f6f42bed3b0c529555ee15/data/corpus.yaml#L2-L13)
  and the
  [license statement](https://github.com/OpenScore/Lieder/blob/f44e962a1ff85ddcc2f6f42bed3b0c529555ee15/README.md#L158-L160).

Inferences and design decisions: the separate identities, the extraction of
the piano part before the reference is defined, the fail-closed screen, the
pooled element measure, the category tables, the pairing rules, the
supplementary checks and the calibration set are decisions of this protocol.
The normalized OMR edit distance of
[Martinez-Sevilla et al. (2025)](https://arxiv.org/abs/2506.10488v3) and the
LMX round-trip check of
[Mayer et al. (2024)](https://arxiv.org/abs/2403.13763v1) informed them, but
neither uses this measure.
