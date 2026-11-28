--------------------------- MODULE ScoreKnowledge ---------------------------
EXTENDS ScoreMeaning

\* I is an ordered printed partition. Provenance (origins, beats) is available
\* to the bridge checker, not to the reader. T is a report (docs, mandatory).
Visible(regions) == [i \in DOMAIN regions |-> regions[i].cue]
OrderedCover(ink,seen) ==
  /\ DOMAIN seen = DOMAIN ink
  /\ \A i \in DOMAIN ink : seen[i].origins = ink[i].origins /\ seen[i].beats = ink[i].beats
AllowsMark(observed,printed) ==
  CASE observed = "?" -> printed \in Glyphs \cup {No}
    [] observed = "soft" -> printed \in {No,"#"}
    [] OTHER -> observed = printed
NoSilentLoss(ink,seen) ==
  LET paper == Join(Visible(ink))
  IN \A i \in DOMAIN seen :
       LET r == seen[i]
           body == r.cue.body
       IN /\ Cardinality(r.beats) = Len(body)
          /\ \A k \in DOMAIN ink : ink[k].beats = r.beats => ink[k].cue.anchor = r.cue.anchor
          /\ \A j \in DOMAIN body :
               LET at == Last(r.beats)-Len(body)+j
               IN at \in r.beats /\ at \in DOMAIN paper /\
                  body[j].role = paper[at].role /\ body[j].finger = paper[at].finger /\
                  AllowsMark(body[j].mark,paper[at].mark)
Clear(cs) == \A i \in DOMAIN cs : \A j \in DOMAIN cs[i].body : cs[i].body[j].mark \notin {"?","soft"}

\* M is the content quotient of the modeled comparison view: signs, pitches,
\* fingerings, roles and typed spans survive; beam and layout do not.
Categories == {"pitch","mark","structure"}
Present(g,b,c) ==
  [i \in 1..(Len(g.notes) \div Beats) |->
     LET lo == (i-1)*Beats+1 IN
       [pitch |-> [j \in 1..Beats |-> <<g.notes[lo+j-1].display,g.notes[lo+j-1].alter>>],
        mark |-> [j \in 1..Beats |-> g.notes[lo+j-1].finger],
        structure |-> <<[j \in 1..Beats |-> g.notes[lo+j-1].tag],
                        {<<s.a-lo,s.b-lo,s.kind>> : s \in {e \in g.spans : e.a \in lo..(i*Beats)}}>>,
        beam |-> i \in b, layout |-> i \in c]]
Music(view) == [i \in DOMAIN view |-> [k \in Categories |-> view[i][k]]]
PaperMusic(cs) == Music(Present(ScoreGraph(Join(cs)),{2},{4}))
Needs(p) == [events |-> Markers(Join(p.docs)), crops |-> p.mandatory]
NoNeeds == [events |-> {}, crops |-> {}]
Complete(p) == Needs(p) = NoNeeds /\ Admits(p.docs,TRUE)
ReadingMusic(p) == IF Complete(p) THEN Music(Present(NativeGraph(Forward(Join(p.docs))),{},{})) ELSE Absent
Bridge(ink,seen,p) ==
  OrderedCover(ink,seen) /\ NoSilentLoss(ink,seen) /\ Clear(Visible(seen)) /\ Complete(p) =>
    ReadingMusic(p) = PaperMusic(Visible(ink))
ReviewZones(p,draftBars,flags) == [must |-> Needs(p), suspect |-> flags, unflagged |-> draftBars \ flags]
=============================================================================
