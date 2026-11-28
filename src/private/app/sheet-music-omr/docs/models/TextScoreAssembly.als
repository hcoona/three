module TextScoreAssembly

open util/ordering[Position] as time

sig Position, Staff, Pitch, Label {}
sig Voice { home: one Staff }
sig Note { line: one Voice, display: one Staff, pitch: one Pitch, on, off: one Position }
enum Kind { Tie, Slur }
sig Span { src, dst: one Note, kind: one Kind }

pred canTie[a, b: Note] {
  a.off = b.on
  a.pitch = b.pitch
  a.line.home = b.line.home or a.display = b.display
}

fact WholeMeasure {
  all n: Note | time/lt[n.on, n.off]
  all v: Voice, p: Position - time/last |
    one n: Note | n.line = v and time/lte[n.on, p] and time/lt[p, n.off]
  all r: Span | time/lt[r.src.on, r.dst.on] and (r.kind = Tie implies canTie[r.src, r.dst])
  all n: Note | lone (kind.Tie & src.n) and lone (kind.Tie & dst.n)
  all a, b: Note | #(kind.Slur & src.a & dst.b) <= 2
}

one sig Cut {
  boundary: one Position,
  leftId, rightId: Span -> lone Label
}
fun leftNotes: set Note { {n: Note | time/lt[n.on, Cut.boundary]} }
fun rightNotes: set Note { Note - leftNotes }
fun crossing: set Span { src.leftNotes & dst.rightNotes }
fun leftNames: Label -> Span { ~(Cut.leftId) }
fun rightNames: Label -> Span { ~(Cut.rightId) }

fact SplitEncoding {
  Cut.boundary not in time/first + time/last
  no n: leftNotes | time/lt[Cut.boundary, n.off]
  all r: Span | (some r.(Cut.leftId) iff r.src in leftNotes)
            and (some r.(Cut.rightId) iff r.dst in rightNotes)
  all l: Label | {
    lone l.leftNames
    lone l.rightNames
    // A carried name identifies a start and kind, not the unseen end or a global span ID.
    let outgoing = l.leftNames & crossing, incoming = l.rightNames & crossing |
      outgoing.src = incoming.src and outgoing.kind = incoming.kind
  }
}

fun localIndex[n: Note, fragment: set Note]: Int {
  add[1, #{p: fragment | p.line = n.line and time/lt[p.on, n.on]}]
}
fun assembledEvents: Voice -> Int -> Note {
  {v: Voice, i: Int, n: Note | v = n.line and
    ((n in leftNotes and i = localIndex[n, leftNotes]) or
     (n in rightNotes and i = add[#(leftNotes & line.v), localIndex[n, rightNotes]]))}
}
fun leftEnds: Label -> Note { leftNames.dst :> leftNotes }
fun openStarts: Label -> Note { (Label - leftEnds.Note) <: leftNames.src }
fun rightStarts: Label -> Note { rightNames.src :> rightNotes }

// Join the two documents using only their projected marks and carry. Counting
// matching labels retains two slurs with the same endpoints as two occurrences.
fun assembledLabels[carry: Label -> Note, a, b: Note, k: Kind]: set Label {
  {l: Label |
    (a in l.leftNames.src and b in l.leftEnds and k in l.leftNames.kind) or
    (a in l.(rightStarts + carry) and b in l.rightNames.dst and k in l.rightNames.kind)}
}
pred sameGraph[carry: Label -> Note] {
  assembledEvents = {v: Voice, i: Int, n: Note | n.line = v and i = localIndex[n, Note]}
  all a, b: Note, k: Kind | #assembledLabels[carry, a, b, k] = #(src.a & dst.b & kind.k)
}
assert LegalCutPreservesGraph { sameGraph[openStarts] }

pred competing[x, y: Label] {
  x != y
  x + y in openStarts.Note & (rightNames.kind).Tie
  let a = x.openStarts, b = y.openStarts, c = x.rightNames.dst, d = y.rightNames.dst | {
    a.line != b.line
    some n: a + b + c + d | n.display != n.line.home
    canTie[a, d] and canTie[b, c]
  }
}
fun swapped[x, y: Label]: Label -> Note {
  openStarts ++ (x -> y.openStarts + y -> x.openStarts)
}
assert PitchAndClosureSuffice {
  all disj x, y: Label | competing[x, y] implies sameGraph[swapped[x, y]]
}

FaithfulUnison: run { some disj x, y: Label | competing[x, y] and sameGraph[openStarts] }
  for 4 but 4 Int, exactly 3 Position, exactly 2 Voice, exactly 2 Staff, exactly 1 Pitch, exactly 2 Span, exactly 2 Label expect 1
ParallelSlurs: run { some disj r, s: crossing | r.kind = Slur and s.kind = Slur and r.src = s.src and r.dst = s.dst and r.(Cut.leftId) != r.(Cut.rightId) }
  for 3 but exactly 3 Position, exactly 1 Voice expect 1
UnequalCarryCounts: run {
  some disj x, y: Label | competing[x, y]
  some disj u, v: Voice | #(line.u & leftNotes) != #(line.v & leftNotes)
  sameGraph[openStarts]
} for 4 but 4 Int, exactly 4 Position, 2 Voice, 2 Staff, 2 Pitch, 6 Note, 3 Span, 3 Label expect 1
ReusedLocalName: run {
  some l: Label | some l.leftEnds and some l.rightStarts
  sameGraph[openStarts]
} for 4 but exactly 5 Position, exactly 1 Voice, exactly 1 Label expect 1
check LegalCutPreservesGraph for 4 but 4 Int, exactly 4 Position, 2 Voice, 2 Staff, 2 Pitch, 6 Note, 3 Span, 3 Label expect 0
check PitchAndClosureSuffice for 4 but 4 Int, exactly 3 Position, exactly 2 Voice, exactly 2 Staff, exactly 1 Pitch, exactly 2 Span, exactly 2 Label expect 1
