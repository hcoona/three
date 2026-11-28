module PrintedCompletion

// Unlike a sort or an anchor, ownership by "the voice whose rhythm it
// completes" is a simultaneous constraint. Work in a single non-grace,
// two-voice measure. A token here is an individual printed rest, not the
// explicitly shared-rest case. Surrounding pitched events fix both voices.
abstract sig Voice {}
one sig V1, V2 extends Voice {}
sig Beat {}
sig Rest { covers: some Beat }
one sig Ink { holes: Voice -> Beat }

// Evaluate every possible assignment rather than putting an arbitrary
// assignment in a `one` field or assuming the measure can be completed.
pred completes[first: set Rest] {
  first.covers = V1.(Ink.holes)
  (Rest-first).covers = V2.(Ink.holes)
  all disj a,b: first | no a.covers & b.covers
  all disj a,b: Rest-first | no a.covers & b.covers
}
// Observable notation: the scope has no per-rest marks or layout hints.
// Equal-duration rests at identical positions can exchange token identities;
// two successive short rests are NOT a single longer rest.
fun shapes[assigned: set Rest]: set Rest {
  {a: Rest | some r: assigned | r.covers = a.covers}
}
assert OneRhythmicReading {
  all a,b: set Rest | completes[a] and completes[b] implies
    (shapes[a] = shapes[b] and shapes[Rest-a] = shapes[Rest-b])
}
assert OneRestPerSlotSuffices {
  (all disj a,b: Rest | no a.covers & b.covers) implies
    (all a,b: set Rest | completes[a] and completes[b] implies a = b)
}
ExchangeableTokens: run {
  some disj a,b: Rest | a.covers = b.covers and completes[a] and completes[b]
} for exactly 2 Rest, exactly 1 Beat expect 1
check OneRhythmicReading for 3 Rest, exactly 2 Beat expect 1
check OneRestPerSlotSuffices for 3 Rest, exactly 2 Beat expect 0
