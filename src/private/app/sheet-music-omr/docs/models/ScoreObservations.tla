-------------------------- MODULE ScoreObservations --------------------------
EXTENDS ScoreKnowledge

Threaded == {"reviewSharp","reviewFlat","tie","slur","reviewAndGap","endingReview"}
Scenes == Threaded \cup {"exact","clipped","noMark","miss","duplicate","merged","split",
                         "anchoredMiss","barAnchoredMiss","drift","tieMiss","hole","shortTail","native","foreignText","noisy"}
PageCases == Scenes \ {"native","foreignText"}
B(i,m,r,f) == [j \in 1..Beats |-> IF j = i THEN Note(m,r,f) ELSE Plain]
Thread(m,ending) ==
  <<[j \in 1..Beats |-> IF j = 1 THEN Note(m,No,FALSE)
                         ELSE IF j = 3 THEN Note("n",No,FALSE) ELSE Plain],
    B(Beats,"#","arc",FALSE),
    [j \in 1..Beats |-> CASE j = 1 -> Note(No,ending,FALSE)
                          [] j = Beats -> Note("b",No,FALSE) [] OTHER -> Plain],
    B(1,No,"firstEnding",FALSE), B(1,No,"tieIn",FALSE)>>
Ordinary == <<B(1,"n",No,FALSE), B(1,No,No,TRUE), B(1,"n",No,FALSE),
              B(1,"b",No,FALSE), B(1,"#",No,FALSE), B(Beats,"b",No,TRUE)>>
BarsOnPaper(w) ==
  CASE w = "endingReview" ->
         [Thread("#","tieEnd") EXCEPT ![3][Beats-1].mark = "b", ![3][Beats].mark = No]
    [] w \in Threaded \cup {"tieMiss"} ->
         Thread(IF w = "reviewFlat" THEN "b" ELSE "#", IF w = "slur" THEN "slurEnd" ELSE "tieEnd")
    [] w \in {"anchoredMiss","drift"} -> [Ordinary EXCEPT ![2][1].role = "change"]
    [] w = "noMark" -> [Ordinary EXCEPT ![2][1].finger = FALSE]
    [] OTHER -> Ordinary
RECURSIVE Flatten(_)
Flatten(xs) == IF xs = <<>> THEN <<>> ELSE Cat(xs[1],Flatten(Slice(xs,2,Len(xs))))
Paper(w) == Flatten(BarsOnPaper(w))

\* Origin sets and physical beat intervals belong to the evaluator, NOT to a
\* beam. The cue contains only what the crop shows, including any reprint.
Region(t,anchor,origins,lo,hi) ==
  [cue |-> [body |-> t, anchor |-> anchor], origins |-> origins, beats |-> lo..hi]
Regions(w) ==
  LET b == BarsOnPaper(w)
      anchor(i) == CASE i = 3 /\ w = "anchoredMiss" -> [Unanchored EXCEPT !.ctx = "away"]
                     [] i = 3 /\ w = "barAnchoredMiss" -> [Unanchored EXCEPT !.bar = i]
                     [] OTHER -> Unanchored
      bars == [i \in DOMAIN b |-> Region(b[i],anchor(i),{i},(i-1)*Beats+1,i*Beats)]
  IN IF w \notin Threaded THEN bars
     ELSE Cat(<<Region(<<b[1][1]>>,Unanchored,{0},1,1),
                Region(Slice(b[1],2,Beats),Unanchored,{1},2,Beats)>>,Slice(bars,2,Len(bars)))
Sensor(w) ==
  LET p == Regions(w)
  IN CASE w \in {"reviewSharp","reviewFlat"} -> [p EXCEPT ![1].cue.body[1].mark = "?"]
       [] w = "reviewAndGap" -> [p EXCEPT ![1].cue.body[1].mark = "?", ![3].cue.body[2].role = "hole"]
       [] w = "endingReview" -> [p EXCEPT ![4].cue.body[Beats-1].mark = "?"]
       [] w = "noisy" -> [p EXCEPT ![2].cue.body[1].mark = "soft"]
       [] w = "clipped" -> [p EXCEPT ![2].cue.body[1].finger = FALSE]
       [] w \in {"miss","anchoredMiss","barAnchoredMiss","drift","tieMiss"} -> Cat(<<p[1]>>,Slice(p,3,Len(p)))
       [] w = "duplicate" -> Cat(<<p[1], [p[1] EXCEPT !.origins = {}, !.beats = {}]>>,Slice(p,3,Len(p)))
       [] w = "merged" -> Cat(<<p[1],Region(Cat(p[2].cue.body,p[3].cue.body),Unanchored,{2,3},Beats+1,3*Beats)>>,
                              Slice(p,4,Len(p)))
       [] w = "split" -> Cat(<<p[1],Region(<<p[2].cue.body[1]>>,Unanchored,{2},Beats+1,Beats+1),
                           Region(Slice(p[2].cue.body,2,Beats),Unanchored,{2},Beats+2,2*Beats)>>,Slice(p,3,Len(p)))
       [] w = "hole" -> [p EXCEPT ![2].cue.body[2].role = "hole"]
       [] w = "shortTail" -> [p EXCEPT ![Len(p)].cue.body = Slice(@,1,Beats-1)]
       [] OTHER -> p
Cues(w) == Visible(Sensor(w))
SecondCues(w) ==
  {Cues(w), [i \in DOMAIN Regions(w) |->
             IF w = "reviewAndGap" /\ i = 1 THEN Cues(w)[i] ELSE Regions(w)[i].cue]}

\* Independent written-side boundary: look up sources instead of running Scan
\* or asking Pick whether it accepted. An open arc is a fault only at the end.
OpenAt(t,i) == LET j == Prior(t,i,Starts \cup Ends) IN
                 IF j = 0 THEN 0 ELSE IF t[j].role \in Starts THEN j ELSE 0
WrittenHeader(t) ==
  LET n == Len(t)
      a == Last({i \in DOMAIN t : Bar(i) = Bar(n+1) /\ t[i].mark \in Glyphs})
      e == OpenAt(t,n+1)
  IN [pos |-> n, ctx |-> IF \E i \in DOMAIN t : t[i].role = "change" THEN "away" ELSE "home",
      acc |-> IF a = 0 THEN No ELSE t[a].mark,
      edge |-> IF e = 0 THEN NoEdge ELSE Edge(e,t[e].role,Tone(t,e))]
VisibleConflict(pre,c,final) ==
  LET h == WrittenHeader(pre)
      t == Cat(pre,c.body)
  IN \/ c.anchor.bar # 0 /\ h.pos # (c.anchor.bar-1)*Beats
     \/ c.anchor.ctx # No /\ h.ctx # c.anchor.ctx
     \/ \E i \in (Len(pre)+1)..Len(t) :
          LET r == t[i].role
              e == OpenAt(t,i)
          IN \/ r = "hole" \/ (r \in Starts /\ e # 0)
             \/ r \in Ends /\ (IF e = 0 THEN TRUE ELSE
                      t[e].role \notin {"arc",IF r = "tieEnd" THEN "tieStart" ELSE "slurStart"} \/
                      (r = "tieEnd" /\ e # i-1))
     \/ final /\ (Len(t) % Beats # 0 \/ OpenAt(t,Len(t)+1) # 0)

Doc(h,c,t) == [header |-> h, anchor |-> c.anchor, body |-> t,
               low |-> \E i \in DOMAIN c.body : c.body[i].mark = "soft"]
Pick(ds,c,final,preferred) ==
  LET h == NextHeader(ds)
      spelling(m) == [i \in DOMAIN c.body |-> IF c.body[i].mark = "soft"
                      THEN [c.body[i] EXCEPT !.mark = m] ELSE c.body[i]]
      offered == [h EXCEPT !.ctx = IF c.anchor.ctx = No THEN @ ELSE c.anchor.ctx,
                            !.pos = IF c.anchor.bar = 0 THEN @ ELSE (c.anchor.bar-1)*Beats]
      choices == <<Doc(offered,c,spelling(preferred)),Doc(h,c,spelling(IF preferred = "#" THEN No ELSE "#"))>>
      usable == {i \in DOMAIN choices : Admits(Append(ds,choices[i]),final)}
  IN IF usable = {} THEN Absent ELSE choices[IF 1 \in usable THEN 1 ELSE 2]

\* A reading is a value, not one clock tick per crop. At the first
\* abstention all later crops lack an incoming context.
Report(ds,n) == [docs |-> ds, mandatory |-> (Len(ds)+1)..n]
RECURSIVE ReadFrom(_,_,_)
ReadFrom(cues,i,ds) ==
  IF i > Len(cues) THEN {Report(ds,Len(cues))}
  ELSE LET choices == {Pick(ds,cues[i],i=Len(cues),p) : p \in {No,"#"}}
       IN UNION {IF d = Absent THEN {Report(ds,Len(cues))}
                 ELSE ReadFrom(cues,i+1,Append(ds,d)) : d \in choices}
Readings(cues) == ReadFrom(cues,1,<<>>)
Uncertain(t) == IF Markers(t) = {} THEN {} ELSE {i \in DOMAIN t : Cardinality(Possible(t,i)) > 1}
Corrected(ds,m) ==
  [i \in DOMAIN ds |-> [ds[i] EXCEPT
    !.body = [j \in DOMAIN @ |-> IF @[j].mark = "?" THEN [@[j] EXCEPT !.mark = m] ELSE @[j]]]]

OneToOne(o,i) ==
  /\ Cardinality(o[i].origins) = 1
  /\ \A j \in DOMAIN o \ {i} : o[i].origins \cap o[j].origins = {}
CropAt(ds,k) == CHOOSE i \in DOMAIN ds :
  (ds[i].header.pos < k /\ k <= ds[i].header.pos + Len(ds[i].body))
\* Every beat of a predicted bar must lie in an accepted one-to-one crop of
\* the proposed reference bar. In particular a repeated spurious bar has no edge.
AllowedGold(w,ds) ==
  LET o == Sensor(w)
      n == Len(Join(ds)) \div Beats
  IN {p \in (1..n) \X (DOMAIN BarsOnPaper(w)) :
       \A k \in ((p[1]-1)*Beats+1)..(p[1]*Beats) :
         LET i == CropAt(ds,k) IN OneToOne(o,i) /\ \E b \in o[i].beats : Bar(b) = p[2]}
=============================================================================
