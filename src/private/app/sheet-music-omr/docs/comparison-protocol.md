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
It is not a result report. Local results that informed it, including the
failed adjacent-measure calibration case and the harness pilots and
private-gold probes behind the voice-position and octave-shift rules, are
recorded in the W1 work carrier,
[Issue #1488](https://github.com/hcoona/three/issues/1488); they do not
establish conformance to this protocol.

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

The comparison oracle is positional: within each matched part, the zero-based
ordinal of a measure in the MusicXML sequence is its identity for comparison.
Printed measure numbers, XML IDs and measure content do not establish that
identity. A measure is compared only with the measure at the same ordinal;
musicdiff is never allowed to align a measure with one at another ordinal.
When one score has no measure at an ordinal present in the other, the absent
side is compared as an empty measure. Its insertions or deletions are
decomposed and scored by the rules below. This makes a missing, added or
shifted measure visible instead of allowing a repeated measure to conceal it.

For category assignment and the fidelity measure, the harness builds
musicdiff's annotated scores and calls `Comparison.annotated_scores_diff` on
each same-ordinal measure pair. It retains each raw operation with its part
and measure ordinal. A separate global-only comparison, run once per pass,
handles staff groups and parts that have no counterpart at the same part
ordinal. It contains no measures from matched parts; each unmatched part is
compared with an empty part, never with a part at another ordinal. Its
operations participate in category assignment and element counting. Its
edit-cost total is reported separately from the sum of per-measure edit costs.
The measure-pair and global-only results, not a content-based alignment across
the score, determine counted differences.

For comparison with the pinned library's ordinary behavior, the harness also
calls `Comparison.annotated_scores_diff` once on each complete pair of
annotated scores and retains its whole-score operations, distance and symbol
counts as a separate diagnostic. These figures are not used to assign
categories, pair objects, count elements or determine fidelity. The harness
does not use musicdiff's batch mode, which skips files whose parsing fails.

At the pinned commit, `Comparison._voices_coupling_recursive` adds the cost
of pairing the first voice of the first list with a voice `c` of the second
only when the first voices of both lists differ, so an unequal pairing can
cost nothing. The harness replaces that condition,
`compare_to[0] != original[0]`, with `c != original[0]`, for both the
measure-pair comparisons and the whole-score diagnostic, and changes nothing
else in musicdiff.

### Detail levels

- **Content pass:**
  `(DetailLevel.AllObjects | DetailLevel.Voicing) & ~(DetailLevel.Lyrics | DetailLevel.LyricIdentifiers | DetailLevel.ChordSymbols)`.
  `Style`, `Metadata` and `NoteStaffPosition` are off.
- **Layout pass:** the content pass with `DetailLevel.Style` added.

For each pass and file the harness retains every raw operation (name, kinds of
both objects, cost and location) for the anchored measure pairs, the
global-only comparison and the whole-score diagnostic, identifying which
comparison produced it. It reports the per-measure edit-cost sum and the
global-only edit cost separately, and the whole-score OMR edit distance,
whole-score symbol counts and whole-score normalized distance as separate
secondary figures. The whole-score normalized distance divides its cost by
the symbol counts of both complete scores and is not a symbol error rate.
Neither these diagnostics nor the measure below is reported as one.

### Elements and the fidelity measure

- An _in-scope element_ of `Y` is an annotated object of `Y` in the content
  pass (a note, rest or chord, or an extra such as a clef or dynamic) whose
  category is one of the 27 content categories, plus each element that a
  supplementary check below defines.
- An excluded reference voice of the voice-order check is not an in-scope
  element: it is neither in the denominator nor identical.
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
  because the format gives voice positions effects that depend on that order,
  and the voice-order check below compares it. Renumbering that keeps the
  canonical voice positions is not a difference;
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
pass plus every element that a supplementary check defines, counted on that
score alone. An element that a check counts only when `T(Y)` has a
counterpart counts without one, so each voice of each measure, each slur, each
tremolo of type `single` or `start`, each `beam` with value `forward hook` or
`backward hook` on a note that is not a rest, and each note, rest or chord
with a `time-modification` is an element, as are each fingering digit, each
note or rest off its voice's staff, each heading line, each `wavy-line` that
stops on a later note and each `dashes` line, each parenthesized notehead or
accidental, each beam on a rest and each note with a `tie` of type `stop`.

A gold piece whose reverse conversion reports unsupported content does not
pass OMR-REQ-010, and the count of such pieces returns to the owner, because
the requirements do not say how unsupported content in gold is treated. A
gold piece whose `X` musicdiff cannot parse also does not pass OMR-REQ-010,
and the count of such pieces returns to the owner.

## Category assignment

Every counted difference is assigned to exactly one of the 27 content
categories of the [format](text-format.md#category-encodings), or to `layout`.
A difference assigned to `layout` is never a content difference: the content
pass disregards it, and the layout pass counts it. For each pass, the
authoritative operations are those from same-ordinal measure pairs and the
global-only comparison. The ordinary whole-score diagnostic is never assigned
or counted. A difference that the table and rules below cannot assign is a
protocol defect. The run stops, and the difference is never counted as
agreement.

| musicdiff 6.0 operation                                                                                                                                  | Assignment                                                                                                                                                                                                                                                                                  |
| -------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `pitchnameedit`                                                                                                                                          | `pitch`; `layout` when both objects are rests, since only the layout pass compares a rest's staff position                                                                                                                                                                                  |
| `pitchtypeedit`                                                                                                                                          | `rhythm_rest` (note against rest)                                                                                                                                                                                                                                                           |
| `inspitch`, `delpitch`                                                                                                                                   | `chord_membership`                                                                                                                                                                                                                                                                          |
| `accidentins`, `accidentdel`, `accidentedit`                                                                                                             | `accidental`                                                                                                                                                                                                                                                                                |
| `headedit`, `dotins`, `dotdel`                                                                                                                           | `rhythm_rest`                                                                                                                                                                                                                                                                               |
| `insspace`, `delspace`, `editspace`                                                                                                                      | `rhythm_rest` (unprinted spacers and gaps in time)                                                                                                                                                                                                                                          |
| `graceedit`                                                                                                                                              | `grace`                                                                                                                                                                                                                                                                                     |
| `insbeam`, `delbeam`, `editbeam`                                                                                                                         | `beam`; `rhythm_rest` when the two notes differ in duration type, since musicdiff 6.0 gives every value from a quarter down the same notehead and encodes its flags only as beam levels                                                                                                     |
| `instuplet`, `deltuplet`, `edittuplet`                                                                                                                   | `tuplet`; `layout` when the two tuplet groups differ only in printed number, bracket or placement                                                                                                                                                                                           |
| `tieins`, `tiedel`                                                                                                                                       | `tie`                                                                                                                                                                                                                                                                                       |
| `insarticulation`, `delarticulation`, `editarticulation`                                                                                                 | `articulation_fermata`; `layout` when only the placement differs; disregarded when the object is a fingering                                                                                                                                                                                |
| `insexpression`, `delexpression`, `editexpression`                                                                                                       | by expression type: fermata `articulation_fermata` (`layout` when only its shape differs, by the fermata reading below); trill, mordent, inverted mordent, turn and inverted turn `ornament` (`layout` when only its placement differs); single-note tremolo `tremolo`; arpeggio `arpeggio` |
| `extrains`, `extradel`, `extracontentedit`, `extrasymboledit`, `extrainfoedit`, `extraoffsetedit`, `extradurationedit`                                   | by the object's `kind`, below; `layout` when only the placement differs                                                                                                                                                                                                                     |
| `extrastyleedit`, `editnoteshape`, `editnoteheadfill`, `editstemdirection`, `editstyle`                                                                  | `layout`                                                                                                                                                                                                                                                                                    |
| `editnoteheadparenthesis`                                                                                                                                | disregarded; the parenthesis check scores it                                                                                                                                                                                                                                                |
| `staffgrpins`, `staffgrpdel`, `staffgrpnameedit`, `staffgrpabbreviationedit`, `staffgrpsymboledit`, `staffgrpbartogetheredit`, `staffgrppartindicesedit` | `layout`                                                                                                                                                                                                                                                                                    |
| `noteins`, `notedel`, `voiceins`, `voicedel`, `insbar`, `delbar`, `inspart`, `delpart`                                                                   | decomposed, below                                                                                                                                                                                                                                                                           |
| `lyricins`, `lyricdel`, `lyricedit`, `lyricidedit`, `lyricoffsetedit`, `lyricstyleedit`, `mditemins`, `mditemdel`, `mditemvalueedit`                     | not expected, since their detail levels are off; any occurrence is a protocol defect                                                                                                                                                                                                        |

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

Before annotation, the harness reads the `words` of both scores by the rule
below, which the converters' MusicXML mapping also uses and which music21 and
musicdiff 6.0 do not apply. Without it, tempo words, which the format assigns
to `tempo_rehearsal`, would be counted as `text`, and navigation words, which
it assigns to `text`, would not be compared. music21 imports words that name a
repeat instruction, such as "D.C. al Fine" or "Fine", as a repeat expression
that holds them as a text expression, and musicdiff 6.0 assigns a repeat
expression no `kind`. The harness replaces it with the text expression it
holds, whose kind is `direction`. A direction without `dashes` has tempo
words when the text of its `words` is not empty and it has a `metronome`, a
`sound` with a `tempo` attribute, or `words` that all have
`font-weight="bold"`, whatever its other direction types. That text is the
character data of its `words` elements, joined in document order with no
separator and then trimmed, and it becomes the text of the direction's
metronome mark, which replaces the text expressions that held it. music21
trims each `words` element separately, and the harness finds the music21
object of each by that trimmed text at the direction's onset. A direction
without a `metronome` gets a new metronome mark without a number in place of
its text expressions, with their offset, the direction's placement and the
style of its first `words` element; the style of the other `words` elements is
not compared. musicdiff 6.0 compares such a mark
by its text: it does not compare a number that music21 infers from the text,
and it compares text of the form note = number by that note and number. Its
kind is `tempo`.
musicdiff 6.0 pairs extras only of the same kind, so a change between tempo
words and other words, such as a change of only the weight of words in a
direction without a `metronome` or a `sound` tempo, is one `tempo_rehearsal`
and one `text` difference. When the harness cannot find the music21 object of
a `words` element, or the metronome mark of a direction with a `metronome`,
the run stops with `ProtocolDefectError`.

With `Style`, the harness also reads the shape of a fermata. musicdiff 6.0
records a fermata only by its expression name, which holds neither its shape
nor its placement, so an upright and an inverted fermata would compare
equal. The harness gives the fermata of a note, chord or rest whose type is
inverted the name "inverted fermata", so that a change of only its shape is
one `editexpression`, assigned to `layout`. MusicXML 4.0 defines a `fermata`
without a `type` as upright, but music21 10.5.2 imports it as inverted.
Before parsing, the harness therefore gives each such `fermata` the type
`upright`, and if one remains without a type, the run stops with
`ProtocolDefectError`. A fermata's placement is not compared; the format
does not encode it.

The harness also reads each octave shift as MusicXML gives it. music21 10.5.2
starts an ottava on a later note, chord or rest of the document, whatever its
staff or offset; ends it on the last one before its stop in that measure, if
any, ignoring the stop's `offset`; and drops it where its stop's `number`
does not match its start's. musicdiff 6.0 compares an ottava from the onset
of its first element to the end of its last, and the written pitch of each
element under it; it annotates no hidden note or rest. Before annotation, the
harness therefore removes music21's ottavas and, for each MusicXML octave
shift, builds an ottava, styled as music21 styles one from the shift's
start `octave-shift` element and `direction`, over the notes, chords and
rests of its staff that musicdiff annotates and whose onsets fall in the
shift: first one at its start, and last one, another unless it is the only
one, that ends at its end. A shift whose stop is written after its last
note, at the start of the next measure or with an `offset` to the barline
is thus one extent. The run stops with `ProtocolDefectError` where the
harness finds no such ottava: when no note, chord or rest that musicdiff
annotates starts the shift at its start or ends it at its end, or when one
alone does both while others lie inside the shift, since a music21 ottava
holds each element once. It also stops where the ottavas so built still
disagree with the MusicXML octave shifts after annotation, such as two
octave shifts that overlap on one staff.

In each score, a key signature that restates the key in effect on its staff,
a clef at the start of a system that restates the clef in effect on its
staff, or a numeric time signature that restates the meter in effect, is a
reprint, which the format does not encode. So is a trill mark or
a `words` direction, read as text or as tempo words, on the first event of a
system on a staff where a
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

The global-only comparison is authoritative for staff-group differences and
content held by unmatched parts. An unmatched part is decomposed as an
insertion or deletion against an empty part, using the same category and
element-counting rules as an unmatched measure. No measure of a matched part
is present in this comparison. Costs from this pass are retained separately
and are not included in the per-measure edit-cost sum.

### Pairing rules

These rules apply before counting, in this order:

1. A deleted note of `Y` and an inserted note of `T(Y)` in the same part and
   measure ordinal, with the same onset, pitches, duration and attached
   symbols, on different staves, are one `cross_staff` difference. The pair
   is disregarded here when the cross-staff check has an element for either
   note. Otherwise, as when a whole voice changes staff, the reference note
   is non-identical and no extra element is added. A note at another measure
   ordinal is never its pair.
2. Such a pair in the same part, measure ordinal and staff, both of whose
   notes have a known canonical position, as defined by the format's
   [voice-order rules](text-format.md#voices-and-staves) and the voice-order
   check below, and whose positions differ, is one `voice` difference: the
   reference note is non-identical and no extra element is added. A pair with
   an undetermined position on either side is not a rule-2 pair. The
   assignment to a canonical position is content; the stem flag
   itself is layout. Raw MusicXML voice numbers alone do not identify a voice
   for this rule.

### Supplementary checks

musicdiff 6.0 does not establish some content. For these facets a
project-owned check compares the MusicXML of `Y` and `T(Y)` directly. Each
facet is scored by exactly one instrument, and the table above disregards the
anchored musicdiff operations on a facet that a check owns. The whole-score
diagnostic does not participate in this assignment. Notes are aligned by
measure ordinal, staff, onset and written pitch. In every key, match and
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
  canonical positions, as defined by the format's
  [voice-order rules](text-format.md#voices-and-staves); MusicXML `voice`
  numbers do not establish those positions. The stem flag itself is layout,
  but a change in the voice-line assignment it determines under those rules is
  content. A voice is identified by its content: the sequence of onsets,
  durations, pitches and attached symbols of its notes and rests. Its
  canonical position is evaluated event by event in the order of that content
  sequence.

    Each event's position is _known_, _undetermined_ or _missing_. It is
    undetermined, with one of the reasons below, where those rules do not
    determine it:

    - `no-stemmed-event`: an unstemmed event's voice has no stemmed event to
      place it, and no partner voice of one stem direction places it opposite;
    - `nearest-stem-tie`: an unstemmed event is equally near stemmed onsets of
      both directions in its voice (the instrument reads "the voice whose rhythm
      it completes" as the nearest stemmed onset);
    - `indistinguishable-voices`: two overlapping voices resolve to one stem
      direction;
    - `mixed-stem-voice`: one of three or four overlapping voices has stems of
      both directions;
    - `stem-counts`: three or four overlapping voices do not split into one or
      two voices per stem direction;
    - `tied-heights`: two voices of one stem direction have the same highest
      pitch, so neither is above the other (the instrument reads "from the
      top" as the highest pitch).

    The reasons are in precedence order: where several apply, the first listed
    is reported. In a two-voice staff-measure an undetermined event makes only
    its own voice undetermined, and that voice's other events keep their known
    positions. In a three- or four-voice staff-measure, every
    voice's reason is collected first; any of the first five reasons then makes
    every voice of that staff-measure undetermined under the first of the
    collected reasons, and only when none applies are the voices ranked by
    height, where a tie makes only the two tied voices undetermined. A voice is
    undetermined, under the first of its events' reasons, when any of its
    events is. MusicXML voice numbers and order do not affect the reported
    reason. A cross-staff beam takes its home-staff anchor's position,
    undetermined if the anchor's is. An undetermined position never equals
    another position, undetermined or not.

    On each staff in each measure, the voices of `Y` and of `T(Y)` with the same
    content are matched one to one. The voices of `Y` whose positions are all
    known are matched first, as many as possible with a voice of `T(Y)` of the
    same ordered position sequence, then with the remaining voices of `T(Y)` of
    that content, known or not. Each matched voice of `Y` is one element,
    identical only when its partner has the same known ordered position
    sequence; a partner with an undetermined position makes it non-identical.
    Comparing only the counts at each position is insufficient. A voice of `Y`
    whose position is undetermined is not an element and never counts as
    agreement: when a voice of `T(Y)` of its content remains after the known
    voices are matched, it is an _excluded reference voice_, counted by its
    reason; where fewer such voices of `T(Y)` remain than undetermined voices
    of `Y` of that content, the excluded voices are those whose reasons come
    first in precedence order. A voice of `Y` without a partner, because no
    voice of `T(Y)` on that staff in that measure has its content or because
    `T(Y)` has fewer voices with that content, is neither: musicdiff and the
    pairing rules score its differences. The voice-order check adds no extra
    elements.

    Excluded reference voices are uncertainty, not agreement. Each file's
    comparison keeps its own count of them by reason; the run reports, for each
    arm, their sum over the arm's files, by reason and in total, beside that
    arm's voice-order elements and fidelity figure. An acceptance claim that
    relies on an arm with excluded reference voices returns to the owner for
    disposition. An exclusion does not by itself establish a format gap or a
    converter defect; a comparator limitation remains a possible cause.

    A missing position is a protocol defect and stops the run with
    `ProtocolDefectError`, as do: a double-stem event, which belongs to both
    voices and whose dual membership the instrument cannot represent; a chord
    whose notes carry different stems; more than four overlapping voices on one
    staff; beam ownership that cannot be resolved; a nested or interrupted
    primary beam; an event in more than one music21 voice; a mixed-stem beam
    group whose first event is outside the measure; a cross-staff beam with no
    home-staff anchor; and a voice to be ranked by height without a pitch. Every
    voice's beams are read before any position is decided, so an undetermined
    voice does not hide a beam stop in another voice of its staff-measure.

    The stop for a mixed-stem beam whose first event is outside the measure
    is a temporarily accepted implementation gap in the comparison instrument,
    not a permanent exclusion of that case. A later extension may support it
    through a reviewed and accepted protocol change with calibration coverage;
    until then this stop remains.

- **Heading** (`heading`): in the instrument, credits are the only carrier of
  heading text, and a `words` direction is `text` or, by the words reading,
  `tempo_rehearsal`, never `heading`.
  Whether the converters agree with that is what the round trip tests. Each
  line of `credit-words` text, after whitespace normalization, is one element.
  Credits whose `credit-type` is `title`,
  `subtitle`, `composer`, `arranger` or `lyricist` count. Credits whose
  `credit-type` is `page number`, `rights` or `part name` do not. Credits
  without a `credit-type` do not count until the open check on them in
  [Evidence limits](#evidence-limits-and-open-checks) is settled. The lines of
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
  element when `T(Y)` has a `tremolo` of the same type on an event with the
  same measure, staff, onset, written pitches and written duration. It is
  identical when the stroke counts are equal. musicdiff scores a tremolo that
  `T(Y)` lacks, and the check adds no extra elements.
- **Beam hooks** (`beam`): musicdiff 6.0 encodes a hook as `partial`,
  without its direction. Each `beam` of `Y` with value `forward hook` or
  `backward hook` is one element when `T(Y)` has a hook of the same beam
  `number` on an event with the same measure, staff, onset, written pitches and
  written duration. It is identical when the two values are equal. musicdiff
  scores a hook that `T(Y)` lacks, and the check adds no extra elements.
- **Rest beams** (`beam`): musicdiff 6.0 discards a rest's beams and rebuilds
  them from its duration and the next event. Each `beam` on a rest, printed or
  not, is one element, keyed by measure, staff, onset, written duration, beam
  `number` and value. Matching is as for fingering, except that an unmatched
  beam is neither an element nor an extra element when a counted musicdiff
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
  start or stop event. Slurs are matched one to one: slurs that would be
  identical are matched first, so the order of the documents does not change
  the tally, and the remaining slurs are matched preferring equal stop onsets,
  staffs, written pitches and durations. The element is identical when the
  start events have the same grace position, the stop events also the same
  measure and onset, and the start and stop events the same staffs, written
  pitches and written durations. The check adds no extra elements.
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
  element of that category, built so that one edit gives one operation,
  except that the `voice` and `cross_staff` pairs give the two operations that
  a pairing rule pairs and the `heading` pair gives no musicdiff operation,
  since only the heading check scores it, and
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
  option's category is that category. The place of a block option is its
  key, so `left=repeat`, the only value of its key, has no pair. A valid text
  is the text of an assembled score, since each fixture is a whole score, so
  a value that only a system document admits, such as `arc@` or `<trill)>`,
  has no value pair;
- for each layout hint that the format lists (stem direction, placement,
  inverted fermata, dynamic placement), one pair that differs only in it; the
  stem-direction pair is a single-voice measure, where stem direction does not
  change the voice 1 assignment,
  one pair that differs only in a rest's staff position and one that differs
  only in the placement of a trill mark;
- pairs that differ only in generated identifiers or `divisions`, a pair whose
  voices are renumbered without a change of canonical positions (for example
  staff 2 voices 5 and 6 against 1 and 2, including a change in their numeric
  rank), a pair in which a fermata of `Y` has no `type` and that of `T(Y)`
  is upright, and identical pairs;
- a pair in which a measure is inserted before later measures and the reverse
  pair in which a measure is deleted, with the expected per-ordinal
  differences fixed by the fixture; neither pair may align content across
  different measure ordinals;
- a pair in which a two-voice measure has one note in each voice and reverses
  their stem directions, keeping each voice group's content fixed while
  exchanging canonical positions, and a second pair in which two one-note,
  same-rhythm unison voices of one staff reverse their stem assignments,
  thereby exchanging canonical positions, with note content that makes
  preserving each voice's content the unique minimum-cost pairing under the
  pinned comparison; articulation differences alone are insufficient; a pair
  in which one non-beamed event of a content-matched voice changes its stem
  while its other events and the other voice remain unchanged; and a pair in
  which two events of one content-matched voice exchange positions without
  changing that voice's position histogram; each changed voice is exactly one
  non-identical voice-order element;
- a mixed-stem beam-group pair whose events retain the position of the first
  non-grace event, plus an identical overlapping-voice pair with a rest inside
  a primary beam group;
- self-pairs in which the two whole-note voices of a staff in one measure are
  both stemmed up, or both unstemmed; the pair of the original file, with
  stems up and down, against the both-up file in each direction; and a
  both-up self-pair whose second voice is renumbered in `T(Y)`;
- a self-pair in which a stemless half-note voice overlaps a quarter-note
  voice with stems of both directions; and a self-pair in which a rest of a
  quarter-note voice lies between an up-stemmed and a down-stemmed event at
  equal distance;
- self-pairs of one staff-measure with three whole-note voices all stemmed
  up, and with a third unstemmed voice; a four-voice self-pair, C4 and E4 up
  and two A4 down; a pair in which `Y` has C4 up, E4 down and G4 down and
  `T(Y)` changes G4 to E4; and a pair in which `Y` has C4 and G4 up and two G4
  down, and `T(Y)` drops one G4 down voice;
- a pair in which `Y` has a C4 voice up and a C4 voice down and `T(Y)` has
  the original C4 and E4 voices without stems;
- the pair of pairing rule 2 with `Y`'s two voices both stemmed up, and the
  pair of pairing rule 1 with `Y`'s two staff-1 voices both stemmed up;
- the pair of pairing rule 2 with, in both files, the equidistant rest in the
  quarter-note voice whose stemmed E5 moves;
- a measure-4 staff with three overlapping voices, C5 quarters up, an A4
  half down and an added F4 whole note down (positions 1, 2 and 4), and the
  same staff with the last C5-voice quarter (E5) stemmed down, making that
  voice mixed: the mixed self-pair, and the mixed and placed files against
  each other in each direction;
- the cross-staff beam of the first two measure-4 eighths with the A4 half
  stemmed up, so that both home voices are up: its self-pair, and it
  against the beam file with the A4 stemmed down in each direction;
- voice 7 of measure 4 with an added D5 eighth and an eighth rest, whose
  beam of C5 and D5 lies on staff 2 and so belongs to staff 2, beside staff
  2's voice 5 stemmed down (placed) or up (unplaced): the unplaced
  self-pair, and the unplaced and placed files against each other in each
  direction;
- the mixed three-voice staff with the F4 unstemmed, as a self-pair and
  again with voice 7 renumbered 1;
- the three-voice measure-4 staff whose C5 voice has a rest in place of its
  second quarter and its third quarter stemmed down, so that a voice with
  stems of both directions has a rest equally near an up and a down stem, as
  a self-pair;
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
  `T(Y)` omits them, a second such pair whose words are bold, and a pair in
  which a wavy line of `Y` starts and stops on its trilled note and `T(Y)` has
  only the trill mark;
- for the words reading, a pair in which bold words become normal words, a
  second such pair whose direction also has dynamics, one in which a
  direction with normal words and no `metronome` loses its `sound` tempo, one
  in which bold tempo words are split after a space into two `words`
  elements, one in which they are split inside a word, one in which `T(Y)`
  drops the words of a direction with a `metronome`, and one for each staff
  in which `T(Y)` drops words that music21 reads as a repeat instruction;
- for the octave-shift reading, a pair in which an 8va over the last two
  quarter notes of measure 1 stops after its last note in `Y` and at the
  start of measure 2 in `T(Y)`, one in which `T(Y)` instead stops before the
  last note with an `offset` to the barline, one in which `T(Y)` stops at the
  start of measure 2 without a `number`, one in which an 8vb of staff 2
  starts before the first note of staff 1 in `T(Y)` and before its chord in
  `Y`, one in which a whole note that starts an 8va with a quarter note of
  another voice ends it, and one in which a hidden rest of another voice
  ends an 8va with a half note, each stopping after its notes in `Y` and at
  the start of measure 2 in `T(Y)`, and the forward conversions of
  `assembled-score.txt` and of the study score, each compared with itself;
  and, against the measure-2 stop, a pair in which `T(Y)` ends the 8va
  before the last note at the same written pitch, one in which that note
  keeps its sounding pitch, and one in which `T(Y)` drops the 8va
  and keeps the sounding pitches;
- a pair in which two heading lines of one page swap their reading order, and
  a pair in which the only change is that a page 2 heading line is moved to
  page 1 after the page 1 lines;
- stop controls: a double-stem event; a chord whose notes carry different
  stems; a staff-measure with five voices; the mixed three-voice staff,
  voice 7 renumbered 1, with a third voice of two eighths F4 up and G4 down
  whose primary beam begins and continues but does not stop in the measure;
  a primary beam begun on the first measure-4 quarter and not carried by the
  next; and an 8va over a whole note that alone starts and ends it while a
  later quarter note of another voice lies inside it.

The instrument passes when it reports:

- exactly one difference, in the target category, for each category pair;
- at least one counted difference or non-identical or extra element of a
  supplementary check, and none outside the option's category, for each
  value pair;
- exactly one `layout` difference, exactly one non-identical or extra layout
  element and no content difference for each layout pair;
- exactly two non-identical `voice` elements and no other content difference
  for each canonical-position-swap pair, since both voices change position;
  each one-note stem-swap pair also has exactly two layout differences, one for
  each changed stem direction;
- exactly one non-identical and one extra `ornament` element for the extended
  trill pair and for the extended-trill move pair, and exactly one
  non-identical and one extra `text` element for
  the dashes pair and for the dashes-text pair, each with no other difference;
- exactly one counted `cross_staff` difference and no other difference for
  the pair of pairing rule 1, the pair with identical adjacent measures, the
  marked pair and the voice that moves to the other staff;
- exactly one non-identical `voice` element and no other difference for the
  pair of pairing rule 2;
- for the voice-position pairs, where a triple gives the voice-order or file
  elements, identical elements and extra elements, and the base file has 19
  voice-order and 79 file elements:
    - for each both-up and both-unstemmed self-pair, voice order (17, 17, 0),
      file (77, 77, 0), no other difference, and two excluded reference
      voices, `indistinguishable-voices` or `no-stemmed-event`; for the
      renumbered both-up pair, the same;
    - for the original `Y` against the both-up `T(Y)`, voice order (19, 17, 0)
      and no other difference; for the both-up `Y` against the original
      `T(Y)`, voice order (17, 17, 0), no difference and two excluded
      reference voices;
    - for the stemless voice beside a mixed-stem voice, voice order
      (18, 18, 0), file (78, 78, 0) and one `no-stemmed-event` exclusion; for
      the equidistant rest, voice order (18, 18, 0), file (78, 78, 0) and one
      `nearest-stem-tie` exclusion;
    - for the three-voice self-pairs (one added note and voice, three voices
      excluded), voice order (17, 17, 0), file (78, 78, 0) and three
      `stem-counts` or `no-stemmed-event` exclusions; for the four-voice
      self-pair (two added notes and voices, two excluded), voice order
      (19, 19, 0), file (81, 81, 0) and two `tied-heights` exclusions;
    - for the G4-to-E4 pair, one `pitch` difference and, in that
      staff-measure, two voice-order elements, one identical (C4) and one not
      (E4 against an E4 of tied height), with no exclusion; for the dropped G4
      voice, one `pitch` and one `rhythm_rest` symbol, voice order
      (19, 19, 0) and one `tied-heights` exclusion, since the placed G4 is
      matched first;
    - for the C4/C4 against unstemmed C4/E4 pair, one `pitch` difference,
      voice order (18, 17, 0) and file (78, 76, 0), with no exclusion;
    - for the rule-2 pair with an undetermined `Y` context, no `voice`
      difference: the deletion and insertion count two `pitch` and three
      `rhythm_rest` symbols and one extra element (deletion: pitch, rhythm and
      the voice-2 gap before it, 1 + 2; insertion: pitch and rhythm, 1 + 1;
      the inserted note, present only in `T(Y)`, is the extra); for the rule-1
      pair with an undetermined `Y` context, one `cross_staff` difference, one
      non-identical voice-order element (the staff-2 voice that gains a
      partner) and one `indistinguishable-voices` exclusion;
    - for the rule-2 pair with the equidistant rest in the moving voice, one
      `voice` difference and no other, voice order (17, 17, 0), file
      (77, 76, 0) and no exclusion, as for the placed rule-2 pair: the rest
      leaves its voice undetermined but not the E5, whose stems place it in
      both files, and the voices that the move changes have no content
      partner;
    - for the three-voice measure-4 staff (placed: 20 voice-order and 81 file
      elements, one more of each than the base for the F4), with no content
      difference in any of its pairs: for the mixed self-pair and for the
      mixed `Y` against the placed `T(Y)`, voice order (17, 17, 0), file
      (78, 78, 0) and three `mixed-stem-voice` exclusions, since the mixed
      voice leaves all three voices undetermined and each still finds a
      content partner; for the placed `Y` against the mixed `T(Y)`, voice
      order (20, 17, 0), file (81, 78, 0) and no exclusion, since each placed
      voice meets an undetermined partner;
    - for the cross-staff beam pairs (80 file elements, the base's 79 and the
      `cross_staff` beam element, and 19 voice-order elements), with no
      content difference: for the self-pair of the both-up file and for it as
      `Y` against the placed `T(Y)`, voice order (17, 17, 0), file
      (78, 78, 0) and two `indistinguishable-voices` exclusions, the beam's
      staff-2 note taking its undetermined home anchor; for the placed `Y`
      against the both-up `T(Y)`, voice order (19, 17, 0) and file
      (80, 78, 0) with no exclusion;
    - for the staff-2 beam pairs (83 file elements, the base's 79, the D5 and
      the rest, and two `cross_staff` beam events; 19 voice-order elements),
      with no content difference: on staff 2 the up-stemmed beam and voice 5
      stemmed up are indistinguishable, so voice 5 and, through its beam
      events, voice 7 are undetermined although voice 7's staff-1 events are
      placed. For the unplaced self-pair and the unplaced `Y` against the
      placed `T(Y)`, voice order (17, 17, 0), file (81, 81, 0) and two
      `indistinguishable-voices` exclusions; for the placed `Y` against the
      unplaced `T(Y)`, voice order (19, 17, 0) and file (83, 81, 0) with no
      exclusion. (A known position given to the beam events would place
      voice 7 and leave one exclusion.)
    - for the unstemmed-F4 mixed staff and its renumbered copy, no difference
      and three `no-stemmed-event` exclusions each, the first in precedence of
      the staff-measure's `mixed-stem-voice` and `no-stemmed-event`;
    - for the three-voice staff whose mixed voice has the equidistant rest,
      voice order (17, 17, 0), file (78, 78, 0), no difference and three
      `nearest-stem-tie` exclusions, the first in precedence of the
      staff-measure's `nearest-stem-tie` and `mixed-stem-voice`;
- the insertion and deletion measure pairs have exactly their fixture's
  predeclared per-ordinal category assignments and non-identical/extra
  element counts; neither fixture pairs content across measure ordinals;
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
- exactly one `text` and one `tempo_rehearsal` difference for each pair in
  which bold words become normal words and for the `sound`-tempo pair,
  exactly one `tempo_rehearsal` difference for the metronome-words pair and
  exactly one `text` difference for each repeat-words pair, each with no
  other difference;
- exactly one `ottava` difference for the shorter-ottava pair at the same
  written pitch, exactly one `ottava` and one `pitch` difference for the
  shorter-ottava pair at the same sounding pitch, and exactly one `ottava`
  and two `pitch` differences for the dropped-ottava pair, each with no other
  difference;
- a `ProtocolDefectError` stop for each stop control;
- no difference for the remaining no-change pairs, including the reprint
  pairs, the split-words pairs, the octave-shift encoding pairs and
  self-comparisons, and the untyped-fermata pair; the octave-shift and
  untyped-fermata pairs also have no difference in the layout pass.

The screen's allowlist is reviewed against the same fixtures before the
samples are selected in Execution step 2. A failure of the instrument or of
that review is a protocol defect: this record is revised under review before
any corpus or gold run.

As a preflight in Execution step 1, one MuseScore conversion of a calibration
fixture is run and the settings-file hash is checked before any corpus
export. The allowlist review and this preflight belong to the screen and to
the preflight; they are not part of the instrument's pass.

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

The layout pass uses the same measure-ordinal pairs and global-only comparison
as the content pass; its ordinary whole-score diagnostic is not scored. It
gives the score, per arm and for gold. A _layout element_ of
`Y` is an annotated object, or an articulation, expression or tuplet group
attached to one, that carries a value other than musicdiff's default of a
property whose difference the operation table assigns to `layout`, such as a
non-empty style, a stem direction other than `unspecified`, a rest staff
position other than the default, a notehead other than `normal` or a set
notehead fill, a placement, an inverted fermata, or a tuplet's printed
number or bracket; or that is a system or page break, staff details or a
staff group. It is identical when no `layout` difference touches it. A
`layout` difference is one whose only category is `layout`, so the
difference of an inserted or deleted note, rest, chord or extra that also
has a symbol of a content category is not one. An articulation,
expression or tuplet operation touches only the attached element that it
changes, not the note that carries it, and another operation on a note does
not touch the elements attached to it. The counterpart of an object of
`T(Y)` that a difference holds is the object of `Y` that the difference
pairs with it, including a pair that the pairing rules form; in the layout
pass, rule 1 pairs two notes even when the cross-staff check has an element
for either. An object of `T(Y)` that no difference holds has the equal
object of `Y` as its counterpart. The counterpart of an attached element is
the element of `Y` that musicdiff aligns with it. An
object or attached element of `T(Y)` that carries such a property without a
counterpart that carries one is an extra layout element, so the layout of
content that `T(Y)` inserts is extra. The score is the number of identical
layout elements divided by the number of layout elements plus extra layout
elements, pooled per arm.
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
`Style`, which exclude coordinates and font family. A layout element of a
note, rest, chord or extra of a content category that `T(Y)` deletes is
touched by no `layout` difference and counts as identical, so layout lost
with deleted content, which the content pass counts, does not lower the
layout score, while the layout of inserted content counts as extra. The
same holds for an articulation, expression or tuplet group of a note that
`T(Y)` keeps: when `T(Y)` drops it or changes it by a content difference,
its placement, fermata shape or printed number or bracket is touched by no
`layout` difference and counts as identical, while one that `T(Y)` adds
counts as extra. A
deleted system or page break, staff details or staff group is a `layout`
difference and counts as non-identical. The heading check compares only the
reading order of heading lines, not which system or page a heading precedes;
a move between systems is not counted. It counts a credit that repeats an
earlier page's line as a heading line, though the format does not encode
running headers, so a conversion that omits one is counted. musicdiff 6.0
compares an arpeggio across events by its type, member count and the onset of
its highest member, not by its members, so a regrouping that keeps these is
not counted.
musicdiff 6.0 also drops a clef that restates an earlier clef of its staff in
the same measure; the screen excludes such clefs in `X`, but one that `T(Y)`
adds is not counted.
The words reading takes its marks of tempo words from the converters' MusicXML
mapping, so it measures agreement with that mapping, not with the format's
rule for tempo words. That MusicXML has no standard mark of tempo words is an
inference. Words of `Y` without these marks are text in both scores, so a
conversion that keeps such tempo words as text is not counted; marked words
that the format assigns to text, such as a bold "Fine" or "rit." with a
`sound` tempo, are counted as `tempo_rehearsal`.
music21 forms a pedal mark only from a `pedal` start that a later `stop`
completes, and musicdiff 6.0 compares pedals only as pedal marks, so a pedal
sign without such a partner is not compared, and its loss is not counted; the
text-space check reports the loss only when the reverse conversion keeps the
sign.
music21 10.5.2 imports each staff of a multi-staff part as a separate part
and puts some objects on every one of them: a barline, a repeat bracket, a
pedal change, gap start or gap end, and a key or time signature without a
staff `number`. Each such mark is one element per staff, so it counts once
per staff in the fidelity figure and the per-category counts, while a pedal
mark counts once.
The fingering, parenthesis, tie-end, slur-endpoint, tremolo-stroke, beam-hook and
extended-trill checks identify a note, or for tremolo strokes and beam hooks an
event, by its written pitch or pitches and written duration, not by its voice,
so a mark or endpoint
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
  musicdiff represents a pickup measure;
- the `PDMX.csv` header, including the prefilter columns and the column that
  holds the compressed MusicXML path, the layout of
  `mxl.tar.gz`, and the Lieder file names at the pinned commit;
- whether MuseScore 4.7.6 exports `instrument-sound` for Lieder piano parts;
- whether a console conversion writes the settings file, which the preflight
  checks;
- the size of the Lieder download and the sizes of the eligible PDMX and
  Lieder sets;
- whether credits without a `credit-type` count as heading credits; this is
  settled on the calibration set and the selected files, by a reviewed
  revision of this rule, before results depend on it.

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
  `annotated_scores_diff` compares unmatched parts and staff groups after its
  matched-part measure loop; see
  [comparison.py lines 1485 to 1543](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L1485-L1543).
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
  These lines are the same in 10.5.2.
- music21 10.5.2 is the version that the lockfile of the harness change
  resolves when it adds musicdiff 6.0; the target branch's lockfile has no
  music21. That version imports `words` that
  name a repeat instruction as a `RepeatExpression` that holds a copy of their
  text expression, and inserts only complete spanners. A pedal mark is
  complete only at its `stop`, and a `stop` without an open pedal mark is
  skipped with a warning. Its `Fermata` has the type `inverted` unless the
  MusicXML gives one. It trims each `words` element separately. It imports
  each staff of a multi-staff part as a separate part with the objects that
  the evidence limits list, and its pedal change, gap start and gap end are
  not expressions.
  See `music21/musicxml/xmlToM21.py` lines 5631 to 5647, 1547 to 1552, 4556
  to 4557, 4516 to 4523, 5591 to 5598, 3880 to 3890, 5698, 1849 to 1863,
  1893 to 1903, 2576 to 2595 and 4532 to 4555, and `music21/expressions.py`
  lines 536 to 555, 2695 to 2700 and 380 to 415, in the
  [PyPI wheel](https://pypi.org/project/music21/10.5.2/#files)
  `music21-10.5.2-py3-none-any.whl`, SHA-256
  `dba8696eafab709a244b6f18c64399edcccf4e96ddd1e0e4de7eff0a291c2dec`.
  If a run records another music21 version, these music21 findings are
  rechecked before any corpus or gold run.
  musicdiff 6.0 has no `kind` for a `RepeatExpression` in the `extra_to_kind`
  lines cited above. It compares a metronome mark that has text and no
  number by its text, except text of the form note = number; see
  [m21utils.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L1476-L1493)
  and
  [lines 1535 to 1543](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L1535-L1543).
  It pairs extras only of the same kind, and reports the others as
  `extradel` and `extrains`; see
  [comparison.py](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L1106-L1113)
  and
  [lines 1174 to 1182](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/comparison.py#L1174-L1182).
  It records a fermata only by its expression name
  ([m21utils.py lines 217 to 219](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L217-L219)),
  and with `Style` adds a tuplet group's placement to its printed number and
  bracket
  ([lines 620 to 641](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L620-L641)).
- MusicXML 4.0 defines a `fermata` without a `type` as upright; see the
  [`fermata` element](https://www.w3.org/2021/06/musicxml40/musicxml-reference/elements/fermata/).
- music21 10.5.2 reads an `octave-shift` start as an `Ottava` whose first
  element is the next note, chord or rest that it parses, of any staff and at
  any offset, and a stop by adding the last note, chord or rest that its
  measure parser has read, if any, without reading the stop's `offset`. A
  stop whose `number` matches no open start raises an import error, which is
  caught with a warning, so that start is never completed or inserted. See
  `music21/musicxml/xmlToM21.py` lines 4449 to 4486, 5591 to 5598, 2454, 2894
  and 2916, and `music21/spanner.py` lines 1505 to 1530, in the 10.5.2 wheel
  cited above. musicdiff 6.0 transposes every note to written pitch, undoing
  ottavas, before annotation
  ([annotation.py lines 1765 to 1769](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/annotation.py#L1765-L1769)),
  does not annotate a hidden note or rest
  ([m21utils.py lines 706 to 708](https://github.com/gregchapman-dev/musicdiff/blob/0368856e8561ba655d990c00c7ae46a2408f071c/musicdiff/m21utils.py#L706-L708)),
  and gives an ottava the extent of its primary and last elements, as cited
  below for spanners.
- The converters read a direction with a `metronome` as a tempo whatever
  its weight and one with `dashes` as a dashes line, and otherwise mark
  tempo words in MusicXML as bold `words` or with a
  `sound` tempo, at commit `36323f54`; see
  [musicxml_reverse.py](https://github.com/hcoona/three/blob/36323f54c3f326e798725e11455a124a510b915a/src/private/app/sheet-music-omr/src/sheet_music_omr/musicxml_reverse.py#L1584-L1598)
  and
  [musicxml_forward.py](https://github.com/hcoona/three/blob/36323f54c3f326e798725e11455a124a510b915a/src/private/app/sheet-music-omr/src/sheet_music_omr/musicxml_forward.py#L1129-L1130).
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
