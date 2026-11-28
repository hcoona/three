--------------------------- MODULE CiSelectionOracle ---------------------------
\* Compare the runnable projects before any native executor starts. The
\* component model supplies the graph, obligation owners and scope decision.
EXTENDS Sequences, TLC
VARIABLE seed, coverage, issue
vars == <<seed, coverage, issue>>
CI == INSTANCE CiComponentComposition WITH ctx <- "unpriv", mode <- coverage,
    planAnomaly <- issue, directNodes <- IF seed = "none" THEN {} ELSE {seed},
    focus <- <<"library", "native">>, fault <- "passed"
Start ==
    /\ coverage \in {"selective", "full"}
    /\ \/ /\ issue = "none" /\ seed \in {"none", "former", "library", "bridge", "independent"}
       \/ /\ issue \in {"inert-path", "unclaimed-path", "prov-b-incomplete"} /\ seed = "none"
Idle == UNCHANGED vars
OracleSpec == Start /\ [][Idle]_vars
RECURSIVE Names(_, _)
Names(order, chosen) ==
    IF order = <<>> THEN ""
    ELSE LET prefix == IF Head(order) \in chosen THEN Head(order) \o ";" ELSE ""
         IN prefix \o Names(Tail(order), chosen)
Export == PrintT("WD-SELECT|" \o coverage \o ":" \o issue \o ":" \o seed \o "|"
    \o CI!PlanEnvelope.scope \o "|"
    \o Names(<<"consumer", "independent", "library", "new-consumer">>, CI!PlanEnvelope.projects))
=============================================================================
