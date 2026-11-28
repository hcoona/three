---------------------------- MODULE ReadingLaws ----------------------------
EXTENDS ReviewEvidence
CONSTANT Cases
VARIABLES page, p0, mask
values == <<page,p0,mask>>
Init == /\ page \in Cases /\ p0 \in Readings(Cues(page)) /\ mask \in Masks(page)
Spec == Init /\ [][UNCHANGED values]_values

ds == p0.docs
t == Join(ds)
ink == Regions(page)
seen == Sensor(page)
cs == Visible(seen)
draft == Draft(page)
flags == BlindReport(p0,draft,DevRisk).flags
gold == MeasureView(Paper(page),{2},{4})
score == ScoreReport(page,p0,draft,flags,mask)
zones == ReviewZones(p0,DOMAIN draft,flags)
Editions(p) == IF Markers(Join(p.docs)) = {} THEN {p.docs}
               ELSE {Rebind(Corrected(p.docs,m),Initial) : m \in Glyphs \cup {No}}
PossibleMusic(cues) ==
  UNION {{ReadingMusic([r EXCEPT !.docs = e]) : e \in Editions(r)} : r \in Readings(cues)} \ {Absent}
PaperBridge ==
  /\ Join(Visible(ink)) = Paper(page)
  /\ Bridge(ink,seen,p0)
  /\ (OrderedCover(ink,seen) /\ NoSilentLoss(ink,seen) => PaperMusic(Visible(ink)) \in PossibleMusic(cs))
ReviewBoundary ==
  /\ zones.must.events = Markers(Join(Slice(cs,1,Len(ds))))
  /\ zones.suspect \subseteq DOMAIN draft /\ Cardinality(zones.suspect) = Len(draft) \div 5
  /\ BlindReport(p0,<<>>,DevRisk).flags = {}
  /\ ((ReadingMusic(p0) = Absent) <=> (zones.must # NoNeeds))
PrefixReading ==
  /\ \A i \in DOMAIN ds : PrefixOK(Slice(ds,1,i))
  /\ Read(t).tones = [i \in DOMAIN t |-> Tone(t,i)]
  /\ (page = "noisy" => {Join(r.docs)[Beats+1].mark : r \in Readings(cs)} = {No,"#"})
  /\ Len(ds) <= Len(cs) /\ p0.mandatory = (Len(ds)+1)..Len(cs)
  /\ \A j \in DOMAIN cs : j <= Len(ds)+1 =>
       LET prefix == Slice(ds,1,j-1)
           pre == Join(prefix)
       IN /\ (j \in DOMAIN ds => ds[j].header = WrittenHeader(pre))
          /\ \A final \in BOOLEAN :
               LET blocked == IF final = (j=Len(cs)) THEN j \in p0.mandatory ELSE Pick(prefix,cs[j],final,No) = Absent
               IN blocked <=> VisibleConflict(pre,cs[j],final)
ReviewKnowledge ==
  CASE page \in {"reviewSharp","reviewFlat","reviewAndGap"} ->
         /\ {1,2} \subseteq Uncertain(t) /\ 3 \notin Uncertain(t)
         /\ Tone(t,2) = "n" /\ Possible(t,2) = Glyphs /\ Tone(Paper(page),2) \in Possible(t,2)
    [] page = "endingReview" -> {3*Beats,4*Beats+1} \subseteq Uncertain(t)
    [] OTHER -> TRUE
Meaning ==
  p0.mandatory = {} => \A e \in Editions(p0) :
    LET text == Join(e)
        x == Forward(text)
        arrival == 2*Beats+1
        ending == 4*Beats+1
        lastDoc == e[Len(e)]
    IN /\ Admits(e,TRUE) /\ Markers(text) = {} /\ NativeGraph(x) = ScoreGraph(text)
       /\ Importable(x) /\ ScoreGraph(DecodeXML(x)) = ScoreGraph(text)
       /\ (page \in Threaded =>
            /\ DecodeXML(x)[arrival-1].role = (IF text[arrival].role = "tieEnd" THEN "tieStart" ELSE "slurStart")
            /\ Tone(text,arrival) = (IF text[arrival].role = "tieEnd" THEN "#" ELSE "n")
            /\ (page # "endingReview" => Tone(text,arrival+1) = "n")
            /\ Scan(Local(lastDoc.header),lastDoc.body).tones[1] = "deferred"
            /\ Tone(text,ending) = Tone(text,3*Beats) /\ Tone(text,ending+1) = "n")
Twin(w) == CASE w = "clipped" -> "noMark" [] w = "noMark" -> "clipped"
             [] w = "reviewFlat" -> "reviewSharp" [] OTHER -> "reviewFlat"
Reports(cues,d) == {BlindReport(p,d,DevRisk) : p \in Readings(cues)}
Omit(xs,k) == Cat(Slice(xs,1,k-1),Slice(xs,k+1,Len(xs)))
BlindPair ==
  /\ (page \in {"clipped","noMark","reviewFlat","reviewSharp"} =>
       LET w == Twin(page) IN PaperMusic(Visible(Regions(w))) # PaperMusic(Visible(ink)) /\
         Cues(w) = cs /\ Reports(Cues(w),Draft(w)) = Reports(cs,draft))
  /\ \A k \in DOMAIN ink : seen = Omit(ink,k) =>
       LET shorter == Omit(Visible(ink),k)
           fits == \A j \in DOMAIN shorter :
                     ~VisibleConflict(Join(Slice(shorter,1,j-1)),shorter[j],j=Len(shorter))
       IN /\ Complete(p0) <=> fits
          /\ (fits => shorter = cs /\ Reports(shorter,draft) = Reports(cs,draft) /\
               ReadingMusic(p0) = PaperMusic(shorter) /\ PaperMusic(shorter) # PaperMusic(Visible(ink)))
Calibration ==
  \A target \in Folds \cup {4}, training \in {Folds} \cup {Folds \ {g} : g \in Folds} :
    Eligible(FitRows(target,training),target) <=> target \notin training
Accounting ==
  /\ score.cmp.A \cap mask = {}
  /\ score.forced \subseteq score.cmp.missing /\ score.errors \subseteq score.denominator
  /\ ((score.rate = Absent) <=> (score.denominator = {}))
  /\ score.cmp.extra = DOMAIN MeasureView(t,{},{}) \ {q[1] : q \in score.cmp.paired}
  /\ LET c == score.cmp
     IN CASE page = "miss" ->
               LET dg == Align(draft,gold,AllPairs(draft,gold))
               IN Left(dg,3) = {} /\ Extras(draft,dg) = {} /\ flags = {2} /\
                  score.caught = (IF <<3,"structure">> \in mask THEN {} ELSE {3})
          [] page = "duplicate" -> 2 \in c.extra /\ 2 \in score.forced /\ (mask = {} => flags = {3} /\ score.caught = {2})
          [] page = "merged" -> {2,3} \subseteq c.extra /\ {2,3} \subseteq score.forced
          [] page = "split" -> 2 \in c.extra /\ 2 \in score.forced
          [] page \in {"clipped","noMark","exact"} ->
               c.missing = {} /\ c.extra = {} /\ c.B = {2} /\ c.C = {4} /\
               c.rawA = (IF page = "clipped" THEN {<<2,"mark">>} ELSE {}) /\ c.A = c.rawA \ mask
          [] page = "drift" -> 2 \in score.forced /\ Len(ds) = Len(gold)-1
          [] OTHER -> TRUE

\* Counterexamples compare values of the same invented page, not phases of a job.
PrintedEdit == Corrected(ds,Paper(page)[CHOOSE i \in Markers(t) : TRUE].mark)
LocalReviewCertificateSurvives == Links(PrintedEdit)
SameHeadersSameEnding == Links(PrintedEdit) => Tone(Join(PrintedEdit),4*Beats+1) = Tone(t,4*Beats+1)
StructurallyCertifiedInk ==
  LET dg == Align(draft,gold,AllPairs(draft,gold))
      x == Forward(t)
  IN (mask = {} /\ Complete(p0) /\ Importable(x) /\ ScoreGraph(DecodeXML(x)) = ScoreGraph(t)) =>
       \A q \in score.cmp.paired :
         (Left(dg,q[2]) \cap zones.unflagged # {} /\ q[2] \notin score.caught) =>
           ReadingMusic(p0)[q[1]] = PaperMusic(Visible(ink))[q[2]]
OrdinalFlagsTransfer == score.caught = flags \cap {q[1] : q \in Cells(draft,gold,Align(draft,gold,AllPairs(draft,gold))) \ mask}
=============================================================================
