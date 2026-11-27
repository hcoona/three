----------------------- MODULE CiComponentComposition -----------------------
\* White-box CI component & interface composition:
\*   Multiple LeftPort fact providers -> Core.ComposeCiPlan ->
\*   PlanEnvelope -> partitioned GroupExecutors -> Engine two-level join.
\* Zero topology, check, survival, or partition constants exist outside the
\* self-contained LeftFactEnvelopes delivered to ComposeCiPlan.
EXTENDS DeliveryPortAlgebra

Nodes == {"former", "bridge", "library", "consumer", "new-consumer", "independent"}
Providers == {"prov-a", "prov-b"}
PathOfNode(n) == <<"path", n>>
InertPath == <<"path", "inert">>
UnclaimedPath == <<"path", "unclaimed">>

ProvCandidateNodes(m) ==
    IF m = "prov-a" THEN {"library", "consumer"} ELSE {"bridge", "new-consumer", "independent"}

ProvOwnedPaths(m) ==
    {PathOfNode(n) : n \in IF m = "prov-a" THEN {"former", "library", "consumer"}
                           ELSE {"bridge", "new-consumer", "independent"}}

RawBasisEdges(m) ==
    IF m = "prov-a" THEN {<<"former", "library">>, <<"library", "consumer">>}
    ELSE {<<"library", "bridge">>}

RawCandEdges(m) == IF m = "prov-a" THEN {} ELSE {<<"bridge", "new-consumer">>}

Prerequisite == <<"tool", "prepare">>
Advisory == <<"project", "advisory">>
SpecEntry(chkId, reqFlag, part) == [id |-> chkId, required |-> reqFlag, partition |-> part]

ProvNodeChecks(m, u) ==
    IF m = "prov-a" THEN
        CASE u = "library"  -> {SpecEntry(<<"library", "portable">>, TRUE, "grp-core"),
                                SpecEntry(<<"library", "native">>, TRUE, "grp-ext"),
                                SpecEntry(Prerequisite, TRUE, "grp-core")}
          [] u = "consumer" -> {SpecEntry(<<"consumer", "test">>, TRUE, "grp-core")}
          [] OTHER          -> {}
    ELSE
        CASE u = "new-consumer" -> {SpecEntry(<<"new-consumer", "test">>, TRUE, "grp-ext")}
          [] u = "independent"  -> {SpecEntry(<<"independent", "test">>, TRUE, "grp-core")}
          [] OTHER              -> {}

AllChecks ==
    {<<"library", "portable">>, <<"library", "native">>, Prerequisite,
     <<"consumer", "test">>, <<"new-consumer", "test">>, <<"independent", "test">>, Advisory}

ExpectedSubject == [basis |-> "comparison", tree |-> "tested-candidate", control |-> "tested-candidate"]

PlanAnomalies == {"none", "inert-path", "unclaimed-path", "prov-a-incomplete",
                  "prov-b-incomplete", "prov-b-wrong-subject", "missing-provider",
                  "substituted-provider", "partition-conflict"}

BoundaryFaults == {"passed", "other-tree", "other-basis", "other-plan",
                   "duplicate", "conflicting", "missing",
                   "failed", "skipped", "cancelled", "timed-out", "unknown",
                   "missing-group", "duplicate-group", "extra-group", "misplaced-group"}

VARIABLES ctx, mode, planAnomaly, directNodes, focus, fault
vars == <<ctx, mode, planAnomaly, directNodes, focus, fault>>

ChangedPaths ==
    {PathOfNode(n) : n \in directNodes}
    \cup (CASE planAnomaly = "inert-path"     -> {InertPath}
            [] planAnomaly = "unclaimed-path" -> {UnclaimedPath}
            [] OTHER                          -> {})

EmitLeftEnvelope(m) ==
    LET subj == IF m = "prov-b" /\ planAnomaly = "prov-b-wrong-subject"
                THEN [ExpectedSubject EXCEPT !.tree = "other-candidate"] ELSE ExpectedSubject
        ok == ~((m = "prov-a" /\ planAnomaly = "prov-a-incomplete") \/
                (m = "prov-b" /\ planAnomaly = "prov-b-incomplete"))
        cNodes == IF ok THEN ProvCandidateNodes(m) ELSE {}
        owned == ProvOwnedPaths(m)
        acct == IF ~ok THEN {}
                ELSE ChangedPaths \cap (owned \cup (IF m = "prov-b" THEN {InertPath} ELSE {}))
        adv == IF ~ok THEN {}
               ELSE IF m = "prov-a" THEN {SpecEntry(Advisory, FALSE, "grp-core")}
               ELSE IF planAnomaly = "partition-conflict"
                    THEN {SpecEntry(<<"library", "native">>, FALSE, "grp-core")}
               ELSE {}
    IN  [provider       |-> IF m = "prov-b" /\ planAnomaly = "substituted-provider" THEN "prov-rogue" ELSE m,
         subject        |-> subj,
         complete       |-> ok,
         pathConsumers  |-> [p \in ChangedPaths |-> IF ok /\ p \in owned THEN {p[2]} ELSE {}],
         accountedPaths |-> acct,
         basisEdges     |-> IF ok THEN RawBasisEdges(m) ELSE {},
         candEdges      |-> IF ok THEN RawCandEdges(m) ELSE {},
         candidateNodes |-> cNodes,
         nodeChecks     |-> [u \in cNodes |-> ProvNodeChecks(m, u)],
         advisoryChecks |-> adv,
         baselineGroups |-> IF m = "prov-a" THEN {"grp-core"} ELSE {}]

DeliveredProviders == IF planAnomaly = "missing-provider" THEN {"prov-a"} ELSE Providers
LeftEnvelopes == [m \in DeliveredProviders |-> EmitLeftEnvelope(m)]
PlanEnvelope == ComposeCiPlan(mode, ChangedPaths, ExpectedSubject, Providers, LeftEnvelopes)

PlanSubject(pEnv) ==
    [tree |-> "tested-candidate", basis |-> "comparison",
     scope |-> [scope |-> pEnv.scope, projects |-> pEnv.projects, work |-> pEnv.work]]

MakeReceipt(k, outcome, subj, occ) == [check |-> k, outcome |-> outcome, subject |-> subj, occurrence |-> occ]

FocusReceiptsFor(pEnv) ==
    LET subj == PlanSubject(pEnv)
        pass1 == MakeReceipt(focus, "passed", subj, "first")
    IN  CASE fault = "passed"      -> {pass1}
          [] fault = "other-tree"  -> {MakeReceipt(focus, "passed", [subj EXCEPT !.tree = "other-tree"], "first")}
          [] fault = "other-basis" -> {MakeReceipt(focus, "passed", [subj EXCEPT !.basis = "other-basis"], "first")}
          [] fault = "other-plan"  -> {MakeReceipt(focus, "passed", [subj EXCEPT !.scope.work = {}], "first")}
          [] fault = "duplicate"   -> {pass1, MakeReceipt(focus, "passed", subj, "second")}
          [] fault = "conflicting" -> {pass1, MakeReceipt(focus, "failed", subj, "second")}
          [] fault = "missing"     -> {}
          [] fault \in {"missing-group", "duplicate-group", "extra-group", "misplaced-group"} -> {pass1}
          [] OTHER                 -> {MakeReceipt(focus, fault, subj, "first")}

CanMisplaceFocus(pEnv) == focus \in DOMAIN pEnv.partitionMap /\ "grp-ext" \in pEnv.groups

ReceiptsInPartition(pEnv, g) ==
    LET subj == PlanSubject(pEnv)
        other == IF g = "grp-core" THEN "grp-ext" ELSE "grp-core"
        allowed == AllowedChecksInGroup(pEnv, g)
        normal == {MakeReceipt(k, "passed", subj, "first") : k \in (allowed \ pEnv.advisory) \ {focus}}
                  \cup (IF focus \in allowed THEN FocusReceiptsFor(pEnv) ELSE {})
    IN  IF fault # "misplaced-group" \/ ~CanMisplaceFocus(pEnv) THEN normal
        ELSE IF pEnv.partitionMap[focus] = g THEN normal \ FocusReceiptsFor(pEnv)
        ELSE IF pEnv.partitionMap[focus] = other THEN normal \cup FocusReceiptsFor(pEnv)
        ELSE normal

EmitGroupEnvelopes(pEnv) ==
    IF pEnv.scope = "unresolved" THEN {}
    ELSE LET subj == PlanSubject(pEnv)
             base == {[group |-> g, subject |-> subj, occurrence |-> "first",
                       receipts |-> ReceiptsInPartition(pEnv, g)] : g \in pEnv.groups}
             drop == IF "grp-ext" \in pEnv.groups THEN "grp-ext" ELSE "grp-core"
         IN  CASE fault = "missing-group"   -> {ge \in base : ge.group # drop}
               [] fault = "duplicate-group" -> base \cup {[group |-> "grp-core", subject |-> subj,
                                                           occurrence |-> "second",
                                                           receipts |-> ReceiptsInPartition(pEnv, "grp-core")]}
               [] fault = "extra-group"     -> base \cup {[group |-> "grp-rogue", subject |-> subj,
                                                           occurrence |-> "first", receipts |-> {}]}
               [] OTHER                     -> base

GroupEnvelopes == EmitGroupEnvelopes(PlanEnvelope)
ProducerJoinValid(pEnv, gEnvs) ==
    pEnv.scope # "unresolved" /\ ValidProducerPartitionJoin(pEnv.groups, PlanSubject(pEnv), pEnv, gEnvs)
CheckJoinValid(pEnv, gEnvs) == ValidRequiredCheckJoin(pEnv.work, PlanSubject(pEnv), gEnvs)
Verdict == ProducerJoinValid(PlanEnvelope, GroupEnvelopes) /\ CheckJoinValid(PlanEnvelope, GroupEnvelopes)

Init ==
    /\ ctx = "unpriv"
    /\ mode \in {"selective", "full"}
    /\ planAnomaly \in PlanAnomalies
    /\ directNodes \in SUBSET Nodes
    /\ focus = Prerequisite
    /\ fault = "passed"

ExecuteAndJoin(f, flt) ==
    /\ ctx = "unpriv" /\ ctx' = "verify"
    /\ focus' = f /\ fault' = flt
    /\ UNCHANGED <<mode, planAnomaly, directNodes>>

Next ==
    \/ (ctx = "unpriv" /\ \E f \in AllChecks, flt \in BoundaryFaults : ExecuteAndJoin(f, flt))
    \/ (ctx = "verify" /\ UNCHANGED vars)

Spec == Init /\ [][Next]_vars

\* ---------------------------------------------------------------------------
\* Refinement mapping to CandidateQualification (#1948) & invariants
\* ---------------------------------------------------------------------------
CqPaired == [basis |-> "comparison", tree |-> "tested-candidate",
             control |-> "tested-candidate", complete |-> TRUE]
CqFacts ==
    CASE planAnomaly \in {"none", "inert-path"} -> CqPaired
      [] planAnomaly = "prov-b-wrong-subject"   -> [CqPaired EXCEPT !.tree = "other-candidate"]
      [] OTHER                                  -> [CqPaired EXCEPT !.complete = FALSE]
CqDirect ==
    (directNodes \cap (Nodes \ {"bridge"}))
    \cup (IF "bridge" \in directNodes THEN {"new-consumer"} ELSE {})
CqEvidence ==
    IF fault \in {"missing-group", "duplicate-group", "extra-group", "misplaced-group"}
    THEN "passed" ELSE fault

CQ == INSTANCE CandidateQualification WITH
    mode <- mode, facts <- CqFacts, direct <- CqDirect, focus <- focus, evidence <- CqEvidence

CiCapabilityIsolation ==
    /\ ~AnyWriteCapIn(ctx)
    /\ (ctx = "unpriv" => LeftPortAllowedIn(ctx))
    /\ (ctx = "verify" => ~LeftPortAllowedIn(ctx))

MultiProviderSoundness ==
    LET ok == planAnomaly \in {"none", "inert-path"}
    IN  /\ (PlanEnvelope.scope # "unresolved" <=> ok)
        /\ (~ok => PlanEnvelope.projects = {} /\ PlanEnvelope.work = {} /\ PlanEnvelope.groups = {})
        /\ (ok => "grp-core" \in PlanEnvelope.groups)
        /\ (planAnomaly = "inert-path" /\ directNodes = {} /\ mode = "selective" =>
              PlanEnvelope.scope = "selective" /\ PlanEnvelope.projects = {} /\ PlanEnvelope.work = {})

TwoLevelJoinDiscipline ==
    ctx = "verify" =>
      LET ok == planAnomaly \in {"none", "inert-path"}
          misplaced == fault = "misplaced-group" /\ CanMisplaceFocus(PlanEnvelope)
          grpFault == fault \in {"missing-group", "duplicate-group", "extra-group"} \/ misplaced
      IN  /\ (grpFault /\ ok => ~ProducerJoinValid(PlanEnvelope, GroupEnvelopes))
          /\ (~ProducerJoinValid(PlanEnvelope, GroupEnvelopes) => ~Verdict)
          /\ (Verdict <=> (ok /\ ~grpFault /\ (focus \notin PlanEnvelope.work \/ fault \in {"passed", "misplaced-group"})))

RefinesCandidateQualification ==
    ctx = "verify" =>
      /\ CQ!Init /\ CQ!ScopeLaw /\ CQ!VerdictLaw
      /\ PlanEnvelope.scope = CQ!Plan.scope
      /\ PlanEnvelope.projects = CQ!Plan.projects
      /\ PlanEnvelope.work = CQ!Plan.work
      /\ (ProducerJoinValid(PlanEnvelope, GroupEnvelopes) => (Verdict <=> CQ!Passes))
      /\ (~ProducerJoinValid(PlanEnvelope, GroupEnvelopes) => ~Verdict)

EndpointLocalRunnableOnly(m, edges) ==
    LET runnable == {u \in LeftEnvelopes[m].candidateNodes :
                       {sp \in LeftEnvelopes[m].nodeChecks[u] : sp.required} # {}}
    IN  {e \in edges : e[2] \in runnable}

EndpointLocalRunnableClosureSuffices ==
    PlanEnvelope.scope = "selective" =>
      LET naive == [m \in Providers |->
                      [LeftEnvelopes[m] EXCEPT
                         !.basisEdges = EndpointLocalRunnableOnly(m, LeftEnvelopes[m].basisEdges),
                         !.candEdges  = EndpointLocalRunnableOnly(m, LeftEnvelopes[m].candEdges)]]
      IN  ComposeCiPlan(mode, ChangedPaths, ExpectedSubject, Providers, naive).projects = PlanEnvelope.projects

CheckOnlyJoinSuffices ==
    (ctx = "verify" /\ PlanEnvelope.scope # "unresolved" /\ CheckJoinValid(PlanEnvelope, GroupEnvelopes)) => Verdict
=============================================================================
