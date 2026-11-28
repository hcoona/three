# Reading rehearsal: paper decisions versus document behavior

These are invented desk transcriptions, not a corpus sample. Read the
descriptions first; then use the real Python parser, validator, writer and
assembler on the resulting texts. The software does **not** read coordinates
or decide whether the transcriber placed the right token. From the repository
root, reproduce the executable examples with
`uv run --offline --package sheet-music-omr python -m doctest src/private/app/sheet-music-omr/docs/models/reading-rehearsal.md`.

**Heading, dynamic and fingering.** Three heading lines A, B and C have
vertical extents `[0,2]`, `[1,3]`, `[2.5,4.5]` in staff spaces (downward).
Their left edges are ordered C, B, A. C must wait for A; A and B can be
written now. Choose B on the left, then A, then C. Their text need not be
examined. This preserves the clear A-before-C relation without treating the
three overlapping intervals as one row.

In 2/4, the upper staff opens with an up-stem C4/E4 chord and a down-stem
quarter note sharing the filled C4 head, followed in voice 2 by a quarter
rest. Two digits above, top to bottom 4 and 2, give E4 the
4 and C4 the 2, the latter **only** on voice 1. The next upper quarter is G4.
On the lower staff the rhythm is two eighths and a quarter. Relative to the
center of a lower-staff `p`, the upper opening chord, second lower eighth
and following quarter are at `-35/8`, `-34/8`, `+33/8` spaces. The opening
lower event is farther away (`-48/8`); the two quarters have the same center.
The minimum is `33/8`; `34/8` is inside the band and `35/8` exactly outside.
The earlier retained onset is the **second eighth**, not the opening chord
or the following quarter. The next 2/4 measure contains six equal eighths
in two three-note beams, no numbers: two `3:2` groups fill the beats.

```pycon
>>> import json
>>> from sheet_music_omr import assemble, parse_document, validate_document, write_document
>>> def system(body, number=1, measure=1, opened=(), meter="2/4"):
...     state = dict(measure=measure, offset="0", event_counts={}, accidental_scope="octave", accidentals=[], open=opened)
...     header = f"format: text-score/2\nkey: 0\ntime: {meter}\nclefs: G2 F4\ncarry: octave\npage: 1\nsystem: {number}\n"
...     return parse_document(header + "carry-in: " + json.dumps(state, separators=(",", ":")) + "\n" + body + "\n")
>>> headings = 'heading: "B"\nheading: "A"\nheading: "C"'
>>> lines = "1.1: C4f2+E4f4/4[up] G4/4[up]\n1.2: C4/4[down] r/4\n2.1: C3/8[up] <p> D3/8[up] E3/4[up]"
>>> groups = "3:2{C4/8[beam1=start,up] D4/8[beam1=continue,up] E4/8[beam1=end,up]}"
>>> page = system(headings + "\nm 1\n" + lines + "\nm 2 right=final\n1.1: " + groups + " " + groups)
>>> validate_document(page)
()
>>> whole = assemble([page])
>>> whole.status.value, headings in whole.text, lines in whole.text
('success', True, True)
>>> write_document(parse_document(whole.text)) == whole.text
True

```

**Rhythm where a label would be a guess.** With five equal unbeamed eighths
in 2/4, either beat can take the three notes while the other takes two. No
whole beat at either end is ordinary in every preferred reading. The entire
measure (`1/2` of a whole note) needs review; do not train
on an arbitrarily chosen triplet placement. Conversely, four eighths in two
pairs in a full 6/8 measure give two `2:3` duplets under the compound-meter
preference. In both examples the measure length is known independently (not
a pickup).

```pycon
>>> unsure = assemble([system("m 1 right=final\n1.1: ?(1/2)")])
>>> unsure.status.value, len(unsure.markers), "?(1/2)" in unsure.text
('needs-person', 1, True)
>>> pair = "2:3{E4/8[up] F4/8[up]}"
>>> compound = assemble([system("m 1 right=final\n1.1: " + pair + " " + pair, meter="6/8")])
>>> compound.status.value, compound.text.count(pair)
('success', 2)

```

**Arc at a system break.** A C4 half note is the last event; its arc tip is
outside every notehead extent and equally distant from the last center and
the staff end. It reaches the edge, so record an open pitch arc, not a slur
back to the last note. In the next system the left tip reaches the initial
signature edge and terminates on an unarticulated C4 at onset zero. The
carried pitch agrees: write a tie. If that event is articulated, write a
slur instead. With the carry omitted, the observed continuation is a
mismatch, not permission to invent a new relation.

```pycon
>>> first = system("m 1\n1.1: C4{arc@r1:start}/2[up]")
>>> start = dict(measure=1, offset="0", staff=1, voice=1, event_index=1, member_index=1)
>>> arc = dict(id="r1", kind="arc", start=start, pitch=dict(step="C", octave=4, alter=0))
>>> def finish(event, opened=(arc,)):
...     return assemble([first, system("m 2 right=final\n1.1: " + event, 2, 2, opened)])
>>> tie = finish("C4{tie@r1:end}/2[up]")
>>> tie.status.value, "C4{tie@r1:start}" in tie.text, "C4{tie@r1:end}" in tie.text
('success', True, True)
>>> slur = finish("C4/2[stacc^,up,rel=slur@r1:end]")
>>> slur.status.value, "rel=slur@r1:start" in slur.text, "rel=slur@r1:end" in slur.text
('success', True, True)
>>> finish("C4{tie@r1:end}/2[up]", ()).status.value
'failed'

```
