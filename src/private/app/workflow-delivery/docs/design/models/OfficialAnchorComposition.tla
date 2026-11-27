---------------------- MODULE OfficialAnchorComposition ----------------------
\* Modular narrow-waist composition for dual-RightPort Official releases
\* (WD-RET-004) across "unpriv" -> "isolated-pub-primary" -> "anchor-gate" ->
\* "isolated-pub-anchor" -> "verify" -> "done":
\* 1. Single-port poset stepping and session wiping are discharged once in
\*    ReleaseComponentProtocol (PubReceiptGuarantee -> IsValidSealedPubReceipt).
\* 2. Observation maps and sealed publisher receipts are reduced via exact
\*    congruence quotients (QuotientObsMaps, QuotientPubReceipts), proved at
\*    startup by ASSUME QuotientBisimulationSound against full ValidObsMaps.
\* 3. Verifies disjoint write capabilities, post-gate refresh & post-ambiguous
\*    PrimaryPermitsAnchor, independent multi-job receipt loss, role-prefixed
\*    operation namespaces (<<"primary", op>> vs <<"anchor", op>>), and 2-run
\*    recovery across canonical non-satisfied Official executions.
EXTENDS DeliveryPortAlgebra

UnitArtifacts == {"art1", "art2", "art3"}

ByteSingleRegistry    == SingleOpProfile(Channels, Channels, "unpriv-always", "served-bytes", "op1", {"art1"})
LiveReadStoreSubset   == SingleOpProfile({"official"}, Channels, "unpriv-live-only", "store-accepted", "op1", {"art1"})
GatedReadBundledStore == SingleOpProfile({"official"}, Channels, "publisher-only", "store-accepted", "op1", {"art2", "art3"})
BlindReceiptStore     == SingleOpProfile({"official"}, Channels, "blind-receipt", "store-accepted", "op1", {"art3"})
ByteOrderedPair ==
    MakeRightProfile(Channels, Channels, "unpriv-always", "served-bytes", <<"op1", "op2">>,
                     [op \in {"op1", "op2"} |-> {}],
                     [op \in {"op1", "op2"} |-> IF op = "op1" THEN {"art1"} ELSE {"art2"}],
                     {{"op1"}, {"op2"}, {"op1", "op2"}})

OfficialAnchorPrimaryProfiles ==
    {ByteSingleRegistry, ByteOrderedPair, LiveReadStoreSubset, GatedReadBundledStore, BlindReceiptStore}

\* Anchor RightPort reuses local IDs "op0", "op1", "op2" (overlapping primary
\* "op1"/"op2") to check role-scoped namespace disjointness on every state.
AnchorProfileFor(primaryProf) ==
    LET tArts == TargetArtifactsOf(primaryProf)
    IN  IF Cardinality(tArts) = 1 THEN
            MakeRightProfile({"official"}, {"official"}, "unpriv-always", "served-bytes", <<"op0", "op1">>,
                             [op \in {"op0", "op1"} |-> IF op = "op1" THEN {"op0"} ELSE {}],
                             [op \in {"op0", "op1"} |-> IF op = "op0" THEN {} ELSE tArts],
                             {{"op1"}, {"op0", "op1"}})
        ELSE
            LET a1 == IF "art1" \in tArts THEN "art1" ELSE "art2"
                a2 == CHOOSE a \in tArts \ {a1} : TRUE
            IN  MakeRightProfile({"official"}, {"official"}, "unpriv-always", "served-bytes", <<"op0", "op1", "op2">>,
                                 [op \in {"op0", "op1", "op2"} |-> IF op = "op0" THEN {} ELSE {"op0"}],
                                 [op \in {"op0", "op1", "op2"} |-> CASE op = "op0" -> {} [] op = "op1" -> {a1} [] OTHER -> {a2}],
                                 {{"op1"}, {"op2"}, {"op1", "op2"}, {"op0", "op1", "op2"}})

\* Mechanical Quotient Bisimulation Theorem: every full-space ValidObsMaps
\* observation and sealed publisher receipt has a behavioral twin in
\* QuotientObsMaps / QuotientPubReceipts with identical PrimaryPermitsAnchor,
\* satisfaction, attempted, outcomesAvailable, and uncertainCalls.
PortQuotientBisimulates(p, destId) ==
    LET fullObs == ValidObsMaps(p)
        qObsSet == QuotientObsMaps(p)
        unobs   == UnobservedMap(p)
        verDom  == fullObs \cup {unobs}
        qOfMap  == [m \in verDom |-> IF m = unobs THEN unobs
                                     ELSE CHOOSE q \in qObsSet : ObsKernel(p, q) = ObsKernel(p, m)]
        req     == [execution |-> 1, unit |-> "unit-a", revision |-> "commit-a", inputs |-> "frozen-inputs",
                    destination |-> destId, channel |-> "official", mode |-> "live", clearance |-> "first"]
        lprep   == [unitSubject |-> LeftUnitSubject(req), origin |-> "release", complete |-> TRUE, clean |-> TRUE,
                    nativeIdentity |-> [pkgName |-> "canonical-a", version |-> "1.2.0", notesReady |-> TRUE],
                    originals |-> [a \in UnitArtifacts |-> <<a, "d">>]]
        tgt     == BindTargetEnvelope(p, TRUE, destId, req, lprep, req.revision, UnitArtifacts)
        inits   == IF BeforeGateMode(p, "live") = "unobserved" THEN {unobs}
                   ELSE {m \in fullObs : AggregateObsState(p, m) = "missing"}
        VerEnv(obs) == [present |-> ~IsUnobservedMap(p, obs), subject |-> req, coordinate |-> tgt.coordinate, obs |-> obs]
    IN  /\ IsValidRightProfile(p, UnitArtifacts)
        /\ qObsSet \subseteq fullObs
        /\ {ObsKernel(p, m) : m \in qObsSet} = {ObsKernel(p, m) : m \in fullObs}
        /\ \A initM \in inits :
             LET qInit  == qOfMap[initM]
                 rtFull == [subject |-> req, coordinate |-> tgt.coordinate, route |-> "publisher",
                            initialObs |-> initM, reviewed |-> ReviewedCeiling(p, "publisher", initM)]
                 rtQuot == [subject |-> req, coordinate |-> tgt.coordinate, route |-> "publisher",
                            initialObs |-> qInit, reviewed |-> ReviewedCeiling(p, "publisher", qInit)]
                 qPool  == QuotientPubReceipts(p, req, tgt, rtQuot)
             IN  /\ \A qRec \in qPool : IsValidSealedPubReceipt(p, req, tgt, rtQuot, qRec)
                 /\ \A fRec \in SealedPubReceiptsFromPool(p, req, tgt, rtFull, fullObs) :
                      \E qRec \in qPool :
                        /\ PrimaryPermitsAnchor(p, req, tgt, rtQuot, qRec) =
                             PrimaryPermitsAnchor(p, req, tgt, rtFull, fRec)
                        /\ \A vObs \in verDom, ret \in BOOLEAN :
                             LET fRep == CompleteFromEnvelopes(p, req, tgt, rtFull, fRec, VerEnv(vObs), ret)
                                 qRep == CompleteFromEnvelopes(p, req, tgt, rtQuot, qRec, VerEnv(qOfMap[vObs]), ret)
                             IN  /\ qRep.satisfaction      = fRep.satisfaction
                                 /\ qRep.attempted         = fRep.attempted
                                 /\ qRep.outcomesAvailable = fRep.outcomesAvailable
                                 /\ qRep.uncertainCalls    = fRep.uncertainCalls
                                 /\ IsDefinitiveUnsatisfied(p, qRep) = IsDefinitiveUnsatisfied(p, fRep)

QuotientBisimulationSound ==
    \A p \in OfficialAnchorPrimaryProfiles :
      LET aProf == AnchorProfileFor(p)
      IN  /\ OpsOf(p) \cap OpsOf(aProf) # {}
          /\ PortQuotientBisimulates(p, "dest-primary")
          /\ PortQuotientBisimulates(aProf, "dest-anchor")

ASSUME QuotientBisimulationSound

\* ---------------------------------------------------------------------------
\* State variables across the 5 Host contexts & independent transfer channels
\* ---------------------------------------------------------------------------
VARIABLES profile, channel, mode, notesReady, refEligible, clearance, retained,
          ctx, preObs, anchorPreObs,
          primaryStatus, primaryRefreshed, primaryCommands, primaryFinalObs,
          primaryAtAnchorStatus,
          anchorStatus, anchorRefreshed, anchorCommands,
          primaryAtVerifyStatus, anchorAtVerifyStatus,
          primaryVerifyObs, anchorVerifyObs, history

reqVars         == <<profile, channel, mode, notesReady, refEligible>>
primaryExecVars == <<primaryStatus, primaryRefreshed, primaryCommands, primaryFinalObs>>
anchorExecVars  == <<anchorStatus, anchorRefreshed, anchorCommands>>
verifyVars      == <<primaryAtVerifyStatus, anchorAtVerifyStatus, primaryVerifyObs, anchorVerifyObs>>
vars            == <<reqVars, clearance, retained, ctx, preObs, anchorPreObs,
                     primaryExecVars, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

AnchorProf   == AnchorProfileFor(profile)
AnchorActive == (channel = "official")

CurrentRequest ==
    [execution |-> Len(history) + 1, unit |-> "unit-a", revision |-> "commit-a",
     inputs |-> "frozen-inputs", destination |-> "dest-primary",
     channel |-> channel, mode |-> mode, clearance |-> clearance]
AnchorRequest == [CurrentRequest EXCEPT !.destination = "dest-anchor", !.clearance = "first"]

LeftPrepEnvelope ==
    [unitSubject    |-> LeftUnitSubject(CurrentRequest),
     origin         |-> "release",
     complete       |-> TRUE,
     clean          |-> TRUE,
     nativeIdentity |-> [pkgName |-> IF channel = "buddy" THEN "scoped-preview-a" ELSE "canonical-a",
                         version |-> "1.2.0", notesReady |-> notesReady],
     originals      |-> [a \in UnitArtifacts |-> <<a, "qualified-digest">>]]

PrimaryTargetEnv == BindTargetEnvelope(profile, AnchorActive, CurrentRequest.destination, CurrentRequest, LeftPrepEnvelope, CurrentRequest.revision, UnitArtifacts)
AnchorTargetEnv  == BindTargetEnvelope(AnchorProf, TRUE, "dest-anchor", AnchorRequest, LeftPrepEnvelope, CurrentRequest.revision, UnitArtifacts)

EligibleChannels       == IF refEligible THEN profile.liveChannels ELSE {}
AnchorEligibleChannels == IF refEligible THEN AnchorProf.liveChannels ELSE {}

SelectedPrimaryRoute == SelectRoute(profile, CurrentRequest, PrimaryTargetEnv.bound, EligibleChannels, AggregateObsState(profile, preObs))
SelectedAnchorRoute  == SelectRoute(AnchorProf, AnchorRequest, AnchorTargetEnv.bound, AnchorEligibleChannels, AggregateObsState(AnchorProf, anchorPreObs))

PrimaryRouteEnv == [subject |-> CurrentRequest, coordinate |-> PrimaryTargetEnv.coordinate, route |-> SelectedPrimaryRoute,
                    initialObs |-> preObs, reviewed |-> ReviewedCeiling(profile, SelectedPrimaryRoute, preObs)]
AnchorRouteEnv  == [subject |-> AnchorRequest, coordinate |-> AnchorTargetEnv.coordinate, route |-> SelectedAnchorRoute,
                    initialObs |-> anchorPreObs, reviewed |-> ReviewedCeiling(AnchorProf, SelectedAnchorRoute, anchorPreObs)]

BuildReceiptEnv(req, coord, deliv, refMap, cmds, fObs) ==
    IF deliv = "present"
    THEN [status |-> "present", subject |-> req, coordinate |-> coord, refreshed |-> refMap, commands |-> cmds, pubFinalObs |-> fObs]
    ELSE [status |-> IF deliv = "none" THEN "lost" ELSE deliv, subject |-> req, coordinate |-> coord]

PrimaryReceiptAtAnchor == BuildReceiptEnv(CurrentRequest, PrimaryTargetEnv.coordinate, primaryAtAnchorStatus, primaryRefreshed, primaryCommands, primaryFinalObs)
PrimaryReceiptAtVerify == BuildReceiptEnv(CurrentRequest, PrimaryTargetEnv.coordinate, primaryAtVerifyStatus, primaryRefreshed, primaryCommands, primaryFinalObs)
AnchorReceiptAtVerify  == BuildReceiptEnv(AnchorRequest, AnchorTargetEnv.coordinate, anchorAtVerifyStatus, anchorRefreshed, anchorCommands, UnobservedMap(AnchorProf))

PrimaryVerifyObsEnv == [present |-> ~IsUnobservedMap(profile, primaryVerifyObs), subject |-> CurrentRequest, coordinate |-> PrimaryTargetEnv.coordinate, obs |-> primaryVerifyObs]
AnchorVerifyObsEnv  == [present |-> ~IsUnobservedMap(AnchorProf, anchorVerifyObs), subject |-> AnchorRequest, coordinate |-> AnchorTargetEnv.coordinate, obs |-> anchorVerifyObs]

PrimaryReport == CompleteFromEnvelopes(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv, PrimaryReceiptAtVerify, PrimaryVerifyObsEnv, retained)
AnchorReport  == CompleteFromEnvelopes(AnchorProf, AnchorRequest, AnchorTargetEnv, AnchorRouteEnv, AnchorReceiptAtVerify, AnchorVerifyObsEnv, retained)
CurrentReport == CombinePrimaryAndAnchor(CurrentRequest, profile, PrimaryReport, AnchorActive, AnchorProf, AnchorReport, retained)

\* ---------------------------------------------------------------------------
\* Transitions across the 5 Host privilege contexts
\* ---------------------------------------------------------------------------
PreGateCanObservePrimary ==
    (channel # "official" \/ notesReady) /\ (mode = "dry" \/ (refEligible /\ channel \in profile.liveChannels)) /\
    channel \in profile.dryChannels /\ BeforeGateMode(profile, mode) = "read-only"

Init ==
    /\ profile \in OfficialAnchorPrimaryProfiles /\ channel \in Channels /\ mode \in Modes
    /\ notesReady \in (IF channel = "official" THEN BOOLEAN ELSE {TRUE})
    /\ refEligible \in (IF mode = "live" THEN BOOLEAN ELSE {TRUE})
    /\ clearance \in (IF profile.readTopology = "blind-receipt" /\ mode = "live" THEN {"first", "unresolved"} ELSE {"first"})
    /\ retained = TRUE /\ history = <<>> /\ ctx = "unpriv"
    /\ preObs \in (IF PreGateCanObservePrimary THEN QuotientObsMaps(profile) ELSE {UnobservedMap(profile)})
    /\ anchorPreObs \in (IF AnchorActive /\ notesReady /\ (mode = "dry" \/ refEligible)
                         THEN QuotientObsMaps(AnchorProf) ELSE {UnobservedMap(AnchorProf)})
    /\ primaryStatus = (IF SelectedPrimaryRoute = "publisher" THEN "awaited" ELSE "not-required")
    /\ primaryRefreshed = BlankObs(profile, "unknown") /\ primaryCommands = <<>>
    /\ primaryFinalObs = UnobservedMap(profile) /\ primaryAtAnchorStatus = "none"
    /\ anchorStatus = (IF AnchorActive /\ SelectedAnchorRoute = "publisher" THEN "awaited" ELSE "not-required")
    /\ anchorRefreshed = BlankObs(AnchorProf, "unknown") /\ anchorCommands = <<>>
    /\ primaryAtVerifyStatus = "none" /\ anchorAtVerifyStatus = "none"
    /\ primaryVerifyObs = UnobservedMap(profile) /\ anchorVerifyObs = UnobservedMap(AnchorProf)

AfterPrimaryCtx == IF AnchorActive THEN "anchor-gate" ELSE "verify"

BypassPrimaryGate ==
    /\ ctx = "unpriv" /\ PrimaryRouteEnv.route # "publisher" /\ ctx' = AfterPrimaryCtx
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

PrimaryGateNoStart ==
    /\ ctx = "unpriv" /\ PrimaryRouteEnv.route = "publisher"
    /\ primaryStatus' = "known-nonstart" /\ ctx' = AfterPrimaryCtx
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryRefreshed, primaryCommands, primaryFinalObs, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

EnterIsolatedPrimary ==
    /\ ctx = "unpriv" /\ PrimaryRouteEnv.route = "publisher"
    /\ HasPublisherAccess(profile, CurrentRequest, PrimaryRouteEnv.route, PrimaryTargetEnv.bound,
                          EligibleChannels, TRUE, PrimaryWriteCapIn("isolated-pub-primary"), "isolated-pub-primary")
    /\ ctx' = "isolated-pub-primary"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

CrashIsolatedPrimary ==
    /\ ctx = "isolated-pub-primary" /\ primaryStatus' = "crashed" /\ ctx' = AfterPrimaryCtx
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryRefreshed, primaryCommands, primaryFinalObs, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

CompleteIsolatedPrimary(rec) ==
    /\ ctx = "isolated-pub-primary"
    /\ primaryStatus' = "present" /\ primaryRefreshed' = rec.refreshed
    /\ primaryCommands' = rec.commands /\ primaryFinalObs' = rec.pubFinalObs /\ ctx' = AfterPrimaryCtx
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryAtAnchorStatus, anchorExecVars, verifyVars, history>>

AnchorDeliveryChoices ==
    CASE primaryStatus = "not-required"   -> {"not-required"}
      [] primaryStatus = "crashed"        -> {"lost"}
      [] primaryStatus = "known-nonstart" -> {"known-nonstart", "lost"}
      [] OTHER                            -> {"present", "lost"}

AnchorPermittedUnder(deliv) ==
    PrimaryPermitsAnchor(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv,
                         BuildReceiptEnv(CurrentRequest, PrimaryTargetEnv.coordinate, deliv, primaryRefreshed, primaryCommands, primaryFinalObs))

SkipIsolatedAnchor(deliv) ==
    /\ ctx = "anchor-gate" /\ ~(AnchorPermittedUnder(deliv) /\ AnchorRouteEnv.route = "publisher")
    /\ primaryAtAnchorStatus' = deliv
    /\ anchorStatus' = IF AnchorRouteEnv.route # "publisher" THEN "not-required" ELSE "known-nonstart"
    /\ ctx' = "verify"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, anchorRefreshed, anchorCommands, verifyVars, history>>

AnchorGateNoStart(deliv) ==
    /\ ctx = "anchor-gate" /\ AnchorPermittedUnder(deliv) /\ AnchorRouteEnv.route = "publisher"
    /\ primaryAtAnchorStatus' = deliv /\ anchorStatus' = "known-nonstart" /\ ctx' = "verify"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, anchorRefreshed, anchorCommands, verifyVars, history>>

EnterIsolatedAnchor(deliv) ==
    /\ ctx = "anchor-gate" /\ AnchorPermittedUnder(deliv) /\ AnchorRouteEnv.route = "publisher"
    /\ HasPublisherAccess(AnchorProf, AnchorRequest, AnchorRouteEnv.route, AnchorTargetEnv.bound,
                          AnchorEligibleChannels, TRUE, AnchorWriteCapIn("isolated-pub-anchor"), "isolated-pub-anchor")
    /\ primaryAtAnchorStatus' = deliv /\ ctx' = "isolated-pub-anchor"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, anchorExecVars, verifyVars, history>>

CrashIsolatedAnchor ==
    /\ ctx = "isolated-pub-anchor" /\ anchorStatus' = "crashed" /\ ctx' = "verify"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, primaryAtAnchorStatus, anchorRefreshed, anchorCommands, verifyVars, history>>

CompleteIsolatedAnchor(arec) ==
    /\ ctx = "isolated-pub-anchor"
    /\ anchorStatus' = "present" /\ anchorRefreshed' = arec.refreshed /\ anchorCommands' = arec.commands /\ ctx' = "verify"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, anchorPreObs, primaryExecVars, primaryAtAnchorStatus, verifyVars, history>>

CanInjectVerifyFaults == Len(history) = 0 /\ mode = "live" /\ PrimaryTargetEnv.bound /\ refEligible

DeliveryChoicesAtVerify(st) ==
    CASE st = "not-required" -> {"not-required"}
      [] st = "crashed"      -> {"lost"}
      [] OTHER               -> IF CanInjectVerifyFaults THEN {st, "lost"} ELSE {st}

PrimaryVerifyObsChoices ==
    IF PrimaryTargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\ refEligible /\
       mode = "live" /\ CompletionReader(profile) = "verifier" /\ PrimaryRouteEnv.route # "blocked"
    THEN QuotientObsMaps(profile) \cup (IF CanInjectVerifyFaults THEN {UnobservedMap(profile)} ELSE {})
    ELSE {UnobservedMap(profile)}

AnchorVerifyObsChoices ==
    IF AnchorActive /\ AnchorTargetEnv.bound /\ AdmittedRequest(AnchorProf, AnchorRequest) /\
       refEligible /\ mode = "live" /\ PrimaryRouteEnv.route # "blocked" /\ AnchorRouteEnv.route # "blocked"
    THEN QuotientObsMaps(AnchorProf) \cup (IF CanInjectVerifyFaults THEN {UnobservedMap(AnchorProf)} ELSE {})
    ELSE {UnobservedMap(AnchorProf)}

VerifyStep(pDeliv, aDeliv, pObs, aObs, retFlag) ==
    /\ ctx = "verify"
    /\ retFlag \in (IF CanInjectVerifyFaults /\ pDeliv # "lost" /\ aDeliv # "lost" THEN BOOLEAN ELSE {TRUE})
    /\ primaryAtVerifyStatus' = pDeliv /\ anchorAtVerifyStatus' = aDeliv
    /\ primaryVerifyObs' = pObs /\ anchorVerifyObs' = aObs /\ retained' = retFlag /\ ctx' = "done"
    /\ UNCHANGED <<reqVars, clearance, preObs, anchorPreObs, primaryExecVars, primaryAtAnchorStatus, anchorExecVars, history>>

\* ---------------------------------------------------------------------------
\* Bipartite 2-run recovery hub (collapses M x N fan-in into M + K*N edges
\* without restricting Run-1 recovery states!)
\* ---------------------------------------------------------------------------
CanRecover ==
    /\ AnchorActive /\ ctx = "done" /\ Len(history) = 0 /\ mode = "live"
    /\ PrimaryTargetEnv.bound /\ refEligible /\ CurrentRequest.clearance = "first"
    /\ CurrentReport.satisfaction # "satisfied"

BlindPrimaryConfirmedInPub ==
    profile.readTopology = "blind-receipt" /\ primaryStatus = "present" /\
    AggregateObsState(profile, primaryFinalObs) = ProofToken(profile.proofMode)

StageRecovery ==
    /\ CanRecover
    /\ LET pRep == PrimaryReport
           aRep == AnchorReport
           cRep == CombinePrimaryAndAnchor(CurrentRequest, profile, pRep, AnchorActive, AnchorProf, aRep, retained)
           nextClrChoices ==
               IF profile.readTopology # "blind-receipt" THEN {"first"}
               ELSE CASE BlindPrimaryConfirmedInPub     -> {"unresolved"}
                      [] retained /\ pRep.attempted = "no" -> {"first"}
                      [] OTHER                          -> {"unresolved", "owner-checked"}
           summary ==
               [execution           |-> CurrentRequest.execution,
                wasSatisfied        |-> (cRep.satisfaction = "satisfied"),
                primaryWasSatisfied |-> (pRep.satisfaction = "satisfied" \/ BlindPrimaryConfirmedInPub),
                hadRetainedNonStart |-> (profile = BlindReceiptStore =>
                                         (retained /\ pRep.attempted = "no")),
                leftOnlyOp2OnAnchor |-> (~IsUnobservedMap(AnchorProf, cRep.anchorObserved) /\
                                         AggregateObsState(AnchorProf, cRep.anchorObserved) = "missing" /\
                                         MissingOps(AnchorProf, cRep.anchorObserved) = {"op2"}),
                allowedClearances   |-> nextClrChoices]
       IN  /\ history' = <<summary>>
           /\ retained' = TRUE /\ ctx' = "recover"
           /\ preObs' = UnobservedMap(profile) /\ anchorPreObs' = UnobservedMap(AnchorProf)
           /\ primaryStatus' = "not-required" /\ primaryRefreshed' = BlankObs(profile, "unknown")
           /\ primaryCommands' = <<>> /\ primaryFinalObs' = UnobservedMap(profile)
           /\ primaryAtAnchorStatus' = "none"
           /\ anchorStatus' = "not-required" /\ anchorRefreshed' = BlankObs(AnchorProf, "unknown")
           /\ anchorCommands' = <<>>
           /\ primaryAtVerifyStatus' = "none" /\ anchorAtVerifyStatus' = "none"
           /\ primaryVerifyObs' = UnobservedMap(profile) /\ anchorVerifyObs' = UnobservedMap(AnchorProf)
    /\ UNCHANGED <<reqVars, clearance>>

RecoveryPreObsChoices ==
    IF BeforeGateMode(profile, "live") = "unobserved" THEN {UnobservedMap(profile)} ELSE QuotientObsMaps(profile)

DispatchRecovery(reproducesAll, nextClearance, nextPreObs, nextAnchorObs) ==
    /\ ctx = "recover"
    /\ LET prevReq == [CurrentRequest EXCEPT !.execution = 1]
           nextReq == [CurrentRequest EXCEPT !.execution = 2, !.clearance = nextClearance]
           nextAReq == [nextReq EXCEPT !.destination = "dest-anchor", !.clearance = "first"]
       IN  /\ ValidRecoveryDispatch(prevReq, nextReq, "manual-dispatch", reproducesAll)
           /\ nextClearance \in history[1].allowedClearances
           /\ LET nextPRoute == SelectRoute(profile, nextReq, TRUE, EligibleChannels, AggregateObsState(profile, nextPreObs))
                  nextARoute == SelectRoute(AnchorProf, nextAReq, TRUE, AnchorEligibleChannels, AggregateObsState(AnchorProf, nextAnchorObs))
              IN  /\ clearance' = nextClearance /\ ctx' = "unpriv"
                  /\ preObs' = nextPreObs /\ anchorPreObs' = nextAnchorObs
                  /\ primaryStatus' = (IF nextPRoute = "publisher" THEN "awaited" ELSE "not-required")
                  /\ anchorStatus' = (IF nextARoute = "publisher" THEN "awaited" ELSE "not-required")
    /\ UNCHANGED <<reqVars, retained, primaryRefreshed, primaryCommands, primaryFinalObs,
                   primaryAtAnchorStatus, anchorRefreshed, anchorCommands, verifyVars, history>>

Next ==
    \/ (ctx = "unpriv" /\ (BypassPrimaryGate \/ PrimaryGateNoStart \/ EnterIsolatedPrimary))
    \/ (ctx = "isolated-pub-primary" /\
        ( CrashIsolatedPrimary
          \/ \E rec \in QuotientPubReceipts(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv) :
               CompleteIsolatedPrimary(rec) ))
    \/ (ctx = "anchor-gate" /\
        \E deliv \in AnchorDeliveryChoices :
          SkipIsolatedAnchor(deliv) \/ AnchorGateNoStart(deliv) \/ EnterIsolatedAnchor(deliv))
    \/ (ctx = "isolated-pub-anchor" /\
        ( CrashIsolatedAnchor
          \/ \E arec \in QuotientPubReceipts(AnchorProf, AnchorRequest, AnchorTargetEnv, AnchorRouteEnv) :
               CompleteIsolatedAnchor(arec) ))
    \/ (ctx = "verify" /\
        \E pd \in DeliveryChoicesAtVerify(primaryStatus),
           ad \in (IF AnchorActive THEN DeliveryChoicesAtVerify(anchorStatus) ELSE {"not-required"}),
           po \in PrimaryVerifyObsChoices, ao \in AnchorVerifyObsChoices :
          \E ret \in (IF CanInjectVerifyFaults /\ pd # "lost" /\ ad # "lost" THEN BOOLEAN ELSE {TRUE}) :
            VerifyStep(pd, ad, po, ao, ret))
    \/ (ctx = "done" /\ (UNCHANGED vars \/ StageRecovery))
    \/ (ctx = "recover" /\
        \E nc \in history[1].allowedClearances, np \in RecoveryPreObsChoices, na \in QuotientObsMaps(AnchorProf) :
          DispatchRecovery(TRUE, nc, np, na))

Spec == Init /\ [][Next]_vars

\* ---------------------------------------------------------------------------
\* Invariants & non-vacuity witnesses
\* ---------------------------------------------------------------------------
CapabilityAndBoundaryIsolation ==
    /\ (AnyWriteCapIn(ctx) => ~LeftPortAllowedIn(ctx))
    /\ ~(PrimaryWriteCapIn(ctx) /\ AnchorWriteCapIn(ctx))
    /\ (~AnchorActive =>
          ctx \notin {"anchor-gate", "isolated-pub-anchor"} /\ anchorCommands = <<>> /\
          (ctx = "done" => CurrentReport.anchorSatisfaction = "not-applicable" /\ CurrentReport.anchorAttempted = "no"))
    /\ (AnchorActive /\ ~notesReady =>
          ~PrimaryTargetEnv.bound /\ ~AnchorTargetEnv.bound /\ primaryCommands = <<>> /\ anchorCommands = <<>> /\
          (ctx = "done" => CurrentReport.satisfaction = "unsatisfied"))
    /\ ((mode = "dry" \/ (mode = "live" /\ ~refEligible)) =>
          ~AnyWriteCapIn(ctx) /\ primaryCommands = <<>> /\ anchorCommands = <<>>)
    /\ ((profile.readTopology = "blind-receipt" /\ mode = "live" /\ PrimaryTargetEnv.bound /\
         AdmittedRequest(profile, CurrentRequest) /\ refEligible /\ CurrentRequest.clearance = "unresolved") =>
          PrimaryRouteEnv.route = "unresolved" /\ ctx \notin {"isolated-pub-primary", "isolated-pub-anchor"} /\
          primaryCommands = <<>> /\ anchorCommands = <<>>)

AnchorGateAndZeroWriteDiscipline ==
    /\ ((ctx = "isolated-pub-anchor" \/ anchorStatus \in {"present", "crashed"} \/ anchorCommands # <<>>) =>
          AnchorActive /\ AnchorRouteEnv.route = "publisher" /\
          PrimaryPermitsAnchor(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv, PrimaryReceiptAtAnchor))
    /\ ((AnchorActive /\ PrimaryRouteEnv.route = "publisher" /\ CompletionReader(profile) = "verifier" /\
         primaryStatus = "present" /\ primaryAtAnchorStatus = "present" /\
         AggregateObsState(profile, primaryRefreshed) = ProofToken(profile.proofMode) /\ primaryCommands = <<>>) =>
          PrimaryPermitsAnchor(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv, PrimaryReceiptAtAnchor))
    /\ ((AnchorActive /\ PrimaryRouteEnv.route = "publisher" /\ profile.readTopology = "publisher-only" /\
         primaryStatus = "present" /\ primaryAtAnchorStatus = "present" /\
         AggregateObsState(profile, primaryFinalObs) = ProofToken(profile.proofMode)) =>
          PrimaryPermitsAnchor(profile, CurrentRequest, PrimaryTargetEnv, PrimaryRouteEnv, PrimaryReceiptAtAnchor))
    /\ ((AggregateObsState(profile, preObs) = ProofToken(profile.proofMode) \/
         (primaryStatus = "present" /\ AggregateObsState(profile, primaryRefreshed) = ProofToken(profile.proofMode))) =>
          primaryCommands = <<>> /\
          (ctx = "done" /\ retained /\ primaryAtVerifyStatus # "lost" => CurrentReport.primaryAttempted = "no"))

IndependentLossAndNamespaceDiscipline ==
    ctx = "done" =>
      LET pRep == PrimaryReport
          aRep == AnchorReport
          cRep == CombinePrimaryAndAnchor(CurrentRequest, profile, pRep, AnchorActive, AnchorProf, aRep, retained)
      IN  /\ (cRep.satisfaction = "simulation" =>
                (AnchorActive => Cardinality(cRep.hypotheticalOps) = Cardinality(pRep.hypotheticalOps) + Cardinality(aRep.hypotheticalOps))
                /\ cRep.attempted = "no")
          /\ (cRep.satisfaction # "simulation" =>
                cRep.hypotheticalOps = {} /\
                Cardinality(cRep.uncertainCalls) = Cardinality(pRep.uncertainCalls) + Cardinality(aRep.uncertainCalls))
          /\ ((IsDefinitiveUnsatisfied(profile, pRep) \/ (AnchorActive /\ IsDefinitiveUnsatisfied(AnchorProf, aRep))) =>
                cRep.satisfaction = "unsatisfied")
          /\ (primaryAtVerifyStatus = "lost" =>
                cRep.primaryAttempted = "unknown" /\ ~cRep.outcomesAvailable /\ cRep.outcomes = <<>> /\
                cRep.satisfaction # "satisfied" /\
                cRep.attempted = (IF cRep.anchorAttempted = "yes" THEN "yes" ELSE "unknown"))
          /\ (AnchorActive /\ anchorAtVerifyStatus = "lost" =>
                cRep.anchorAttempted = "unknown" /\ ~cRep.outcomesAvailable /\ cRep.anchorOutcomes = <<>> /\
                cRep.satisfaction # "satisfied" /\
                cRep.attempted = (IF cRep.primaryAttempted = "yes" THEN "yes" ELSE "unknown"))
          /\ (cRep.satisfaction = "satisfied" =>
                retained /\ mode = "live" /\ refEligible /\ PrimaryTargetEnv.bound /\ pRep.satisfaction = "satisfied" /\
                (AnchorActive => notesReady /\ AnchorTargetEnv.bound /\ aRep.satisfaction = "satisfied"))

RecoveryHistoryDiscipline ==
    Len(history) = 1 =>
      /\ history[1].execution = 1 /\ ~history[1].wasSatisfied /\ CurrentRequest.execution = 2
      /\ (ctx # "recover" /\ profile = BlindReceiptStore /\ ~history[1].hadRetainedNonStart =>
            CurrentRequest.clearance # "first")
      /\ (ctx # "recover" /\ profile = BlindReceiptStore /\ history[1].primaryWasSatisfied =>
            CurrentRequest.clearance = "unresolved" /\ PrimaryRouteEnv.route = "unresolved" /\
            primaryCommands = <<>> /\ anchorCommands = <<>>)

NoPostGateObservedPrimaryPermitsAnchor ==
    ctx = "done" =>
      LET cRep == CurrentReport
      IN  ~(AnchorActive /\ Len(history) = 0 /\ retained /\ profile = ByteOrderedPair /\
            preObs = BlankObs(profile, "missing") /\ primaryStatus = "present" /\ primaryAtAnchorStatus = "present" /\
            AggregateObsState(profile, primaryRefreshed) = ProofToken(profile.proofMode) /\
            primaryCommands = <<>> /\ cRep.primaryAttempted = "no" /\ anchorCommands # <<>> /\
            cRep.anchorAttempted = "yes" /\ cRep.attempted = "yes" /\ cRep.satisfaction = "satisfied")

NoIndependentVerifyLossPreservesAnchorAttempt ==
    ctx = "done" =>
      LET cRep == CurrentReport
      IN  ~(AnchorActive /\ Len(history) = 0 /\ retained /\ profile = ByteOrderedPair /\
            primaryAtAnchorStatus = "present" /\ primaryAtVerifyStatus = "lost" /\
            cRep.primaryAttempted = "unknown" /\ cRep.anchorAttempted = "yes" /\ cRep.attempted = "yes" /\
            <<"primary", "op2">> \in cRep.uncertainCalls /\ <<"anchor", "op2">> \in cRep.uncertainCalls /\
            cRep.satisfaction = "unknown")

NoReadablePrimarySkipAnchorRecovery ==
    ctx = "done" =>
      LET cRep == CurrentReport
      IN  ~(AnchorActive /\ Len(history) = 1 /\ retained /\ profile = GatedReadBundledStore /\
            history[1].primaryWasSatisfied /\ history[1].leftOnlyOp2OnAnchor /\ ~history[1].wasSatisfied /\
            primaryCommands = <<>> /\ cRep.primaryAttempted = "no" /\
            anchorCommands = <<<<"op2", "acknowledged">>>> /\
            cRep.anchorAttempted = "yes" /\ cRep.attempted = "yes" /\ cRep.satisfaction = "satisfied")

NoBlindPostReceiptAnchorFailureBlocksRedispatch ==
    ctx = "done" =>
      LET cRep == CurrentReport
      IN  ~(AnchorActive /\ Len(history) = 1 /\ retained /\ profile = BlindReceiptStore /\
            history[1].primaryWasSatisfied /\ ~history[1].wasSatisfied /\
            CurrentRequest.clearance = "unresolved" /\ PrimaryRouteEnv.route = "unresolved" /\
            primaryCommands = <<>> /\ anchorCommands = <<>> /\ cRep.attempted = "no")
=============================================================================
