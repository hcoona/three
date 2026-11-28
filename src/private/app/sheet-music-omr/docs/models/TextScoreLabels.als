module TextScoreLabels

// Two slurs start in system 1; one ends in system 2, the other in system 3.
// Everything except the printed relation marks and carry entries is fixed.
abstract sig Start {}
one sig Earlier, Later extends Start {}
abstract sig End {}
one sig Middle, Last extends End {}
abstract sig Name {}
one sig R1, R2 extends Name {}
sig Slur { src: one Start, dst: one End }
fact TwoEnds { all e: End | one dst.e }

// 'first' is the writer's traversal tie-break, not a feature of the reading.
fun firstNames[first: Slur]: Slur -> Name {
  let low = (Slur.src = Start) => src.Earlier else first |
    low->R1 + (Slur-low)->R2
}
fun carried[first: Slur]: Name -> Start { ~(firstNames[first]).src }
fun endNames[first: Slur]: Slur -> Name {
  // Only entries identical apart from ID can exchange names. For these,
  // assign the lowest carried name to the end visible in this document.
  (one Slur.src) => dst.Middle->R1 + dst.Last->R2 else firstNames[first]
}
fun middleMark[n: Slur -> Name]: one Name { dst.Middle.n }
fun nextCarry[first: Slur, n: Slur -> Name]: Name -> Start {
  (Name - middleMark[n]) <: carried[first]
}
fun lastMark[n: Slur -> Name]: one Name { dst.Last.n }
fun graph[first: Slur, n: Slur -> Name]: Start -> End {
  middleMark[n].(carried[first])->Middle +
    lastMark[n].(nextCarry[first,n])->Last
}
pred sameText[a,b: Slur, n,m: Slur -> Name] {
  carried[a] = carried[b] // First-system starts and the second header.
  middleMark[n] = middleMark[m]
  nextCarry[a,n] = nextCarry[b,m]
  lastMark[n] = lastMark[m]
}
assert OneTextAndGraph {
  all a,b: Slur | sameText[a,b,endNames[a],endNames[b]]
  all a: Slur | graph[a,endNames[a]] = ~src.dst
}
assert KeepingEveryNameSuffices {
  all a,b: Slur | sameText[a,b,firstNames[a],firstNames[b]]
}
assert RenumberingAllCarriesIsSafe {
  all a: Slur | graph[a,dst.Middle->R1 + dst.Last->R2] = ~src.dst
}
check OneTextAndGraph for 2 expect 0
check KeepingEveryNameSuffices for 2 expect 1
check RenumberingAllCarriesIsSafe for 2 expect 1
