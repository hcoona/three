module PrintedEndpoints

// Staff already known. Stay outside the tolerance band: all coordinates
// here are whole staff spaces. An event's noteheads share a horizontal center.
sig Chord { x, halfWidth: one Int }
sig Head { chord: one Chord, y: one Int }
enum Bend { Upward, Downward }
one sig Arc { tipX, tipY, farX: one Int, bend: one Bend }
fun coord: set Int { {i: Int | i >= 0 and i <= 5} }
fact Page {
  Chord.x + Head.y + Arc.tipX + Arc.tipY + Arc.farX in coord
  Chord.halfWidth in 0 + 1
  Chord = Head.chord
}
fun gap[a,b: Int]: Int { a >= b => sub[a,b] else sub[b,a] }
fun closest[cs: set Chord, to: Int]: set Chord {
  {c: cs | no d: cs | gap[d.x,to] < gap[c.x,to]}
}
fun candidates: set Head {
  let holds = {c: Chord | gap[c.x,Arc.tipX] <= c.halfWidth},
      cs = (some holds => holds else closest[closest[Chord,Arc.tipX],Arc.farX]) |
    {h: cs.~chord | no k: cs.~chord |
      gap[k.y,Arc.tipY] < gap[h.y,Arc.tipY] or
      (gap[k.y,Arc.tipY] = gap[h.y,Arc.tipY] and
       (Arc.bend = Upward => k.y < h.y else k.y > h.y))}
}
assert EndExists { some Head implies some candidates }
assert EndNamesOneHead { lone candidates }
assert DistinctHeightsSuffice {
  (all disj a,b: Head | a.y != b.y) implies lone candidates
}
// The same visible head can be shared by two events. Do not equate pitch
// height or ink coordinates with event-level endpoint identity.
SharedPoint: run { some disj a,b: candidates |
  a.chord != b.chord and a.chord.x = b.chord.x and a.y = b.y }
  for 3 but 5 Int expect 1
check EndExists for 3 but 5 Int expect 0
check EndNamesOneHead for 3 but 5 Int expect 1
check DistinctHeightsSuffice for 3 but 5 Int expect 0
