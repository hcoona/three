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

| Decision and observable output                                       | Executable scope                                                                                                                                                                                                                      | Boundary not established by that check                                                                                                                                                                                                  |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Heading lines → sequence                                             | [Geometry](PrintedGeometry.als): derive precedence from horizontal order and vertical extents; check existence independently of conditional uniqueness.                                                                               | Tests disjoint columns and extents clearly separated or overlapping. It does not guess row clusters or handle title/tempo classification.                                                                                               |
| Mark or dynamic → event or onset                                     | Geometry: compare distances and earlier onsets; compare pairwise and minimum-band interpretations. Witness two distinct simultaneous owners but one onset.                                                                            | A dynamic can consider both staves. The rule specifically for a mark _between two events_ does not by itself generate a three-candidate contest.                                                                                        |
| First eligible event to the right → onset or end-of-system           | Geometry: for **every** subset of eligible events check the minimum observable onset, including equal onsets and the specified empty fallback.                                                                                        | Candidate filtering by extent, whole-measure rest and staff is an input. Trill-end eligibility and carry are different rules; do not apply the system-end fallback to them.                                                             |
| Arc tip → event/member                                               | [Endpoints](PrintedEndpoints.als): prefer containing extents, otherwise nearest center and then proximity to the far end; vertical distance and bend break remaining ties.                                                            | Known staff, whole-space coordinates, symmetric event extents; no grace ordering, staff-edge candidate, carry or subsequent tie/slur classification. Multiple head identities at one point are not automatically multiple _legal ties_. |
| Cross-staff beam → staff and stem-derived voice                      | [Ownership](PrintedOwnership.als): majority, first-on-tie, first non-grace event on the chosen staff. Compare with first-anywhere as a negative control.                                                                              | One nonempty group, two-voice measure. No inference of rhythmic overlap, three/four-voice ranking, or all-grace groups.                                                                                                                 |
| Direction at a known onset → staff/voice address                     | Ownership: lowest eligible local line; spare local voice for a staff-local mark, otherwise eligible line on the other staff. Identify the two empty cases.                                                                            | Staff and event boundaries are inputs. Exhausting four local lines is explicitly unsupported; a regular direction with no eligible line anywhere is not shown reachable in a valid measure.                                             |
| Rest → voice completing its rhythm                                   | [Completion](PrintedCompletion.als): enumerate assignments against **both** independent time holes and compare the voice lines modulo indistinguishable rest tokens.                                                                  | Given the sounding events and holes. No printed vertical offsets or durations beyond two slots; excludes the one-rest-for-both convention.                                                                                              |
| Identity/classification → token, relation or disposition             | The existing [reading models](README.md#layer-0-what-can-a-reading-establish-about-the-paper) exercise ambiguous marks and deferred arcs; [assembly](TextScoreAssembly.als) and [labels](TextScoreLabels.als) start with known spans. | Fingering rank/substitution, point versus extended ornaments, word versus heading, and system-edge/reprint eligibility are not established by the new modules.                                                                          |
| Implicit tuplets → grouping, or determined prefix/suffix plus marker | The specification explicitly distinguishes zero, one and several remaining groupings. The rest-assignment model is **not** a tuplet-grouping model.                                                                                   | Beat filling, nested/printed groups, beam exclusions, ratio preference, pickup length and marker closure remain separate obligations.                                                                                                   |

## Bounded results and their interpretation

- **A sequence can be impossible even if no two sequences disagree.** The
  solver discovers a three-heading cycle from geometry, without a fixture
  encoding its edges. A noncyclic control has a unique order; wherever the
  clearly separated pairwise rules admit an order it is unique in scope.
  See [the design finding](https://github.com/hcoona/three/issues/1998).
- **Local tolerance is not a global ordering.** For three successive events
  at distances `3/8`, `2/8`, `1/8` staff space from a dynamic, the earlier
  event wins each adjacent comparison, but the last beats the first. The
  pairwise interpretation has **no winner**. Choosing the exact minimum
  first, then the earliest onset strictly within `1/4` of it, gives the
  middle onset. The latter has a unique onset throughout this scope and
  agrees with the pairwise rule for at most two candidates. It is a design
  alternative, **not** an accepted rule or evidence of a converter bug.
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
```

All 21 command expectations matched on Linux x64. `check ... expect 1`
requires a counterexample; `expect 0` on a check means none in its finite
scope. Geometry uses up to three headings or events, positions `0..7` in
eighth-spaces; endpoints up to three chords and heads; ownership up to four
beam notes or four numbered lines per staff; completion up to three rests
and two beat slots. No private inputs, product execution or specification
amendment. To extend this audit, identify a rule's input evidence, observable
output and defined non-success outcomes **before** adding a larger scope.
