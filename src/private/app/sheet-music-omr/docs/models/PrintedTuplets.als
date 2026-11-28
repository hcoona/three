module PrintedTuplets

// Two beats of equal written eighth notes. Each inferred group occupies
// one whole beat. A split is the number of eighths played in beat one.
one sig Line { count, perBeat: one Int, beam: Int -> lone Int }
fact Evidence {
  Line.count >= 2 and Line.count <= 7
  Line.perBeat in 2 + 3
  Line.count != add[Line.perBeat,Line.perBeat]
  Line.beam in {i: Int | i >= 0 and i < Line.count} -> (0 + 1 + 2)
  all id: Int, i,j: (Line.beam).id | all k: Int |
    i < k and k < j implies k->id in Line.beam
  all id: Int | some (Line.beam).id implies not lone (Line.beam).id
}
fun segment[a,b: Int]: set Int { {i: Int | a <= i and i < b} }
pred allowed[a,b: Int] {
  sub[b,a] = Line.perBeat or lone segment[a,b].(Line.beam)
}
fun legal: set Int {
  {i: Int | 0 < i and i < Line.count and allowed[0,i] and allowed[i,Line.count]}
}
fun preferred: set Int {
  {i: legal | i in 2 + 3 and sub[Line.count,i] in 2 + 3}
}
fun readings: set Int { some preferred => preferred else legal }
assert PreferenceNeverLosesLastReading { some legal iff some readings }
assert UnbeamedReferenceReadings {
  no Line.beam implies {
    Line.perBeat = 2 and Line.count = 6 implies readings = 3
    Line.perBeat = 2 and Line.count = 5 implies readings = 2 + 3
    Line.perBeat = 3 and Line.count = 4 implies readings = 2
    Line.count = 7 implies readings = segment[1,7]
  }
}
UniqueTriplets: run { Line.perBeat = 2 and Line.count = 6 and
  readings = 3 and some legal - readings } for 4 Int expect 1
AmbiguousBeat: run { Line.perBeat = 2 and Line.count = 5 and
  readings = 2 + 3 } for 4 Int expect 1
CompoundDuplets: run { Line.perBeat = 3 and Line.count = 4 and
  readings = 2 and some legal - readings } for 4 Int expect 1
check PreferenceNeverLosesLastReading for 4 Int expect 0
check UnbeamedReferenceReadings for 4 Int expect 0
NoLegalSplit: run { no legal } for 4 Int expect 1
