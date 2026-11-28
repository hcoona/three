module PrintedOwnership
open ReadingDecisions

enum Staff { Upper, Lower }
enum Stem { Up, Down }
enum Placement { EitherStaff, StaffLocal }
sig Note { displayed: one Staff, stem: one Stem, ordinal: one Int }
fact InBeamOrder {
  all n: Note | n.ordinal >= 0 and n.ordinal <= 3
  all disj a,b: Note | a.ordinal != b.ordinal
}
fun earliest[ns: set Note]: set Note {
  unbeaten[ns,{a,b: Note | a.ordinal < b.ordinal}]
}
fun beamHome: set Staff {
  let u = #(displayed.Upper), l = #(displayed.Lower) |
    u > l => Upper else (u < l => Lower else earliest[Note].displayed)
}
// One beam of non-grace events, in a two-voice measure. Apply the mixed-stem
// override to the first event on the chosen staff, not the first anywhere.
fun beamStem: set Stem { earliest[displayed.beamHome].stem }
assert BeamHasOwner {
  some Note implies one beamHome and one beamStem
}
assert FirstAnywhereSuffices { some Note implies earliest[Note].stem = beamStem }
MajorityTie: run {
  some Note and #(displayed.Upper) = #(displayed.Lower) and beamHome = Lower
} for 4 expect 1
check BeamHasOwner for 4 expect 0
check FirstAnywhereSuffices for 4 expect 1

// At one determined onset, several staff-local voice lines can have a boundary.
// The output is a (staff,voice) address, not the input event.
one sig Direction {
  home: one Staff, policy: one Placement,
  active, boundary: Staff -> Int
} {
  active in Staff -> (1 + 2 + 3 + 4)
  boundary in active
}
fun lowest[vs: set Int]: set Int { unbeaten[vs,{a,b: Int | a < b}] }
fun here: set Int { Direction.home.(Direction.boundary) }
fun other: one Staff { Staff - Direction.home }
fun there: set Int { other.(Direction.boundary) }
fun spare: set Int { (1 + 2 + 3 + 4) - Direction.home.(Direction.active) }
fun address: Staff -> Int {
  some here => Direction.home->lowest[here] else
    (Direction.policy = StaffLocal => Direction.home->lowest[spare] else other->lowest[there])
}
assert AddressOrExplicitBoundary {
  lone address
  no address iff (no here and
    ((Direction.policy = StaffLocal and no spare) or
     (Direction.policy = EitherStaff and no there)))
  some here implies address in Direction.home->here
  some here implies (no n: here | n < Staff.address)
  no here and Direction.policy = StaffLocal implies {
    address in Direction.home->spare
    no n: spare | n < Staff.address
  }
  no here and Direction.policy = EitherStaff implies {
    address in other->there
    no n: there | n < Staff.address
  }
}
FullStaff: run { no here and no spare and Direction.policy = StaffLocal }
  for 4 but 0 Note expect 1
check AddressOrExplicitBoundary for 4 but 0 Note expect 0
