---------------------------- MODULE ScoreMeaning ----------------------------
EXTENDS Naturals, Sequences, FiniteSets
CONSTANTS Beats, Absent
ASSUME Beats \in {3, 4}

No == "-"
Glyphs == {"#", "n", "b"}
Starts == {"arc", "tieStart", "slurStart"}
Ends == {"tieEnd", "slurEnd"}
WrittenEvent == [mark : Glyphs \cup {No,"?"}, role : Starts \cup Ends \cup {No,"change","firstEnding","tieIn"},
                 finger : BOOLEAN]
Note(m,r,f) == [mark |-> m, role |-> r, finger |-> f]
Plain == Note(No,No,FALSE)
Bar(i) == 1 + ((i-1) \div Beats)
Last(s) == IF s = {} THEN 0 ELSE CHOOSE i \in s : \A j \in s : j <= i
Span(a,b,k) == [a |-> a, b |-> b, kind |-> k]
Slice(s,lo,hi) == [i \in 1..(hi-lo+1) |-> s[lo+i-1]]
Cat(a,b) == [i \in 1..(Len(a)+Len(b)) |-> IF i <= Len(a) THEN a[i] ELSE b[i-Len(a)]]
Kind(r) == IF r = "tieEnd" THEN "tie" ELSE "slur"
Edge(at,r,p) == [at |-> at, role |-> r, pitch |-> p]
NoEdge == Edge(0,No,No)
Initial == [pos |-> 0, ctx |-> "home", acc |-> No, edge |-> NoEdge]
Key(ctx) == IF ctx = "home" THEN "n" ELSE "#"

\* Streaming interpretation. Only h crosses the local document boundary.
\* The source before a run of endings is not a Text Score carry field.
Local(h) == [h |-> h, last |-> "deferred", source |-> "deferred",
             tones |-> <<>>, spans |-> {}, ok |-> TRUE]
Step(s,e) ==
  LET h == s.h
      i == h.pos+1
      r == e.role
      ctx == IF r = "change" THEN "away" ELSE h.ctx
      inherited == CASE r = "tieEnd" -> h.edge.pitch [] r = "tieIn" -> s.source
                     [] h.acc # No -> h.acc [] OTHER -> Key(ctx)
      pitch == CASE e.mark = "?" -> "unknown" [] e.mark \in Glyphs -> e.mark [] OTHER -> inherited
      edge == CASE r \in Starts -> Edge(i,r,pitch) [] r \in Ends -> NoEdge [] OTHER -> h.edge
      ok == /\ e \in WrittenEvent /\ (e.mark = "?" => r = No)
            /\ (r \in Starts => h.edge = NoEdge)
            /\ (r \in Ends => h.edge.role \in {"arc", IF r = "tieEnd" THEN "tieStart" ELSE "slurStart"})
            /\ (r = "tieEnd" => h.edge.at = i-1)
            /\ (r \in {"tieEnd","tieIn"} => inherited = "deferred" \/ pitch = inherited)
            /\ (r \in {"firstEnding","tieIn","change"} => h.pos % Beats = 0)
  IN [h |-> [pos |-> i, ctx |-> ctx,
             acc |-> IF i % Beats = 0 THEN No ELSE IF e.mark \in Glyphs THEN e.mark ELSE h.acc,
             edge |-> edge],
      last |-> pitch, source |-> IF r = "firstEnding" THEN s.last ELSE s.source,
      tones |-> Append(s.tones,pitch),
      spans |-> IF r \in Ends THEN s.spans \cup {Span(h.edge.at,i,Kind(r))} ELSE s.spans,
      ok |-> s.ok /\ ok]
RECURSIVE ScanFrom(_, _, _)
ScanFrom(s,t,i) == IF i > Len(t) THEN s ELSE ScanFrom(Step(s,t[i]),t,i+1)
Scan(s,t) == ScanFrom(s,t,1)
Read(t) == Scan(Local(Initial),t)
RECURSIVE Bodies(_, _)
Bodies(ds,n) == IF n = 0 THEN <<>> ELSE Bodies(ds,n-1) \o Slice(ds[n].body,1,Len(ds[n].body))
Join(ds) == Bodies(ds,Len(ds))
Out(d) == Scan(Local(d.header),d.body).h
NextHeader(ds) == IF ds = <<>> THEN Initial ELSE Out(ds[Len(ds)])

\* Denotation from the whole written score: look up sources by position, not
\* by replaying the streaming accumulator. Arcs acquire their kind at the end.
Prior(t,i,roles) == Last({j \in 1..(i-1) : t[j].role \in roles})
RECURSIVE Tone(_, _)
Tone(t,i) ==
  IF i = 0 THEN "deferred"
  ELSE LET e == t[i]
           first == Prior(t,i,{"firstEnding"})
           priorMark == Last({j \in 1..(i-1) : Bar(j) = Bar(i) /\ t[j].mark \in Glyphs})
       IN CASE e.mark = "?" -> "unknown" [] e.mark \in Glyphs -> e.mark
            [] e.role = "tieEnd" -> Tone(t,Prior(t,i,Starts))
            [] e.role = "tieIn" -> IF first = 0 THEN "deferred" ELSE Tone(t,first-1)
            [] priorMark # 0 -> t[priorMark].mark
            [] OTHER -> IF \E j \in 1..i : t[j].role = "change" THEN "#" ELSE "n"
VoltasOK(t) == \A i \in DOMAIN t : t[i].role = "tieIn" =>
  LET f == Prior(t,i,{"firstEnding"}) IN f > 1 /\ Bar(i) = Bar(f)+1 /\ t[f-1].mark # "?"
Unanchored == [bar |-> 0, ctx |-> No]
AnchorOK(d) == /\ d.anchor.bar = 0 \/ d.header.pos = (d.anchor.bar-1)*Beats
               /\ d.anchor.ctx = No \/ d.header.ctx = d.anchor.ctx
Links(ds) == \A j \in DOMAIN ds :
  /\ ds[j].header = (IF j = 1 THEN Initial ELSE Out(ds[j-1]))
  /\ AnchorOK(ds[j])
Markers(t) == {i \in DOMAIN t : t[i].mark = "?"}
PrefixOK(ds) == LET t == Join(ds) IN Links(ds) /\ Read(t).ok /\ VoltasOK(t)
Closed(t) == t # <<>> /\ Len(t) % Beats = 0 /\ Read(t).h.edge = NoEdge
Admits(ds,final) == PrefixOK(ds) /\ (~final \/ Closed(Join(ds)))
RECURSIVE Rebind(_, _)
Rebind(ds,h) ==
  IF ds = <<>> THEN <<>>
  ELSE LET d == [ds[1] EXCEPT !.header = h] IN Cat(<<d>>,Rebind(Slice(ds,2,Len(ds)),Out(d)))
ReplaceMark(t,i,m) == [t EXCEPT ![i].mark = m]
Possible(t,i) ==
  IF Markers(t) = {} THEN {Tone(t,i)}
  ELSE {Tone(ReplaceMark(t,CHOOSE p \in Markers(t) : TRUE,m),i) : m \in Glyphs \cup {No}}

Tag(r) == IF r \in Starts \cup Ends THEN No ELSE r
NativeNote(e,p) == [display |-> e.mark, alter |-> p, tag |-> Tag(e.role), finger |-> e.finger]
ScoreGraph(t) ==
  [notes |-> [i \in DOMAIN t |-> NativeNote(t[i],Tone(t,i))],
   spans |-> {Span(Prior(t,i,Starts),i,Kind(t[i].role)) : i \in {j \in DOMAIN t : t[j].role \in Ends}}]
Forward(t) ==
  LET s == Read(t)
  IN [notes |-> [i \in DOMAIN t |-> NativeNote(t[i],s.tones[i])], spans |-> s.spans, foreign |-> FALSE]
NativeGraph(x) == [notes |-> x.notes, spans |-> x.spans]
DecodeXML(x) ==
  [i \in DOMAIN x.notes |->
     LET n == x.notes[i]
         from == {e \in x.spans : e.a = i}
         to == {e \in x.spans : e.b = i}
         r == CASE from # {} -> IF (CHOOSE e \in from : TRUE).kind = "tie" THEN "tieStart" ELSE "slurStart"
                [] to # {} -> IF (CHOOSE e \in to : TRUE).kind = "tie" THEN "tieEnd" ELSE "slurEnd"
                [] OTHER -> n.tag
     IN Note(n.display,r,n.finger)]
Importable(x) ==
  IF x.foreign THEN FALSE
  ELSE LET t == DecodeXML(x)
       IN Read(t).ok /\ VoltasOK(t) /\ Closed(t) /\ Markers(t) = {} /\ ScoreGraph(t) = NativeGraph(x)
=============================================================================
