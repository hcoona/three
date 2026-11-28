module PrintedGeometry
open ReadingDecisions

// One unit = 1/8 staff space. Nonnegative bounded coordinates and 5-bit
// integers keep differences, including abs, representable.
fun coord: set Int { {i: Int | i >= 0 and i <= 7} }
fun dist[a,b: Int]: Int { a >= b => sub[a,b] else sub[b,a] }
pred near[a,b: Int] { dist[a,b] < 2 }

sig Heading { x, top, bottom, words: one Int }
fact Coordinates {
  all h: Heading | h.x + h.top + h.bottom in coord and h.top < h.bottom
  Heading.words in 0 + 1 + 2
}
// Stay clear of tolerance-band edge cases for this family.
pred above[a,b: Heading] { a.bottom <= b.top and not near[a.bottom,b.top] }
pred overlap[a,b: Heading] {
  sub[a.bottom,b.top] >= 2 and sub[b.bottom,a.top] >= 2
}
pred clearBoxes {
  all disj a,b: Heading | not near[a.x,b.x]
  all disj a,b: Heading | above[a,b] or above[b,a] or overlap[a,b]
}
// words is the rank of the decoded text, not an identifier of a line.
pred precedes[a,b: Heading] {
  a.x < b.x or (a.x = b.x and (a.top < b.top or
    (a.top = b.top and (a.bottom < b.bottom or
      (a.bottom = b.bottom and a.words < b.words)))))
}
fun nextLine[left: set Heading]: set Heading {
  let ready = unbeaten[left,{a,b: Heading | above[a,b]}] |
    unbeaten[ready,{a,b: Heading | precedes[a,b]}]
}
assert ProgressWithoutTraversalOrder {
  all left: set Heading | some left implies {
    one nextLine[left].words
    all h: nextLine[left] | no a: left | above[a,h]
    all a,b: nextLine[left] | a.x = b.x and a.top = b.top and a.bottom = b.bottom
  }
}
fun headingEdges: Heading -> Heading {
  {a,b: Heading | above[a,b] or (overlap[a,b] and a.x < b.x)}
}
assert PreservesFeasibleOrder {
  clearBoxes implies (all left: set Heading | orderExists[left,headingEdges] implies
    nextLine[left] = unbeaten[left,headingEdges])
}
assert HeadingsHaveAnOrder { clearBoxes implies orderExists[Heading,headingEdges] }
assert FeasibleHeadingOrderIsUnique {
  clearBoxes and orderExists[Heading,headingEdges] implies orderUnique[Heading,headingEdges]
}

sig Event { center, onset: one Int }
one sig Mark { at: one Int }
fact Positions {
  Mark.at in coord
  all e: Event | e.center + e.onset in coord
  all a,b: Event | a.onset < b.onset implies a.center <= b.center
}
fun distance[e: Event]: Int { dist[e.center,Mark.at] }
fun nearestEdges: Event -> Event {
  {a,b: Event | let p = distance[a], q = distance[b] |
    near[p,q] => a.onset < b.onset else p < q}
}
fun nearest: set Event { unbeaten[Event,nearestEdges] }
// A different interpretation: compare every distance with the actual minimum
// first, then take earliest onset in that band.
fun bandChoice: set Event {
  let band = {e: Event | some m: Event |
        (all n: Event | distance[m] <= distance[n]) and near[distance[m],distance[e]]} |
    unbeaten[band,{a,b: Event | a.onset < b.onset}]
}
assert PairwiseNearestExists { some Event implies some nearest }
assert EarlierSettlesEvent { some Event implies lone nearest }
// An event-bound articulation needs an identity; a dynamic only needs
// the onset. The same unresolved tie can therefore be harmless for one.
SameOnsetDifferentOwners: run {
  some disj a,b: nearest | a.center != b.center and a.onset = b.onset and
    a.center < Mark.at and Mark.at < b.center
} for exactly 2 Event, 0 Heading, 5 Int expect 1
assert BandDeterminesDynamicOnset { some Event implies one bandChoice.onset }
assert TwoEventInterpretationsAgree { #Event <= 2 implies nearest = bandChoice }
// First event at or to the right: once candidate extents/staff have been
// filtered, minimize onset, not event identity. Empty uses system end.
fun anchor[eligible: set Event]: Int {
  no eligible => 8 else unbeaten[eligible,{a,b: Event | a.onset < b.onset}].onset
}
assert FirstOnsetOrEdge {
  all eligible: set Event | one anchor[eligible] and
    (8 in anchor[eligible] iff no eligible) and
    (some eligible implies (anchor[eligible] in eligible.onset and
       (all e: eligible | anchor[eligible] <= e.onset)))
}

check HeadingsHaveAnOrder for 3 but 5 Int, 0 Event expect 1
check FeasibleHeadingOrderIsUnique for 3 but 5 Int, 0 Event expect 0
OrdinaryHeadings: run { #Heading = 3 and clearBoxes and some headingEdges and
  orderUnique[Heading,headingEdges] } for 3 but 5 Int, 0 Event expect 1
check PairwiseNearestExists for 3 but 5 Int, 0 Heading expect 1
check EarlierSettlesEvent for 2 but 5 Int, 0 Heading expect 1
check BandDeterminesDynamicOnset for 3 but 5 Int, 0 Heading expect 0
check TwoEventInterpretationsAgree for 3 but 5 Int, 0 Heading expect 0
check FirstOnsetOrEdge for 3 but 5 Int, 0 Heading expect 0
check ProgressWithoutTraversalOrder for 3 but 5 Int, 0 Event expect 0
check PreservesFeasibleOrder for 3 but 5 Int, 0 Event expect 0
CycleNowHasNext: run { clearBoxes and not orderExists[Heading,headingEdges] and
  one nextLine[Heading] } for 3 but 5 Int, 0 Event expect 1
