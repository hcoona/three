------------------------ MODULE CandidateQualification ------------------------
\* A decision on complete native facts, not a scheduler. Edges point from an
\* input to its consumer. "former" has no runnable target in the candidate.
Projects == {"former", "library", "consumer", "new-consumer", "independent"}
Survivors == Projects \ {"former"}
Basis == {<<"former", "library">>, <<"library", "consumer">>}
Candidate == {<<"library", "new-consumer">>}

VARIABLES mode, facts, direct, focus, evidence
vars == <<mode, facts, direct, focus, evidence>>
Paired == [basis |-> "comparison", tree |-> "tested-candidate",
           control |-> "tested-candidate", complete |-> TRUE]
Inputs == {Paired, [Paired EXCEPT !.complete = FALSE], [Paired EXCEPT !.basis = "other-basis"],
           [Paired EXCEPT !.tree = "other-candidate"], [Paired EXCEPT !.control = "other-candidate"]}

Closed(s) == \A edge \in Basis \cup Candidate : edge[1] \in s => edge[2] \in s
Affected == {p \in Survivors : \A s \in SUBSET Projects :
                 (direct \subseteq s /\ Closed(s)) => p \in s}
Selected == IF mode = "full" THEN Survivors ELSE Affected

Checks(p) == IF p = "library" THEN {<<p, "portable">>, <<p, "native">>}
             ELSE {<<p, "test">>}
Prerequisite == <<"tool", "prepare">>
Advisory == <<"project", "advisory">>
Required == UNION {Checks(p) : p \in Selected}
            \cup (IF "library" \in Selected THEN {Prerequisite} ELSE {})
Plan == IF facts = Paired THEN [scope |-> mode, projects |-> Selected, work |-> Required]
        ELSE [scope |-> "unresolved", projects |-> {}, work |-> {}]

\* Subject and occurrence labels keep two different comparisons or two
\* identical reported passes from collapsing at a multi-job join.
Subject == [tree |-> "tested-candidate", basis |-> "comparison", scope |-> Plan]
Receipt(k, outcome, subject, occurrence) ==
    [check |-> k, outcome |-> outcome, subject |-> subject, occurrence |-> occurrence]
Pass(k) == Receipt(k, "passed", Subject, "first")
FocusResults ==
    CASE evidence = "passed" -> {Pass(focus)}
      [] evidence = "other-tree" -> {Receipt(focus, "passed", [Subject EXCEPT !.tree = "other-tree"], "first")}
      [] evidence = "other-basis" -> {Receipt(focus, "passed", [Subject EXCEPT !.basis = "other-basis"], "first")}
      [] evidence = "other-plan" -> {Receipt(focus, "passed", [Subject EXCEPT !.scope.work = {}], "first")}
      [] evidence = "duplicate" -> {Pass(focus), Receipt(focus, "passed", Subject, "second")}
      [] evidence = "conflicting" -> {Pass(focus), Receipt(focus, "failed", Subject, "second")}
      [] evidence = "missing" -> {}
      [] OTHER -> {Receipt(focus, evidence, Subject, "first")}
Results == {Pass(k) : k \in Plan.work \ {focus}}
           \cup (IF focus \in Plan.work \cup {Advisory} THEN FocusResults ELSE {})
Passes == /\ Plan.scope # "unresolved"
          /\ \A k \in Plan.work :
                 LET received == {r \in Results : r.check = k}
                 IN  \E r \in received :
                         received = {r} /\ r.subject = Subject /\ r.outcome = "passed"

Init == /\ mode \in {"selective", "full"}
        /\ facts \in Inputs
        /\ direct \in SUBSET Projects
        /\ focus \in UNION {Checks(p) : p \in Survivors} \cup {Prerequisite, Advisory}
        /\ evidence \in {"passed", "other-tree", "other-basis", "other-plan", "duplicate", "conflicting", "missing",
                         "failed", "skipped", "cancelled", "timed-out", "unknown"}
Spec == Init /\ [][UNCHANGED vars]_vars

\* An independent reading of the example topology, including the surviving
\* consumers of a removed input, an added edge, and a genuinely unrelated unit.
ScopeLaw ==
    LET inherited == direct \cap {"former", "library"} # {}
        expected == (direct \cap Survivors)
                    \cup (IF inherited THEN {"library", "consumer", "new-consumer"} ELSE {})
        projects == IF mode = "full" THEN Survivors ELSE expected
    IN  IF facts = Paired THEN
            /\ Plan.scope = mode /\ Plan.projects = projects
            /\ Plan.work = {<<p, "test">> : p \in projects \ {"library"}}
                 \cup (IF "library" \in projects
                       THEN {<<"library", "portable">>, <<"library", "native">>, Prerequisite}
                       ELSE {})
        ELSE Plan.scope = "unresolved" /\ Plan.work = {}
VerdictLaw == Passes <=> (facts = Paired /\ (focus \notin Required \/ evidence = "passed"))

\* Rejected alternative: infer the obligations from the successful receipts.
GreenReceiptsSuffice ==
    (facts = Paired /\ Results # {} /\
     \A r \in Results : r.subject = Subject /\ r.outcome = "passed" /\
                        {s \in Results : s.check = r.check} = {r}) => Passes
=============================================================================
