------------------------------- MODULE Pilot -------------------------------
EXTENDS Naturals, FiniteSets
CONSTANTS Stages, PriorCounts, Recovery
ASSUME /\ Stages \subseteq {"final","otherStage"} /\ PriorCounts \subseteq 0..3
       /\ Recovery \in {"byLease","callerReceipt"}
VARIABLES stage, used, asked, ready, reads, view, run, fence, other
vars == <<stage,used,asked,ready,reads,view,run,fence,other>>
Workers == {"left","right"}
Entries == {x \in used : x[1] = stage}
Room == Entries = {} /\ Cardinality(used) < 3 /\ (stage = "final" => used = {})
Init ==
  /\ stage \in Stages /\ \E n \in PriorCounts : used = {<<<<"p1","p2","p3">>[i],"prior">> : i \in 1..n}
  /\ asked = {} /\ ready = {} /\ reads = {} /\ view = {} /\ run = "waiting" /\ fence = "free" /\ other = FALSE
Prepare(w) ==
  /\ run = "waiting" /\ Room /\ w \notin asked /\ asked' = asked \cup {w} /\ ready' = ready \cup {w}
  /\ UNCHANGED <<stage,used,reads,view,run,fence,other>>
Issue(w) ==
  /\ w \in ready /\ ready' = ready \ {w}
  /\ IF Room /\ fence = "free" THEN used' = used \cup {<<stage,w>>} /\ fence' = "active"
     ELSE UNCHANGED <<used,fence>>
  /\ UNCHANGED <<stage,asked,reads,view,run,other>>
Deny ==
  /\ run = "waiting" /\ Entries = {} /\ ~Room /\ run' = "denied"
  /\ UNCHANGED <<stage,used,asked,ready,reads,view,fence,other>>
\* Access to protected input, not completion of a score or mere receipt of a lease.
ReadInput(w) ==
  /\ run = "waiting" /\ fence = "active" /\ <<stage,w>> \in used
  /\ reads' = reads \cup {<<stage,w>>} /\ run' = "reading" /\ UNCHANGED <<stage,used,asked,ready,view,fence,other>>
Finish ==
  /\ run = "reading" /\ run' = "finished" /\ UNCHANGED <<stage,used,asked,ready,reads,view,fence,other>>
Interrupt ==
  /\ run \in {"waiting","reading"} /\ fence = "active" /\ run' = "interrupted" /\ fence' = "unknown"
  /\ UNCHANGED <<stage,used,asked,ready,reads,view,other>>
Settle ==
  /\ run = "finished" /\ fence = "active" /\ fence' = "free" /\ UNCHANGED <<stage,used,asked,ready,reads,view,run,other>>
VerifyStopped ==
  /\ fence = "unknown" /\ fence' = "safe" /\ UNCHANGED <<stage,used,asked,ready,reads,view,run,other>>
Reconcile ==
  /\ fence = "safe" /\ Recovery = "byLease" /\ Entries # {} /\ fence' = "free"
  /\ UNCHANGED <<stage,used,asked,ready,reads,view,run,other>>
OtherWork ==
  /\ run = "interrupted" /\ fence = "free" /\ ~other /\ other' = TRUE /\ UNCHANGED <<stage,used,asked,ready,reads,view,run,fence>>
PublishView ==
  /\ view # Entries /\ view' = Entries /\ UNCHANGED <<stage,used,asked,ready,reads,run,fence,other>>
Next == \/ \E w \in Workers : Prepare(w) \/ Issue(w) \/ ReadInput(w)
        \/ Deny \/ Finish \/ Interrupt \/ Settle \/ VerifyStopped \/ Reconcile \/ OtherWork \/ PublishView
        \/ (run \in {"interrupted","denied"} \/ (run = "finished" /\ fence = "free")) /\ UNCHANGED vars
Spec == Init /\ [][Next]_vars
FairSettlement == Spec /\ WF_vars(Settle) /\ WF_vars(Reconcile) /\ WF_vars(OtherWork)
LeaseSafety ==
  /\ Cardinality(used) <= 3 /\ Cardinality(Entries) <= 1 /\ reads \subseteq Entries /\ view \subseteq Entries
  /\ (run \in {"reading","finished"} => reads # {}) /\ (stage = "final" /\ reads # {} => Cardinality(used) = 1)
  /\ (fence \in {"unknown","safe"} => ~other)
BeforeAccess == [][used \subseteq used' /\ reads \subseteq reads' /\ (reads' \ reads) \subseteq used]_vars
Convergence == <>[][FALSE]_vars
NormalSettlement == (run = "finished") ~> (fence = "free")
ResourceProgress == (fence = "safe") ~> other
HandoffProgress == (fence = "safe" /\ reads = {}) ~> other
NoRecoveredContestedRead == ~(asked = Workers /\ ready = {} /\ other /\ fence = "free" /\
                              Cardinality(reads) = 1 /\ reads = used /\ view = {})
=============================================================================
