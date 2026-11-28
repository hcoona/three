module ReadingDecisions

// Orient an edge from the preferred candidate to the less preferred one.
// For a single choice, unlike a full ordering, cycles among losers do not matter.
fun unbeaten[c: set univ, edges: univ -> univ]: set univ {
  c - c.(edges :> c)
}

// A finite precedence graph has a linear extension iff acyclic. That extension
// is unique iff each pair is comparable by reachability. No solver-chosen
// enumeration of atoms is used to break a tie.
pred orderExists[c: set univ, edges: univ -> univ] {
  let r = c <: edges :> c | no iden & ^r
}
pred orderUnique[c: set univ, edges: univ -> univ] {
  orderExists[c,edges]
  let r = ^(c <: edges :> c) | all disj a,b: c | a->b in r or b->a in r
}
