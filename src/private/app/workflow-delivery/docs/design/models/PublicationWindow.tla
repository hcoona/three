-------------------------- MODULE PublicationWindow --------------------------
EXTENDS ReleaseRules, Naturals, Sequences, FiniteSets

CONSTANT Surface
ASSUME Surface \in {"python", "github-release"}
Order == IF Surface = "python" THEN <<"wheel", "sdist">>
         ELSE <<"create-release", "installer", "portable">>
Ops == {Order[i] : i \in 1..Len(Order)}
Blank(value) == [op \in Ops |-> value]
Reads ==
    {r \in [Ops -> {"missing", "exact", "conflict", "unknown"}] :
        IF Surface = "github-release" THEN
            r["create-release"] = "missing" => r = Blank("missing")
        ELSE TRUE}
Missing(r) == {op \in Ops : r[op] = "missing"}
Has(r, value) == \E op \in Ops : r[op] = value
StateOf(r) ==
    IF Has(r, "conflict") THEN "conflict"
    ELSE IF Has(r, "unknown") THEN "unknown"
    ELSE IF Missing(r) # {} THEN "missing" ELSE "served-originals"

\* Each window starts with a new, independently qualified live request on an
\* admitted byte-verified route. ReleaseDecisions checks the entry contract.
Subject(id) == [execution |-> id, revision |-> "commit-a", inputs |-> "frozen-inputs",
                unit |-> "unit-a", coordinate |-> Surface, destination |-> "originals",
                channel |-> "official", mode |-> "live"]
Eligible == {"official"}
VARIABLES before, refreshed, phase, pending, commands, receipt, finalRead, previous
vars == <<before, refreshed, phase, pending, commands, receipt, finalRead, previous>>
Request == Subject(Len(previous) + 1)
RouteFor(r) == Route(Request, TRUE, Eligible, StateOf(r))
RouteHere == RouteFor(before)
EntryPhase(r) == IF RouteFor(r) = "publisher" THEN "gate" ELSE "verify"
EntryReceipt(r) == IF RouteFor(r) = "publisher" THEN "awaited" ELSE "not-required"
Reviewed == Missing(before)
Begin ==
    /\ before \in Reads /\ refreshed = Blank("unknown") /\ finalRead = Blank("unknown")
    /\ phase = EntryPhase(before) /\ receipt = EntryReceipt(before)
    /\ pending = {} /\ commands = <<>>
Init == previous = <<>> /\ Begin

\* The two reads are separate inputs: approval does not freeze the registry.
InsideCeiling(r) == Missing(r) \subseteq Reviewed
Grant(r) ==
    LET access == PublisherAccess(Request, RouteHere, TRUE, Eligible, TRUE, "isolated-publisher")
        permitted == CanInvoke(Request, access, StateOf(r), "first") /\ InsideCeiling(r)
        remaining == IF permitted THEN Missing(r) ELSE {}
    IN  /\ phase = "gate" /\ refreshed' = r /\ pending' = remaining
        /\ phase' = IF remaining = {} THEN "verify" ELSE "write"
        /\ receipt' = "present" /\ UNCHANGED <<before, commands, finalRead, previous>>

NoStart == /\ phase = "gate" /\ phase' = "verify" /\ receipt' = "known-nonstart"
           /\ UNCHANGED <<before, refreshed, pending, commands, finalRead, previous>>
LoseResult ==
    /\ RouteHere = "publisher" /\ phase \in {"gate", "write", "verify"}
    /\ receipt \in {"awaited", "present"}
    /\ phase' = "verify" /\ receipt' = "lost" /\ pending' = {}
    /\ UNCHANGED <<before, refreshed, commands, finalRead, previous>>

FirstRemaining ==
    Order[CHOOSE i \in 1..Len(Order) :
        Order[i] \in pending /\ \A j \in 1..(i-1) : Order[j] \notin pending]
Invoke(result) ==
    /\ phase = "write"
    /\ commands' = Append(commands, <<FirstRemaining, result>>)
    /\ pending' = IF result = "acknowledged" THEN pending \ {FirstRemaining} ELSE {}
    /\ phase' = IF pending' = {} THEN "verify" ELSE "write"
    /\ UNCHANGED <<before, refreshed, receipt, finalRead, previous>>
Verify(r) == /\ phase = "verify" /\ phase' = "done" /\ finalRead' = r
             /\ UNCHANGED <<before, refreshed, pending, commands, receipt, previous>>

Issued(log) == {log[i][1] : i \in 1..Len(log)}
WithResult(log, outcome) == {op \in Ops : \E i \in 1..Len(log) : log[i] = <<op, outcome>>}
Returned == IF receipt = "present" THEN commands ELSE <<>>
FinalEvidence ==
    IF StateOf(finalRead) # "missing" THEN StateOf(finalRead)
    ELSE IF Missing(finalRead) \cap Issued(Returned) # {} THEN "unknown" ELSE "established-incomplete"
Report ==
    [execution |-> Len(previous) + 1, initial |-> before, observed |-> finalRead,
     publisherRead |-> IF receipt = "present" THEN refreshed ELSE Blank("unknown"),
     publisherRecord |-> receipt,
     satisfaction |-> Conclusion("originals", RouteHere, TRUE, receipt, "verifier", FinalEvidence),
     attempted |-> Attempted(RouteHere, receipt, Returned),
     outcomesAvailable |-> receipt # "lost", outcomes |-> Returned,
     uncertainCalls |-> IF receipt = "lost" THEN Reviewed
                        ELSE Issued(Returned) \ WithResult(Returned, "acknowledged")]

\* Compose two windows only at the boundary of interest. The saved report is
\* observer history for checking non-revision, not input to the new publisher.
\* The native rebuild must reproduce even the unpublished sdist.
NewManualDispatch(reproducesSet) ==
    /\ Surface = "python" /\ phase = "done" /\ previous = <<>> /\ receipt = "present"
    /\ before = Blank("missing") /\ refreshed = before
    /\ commands = <<<<"wheel", "ambiguous">>>> /\ finalRead = Blank("missing")
    /\ FreshRecoveryOf(Request, Subject(2), "manual-dispatch", reproducesSet)
    /\ previous' = <<Report>> /\ before' \in Reads
    /\ phase' = EntryPhase(before') /\ receipt' = EntryReceipt(before')
    /\ refreshed' = Blank("unknown") /\ finalRead' = Blank("unknown")
    /\ pending' = {} /\ commands' = <<>>
Next == \/ \E r \in Reads : Grant(r) \/ Verify(r)
        \/ NoStart \/ LoseResult
        \/ \E outcome \in {"acknowledged", "failed", "ambiguous"} : Invoke(outcome)
        \/ \E reproducesSet \in BOOLEAN : NewManualDispatch(reproducesSet)
        \/ (phase = "done" /\ UNCHANGED vars)
Spec == Init /\ [][Next]_vars

WriteDiscipline ==
    /\ Issued(commands) \subseteq Reviewed /\ Issued(commands) \subseteq Missing(refreshed)
    /\ Len(commands) = Cardinality(Issued(commands))
    /\ (commands # <<>> => RouteHere = "publisher" /\ StateOf(refreshed) = "missing" /\
                           InsideCeiling(refreshed))
    /\ \A i \in 1..(Len(commands)-1) : commands[i][2] = "acknowledged"
    /\ \A i,j \in 1..Len(Order) :
          (i < j /\ Order[i] \in Missing(refreshed) /\ Order[j] \in Issued(commands)) =>
              \E a,b \in 1..Len(commands) :
                  a < b /\ commands[a][1] = Order[i] /\ commands[b][1] = Order[j]

\* Check permitted progress, not just prohibitions. Under favorable inputs,
\* all still-needed operations remain available in order until one fails.
Progress ==
    /\ ((receipt = "present" /\ StateOf(refreshed) = "missing" /\ InsideCeiling(refreshed) /\
         Issued(commands) = WithResult(commands, "acknowledged")) =>
           pending = Missing(refreshed) \ Issued(commands))
    /\ (phase = "gate" => ENABLED Grant(before))
    /\ ~ENABLED NewManualDispatch(FALSE)
    /\ (phase = "write" <=> pending # {})
    /\ (phase = "write" => ENABLED Invoke("acknowledged"))
    /\ (phase = "verify" => ENABLED Verify(Blank("exact")))
    /\ (phase = "done" /\ finalRead = Blank("exact") =>
         (Report.satisfaction = "satisfied" <=>
          (RouteHere = "verify-without-write" \/ (RouteHere = "publisher" /\ receipt = "present"))))

EvidenceDiscipline ==
    phase = "done" =>
      /\ (Report.satisfaction = "satisfied" => finalRead = Blank("exact"))
      /\ (receipt = "lost" => Report.attempted = "unknown" /\ ~Report.outcomesAvailable /\
                               Report.outcomes = <<>> /\
                               Report.satisfaction = IF Has(finalRead, "conflict") THEN "unsatisfied" ELSE "unknown")
      /\ (receipt = "known-nonstart" => Report.attempted = "no" /\ commands = <<>>)
      /\ (StateOf(before) = "conflict" => Report.satisfaction = "unsatisfied" /\ Report.attempted = "no")
      /\ (StateOf(before) = "unknown" => Report.satisfaction # "satisfied" /\ Report.attempted = "no")
      /\ (receipt = "present" => Report.outcomes = commands /\
                                (Report.attempted = "yes" <=> commands # <<>>))
      /\ (StateOf(finalRead) = "missing" /\ receipt = "present" /\
          Missing(finalRead) \cap WithResult(commands, "acknowledged") # {} =>
              Report.satisfaction = "unknown")
HistoryDiscipline ==
    Len(previous) = 1 =>
      /\ previous[1].execution = 1 /\ previous[1].satisfaction = "unknown"
      /\ previous[1].observed = Blank("missing") /\ previous[1].attempted = "yes"
      /\ previous[1].outcomesAvailable /\ previous[1].outcomes = <<<<"wheel", "ambiguous">>>>
      /\ previous[1].uncertainCalls = {"wheel"} /\ Report.execution = 2

\* Deliberately rejected alternatives in separate configurations.
CountBoundsAuthority ==
    (receipt = "present" /\ StateOf(refreshed) = "missing" /\
     Cardinality(Missing(refreshed)) <= Cardinality(Reviewed)) => InsideCeiling(refreshed)
NoSuccessfulSubsetRecovery ==
    ~(Len(previous) = 1 /\ phase = "done" /\ Reviewed = {"sdist"} /\
      Report.outcomes = <<<<"sdist", "acknowledged">>>> /\ Report.satisfaction = "satisfied")
=============================================================================
