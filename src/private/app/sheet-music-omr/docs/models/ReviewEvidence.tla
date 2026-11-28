-------------------------- MODULE ReviewEvidence --------------------------
EXTENDS ScoreObservations

Folds == 0..3
FitRows(target,training) ==
  {[fold |-> g, weights |-> training \ {g},
    weakErrors |-> IF g = 1 THEN 0 ELSE 2, strongErrors |-> IF g = 1 THEN 1 ELSE 0] :
       g \in Folds \ {target}}
Eligible(rows,target) == \A r \in rows :
  r.fold \in Folds /\ r.weights \subseteq Folds /\ r.fold \notin r.weights /\
  target # r.fold /\ target \notin r.weights
\* Abstract calibration to two uncertainty bands. Each synthetic evidence
\* row represents two examples of each band; only their ordering is used.
Calibrate(rows) ==
  {band \in {"weakErrors","strongErrors"} :
     Cardinality({p \in rows \X (1..2) : p[2] <= p[1][band]}) > Cardinality(rows)}
MeasureView(t,b,c) == Present(ScoreGraph(t),b,c)
Draft(w) ==
  LET base == IF w \in Threaded \cup {"tieMiss"} THEN Thread("#","tieEnd") ELSE Ordinary
      author == [base EXCEPT ![2][1].finger = ~@, ![4][Beats].mark = "n"]
      contents == CASE w = "miss" -> Cat(Slice(base,1,2),Slice(author,4,Len(author)))
                    [] w = "duplicate" -> Cat(<<author[1]>>,author) [] OTHER -> author
  IN MeasureView(Flatten(contents),{1},{3})
Diff(a,b) == {c \in Categories : a[c] # b[c]}

\* Minimum edit alignment of two ordered measure sequences. A forbidden
\* substitution cannot be bought at a high cost. Stable ties prefer pairing,
\* then dropping a predicted bar. Pairs retain BOTH ordinal namespaces.
Cell(cost,pairs) == [cost |-> cost, pairs |-> pairs]
GapCost == Cardinality(Categories)+1
RECURSIVE Columns(_,_,_,_,_,_)
Columns(a,b,allowed,i,prev,row) ==
  IF Len(row) > Len(b) THEN row
  ELSE LET j == Len(row)
           drop == Cell(GapCost+prev[j+1].cost,prev[j+1].pairs)
           miss == Cell(GapCost+row[j].cost,row[j].pairs)
           unpaired == IF drop.cost <= miss.cost THEN drop ELSE miss
           diag == Cell(prev[j].cost + Cardinality(Diff(a[i],b[j])),
                        prev[j].pairs \cup {<<i,j>>})
           best == IF <<i,j>> \in allowed /\ diag.cost <= unpaired.cost THEN diag ELSE unpaired
       IN Columns(a,b,allowed,i,prev,Append(row,best))
RECURSIVE Rows(_,_,_,_,_)
Rows(a,b,allowed,i,prev) ==
  IF i > Len(a) THEN prev[Len(b)+1].pairs
  ELSE Rows(a,b,allowed,i+1,Columns(a,b,allowed,i,prev,<<Cell(i*GapCost,{})>>))
Align(a,b,allowed) == Rows(a,b,allowed,1,[j \in 1..(Len(b)+1) |-> Cell((j-1)*GapCost,{})])
AllPairs(a,b) == (DOMAIN a) \X (DOMAIN b)
\* Two candidates for one reference, in either order. Only category equality
\* is observable to Align, so these subsets exhaust the unequal-distance cases.
NearestPair ==
  LET bar(s) == [c \in Categories |-> c \in s]
      ref == <<bar({})>>
  IN \A l,r \in SUBSET Categories : Cardinality(l) # Cardinality(r) =>
       LET a == <<bar(l),bar(r)>> IN
         Align(a,ref,AllPairs(a,ref)) = {<<IF Cardinality(l) < Cardinality(r) THEN 1 ELSE 2,1>>}
Left(p,j) == {q[1] : q \in {v \in p : v[2] = j}}
Anchor(p,i) == LET j == Last({q[2] : q \in {v \in p : v[1] < i}}) IN IF j = 0 THEN 1 ELSE j
Extras(a,p) == DOMAIN a \ {q[1] : q \in p}
At(a,b,p,j) ==
  LET is == Left(p,j)
      matched == IF is = {} THEN {"structure"} ELSE Diff(a[CHOOSE i \in is : TRUE],b[j])
  IN matched \cup (IF \E i \in Extras(a,p) : Anchor(p,i) = j THEN {"structure"} ELSE {})
Cells(a,b,p) == UNION {{<<j,c>> : c \in At(a,b,p,j)} : j \in DOMAIN b}
Blind(a,d,low,cal) ==
  LET p == Align(a,d,AllPairs(a,d))
      delta(j) == Cardinality(At(a,d,p,j))
      \* Two already calibrated confidence bands. No fitting on this split.
      u(j) == IF Left(p,j) = {} THEN 1
              ELSE IF (IF Left(p,j) \cap low # {} THEN "weakErrors" ELSE "strongErrors") \in cal THEN 1 ELSE 0
      weight(j) == delta(j)*(1+u(j))
      before(i,j) == weight(i)>weight(j) \/ (weight(i)=weight(j) /\
                       (u(i)>u(j) \/ (u(i)=u(j) /\ i<j)))
  IN {j \in DOMAIN d : Cardinality({i \in DOMAIN d : before(i,j)}) < Len(d) \div 5}
Compare(a,g,allowed,mask) ==
  LET p == Align(a,g,allowed)
      all == Cells(a,g,p)
      changed(f) == {q[2] : q \in {v \in p : a[v[1]][f] # g[v[2]][f]}}
  IN [paired |-> p, extra |-> Extras(a,p), missing |-> DOMAIN g \ {q[2] : q \in p},
      rawA |-> all, A |-> all \ mask, B |-> changed("beam"), C |-> changed("layout"),
      eligible |-> {j \in DOMAIN g : \E c \in Categories : <<j,c>> \notin mask}]
Caught(d,g,flags,mask) ==
  LET p == Align(d,g,AllPairs(d,g))
      guilty == {v[1] : v \in Cells(d,g,p) \ mask}
      anchors(j) == IF Left(p,j) # {} THEN Left(p,j)
                    ELSE {Anchor({<<q[2],q[1]>> : q \in p},j)}
  IN {j \in guilty : flags \cap (anchors(j) \cup {i \in Extras(d,p) : Anchor(p,i) = j}) # {}}

Low(ds) == {Bar(i) : i \in Uncertain(Join(ds))} \cup
           UNION {{Bar(ds[j].header.pos+i) : i \in DOMAIN ds[j].body} : j \in {k \in DOMAIN ds : ds[k].low}}
DevRisk == Calibrate(FitRows(0,Folds \ {0}))
Masks(w) == {{}, {<<2,"mark">>}, {<<4,"pitch">>}, (DOMAIN BarsOnPaper(w)) \X Categories}
BlindReport(p,d,cal) ==
  [p0 |-> p, draft |-> d, flags |-> Blind(MeasureView(Join(p.docs),{},{}),d,Low(p.docs),cal)]
ScoreReport(w,p,d,flags,mask) ==
  LET a == MeasureView(Join(p.docs),{},{})
      g == MeasureView(Paper(w),{2},{4})
      allowed == AllowedGold(w,p.docs)
      forced == DOMAIN g \ {q[2] : q \in allowed}
      c == Compare(a,g,allowed,mask)
      den == c.eligible \cup forced
      errors == {q[1] : q \in c.A} \cup forced
  IN [cmp |-> c, forced |-> forced, caught |-> Caught(d,g,flags,mask),
      denominator |-> den, errors |-> errors,
      rate |-> IF den = {} THEN Absent ELSE <<Cardinality(errors),Cardinality(den)>>]
=============================================================================
