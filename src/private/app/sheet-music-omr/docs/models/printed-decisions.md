# From printed evidence to one reading

The [transcription rules](../text-format.md#transcription-rules) do more than
define valid syntax. Given fixed evidence, which outputs satisfy the rules?
The [writer model](TextScoreTarget.als) starts too late to answer that for
headings, anchors or spans: it already knows its note, voice and stem.

## What counts as an answer?

First identify eligible candidates, **without** assuming that a winning
candidate exists. Apply the rule, then compare the results at the level the
text actually records. A dynamic needs an onset; an articulation needs an
event; a tie needs a member of an event. Two tied candidates can give one
onset but two different attachments. Likewise, exchanging indistinguishable
rests need not change either voice line.

For determined, supported evidence, ask separately whether there is **any**
observable output and whether there is **at most one**. For other cases ask
which outcome the contract calls for: a review marker, an unsupported input,
a carry mismatch, or an explicit fallback. Do not select the first atom to
make the model deterministic. Nor does an arbitrary underspecified test
scene establish a defect in the format.

[ReadingDecisions](ReadingDecisions.als) has only the shared finite-order
operations. For a single choice, keep candidates with no incoming preference
edge. For a sequence, existence means the precedence graph is acyclic;
uniqueness additionally requires reachability in one direction for every
distinct pair. These criteria concern **all** linear extensions, not whether
one chosen topological sort happens to be stable.

## Rule-family audit

| Decision and observable output                             | Executable scope                                                                                                                                                                                                             | Boundary not established by that check                                                                                                                                                         |
| ---------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Heading lines → sequence                                   | [Geometry](PrintedGeometry.als): every nonempty remainder has one next text; ties denote geometrically identical lines with identical words.                                                                                 | Compatibility with the former pairwise order is checked on clearly separated columns/extents. Heading versus tempo is an input.                                                                |
| Mark or dynamic → event or onset                           | Geometry: compare distances and earlier onsets; compare pairwise and minimum-band interpretations. Witness two distinct simultaneous owners but one onset.                                                                   | A dynamic can consider both staves. The rule specifically for a mark _between two events_ does not by itself generate a three-candidate contest.                                               |
| First eligible event to the right → onset or end-of-system | Geometry: for **every** subset of eligible events check the minimum observable onset, including equal onsets and the specified empty fallback.                                                                               | Candidate filtering by extent, whole-measure rest and staff is an input. Trill-end eligibility and carry are different rules; do not apply the system-end fallback to them.                    |
| Arc tip → event/member or system edge                      | [Endpoints](PrintedEndpoints.als): a containing extent wins; otherwise the margin competes as a center, before choosing noteheads.                                                                                           | Known staff and relevant margin, whole-space coordinates, symmetric extents. Several head identities are not automatically several legal ties. Carry is exercised independently below.         |
| Cross-staff beam → staff and stem-derived voice            | [Ownership](PrintedOwnership.als): majority, first-on-tie, first non-grace event on the chosen staff. Compare with first-anywhere as a negative control.                                                                     | One nonempty group, two-voice measure. No inference of rhythmic overlap, three/four-voice ranking, or all-grace groups.                                                                        |
| Direction at a known onset → staff/voice address           | Ownership: lowest eligible local line; spare local voice for a staff-local mark, otherwise eligible line on the other staff. Identify the two empty cases.                                                                   | Staff and event boundaries are inputs. Exhausting four local lines is explicitly unsupported; a regular direction with no eligible line anywhere is not shown reachable in a valid measure.    |
| Rest → voice completing its rhythm                         | [Completion](PrintedCompletion.als): enumerate assignments against **both** independent time holes and compare the voice lines modulo indistinguishable rest tokens.                                                         | Given the sounding events and holes. No printed vertical offsets or durations beyond two slots; excludes the one-rest-for-both convention.                                                     |
| Fingering digits → pitch and voice                         | [Fingering](PrintedFingering.als): one column, rank from the printed side; put a shared head's digit in its lowest voice. Detect excess single digits.                                                                       | Distinct visible heights and already identified single digits. Substitution marks, beside-head digits and digit pairs are not ranked by this model.                                            |
| Identity/classification → token, relation or disposition   | The [reading models](README.md#layer-0-what-can-a-reading-establish-about-the-paper) exercise ambiguous marks and deferred arcs; [assembly](TextScoreAssembly.als) and [labels](TextScoreLabels.als) start with known spans. | Point versus extended ornaments, word versus heading and reprint eligibility are not established here.                                                                                         |
| Implicit tuplets → grouping or review                      | [Tuplets](PrintedTuplets.als): enumerate beat splits of 2–7 equal eighths in two beats, exclude groups mixing beam runs and apply the meter's ratio preference. Witness zero, one and several splits.                        | One-beat groups only. No nested/printed groups, uneven durations, pickup inference, general prefix/suffix or marker closure. A missing split is not a no-solution claim outside that fragment. |

## Bounded results and their interpretation

- **A sequence can be impossible even if no two sequences disagree.** The
  solver discovers a three-heading cycle from geometry, without a fixture
  encoding its edges. A noncyclic control has a unique order; wherever the
  clearly separated pairwise rules admit an order it is unique in scope.
  The revised rule makes only clear vertical separation mandatory, choosing
  the leftmost available line at each step. This avoids transitively merging
  overlapping rows. The model also checks agreement with every feasible old
  order in the clear-box fragment. See [the design finding](https://github.com/hcoona/three/issues/1998).
- **Local tolerance is not a global ordering.** For three successive events
  at distances `3/8`, `2/8`, `1/8` staff space from a dynamic, the earlier
  event wins each adjacent comparison, but the last beats the first. The
  pairwise interpretation has **no winner**. Choosing the exact minimum
  first, then the earliest onset strictly within `1/4` of it, gives the
  middle onset. The latter has a unique onset throughout this scope and
  agrees with the pairwise rule for at most two candidates. The revised
  transcription rule uses this minimum band; the legacy comparison stays
  in the model as a negative control.
  See [the clarification](https://github.com/hcoona/three/issues/2002).
- **Some multiplicity belongs to the evidence.** A shared point can leave
  two arc endpoint identities even though a head always exists on a nonempty
  modeled staff; distinct heights suffice for uniqueness here. In two equal
  rhythmic holes, a long rest in one voice and two short rests in the other
  can trade voices while still filling both. Unlike exchanging identical
  tokens, this changes the text. Rhythm alone does not determine the answer;
  neither witness licenses silently picking a voice or declaring the format
  incapable of representing the alternatives.
- **Not all choices are problematic.** Beam majority plus first-on-tie and
  the on-home-staff stem rule give one owner whenever the modeled group is
  nonempty. The first event anywhere is _not_ an equivalent shortcut.
  The direction-line check distinguishes a unique address from an exhausted
  or absent line rather than creating a fifth voice.

## Reproduce

From the repository root, using the locked Alloy 6.2.0 and bundled SAT4J:

```shell
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedGeometry.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedEndpoints.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedOwnership.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedCompletion.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedFingering.als
mise run alloy:exec -- src/private/app/sheet-music-omr/docs/models/PrintedTuplets.als
```

All 35 command expectations matched on Linux x64. `check ... expect 1`
requires a counterexample; `expect 0` on a check means none in its finite
scope. Geometry uses up to three headings or events, positions `0..7` in
eighth-spaces; endpoints up to three chords and heads; ownership up to four
beam notes or four numbered lines per staff; completion up to three rests
and two beat slots. The [reading rehearsal](reading-rehearsal.md) separately
exercises the written decisions through Python on invented systems, without
access to private inputs. To extend this audit, identify a rule's input evidence, observable
output and defined non-success outcomes **before** adding a larger scope.
