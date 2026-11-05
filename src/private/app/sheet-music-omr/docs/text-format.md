# Text Score 2 Format

This record is the normative specification of Text Score 2, a line-oriented
text notation for the printed content of piano music. A recognizer reads one
printed grand-staff system into a _system document_. Deterministic tools
assemble system documents into an _assembled score_, the form that converters
exchange with other notation formats.

This record specifies a format only. It does not claim that a parser,
validator, converter, recognizer or model exists. Requirements, acceptance
measures and the validation basis are in [requirements.md](requirements.md).
The project purpose and record routing are in [README.md](../README.md).

## Conformance and terms

The words _shall_, _shall not_ and _may_ are normative. Every rule is a design
decision unless the Source basis section labels it as a source finding or an
inference. The Transcription rules section chooses which valid document
transcribes a printed system and adds no rule of validity.

A document is _valid_ when it satisfies the grammar after preprocessing and
every semantic rule in this record. A conforming tool:

- accepts every valid document;
- rejects every invalid document with an explicit error that names the rule
  class (lexical, grammar, semantic, assembly or unsupported) and the location
  in the original text;
- never repairs, guesses or silently drops content;
- writes only canonical form.

The number in `format: text-score/2` is the major version. A change that alters
the meaning or validity of a previously valid document needs a new major
version, and a reader shall reject a version it does not support. The earlier,
unversioned convention of the existing private workflow has no record here, and
its documents are not valid `text-score/2` documents.

Terms used throughout:

- _As-printed_: the text records what the page shows (clef, key signature,
  accidentals, ottava lines, marks) rather than a performance reading. Derived
  readings, such as sounding pitch, are defined by rule from the recorded text.
- _Content_: a printed distinction that changes the notated music. _Layout_: a
  printed distinction that changes only the engraved appearance. The format
  encodes content, plus a few layout hints that never count as content.
- _Voice line_: the line for staff `S` (1 is the upper staff, 2 the lower) and
  voice `V` (1 to 4) within a measure block, written `S.V`.
- _Event_: a direction, note, chord, rest, spacer or whole-measure rest in a
  voice line. A tuplet is a container, not an event.
- _Onset_: the position of an event in its measure, as a reduced rational
  number of whole notes from the measure start.
- _Fragment_: a measure block that covers only part of its measure.

## Documents

A _system document_ transcribes one printed grand-staff system. Its lines come
in this fixed order:

- `format: text-score/2`
- `key: K`, the key signature as a count of sharps (positive) or flats
  (negative), from `-7` to `7`
- `time: N/D`, the meter
- `clefs: X Y`, the clefs of staff 1 and staff 2
- `carry: octave` or `carry: letter`, the accidental carry mode
- `page: P` and `system: S`, positive integers that order the system within the
  piece
- `carry-in: {...}`, the carry state at the start of the system
- one or more measure blocks

The header lines are required, and there are no defaults. `key`, `time` and
`clefs` give the state in effect at the carry-in position, and measure blocks
are transitions from that state. `carry` fixes how an accidental stays in force
and is constant across a piece. This record does not say which party supplies
each header line to a recognizer.

An _assembled score_ transcribes a whole piece. Its lines are `format`, an
optional `title: "..."`, an optional `composer: "..."`, `key`, `time`, `clefs`
and `carry`, followed by measure blocks. The header gives the state at the
start of measure 1, there is no `page`, `system` or `carry-in` line, and every
block is a complete measure. Assembly produces an assembled score from system
documents, and a converter may also produce one directly. Title and composer
are optional metadata and are not part of any system document.

### Examples

The examples are written for this record and transcribe no existing score. The
first system of a short piece ends in the middle of measure 2:

```text
format: text-score/2
key: 0
time: 4/4
clefs: G2 F4
carry: octave
page: 1
system: 1
carry-in: {"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave","accidentals":[],"open":[]}
m 1 time-symbol=common
1.1: C5/8[beam1=start] D5/8[beam1=continue] E5/8[beam1=continue] F5/8[beam1=end] G5/2[rel=slur@r1:start]
2.1: C3/2 G3/2
m 2 to=1/2
1.1: A5/4 r/4
2.1: C3/2
```

- Measure 1 beams four eighth notes at level 1 and opens slur `r1` on its fifth
  event, the half note `G5/2`. `time-symbol=common` records the printed
  common-time symbol.
- Measure 2 stops at `to=1/2`, so the system ends inside the measure. Each of
  its voice lines sums to `1/2`.

The second system starts where the first stopped. Its `carry-in` equals the
carry-out derived from the first system, with the open slur, and its first
block continues measure 2:

```text
format: text-score/2
key: 0
time: 4/4
clefs: G2 F4
carry: octave
page: 1
system: 2
carry-in: {"measure":2,"offset":"1/2","event_counts":{"1.1":2,"2.1":1},"accidental_scope":"octave","accidentals":[],"open":[{"id":"r1","kind":"slur","start":{"measure":1,"offset":"1/2","staff":1,"voice":1,"event_index":5,"member_index":null}}]}
m 2 right=final from=1/2
1.1: <mf> 3:2{G5/8 F5/8 E5/8} D5/4[rel=slur@r1:end]
2.1: G3/2
```

- The block id equals `carry-in.measure`, and `from` equals `carry-in.offset`.
- Event indices continue from `event_counts`, so `<mf>` is event 3 of voice
  line `1.1`. The slur ends on event 7.
- The triplet `3:2{...}` holds three eighth notes in the time of two, which is
  `1/4`. The direction adds no time. The voice line sums to `1/2`.

Assembly joins the two fragments of measure 2:

```text
format: text-score/2
title: "Example"
key: 0
time: 4/4
clefs: G2 F4
carry: octave
m 1 time-symbol=common
1.1: C5/8[beam1=start] D5/8[beam1=continue] E5/8[beam1=continue] F5/8[beam1=end] G5/2[rel=slur@r1:start]
2.1: C3/2 G3/2
m 2 right=final
1.1: A5/4 r/4 <mf> 3:2{G5/8 F5/8 E5/8} D5/4[rel=slur@r1:end]
2.1: C3/2 G3/2
```

## Lexical rules and preprocessing

A document is UTF-8 text without a byte order mark, with LF line ends only, no
tab characters and a final LF. Lines start at column 0.

Preprocessing runs before the grammar. For each line it removes a `//` comment
that lies outside a quoted string, together with the spaces before it, and then
removes trailing spaces. It then drops empty lines. Inside a quoted string a
backslash escapes the next character, so `"a\"//b"` contains no comment. The
grammar and every rule below apply to the normalized text.

Quoted text uses JSON string syntax. The escapes are `\"`, `\\`, `\/`, `\b`,
`\f`, `\n`, `\r`, `\t` and `\uXXXX`, and raw control characters (U+0000 to
U+001F) are not allowed. Text values are compared as decoded code point
sequences, and no Unicode normalization applies.

Other lexical rules:

- Spaces occur only where the grammar writes `sp` (one or more spaces) and
  inside quoted text.
- A rational number is non-negative and reduced: `0`, an integer written
  without a denominator, or `n/d` with `d` of at least 2 and no common factor
  of `n` and `d`. `4/2` and `2/1` are invalid.
- A meter is `N/D` with `D` a power of two from 1 to 128. It is not reduced, so
  `2/2` and `4/4` are different meters. The length of a meter is the reduced
  rational `N/D`, in whole notes.
- Decoded text shall contain only characters that XML 1.0 allows (U+0009,
  U+000A, U+000D, U+0020 to U+D7FF, U+E000 to U+FFFD and U+10000 to U+10FFFF),
  so that every valid assembled score converts to MusicXML. An escape of a high
  surrogate shall be immediately followed by an escape of a low surrogate, and
  the pair decodes to one code point. Any other surrogate, `\b`, `\f`, a
  control character other than tab, LF and CR, and U+FFFE and U+FFFF are
  lexical errors.

## Grammar

The grammar is scannerless: it describes characters, and every space is
explicit. Its notation is:

- `name = expression` defines a rule. Continuation lines are indented.
- `,` concatenates and binds tighter than `|`, which separates alternatives.
- `( )` groups, `[ ]` marks an optional item, and a postfix `*` or `+` repeats
  the preceding item zero or more, or one or more, times.
- `"text"` and `'text'` are literals. Single quotes are used when the text
  contains `"`. A literal has no escapes, so `"\"` is one backslash.
- `"a".."z"` is an inclusive character range.
- `? description ?` is a special sequence that its description defines.
- A line that starts with `(*` is a comment.

```ebnf
(* Documents. Each line is terminated by lf. *)
system_document = format_line, key_line, time_line, clefs_line, carry_line,
    page_line, system_line, carry_in_line, measure_block+
assembled_score = format_line, [title_line], [composer_line], key_line,
    time_line, clefs_line, carry_line, measure_block+

(* Header lines *)
format_line = "format: text-score/2", lf
title_line = "title: ", nonempty_quoted, lf
composer_line = "composer: ", nonempty_quoted, lf
key_line = "key: ", fifths, lf
time_line = "time: ", meter, lf
clefs_line = "clefs: ", clef, sp, clef, lf
carry_line = "carry: ", carry_mode, lf
page_line = "page: ", pos_int, lf
system_line = "system: ", pos_int, lf
carry_in_line = "carry-in: ", carry_state, lf
carry_mode = "octave" | "letter"

(* Measure blocks and voice lines *)
measure_block = "m ", pos_int, (sp, measure_option)*, lf, voice_line+
measure_option = "len=", rational
    | "key=", fifths
    | "time=", meter
    | "time-symbol=", ("numeric" | "common" | "cut")
    | "left=repeat"
    | "right=", ("final" | "double" | "repeat")
    | "from=", rational
    | "to=", rational
    | "ending=", ending
ending = pos_int, ":", ("start" | "single") | "continue" | "end"
voice_line = staff_no, ".", voice_no, ":", sp, event, (sp, event)*, lf

(* Events *)
event = direction | tuplet | note | rest | mrest
tuplet = pos_int, ":", pos_int, "{", tuplet_item, (sp, tuplet_item)*, "}"
tuplet_item = direction | tuplet | note | rest
note = pitch, ("+", pitch)*, "/", duration, [note_flags]
rest = "r/", duration, [rest_flags]
    | "x/", duration, [beam_flags]
mrest = "mrest", ["[", fermata, "]"]
duration = ("1" | "2" | "4" | "8" | "16" | "32" | "64" | "128"), "."*

(* Pitches *)
pitch = (pitch_name | "(", pitch_name, ")"), [fingering], [tie_marks]
pitch_name = step, [accidental], octave
step = "A".."G"
accidental = "##" | "bb" | "#" | "b" | "n"
    | "(##)" | "(bb)" | "(#)" | "(b)" | "(n)"
octave = "0".."9"
fingering = "f", "1".."5", ["1".."5"]
tie_marks = "{", (tie_start | tie_end | tie_end, ",", tie_start
    | "tie-in" | "tie-in", ",", tie_start), "}"
tie_start = "tie@", label, ":start"
tie_end = "tie@", label, ":end"
label = "r", pos_int

(* Event flags *)
note_flags = "[", note_flag, (",", note_flag)*, "]"
note_flag = articulation | fermata | beam | event_relation
    | "arp" | "up" | "down" | "grace" | "grace/" | "xs", staff_no
    | "orn=", ornament | "tremolo=single:", stroke
rest_flags = "[", rest_flag, (",", rest_flag)*, "]"
rest_flag = fermata | beam
beam_flags = "[", beam, (",", beam)*, "]"
articulation = ("stacc" | "acc" | "ten" | "stacciss" | "marc" | "port"),
    [placement]
placement = "^" | "_"
fermata = "ferm" | "ferm_"
beam = "beam", "1".."5", "=", beam_state
beam_state = "start" | "continue" | "end" | "hook-forward" | "hook-backward"
ornament = "trill" | "mordent" | "inverted-mordent" | "turn"
    | "inverted-turn"
stroke = "1".."8"
event_relation = "rel=slur@", label, ":", ("start" | "end")
    | "rel=tremolo@", label, ":", ("two-start" | "two-end"), ":", stroke
    | "rel=arpeggio@", label, ":member"
    | "rel=ornament@", label, ":", ("start" | "end"), ":extended-trill"

(* Directions *)
direction = "<", plain_body, ">"
    | "<cresc(>[rel=hairpin@", label, ":start:cresc]"
    | "<dim(>[rel=hairpin@", label, ":start:dim]"
    | "<hairpin)>[rel=hairpin@", label, ":end]"
    | "<", ottava_sign, "(>[rel=ottava@", label, ":start:", ottava_sign, "]"
    | "<", ottava_sign, ")>[rel=ottava@", label, ":end:", ottava_sign, "]"
    | "<ped(>[rel=pedal@", label, ":line-start]"
    | "<ped(>[rel=pedal@", label, ":line-change]"
    | "<ped)>[rel=pedal@", label, ":line-release]"
    | "<", nonempty_quoted, "(>[rel=dashes@", label, ":start]"
    | "<dashes)>[rel=dashes@", label, ":end]"
ottava_sign = "8va" | "8vb" | "15ma" | "15mb"
plain_body = ["^"], dynamic
    | "clef ", clef
    | nonempty_quoted
    | "tempo ", tempo_args
    | "rehearsal ", nonempty_quoted
    | "pedal-sign ", ("down" | "up")
tempo_args = quoted, sp, metronome | nonempty_quoted
metronome = ("q." | "h." | "q" | "h" | "e"), "=", pos_int
dynamic = "pppp" | "ppp" | "pp" | "p" | "mp" | "mf" | "f" | "ff" | "fff"
    | "ffff" | "sf" | "sfz" | "sffz" | "fp" | "rf" | "rfz" | "fz" | "sfp"
    | "sfpp" | "pf"

(* Carry state: one canonical JSON object on one line *)
carry_state = '{"measure":', pos_int, ',"offset":"', rational, '"',
    ',"event_counts":{', [count_entry, (",", count_entry)*], "}",
    ',"accidental_scope":"', carry_mode, '"',
    ',"accidentals":[', [acc_entry, (",", acc_entry)*], "]",
    ',"open":[', [open_entry, (",", open_entry)*], "]}"
count_entry = '"', staff_no, ".", voice_no, '":', pos_int
acc_entry = '{"staff":', staff_no, ',"step":"', step, '"',
    [',"octave":', octave], ',"alter":', alter, "}"
alter = "-2" | "-1" | "0" | "1" | "2"
open_entry = '{"id":"', label, '","kind":"slur","start":', endpoint, "}"
    | '{"id":"', label, '","kind":"tie","start":', endpoint,
        ',"pitch":', sounding, "}"
    | '{"id":"', label, '","kind":"tremolo","start":', endpoint,
        ',"variant":"', stroke, '","written":"', rational,
        '","scale":"', rational, '"}'
    | '{"id":"', label, '","kind":"ornament","start":', endpoint,
        ',"variant":"extended-trill"}'
    | '{"id":"', label, '","kind":"hairpin","start":', endpoint,
        ',"variant":"', ("cresc" | "dim"), '"}'
    | '{"id":"', label, '","kind":"ottava","start":', endpoint,
        ',"variant":"', ottava_sign, '"}'
    | '{"id":"', label, '","kind":"pedal","start":', endpoint, "}"
    | '{"id":"', label, '","kind":"dashes","start":', endpoint, "}"
endpoint = '{"measure":', pos_int, ',"offset":"', rational,
    '","staff":', staff_no, ',"voice":', voice_no, ',"event_index":', pos_int,
    ',"member_index":', (pos_int | "null"), "}"
sounding = '{"step":"', step, '","octave":', octave,
    ',"alter":', (alter | '"tie-in"'), "}"

(* Shared lexical rules *)
clef = ("G" | "F" | "C"), "1".."5", [("+" | "-"), "8"]
fifths = "0" | ["-"], "1".."7"
meter = pos_int, "/", ("1" | "2" | "4" | "8" | "16" | "32" | "64" | "128")
rational = "0" | pos_int | pos_int, "/", pos_int
staff_no = "1" | "2"
voice_no = "1".."4"
pos_int = "1".."9", ("0".."9")*
sp = " "+
lf = ? line feed U+000A ?
quoted = '"', text_char*, '"'
nonempty_quoted = '"', text_char+, '"'
text_char = ? any character except '"', '\' and U+0000 to U+001F ?
    | "\", ('"' | "\" | "/" | "b" | "f" | "n" | "r" | "t")
    | "\u", hex, hex, hex, hex
hex = "0".."9" | "a".."f" | "A".."F"
```

The grammar admits strings that the semantic rules reject, such as the rational
number `4/2`. A document is valid only when it also satisfies every rule below.

## Semantics

### Headers and state

- A system document's `key`, `time` and `clefs` lines give the state at the
  start of its first block: measure `carry-in.measure`, offset
  `carry-in.offset`. An assembled score's lines give the state at the start of
  measure 1.
- `key=K` on a block sets the key signature from that measure on, `time=N/D`
  sets the meter from that measure on, and a `clef` direction sets the clef of
  its line's staff from its onset on. The state after a document is its header
  state with these changes applied.
- A positive key `K` sharpens the first `K` steps of F, C, G, D, A, E, B. A
  negative key `-K` flattens the first `K` steps of B, E, A, D, G, C, F.
- `carry` and `carry-in.accidental_scope` shall be equal.
- A `key` or `time` option that restates the value already in effect is a
  reprint. A reprint is valid input and carries no content, and canonical form
  omits it.
- The header lines alone give the initial state of a piece. In the first block
  of an assembled score, and of a system document whose `carry-in` has
  `measure` 1 and `offset` `"0"`, a `key` or `time` option shall be a reprint
  and no `clef` direction shall have onset `0`.

### Measure blocks and options

A measure block is a line `m N` with options, followed by its voice lines.
Measure ids are positive integers and are positional: in a system document the
first block has the id `carry-in.measure` and each later block has the previous
id plus 1, and in an assembled score the first id is 1. A pickup measure is
written `m 1 len=...`. Printed measure numbers are not encoded.

The _effective length_ of a measure is its `len` option or, when absent, the
length of the meter in effect. A block covers the range from `from` (default
`0`) to `to` (default the effective length), and `0 <= from < to <= effective
length`. A block shall carry each of these options at most once:

- `len=R`: the measure length in whole notes.
- `key=K`: a new key signature, spelled as in the `key` line.
- `time=N/D`: a new meter.
- `time-symbol=numeric|common|cut`: the printed form of the time signature at
  the start of the measure, `numeric` by default. `common` requires an
  effective meter of `4/4` and `cut` requires `2/2`. It annotates one measure
  and is not state.
- `left=repeat`: a start-repeat barline.
- `right=final|double|repeat`: the closing barline. Absent means an ordinary
  barline.
- `from=R`, `to=R`: the covered range.
- `ending=...`: a volta bracket, as described below.

Fragments:

- Only the first and the last block of a system document may be fragments, and
  every other block covers a full measure. The first block's `from` equals
  `carry-in.offset`.
- A block with `from=0` owns `key`, `time`, `time-symbol`, `left` and `ending`.
  A block whose `to` equals the effective length owns `right`. A block shall not
  carry an option that it does not own.
- `len` is repeated identically on both fragments of a split measure.
- A voice line of a block whose `to` is less than the effective length shall
  not hold a grace note after its last non-grace rhythmic event.

Volta brackets are matched by adjacency of measures, and a continuation
fragment (`from>0`) is part of the measure that it continues. `ending=N:start`
opens a bracket with printed number `N`, and `ending=N:single` is a
one-measure bracket. The next measure after a `start` or a `continue` shall
carry `ending=continue` or `ending=end`, and no other measure shall carry
`continue` or `end`. A validator checks each pair of adjacent measures whose
`ending` options are both in its document. Assembly checks every pair of the
score, including a pair across a system boundary. A multi-number bracket is
unsupported, and the closing hook of a bracket is not encoded.

### Voice lines and durations

- A block has at least one voice line and at most one for each `S.V`, in any
  order. Canonical form sorts the lines by staff, then voice. A missing voice
  line is never a rest. It means that nothing is printed for that voice.
- A note or rest lasts its written value (`N` is `1/N` of a whole note) times
  `2 - 1/2^d` for `d` dots. A tuplet `N:M{...}` multiplies each member by `M/N`
  (`N` notes in the time of `M`), and the ratios of nested tuplet containers
  multiply.
- Directions and grace notes last nothing.
- The two endpoints of a two-note tremolo each last half of their written value
  times the tuplet multiplier.
- The durations in a voice line shall sum to `to - from` of its block.
- A tuplet shall contain at least one rhythmic member that is not a grace note.
- A tuplet lies within one voice line of one measure block, and a tuplet group
  across a barline or a fragment boundary is unsupported.
- `x/` is an unprinted timed spacer. It fills time without a printed rest and
  may carry beams. A voice that starts after the measure start begins with a
  spacer.
- `mrest` is a whole-measure rest. It is valid only in a block with `from=0`
  and `to` equal to the effective length, never inside a tuplet, and as the
  only rhythmic event of its line, though directions may accompany it. A
  whole-measure rest in a split measure is unsupported.

### Event flags

Flags follow a note's duration in brackets and are separated by commas. A rest
takes only fermata and beam flags, and a spacer takes only beam flags.

- Articulations are `stacc`, `acc`, `ten`, `stacciss`, `marc` and `port`
  (detached legato), each with an optional placement suffix, `^` for above or
  `_` for below.
- `ferm` is a fermata and `ferm_` an inverted fermata. `mrest` takes a fermata
  in brackets: `mrest[ferm]`.
- `arp` is a rolled chord within one event.
- `up` and `down` give the stem direction.
- `grace` is a grace note and `grace/` a slashed grace note.
- `xs1` and `xs2` place the whole event on another staff, as described under
  pitch.
- `orn=` takes `trill`, `mordent`, `inverted-mordent`, `turn` or
  `inverted-turn`.
- `tremolo=single:S` is a single-note tremolo with `S` strokes, `1` to `8`.
- `beamN=state` and `rel=...` are described under beams and relations.

An event shall not repeat a mark: at most one of each articulation kind
whatever its placement, one fermata, one of `up` and `down`, one of `grace` and
`grace/`, one `xs`, one `orn`, one `tremolo=single` and one beam state per
level. A grace flag is valid only on a note.

### Pitch and accidentals

- Octaves use scientific numbering: `C4` is middle C, and octaves run from `0`
  to `9`.
- A pitch is the _written_ pitch. It is the pitch that the staff position and
  clef give, including the octave change of a clef such as `G2-8`, and it
  ignores any ottava line. The _sounding_ pitch adds the ottava line that
  applies to its event, as described under directions: `8va` raises it one
  octave, `8vb` lowers it one, `15ma` raises it two and `15mb` lowers it two. A
  sounding octave shall also lie from `0` to `9`.
- An accidental alters its step by `#` +1, `##` +2, `b` -1, `bb` -2 and `n` 0.
  A parenthesized accidental is still a printed accidental. `x` is not a
  spelling of the double sharp.

A pitch resolves its alteration by the first of these rules that applies:

1. Its printed accidental.
2. Tie inheritance: a pitch that ends a tie and has no printed accidental takes
   the alteration of the pitch where the tie started, including across a
   barline. A `tie-in` pitch without a printed accidental takes the alteration
   of its source, as described under relations, which may defer it to
   assembly.
3. The most recent earlier printed accidental on the same step in scope. The
   scope is the same measure, the same displayed staff and any voice of that
   staff. With `carry: octave` the octave shall also be the same, and with
   `carry: letter` any octave counts. Earlier means a smaller onset, or an
   equal onset and an earlier position by voice number, then by the staff of
   the line, then position in the line, with chord members in listed order.
4. The key signature.

For example, in key `0` with `carry: octave`, `F#4` makes the later `F4` sharp
and leaves `F5` natural. With `carry: letter`, `F5` is sharp too.

```text
1.1: F#4/4 F4/4 F5/4 Fn4/4
```

Further pitch rules:

- The _displayed staff_ of an event is its line's staff, unless the event
  carries `xsN`. `N` shall differ from the line's staff, the flag moves the
  whole event, and rests and spacers cannot carry it.
- Chord members are written joined by `+` in ascending order of diatonic
  position (the octave times 7 plus the step index, with C as 0 and B as 6),
  then by accidental token in ASCII order with an absent accidental first. A
  chord shall not repeat a step, accidental and octave. Mixed-staff chords are
  unsupported. `member_index` counts members from 1 in listed order.
- A pitch has at most one fingering, `f1` to `f5`. A pitch of an event that
  carries `orn=trill` may instead carry two different digits, such as `f13`,
  the two fingers of the trill.
- A pitch written in parentheses, as in `F3+(A3)/2`, is a parenthesized
  notehead. The parentheses are content and apply to the whole pitch, so its
  accidental shall not be parenthesized again. They do not change how the pitch
  resolves, and chord order ignores them.

### Beams

- A beam run lies within one measure block. Directions between events are
  ignored, grace notes and other notes cannot mix in a run, and a run across a
  barline or a fragment boundary is unsupported.
- Level 1 states form `start continue* end` over consecutive events.
- A level `N` of 2 or more requires level `N-1` on the same event. Its states
  form `start continue* end` runs over consecutive events of the level `N-1`
  run, or a single-event hook. `hook-forward` needs a following event in the
  group and `hook-backward` a preceding one, and a hook is never inside a
  `start` to `end` span of its level.
- The highest level of a beamed note equals the flag count of its duration: `8`
  allows level 1, `16` level 2, `32` level 3, `64` level 4 and `128` level 5. A
  duration of `4` or longer cannot be beamed.
- Rests and spacers may carry beams. A rest or spacer carries the levels whose
  beams pass over it, from level 1 up, and at most its flag count. A rest under
  only the primary beam of a group carries only `beam1`.

### Relations

A relation links endpoints that are written on events or pitches. Each has a
label `rN`.

- A tie is written on a pitch as `{tie@rN:start}` or `{tie@rN:end}`. A pitch
  that both ends and starts a tie writes the end first:
  `{tie@r1:end,tie@r2:start}`. The end pitch lies in the next non-grace
  rhythmic event of the same voice line, or in a non-grace rhythmic event of
  another voice line of the same staff that starts when the start event ends,
  across barlines. It has the same sounding step, octave and alteration, and
  assembly checks the alteration when that of the start is deferred.
- `{tie-in}` marks a pitch that a tie enters at the start of a later volta
  bracket, as in a tie into a second ending. A _volta group_ is a run of
  adjacent measures that each carry an `ending` option, and its _source
  measure_ is the measure before the group. The pitch lies in the first
  non-grace rhythmic event of its voice line in the first measure of a bracket
  other than the group's first. Its _source_ is a pitch with the same sounding
  step and octave in a non-grace rhythmic event of the same staff that ends at
  the end of the source measure. At least one source shall exist, all sources
  shall have the same alteration, and the tie-in pitch shall resolve to that
  alteration, whether it inherits it or prints an accidental. A tie-in is not a
  relation and has no
  label, and its pitch may also start a tie: `{tie-in,tie@r2:start}`. A
  validator checks the source when the document contains it, and assembly
  checks every tie-in. When the document does not contain the source, the
  alteration of a tie-in pitch without a printed accidental is _deferred_, and
  so is that of each pitch that inherits it through ties, including from a
  carried tie whose `pitch` has `"alter":"tie-in"`. The document's validator
  does not check a deferred alteration, a tie still open from such a pitch at
  the end of the document carries `"alter":"tie-in"`, and assembly resolves
  and checks every deferred alteration.
- A slur uses `rel=slur@rN:start` and `rel=slur@rN:end` on two different notes,
  the start before the end.
- A two-note tremolo uses `rel=tremolo@rN:two-start:S` and
  `rel=tremolo@rN:two-end:S` on consecutive non-grace rhythmic events of one
  voice line, with equal written durations and the same `S`. Directions may lie
  between them.
- An extended trill uses `rel=ornament@rN:start:extended-trill` on a note that
  carries `orn=trill`, and `rel=ornament@rN:end:extended-trill` on a later note.
- An arpeggio uses `rel=arpeggio@rN:member` on each member event. It has at
  least two members at the same onset, possibly on different staves, and is
  never open across a system boundary. `arp` is the unrelated flag for a rolled
  chord within one event.
- A hairpin, an ottava line, a pedal line and a dashes line are written on
  directions, as described under directions.

Rules for all relations:

- Endpoints with the same label agree in kind and variant. A label that starts
  outside the document is valid only if `carry-in` lists it as open. A relation
  may stay open at the end of a system document, and then appears in its
  carry-out. No relation stays open at the end of an assembled score.
  Arpeggios cannot stay open.
- Labels are document-scoped and are never reused within a document. A carried
  relation keeps its carry-in label, except that carried relations whose
  `carry-in` entries differ only by `id` take the labels of that group in
  ascending order: first those that end in the document, in the order of their
  last endpoints, and then those that stay open. Each new relation takes the
  smallest `rN` that is unused, processing new relations in this order: first
  endpoint (measure, offset, staff, voice, event index, member index, with an
  event-level endpoint before member endpoints), then kind in ASCII order, then
  last endpoint in the document (a relation that stays open follows those that
  end), then variant. Assembly renames every label to `r1`, `r2`, ... in the
  same order, following carry links.
- A tie endpoint always names its member, so `member_index` is 1 for a
  single-pitch note. Every other kind is event-level, and `member_index` is
  null.
- Two relations that are identical in kind, endpoints and variant are invalid,
  unless both are still open at the end of the document or both are slurs. At
  most two slurs share both endpoints, as when one slur is printed above and
  one below the same two chords.

### Directions

A direction is written in the voice line where it occurs, takes the current
onset and has no duration. It is an event for indexing, and its onset shall be
an event boundary in its line.

- `<mf>` and the other dynamics: `pppp`, `ppp`, `pp`, `p`, `mp`, `mf`, `f`,
  `ff`, `fff`, `ffff`, `sf`, `sfz`, `sffz`, `fp`, `rf`, `rfz`, `fz`, `sfp`,
  `sfpp` and `pf`. A leading `^`, as in `<^f>`, marks a dynamic printed above
  the staff and is a layout hint.
- `<clef X>` changes the clef of its line's staff from its onset on. Two clef
  directions for the same staff at the same onset are invalid, and a later
  onset replaces an earlier one.
- `<"text">` is expression text. Words such as "cresc." and "D.C." are text, not
  hairpins or navigation marks.
- `<tempo "Allegro">`, `<tempo "Allegro" q=120>` and `<tempo "" q=120>` record
  tempo text and a metronome mark. The unit is `q.`, `h.`, `q`, `h` or `e`
  (dotted quarter, dotted half, quarter, half or eighth) and the value is a
  positive number of units per minute.
- `<rehearsal "A">` is a rehearsal mark.
- `<pedal-sign down>` and `<pedal-sign up>` record the pedal glyphs, which are
  distinct from pedal lines.
- A hairpin starts with `<cresc(>[rel=hairpin@rN:start:cresc]` or
  `<dim(>[rel=hairpin@rN:start:dim]` and ends with
  `<hairpin)>[rel=hairpin@rN:end]`.
- An ottava line starts with `<S(>[rel=ottava@rN:start:S]` and ends with
  `<S)>[rel=ottava@rN:end:S]`, where `S` is `8va`, `8vb`, `15ma` or `15mb`. The
  variant shall equal the sign, and the end shall equal its start. The line
  applies to the pitches of every event, in any voice line, whose displayed
  staff is the staff of the voice line that holds its start direction, from the
  onset of that direction, inclusive, to the onset of its end direction,
  exclusive. The end direction shall be on that staff, and ottava lines on one
  staff shall not overlap.
- A pedal line has one `<ped(>[rel=pedal@rN:line-start]`, any number of
  `<ped(>[rel=pedal@rN:line-change]` and one
  `<ped)>[rel=pedal@rN:line-release]`.
- A dashes line is the dashed extension of expression text, such as "dim."
  followed by dashes. It starts with `<"dim."(>[rel=dashes@rN:start]`, which
  also records the text, and ends with `<dashes)>[rel=dashes@rN:end]`.

### Event indexing

An event index is 1-based per measure and voice line. It is measure-global: a
fragment continues the count of the preceding fragments of its measure, which
`carry-in.event_counts` supplies. Indexing is depth-first through tuplet
containers.
Directions and grace notes count, a chord is one event, and tuplet containers do
not count.

## Carry state and assembly

### Carry state

`carry-in` is one JSON object on one line, with no whitespace and these keys in
this order:

- `measure`: the measure id of the carry position.
- `offset`: the onset in that measure, as a quoted rational.
- `event_counts`: an object from `"S.V"` to the positive number of events
  already written for that voice line in the carry measure. Voice lines with no
  events are omitted, and the object is empty at offset `"0"`. Keys ascend by
  staff, then voice.
- `accidental_scope`: `octave` or `letter`.
- `accidentals`: the accidentals in force in the carry measure, empty at offset
  `"0"`. Each entry is `{"staff":S,"step":"F","octave":4,"alter":1}`, with
  `octave` present only when the scope is `octave`, and records the last
  printed alteration of that displayed staff and step. Entries ascend by staff,
  step letter and octave.
- `open`: the relations that are open at the carry position, in ascending label
  order. An entry has `id`, `kind` and `start`, followed by fields for its kind.
  `start` is an endpoint with `measure`, `offset`, `staff`, `voice`,
  `event_index` and `member_index`. A tie adds `pitch`, the sounding step,
  octave and alteration, with `"alter":"tie-in"` when the alteration is
  deferred to a tie-in source. A tremolo adds `variant` (the stroke count),
  `written` (the written duration of its first note) and `scale` (its tuplet
  multiplier). An ornament adds `variant` `extended-trill`, a hairpin adds
  `cresc` or `dim`, and an ottava adds its sign. Slurs, pedal lines and dashes
  lines add nothing.

The first system of a piece has `measure` 1 and `offset` `"0"`, with empty
`event_counts`, `accidentals` and `open`.

### Carry-out

The carry-out of a system document has the shape of a carry-in. It is derived
from the parsed text and is never written in the document.

- When the last block ends at the end of its measure, `measure` is the last id
  plus 1, `offset` is `"0"`, and `event_counts` and `accidentals` are empty.
- Otherwise `measure` is the last id and `offset` is that block's `to`.
  `event_counts` holds, for each voice line, the events of the last block plus
  the carry-in counts when that block is also the first block, omitting zero
  counts. `accidentals` holds the state at the end of the block.
- `open` holds every relation still open after the last block. The carried
  kinds are tie, slur, tremolo, ornament, hairpin, ottava, pedal and dashes.

### Assembly

Assembly orders system documents by `page` and `system` and then:

1. requires the first carry-in to be the initial state, `measure` 1 and
   `offset` `"0"` with nothing carried, and the last carry-out to leave no
   relation open;
2. requires each document's carry-in to equal the carry-out derived from the
   previous document, comparing parsed values;
3. requires the state after each document (key, meter, clefs and `carry`) to
   equal the header of the next;
4. joins the fragments of each split measure line by line and requires every
   joined voice line to fill the whole measure, so a gap fails with the
   unfilled range;
5. takes `key`, `time`, `time-symbol`, `left` and `ending` from the first
   fragment and `right` from the last, and drops `from` and `to`;
6. closes a bracket left open at the end of a system document: when the last
   measure of a document carries `ending=N:start` or `ending=continue` and the
   next measure carries no `ending` option, the option becomes `ending=N:single`
   or `ending=end`;
7. checks volta adjacency and every tie-in across the whole score, resolves
   and checks every deferred alteration, and renames relation labels globally.

The result is an assembled score. Assembly never fills a missing event with an
invented rest, infers an unprinted accidental or discards an unsupported mark
while reporting success.

## Canonical form

A tool shall write one spelling of a valid document.

- Header lines keep the grammar's order. Blocks are in id order, voice lines
  are in staff, then voice order, and events keep their line order.
- Items are separated by single spaces, lines end with LF, and there are no
  comments, blank lines or trailing spaces.
- Block options follow the order `len`, `key`, `time`, `time-symbol`, `left`,
  `right`, `from`, `to`, `ending`. The canonical form omits a `len` equal to the
  length of the meter in effect, a reprint of `key` or `time`,
  `time-symbol=numeric`, `from=0` and a `to` equal to the effective length.
- Event flags are ordered by class: articulations (in ASCII order of their
  names), fermatas, `arp`, `up` or `down`, `grace` or `grace/`, `xs1` or `xs2`,
  beams (by level), `orn`, `tremolo=single` and relations (by kind in ASCII
  order, then by ascending label number).
- Chord members use the order defined under pitch.
- Relation labels follow the rule under relations.
- Rationals are reduced. Text is written as raw characters except `"`, `\`, tab,
  LF and CR, which are written `\"`, `\\`, `\t`, `\n` and `\r`. A supplementary
  character is written raw, and `/` is never escaped.
- A carry state has the spelling of the grammar, in the order defined under
  carry state.

Normalization identifies two spellings only when they encode the same content
and the same encoded layout hints. It never erases a content distinction or an
encoded layout hint.

## Transcription rules

A printed system often admits several valid documents. These rules choose the
one that transcribes it, so that writers who read the page alike write the same
text. A validator cannot check them from the text alone, and they add no
validity rule: a valid document that breaks them is still valid, but it is not
the transcription of that page. The examples elsewhere in this record show
validity and omit the layout hints that a transcription writes. Handwritten
additions to a printed copy are not transcribed.

### Voices and staves

- Noteheads on one stem form one event. A notehead with both an up-stem and a
  down-stem belongs to two voices.
- A staff that prints one voice throughout a measure writes it as voice 1,
  whatever the voices of the neighboring measures.
- Where a staff prints more than one voice in a measure, an event with an
  up-stem belongs to voice 1 and one with a down-stem to voice 2. With three or
  four voices, the up-stem voices are 1 and 3 and the down-stem voices 2 and 4,
  each pair numbered from the top. A stemless note or a rest belongs to the
  voice whose rhythm it completes. A rest printed once for both voices belongs
  to voice 1, and the other voice has a spacer there.
- A beam group whose events are displayed on both staves belongs to the staff
  that displays most of its non-grace events, or its first one when the counts
  are equal. Its events on the other staff carry `xsN`. Every other event
  belongs to the staff that displays it.

### Marks and words

- Every note event with a printed stem carries `up` or `down`, and every
  articulation carries `^` or `_`, as printed. A dynamic printed above its staff
  carries `^`.
- A mark of an event (an articulation, fermata, ornament or fingering) that is
  printed between two events belongs to the event whose notehead or rest is
  horizontally nearest to the mark's center, or to the earlier one at equal
  distance.
- An arc that joins two consecutive single notes of the same pitch in one voice
  line is a tie, unless the second note carries an articulation, in which case
  it is a slur.
- Printed words are encoded as printed. "loco" at the end of an ottava line is
  `<"loco">` besides the ottava end. Words for a gradual tempo change, such as
  "rit.", "rall." and "accel.", are text and never `tempo`.
- A `Ped.` glyph that begins a pedal line is the start of that line.
  `<pedal-sign down>` and `<pedal-sign up>` record only glyphs that are not
  joined to a line.

### Directions

- Onset: a dynamic takes the onset of the event nearest to its center, or of
  the earlier one at equal distance. Any other direction, and the start of a
  hairpin or line, takes the onset of the first event at or to the right of its
  left edge. The end of a hairpin or line, and a pedal change, take the onset of
  the first event at or to the right of the end or change point. The events
  counted are the notes and rests of both staves, except that a clef or ottava
  direction counts only events displayed on its own staff.
- System edges: a point with no counted event at or to the right of it in the
  system takes the end of the last block as its onset, and its direction is
  written after the rhythmic events of its line. A hairpin or line that reaches
  the right edge of the system without an end stays open into the next system,
  and one that visibly ends before that edge ends in the system. A relation
  that `carry-in` lists as open and that shows no continuation in the system
  ends at the first event of the system that can hold its end. A volta bracket
  that reaches the right edge of the system without a closing hook is written
  `start` or `continue`, and one that ends there with a hook is written
  `single` or `end`.
- Reprints at a system start: an unchanged clef is recorded only in the `clefs`
  line, and a `tr` sign or text reprinted over a trill or dashes line that
  `carry-in` lists as open continues that relation and is not encoded.
- Staff: a clef or ottava direction belongs to its own staff. Any other
  direction belongs to staff 2 when it is printed on or below staff 2, and
  otherwise to staff 1, including when it is printed between the staves.
- Line: a direction is written in the first voice line of its staff, by voice
  number, that has an event boundary at its onset. Failing that, a clef or
  ottava direction is written in a new voice line of its staff with the lowest
  unused voice number, which holds only spacers and the direction, and any
  other direction in the first such line of the other staff. A fifth voice line
  is unsupported, as more than four voices on a staff are. In its line a
  direction follows any grace notes at its onset and precedes the rhythmic
  event there, if any. Directions at one onset of a line are written in the
  ASCII order of their spellings with relation labels ignored, and those whose
  spellings then agree in ascending label order.

### Rhythm

- Notes whose printed values fill their measure only in tuplet groups are
  written in tuplet groups even when no tuplet number is printed. A _grouping_
  places some rhythmic events of a voice line in groups so that the line fills
  its measure. Beams decide first: no group contains two consecutive events
  that both carry beams but share no level-1 beam. Within that limit, each
  group starts on a beat, covers the fewest whole beats that its events fill,
  and has the reduced ratio of its written duration to that time, such as
  `3:2` for three eighth notes in one quarter-note beat. A beat is `1/D` of the
  meter `N/D`, or `3/D` in a compound meter, where `N` is 6, 9 or 12 and `D` is
  at least 8. When some groupings use only `3:2` groups in a simple meter, or
  only `2:3` groups in a compound meter, only those count. Exactly one grouping
  shall remain, and a measure with none or several is unsupported.
- `len` is written only when the printed content of a measure is shorter or
  longer than its meter, as in a pickup.

## Category encodings

The format covers 26 content categories. Each bullet gives the encoding and a
witness, a string that the grammar accepts.

- `pitch`: step, optional accidental and octave, in parentheses for a
  parenthesized notehead. Witnesses: `C4/4`; `F3+(A3)/2`.
- `rhythm_rest`: durations `1` to `128` with dots, rests `r/`, unprinted spacers
  `x/` and whole-measure rests `mrest`. Witness: `C4/4. r/8 x/4`.
- `chord_membership`: pitches joined by `+`. Witness: `C4+E4+G4/2`.
- `accidental`: the printed accidental, in parentheses when printed so.
  Witness: `F#4/4 Cn4/4 B(b)3/4`.
- `voice`: the voice number in the line prefix. Witness: `1.2: C4/4`.
- `cross_staff`: the `xsN` flag. Witness: `C4/8[xs2]`.
- `clef`: the `clefs` header and `clef` directions. Witness: `<clef F4>`.
- `key_signature`: the `key` header and the `key=` option. Witness:
  `m 4 key=-3`.
- `time_signature`: the `time` header and the `time=`, `time-symbol=` and `len=`
  options. Witnesses: `m 5 time=2/2 time-symbol=cut`; `m 1 len=1/4`.
- `beam`: beam states by level. Witness: `C5/8[beam1=start] D5/8[beam1=end]`.
- `tuplet`: `N:M{...}`. Witness: `3:2{C4/8 D4/8 E4/8}`.
- `tie`: tie marks on a pitch. Witnesses: `C4{tie@r1:start}/2`;
  `C4{tie-in}/2`.
- `slur`: slur relation flags. Witness:
  `C4/4[rel=slur@r1:start] D4/4[rel=slur@r1:end]`.
- `grace`: `grace` and `grace/`. Witness: `D5/16[grace/] C5/4`.
- `articulation_fermata`: articulations and fermatas. Witnesses:
  `C4/4[stacc^,ferm]`; `C4/4[ten,ferm_]`.
- `ornament`: `orn=` and the extended-trill relation. Witnesses:
  `C5/4[orn=trill]`;
  `C5/2[orn=trill,rel=ornament@r1:start:extended-trill]`;
  `D5/2[rel=ornament@r1:end:extended-trill]`.
- `tremolo`: single-note and two-note tremolos. Witnesses:
  `C4/2[tremolo=single:3]`; `C4/2[rel=tremolo@r1:two-start:3]`;
  `E4/2[rel=tremolo@r1:two-end:3]`.
- `fingering`: the pitch suffix `f1` to `f5`, or two digits on a trilled
  pitch. Witnesses: `C4f1/8 D4f2/8`; `A4f13/4[orn=trill]`.
- `dynamic`: dynamic directions. Witnesses: `<mf>`; `<^f>`.
- `hairpin`: hairpin directions. Witnesses:
  `<cresc(>[rel=hairpin@r1:start:cresc]`; `<hairpin)>[rel=hairpin@r1:end]`.
- `ottava`: ottava directions. Witnesses:
  `<8va(>[rel=ottava@r1:start:8va]`; `<8va)>[rel=ottava@r1:end:8va]`.
- `pedal`: pedal lines and pedal signs. Witnesses:
  `<ped(>[rel=pedal@r1:line-start]`; `<ped)>[rel=pedal@r1:line-release]`;
  `<pedal-sign down>`.
- `arpeggio`: the `arp` flag and the arpeggio relation. Witnesses:
  `C4+E4+G4/2[arp]`; `E4+G4/2[rel=arpeggio@r1:member]`.
- `repeat_ending`: repeat barlines and volta brackets. Witnesses:
  `m 3 left=repeat`; `m 7 right=repeat ending=1:single`;
  `m 8 right=final ending=2:single`.
- `text`: expression text directions and dashes lines. Witnesses:
  `<"dolce">`; `<"dim."(>[rel=dashes@r1:start]`;
  `<dashes)>[rel=dashes@r1:end]`.
- `tempo_rehearsal`: tempo and rehearsal directions. Witnesses:
  `<tempo "Allegro" q=120>`; `<rehearsal "A">`.

## Layout, unsupported content and open points

Layout hints are encoded and never count as content differences: the `up` and
`down` stem flags, the `^` and `_` placement suffixes, `ferm_` against `ferm`,
and the `^` on a dynamic. Some printed layout has no encoding at all: curve
shape, spacing, fonts, cautionary key-signature glyphs, a reprinted unchanged
key or numeric time signature, and the closing hook of a volta bracket.

The requirements record lists unsupported content. This format has no spelling
for it. A validator rejects a document that carries it, and no tool shall drop
or approximate it while reporting success.

Open points:

- A compact shorthand may be added in a later major version if tokenizer and
  model evidence justifies it. Text Score 2 specifies none.
- A recognition sidecar (locations, confidence and coverage) is not part of this
  format and is subject to its own record admission.

## Source basis

Each statement below is a source finding, an inference or a design decision.
The source findings quote the MusicXML 4.0 reference and the XML 1.0
specification.

Source findings:

- The `clef-octave-change` element is "used for transposing clefs. A treble clef
  for tenors would have a value of -1." See
  [clef-octave-change](https://www.w3.org/2021/06/musicxml40/musicxml-reference/elements/clef-octave-change/).
- The `octave-shift` element "indicates where notes are shifted up or down from
  their performed values because of printing difficulty. Thus a treble clef line
  noted with 8va will be indicated with an octave-shift down from the pitch data
  indicated in the notes." See
  [octave-shift](https://www.w3.org/2021/06/musicxml40/musicxml-reference/elements/octave-shift/).
- For a double-note tremolo, each note's duration "should correspond to half of
  the notated type value". A `time-modification` of 2/1 is added and multiplied
  with any tuplet ratio, and the stroke count is 0 to 8 and excludes beams. See
  [tremolo](https://www.w3.org/2021/06/musicxml40/musicxml-reference/elements/tremolo/).
- "Octaves are represented by the numbers 0 to 9, where 4 indicates the octave
  started by middle C." See
  [octave](https://www.w3.org/2021/06/musicxml40/musicxml-reference/data-types/octave/).
- The XML 1.0 character range is "Char ::= #x9 | #xA | #xD | [#x20-#xD7FF] |
  [#xE000-#xFFFD] | [#x10000-#x10FFFF]", described as "any Unicode character,
  excluding the surrogate blocks, FFFE, and FFFF." See
  [XML 1.0 (Fifth Edition), section 2.2](https://www.w3.org/TR/2008/REC-xml-20081126/#charsets).

Inference from those findings:

- Pitch data in MusicXML are performed values. A text pitch that includes the
  clef octave change and excludes the ottava line therefore maps to MusicXML
  pitch data after adding the ottava shift.

Design decisions that follow the findings:

- The pitch convention: written pitch with the clef octave change and without
  the ottava line, and a derived sounding pitch.
- The octave range `0` to `9`, which is the range of the MusicXML data type.
- The half-duration rule for two-note tremolo endpoints, which follows the
  MusicXML definition.
- The stroke range `1` to `8` for tremolo marks. MusicXML also allows 0, which
  this format does not encode.
- The text character set, which follows the XML 1.0 `Char` range so that every
  valid text converts to an XML 1.0 document.

Every other rule in this record is a design decision. The examples are written
for this record and contain no transcribed score.
