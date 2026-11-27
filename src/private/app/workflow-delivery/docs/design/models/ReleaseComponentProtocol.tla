---------------------- MODULE ReleaseComponentProtocol ----------------------
\* White-box single-RightPort Release protocol across capability-isolated Host
\* contexts ("unpriv" -> "isolated-pub-primary" -> "verify" -> "done"):
\* 1. Destination-blind LeftPort (LeftUnitSubject -> nativeIdentity + originals).
\* 2. RightPort coordinate binding, artifact projection & general finite posets.
\* 3. Context-wiped pluginSession ({} outside isolated-pub-primary).
\* 4. Pre-gate "unresolved" clearance stop & PubReceiptGuarantee discharging
\*    IsValidSealedPubReceipt for OfficialAnchorComposition.
EXTENDS DeliveryPortAlgebra

CONSTANT ProfileScope
ASSUME ProfileScope \in {"single-port-matrix", "ordered-pair-official", "container-assets-official"}

UnitArtifacts == {"art1", "art2", "art3"}

ByteSingleRegistry    == SingleOpProfile(Channels, Channels, "unpriv-always", "served-bytes", "op1", {"art1"})
VersionOnlyFeed       == SingleOpProfile({"official"}, {"official"}, "unpriv-always", "version-available", "op1", {"art1"})
LiveReadStoreSubset   == SingleOpProfile({"official"}, Channels, "unpriv-live-only", "store-accepted", "op1", {"art1"})
GatedReadBundledStore == SingleOpProfile({"official"}, Channels, "publisher-only", "store-accepted", "op1", {"art2", "art3"})
BlindReceiptStore     == SingleOpProfile({"official"}, Channels, "blind-receipt", "store-accepted", "op1", {"art3"})

ByteOrderedPair ==
    MakeRightProfile(Channels, Channels, "unpriv-always", "served-bytes", <<"op1", "op2">>,
                     [op \in {"op1", "op2"} |-> {}],
                     [op \in {"op1", "op2"} |-> IF op = "op1" THEN {"art1"} ELSE {"art2"}],
                     {{"op1"}, {"op2"}, {"op1", "op2"}})
ByteOrderedPairNoSubset == [ByteOrderedPair EXCEPT !.completableSubsets = {{"op1", "op2"}}]

OneAssetContainer ==
    MakeRightProfile(Channels, Channels, "unpriv-always", "served-bytes", <<"op0", "op1">>,
                     [op \in {"op0", "op1"} |-> IF op = "op1" THEN {"op0"} ELSE {}],
                     [op \in {"op0", "op1"} |-> IF op = "op0" THEN {} ELSE {"art1"}],
                     {{"op1"}, {"op0", "op1"}})

TwoAssetContainer ==
    MakeRightProfile(Channels, Channels, "unpriv-always", "served-bytes", <<"op0", "op1", "op2">>,
                     [op \in {"op0", "op1", "op2"} |-> IF op = "op0" THEN {} ELSE {"op0"}],
                     [op \in {"op0", "op1", "op2"} |-> CASE op = "op0" -> {} [] op = "op1" -> {"art1"} [] OTHER -> {"art2"}],
                     {{"op1"}, {"op2"}, {"op1", "op2"}, {"op0", "op1", "op2"}})

ChainedThreeOpDag ==
    MakeRightProfile(Channels, Channels, "unpriv-always", "served-bytes", <<"op0", "op1", "op2">>,
                     [op \in {"op0", "op1", "op2"} |-> CASE op = "op0" -> {} [] op = "op1" -> {"op0"} [] OTHER -> {"op1"}],
                     [op \in {"op0", "op1", "op2"} |-> CASE op = "op0" -> {} [] op = "op1" -> {"art1"} [] OTHER -> {"art2"}],
                     {{"op2"}, {"op1", "op2"}, {"op0", "op1", "op2"}})

SinglePortMatrixProfiles ==
    {ByteSingleRegistry, ByteOrderedPair, ByteOrderedPairNoSubset, OneAssetContainer,
     TwoAssetContainer, ChainedThreeOpDag, VersionOnlyFeed, LiveReadStoreSubset,
     GatedReadBundledStore, BlindReceiptStore}

GeneratedOneOpProfiles ==
    {SingleOpProfile(cp.live, cp.dry, rt, pm, "op1", c1) :
       cp \in ValidChannelPolicies, rt \in ReadTopologies,
       pm \in ProofModes, c1 \in (SUBSET {"art1", "art2"}) \ {{}}}

GeneratedTwoOpProfiles ==
    {MakeRightProfile(cp.live, cp.dry, rt, pm, <<"op1", "op2">>,
                      [op \in {"op1", "op2"} |-> IF op = "op2" THEN pre2 ELSE {}],
                      [op \in {"op1", "op2"} |-> IF op = "op1" THEN c1 ELSE {"art2"}],
                      IF ~ps THEN {{"op1", "op2"}}
                      ELSE IF pre2 # {} THEN {{"op2"}, {"op1", "op2"}}
                      ELSE {{"op1"}, {"op2"}, {"op1", "op2"}}) :
       cp \in ValidChannelPolicies, rt \in ReadTopologies \ {"blind-receipt"},
       pm \in ProofModes, pre2 \in {{}, {"op1"}}, c1 \in {{}, {"art1"}}, ps \in BOOLEAN}

BoundedValidProfiles ==
    {p \in GeneratedOneOpProfiles \cup GeneratedTwoOpProfiles \cup SinglePortMatrixProfiles :
       IsValidRightProfile(p, UnitArtifacts)}

RawClassificationOnto(pm) ==
    /\ {ClassifyRawOpObs(pm, ev) : ev \in RawOpEvidenceSpace} = OpObsValues
    /\ \A ev \in RawOpEvidenceSpace :
         /\ (ClassifyRawOpObs(pm, ev) = "established") <=>
              (ev.present /\ ev.status \notin {"absent", "unknown", "rejected"} /\
               EvaluatePositiveProof(pm, ev.coordMatch, ev.metadataMatch, ev.bytesMatch, ev.status) = ProofToken(pm))
         /\ (ClassifyRawOpObs(pm, ev) = "missing") <=> (ev.coordMatch /\ ~ev.present /\ ev.status = "absent")
         /\ (ClassifyRawOpObs(pm, ev) = "conflict") <=>
              (ev.present /\
               (~ev.coordMatch \/ ev.status = "rejected" \/
                (pm = "served-bytes" /\ ev.status \notin {"absent", "unknown"} /\
                 (~ev.metadataMatch \/ ~ev.bytesMatch))))
         /\ (ClassifyRawOpObs(pm, ev) = "unknown") <=>
              ((~ev.coordMatch /\ ~ev.present) \/
               (ev.coordMatch /\ ~ev.present /\ ev.status # "absent") \/
               (ev.coordMatch /\ ev.present /\
                (ev.status \in {"absent", "unknown"} \/
                 (pm # "served-bytes" /\ ev.status # "rejected" /\
                  EvaluatePositiveProof(pm, ev.coordMatch, ev.metadataMatch, ev.bytesMatch, ev.status) # ProofToken(pm)))))

EnvelopeTamperingBlocked(p) ==
    LET req       == [execution |-> 2, unit |-> "u", revision |-> "c1", inputs |-> "in1",
                      destination |-> "dest-a", channel |-> CHOOSE ch \in p.liveChannels : TRUE,
                      mode |-> "live", clearance |-> "first"]
        staleReq  == [req EXCEPT !.execution = 1]
        otherDst  == [req EXCEPT !.destination = "dest-b"]
        goodLeft  == [unitSubject |-> LeftUnitSubject(req), origin |-> "release", complete |-> TRUE,
                      clean |-> TRUE, nativeIdentity |-> [pkgName |-> "pkg", version |-> "1.2.0", notesReady |-> TRUE],
                      originals |-> [a \in UnitArtifacts |-> <<a, "digest">>]]
        badLefts  == {[goodLeft EXCEPT !.unitSubject = LeftUnitSubject(staleReq)],
                      [goodLeft EXCEPT !.unitSubject = LeftUnitSubject([req EXCEPT !.revision = "c2"])],
                      [goodLeft EXCEPT !.origin = "CI"],
                      [goodLeft EXCEPT !.clean = FALSE],
                      [goodLeft EXCEPT !.complete = FALSE],
                      [goodLeft EXCEPT !.originals = [a \in UnitArtifacts \cup {"extra"} |-> <<a, "digest">>]]}
        goodTgt   == BindTargetEnvelope(p, FALSE, req.destination, req, goodLeft, req.revision, UnitArtifacts)
        otherTgt  == BindTargetEnvelope(p, FALSE, "dest-b", otherDst, goodLeft, req.revision, UnitArtifacts)
        estObs    == BlankObs(p, "established")
        firstOp   == p.ops[1]
        goodRt    == [subject |-> req, coordinate |-> goodTgt.coordinate, route |-> "publisher",
                      initialObs |-> IF BeforeGateMode(p, "live") = "unobserved" THEN UnobservedMap(p) ELSE BlankObs(p, "missing"),
                      reviewed |-> OpsOf(p)]
        notReqPub == [status |-> "not-required", subject |-> req, coordinate |-> goodTgt.coordinate]
        goodPub   == [status |-> "present", subject |-> req, coordinate |-> goodTgt.coordinate,
                      refreshed |-> BlankObs(p, "missing"), commands |-> <<>>, pubFinalObs |-> estObs]
        cmdPub    == [goodPub EXCEPT !.commands = <<<<firstOp, "acknowledged">>>>]
        goodVer   == [present |-> CompletionReader(p) = "verifier", subject |-> req,
                      coordinate |-> goodTgt.coordinate, obs |-> estObs]
        staleNoCmd   == CompleteFromEnvelopes(p, req, goodTgt, [goodRt EXCEPT !.subject = staleReq], goodPub, goodVer, TRUE)
        staleWithCmd == CompleteFromEnvelopes(p, req, goodTgt, [goodRt EXCEPT !.subject = staleReq], cmdPub, goodVer, TRUE)
        expiredCmd   == CompleteFromEnvelopes(p, req, goodTgt, goodRt, cmdPub, goodVer, FALSE)
    IN  /\ goodTgt.bound /\ DOMAIN goodTgt.selectedOriginals = TargetArtifactsOf(p)
        /\ ~BindTargetEnvelope(p, TRUE, req.destination, req, [goodLeft EXCEPT !.nativeIdentity.notesReady = FALSE], req.revision, UnitArtifacts).bound
        /\ ~BindTargetEnvelope(p, FALSE, "wrong-dest", req, goodLeft, req.revision, UnitArtifacts).bound
        /\ ~BindTargetEnvelope(p, FALSE, req.destination, req, goodLeft, "other-control", UnitArtifacts).bound
        /\ \A bad \in badLefts : ~BindTargetEnvelope(p, FALSE, req.destination, req, bad, req.revision, UnitArtifacts).bound
        /\ PrimaryPermitsAnchor(p, req, goodTgt, [goodRt EXCEPT !.route = "verify-without-write"], notReqPub)
        /\ ~PrimaryPermitsAnchor(p, req, goodTgt, [goodRt EXCEPT !.coordinate = otherTgt.coordinate, !.route = "verify-without-write"], notReqPub)
        /\ (CompletionReader(p) = "verifier" =>
              PrimaryPermitsAnchor(p, req, goodTgt, goodRt,
                                   [goodPub EXCEPT !.refreshed = estObs, !.commands = <<>>, !.pubFinalObs = UnobservedMap(p)]))
        /\ (p.readTopology = "publisher-only" =>
              /\ PrimaryPermitsAnchor(p, req, goodTgt, goodRt,
                                      [goodPub EXCEPT !.commands = <<<<firstOp, "ambiguous">>>>, !.pubFinalObs = estObs])
              /\ ~PrimaryPermitsAnchor(p, req, goodTgt, goodRt,
                                       [goodPub EXCEPT !.commands = <<<<firstOp, "failed">>>>, !.pubFinalObs = estObs]))
        /\ \A rec \in SealedPubReceiptsFromPool(p, req, goodTgt, goodRt, ValidObsMaps(p)) :
             IsValidSealedPubReceipt(p, req, goodTgt, goodRt, rec)
        /\ staleNoCmd.satisfaction # "satisfied" /\ staleNoCmd.attempted = "unknown"
        /\ staleWithCmd.satisfaction # "satisfied" /\ staleWithCmd.attempted = "yes"
        /\ expiredCmd.satisfaction = "unknown" /\ expiredCmd.attempted = "yes"
        /\ \A badPub \in {[goodPub EXCEPT !.coordinate = otherTgt.coordinate],
                          [goodPub EXCEPT !.subject = [req EXCEPT !.clearance = "unresolved"]]} :
             CompleteFromEnvelopes(p, req, goodTgt, goodRt, badPub, goodVer, TRUE).satisfaction # "satisfied"
        /\ (CompletionReader(p) = "verifier" =>
              CompleteFromEnvelopes(p, req, goodTgt, goodRt, goodPub,
                                    [goodVer EXCEPT !.coordinate = otherTgt.coordinate], TRUE).satisfaction # "satisfied")

TraitAlgebraSound ==
    /\ \A pm \in ProofModes : RawClassificationOnto(pm)
    /\ SinglePortMatrixProfiles \subseteq BoundedValidProfiles
    /\ \A p \in BoundedValidProfiles :
         /\ IsValidRightProfile(p, UnitArtifacts) /\ EnvelopeTamperingBlocked(p)
         /\ (BeforeGateMode(p, "dry") = "unobserved" <=> p.readTopology \in {"unpriv-live-only", "publisher-only", "blind-receipt"})
         /\ (BeforeGateMode(p, "live") = "unobserved" <=> p.readTopology \in {"publisher-only", "blind-receipt"})
         /\ (CompletionReader(p) = "publisher" <=> p.readTopology \in {"publisher-only", "blind-receipt"})
         /\ \A c \in Clearances : MayMutatePostGate(p, TRUE, "unobserved", c) <=>
                                    (p.readTopology = "blind-receipt" /\ c \in {"first", "owner-checked"})

ASSUME TraitAlgebraSound

SelectedProfiles ==
    CASE ProfileScope = "ordered-pair-official"     -> {ByteOrderedPair}
      [] ProfileScope = "container-assets-official" -> {TwoAssetContainer}
      [] OTHER                                      -> SinglePortMatrixProfiles

VARIABLES profile, channel, mode, leftValid, refEligible, clearance,
          retained, ctx, pubSubphase, pluginSession,
          preObs, pubRefreshed, pending, pubCommands, pubFinalObs,
          pubReceiptStatus, verifyObs, history

reqVars == <<profile, channel, mode, leftValid, refEligible>>
vars == <<reqVars, clearance, retained, ctx, pubSubphase, pluginSession,
          preObs, pubRefreshed, pending, pubCommands, pubFinalObs,
          pubReceiptStatus, verifyObs, history>>

CurrentRequest ==
    [execution |-> Len(history) + 1, unit |-> "unit-a", revision |-> "commit-a",
     inputs |-> "frozen-inputs", destination |-> "dest-primary",
     channel |-> channel, mode |-> mode, clearance |-> clearance]

LeftPrepEnvelope ==
    [unitSubject    |-> LeftUnitSubject(CurrentRequest),
     origin         |-> IF leftValid THEN "release" ELSE "CI",
     complete       |-> leftValid,
     clean          |-> leftValid,
     nativeIdentity |-> [pkgName |-> IF channel = "buddy" THEN "scoped-preview-a" ELSE "canonical-a",
                         version |-> "1.2.0", notesReady |-> TRUE],
     originals      |-> [a \in UnitArtifacts |-> <<a, "qualified-digest">>]]

TargetEnv == BindTargetEnvelope(profile, FALSE, CurrentRequest.destination, CurrentRequest, LeftPrepEnvelope, CurrentRequest.revision, UnitArtifacts)
EligibleChannels == IF refEligible THEN profile.liveChannels ELSE {}
SelectedRoute == SelectRoute(profile, CurrentRequest, TargetEnv.bound, EligibleChannels, AggregateObsState(profile, preObs))

RouteEnv ==
    [subject |-> CurrentRequest, coordinate |-> TargetEnv.coordinate, route |-> SelectedRoute,
     initialObs |-> preObs, reviewed |-> ReviewedCeiling(profile, SelectedRoute, preObs)]

PubReceiptEnv ==
    IF pubReceiptStatus = "present"
    THEN [status |-> "present", subject |-> CurrentRequest, coordinate |-> TargetEnv.coordinate,
          refreshed |-> pubRefreshed, commands |-> pubCommands, pubFinalObs |-> pubFinalObs]
    ELSE [status |-> pubReceiptStatus, subject |-> CurrentRequest, coordinate |-> TargetEnv.coordinate]

VerifyObsEnv ==
    [present |-> ~IsUnobservedMap(profile, verifyObs), subject |-> CurrentRequest,
     coordinate |-> TargetEnv.coordinate, obs |-> verifyObs]

CurrentReport == CompleteFromEnvelopes(profile, CurrentRequest, TargetEnv, RouteEnv, PubReceiptEnv, VerifyObsEnv, retained)

\* ---------------------------------------------------------------------------
\* Transitions across Host privilege contexts
\* ---------------------------------------------------------------------------
Init ==
    /\ profile \in SelectedProfiles
    /\ IF ProfileScope = "single-port-matrix" THEN
           /\ channel \in Channels /\ mode \in Modes
           /\ leftValid \in BOOLEAN /\ refEligible \in BOOLEAN
           /\ clearance \in (IF profile.readTopology = "blind-receipt" THEN {"first", "unresolved"} ELSE {"first"})
       ELSE
           /\ channel = "official" /\ mode = "live"
           /\ leftValid = TRUE /\ refEligible = TRUE /\ clearance = "first"
    /\ retained = TRUE /\ history = <<>> /\ ctx = "unpriv"
    /\ pubSubphase = "none" /\ pluginSession = {}
    /\ preObs \in (IF leftValid /\ (mode = "dry" \/ refEligible) /\ BeforeGateMode(profile, mode) = "read-only"
                   THEN ValidObsMaps(profile) ELSE {UnobservedMap(profile)})
    /\ pubRefreshed = BlankObs(profile, "unknown") /\ pending = {} /\ pubCommands = <<>>
    /\ pubFinalObs = UnobservedMap(profile)
    /\ pubReceiptStatus = (IF SelectedRoute = "publisher" THEN "awaited" ELSE "not-required")
    /\ verifyObs = UnobservedMap(profile)

BypassGate ==
    /\ ctx = "unpriv" /\ RouteEnv.route # "publisher"
    /\ ctx' = "verify" /\ pluginSession' = {}
    /\ UNCHANGED <<reqVars, clearance, retained, pubSubphase, preObs, pubRefreshed,
                   pending, pubCommands, pubFinalObs, pubReceiptStatus, verifyObs, history>>

GateNoStart ==
    /\ ctx = "unpriv" /\ RouteEnv.route = "publisher"
    /\ ctx' = "verify" /\ pubSubphase' = "none" /\ pluginSession' = {}
    /\ pubReceiptStatus' = "known-nonstart"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, pubRefreshed,
                   pending, pubCommands, pubFinalObs, verifyObs, history>>

EnterPubAndRefresh(r) ==
    /\ ctx = "unpriv" /\ RouteEnv.route = "publisher"
    /\ LET access == HasPublisherAccess(profile, CurrentRequest, RouteEnv.route, TargetEnv.bound,
                                        EligibleChannels, TRUE, TRUE, "isolated-pub-primary")
       IN  IF profile.readTopology = "blind-receipt" THEN
               /\ IsUnobservedMap(profile, r)
               /\ LET rem == IF MayMutatePostGate(profile, access, "unobserved", CurrentRequest.clearance)
                             THEN OpsOf(profile) ELSE {}
                  IN  /\ ctx' = "isolated-pub-primary" /\ pluginSession' = {}
                      /\ pubSubphase' = IF rem = {} THEN "pub-seal" ELSE "pub-write"
                      /\ pubRefreshed' = BlankObs(profile, "unknown") /\ pending' = rem
                      /\ pubFinalObs' = IF rem = {} THEN BlankObs(profile, "unknown") ELSE UnobservedMap(profile)
                      /\ pubReceiptStatus' = "present"
           ELSE
               /\ r \in ValidObsMaps(profile)
               /\ LET ok  == MayMutatePostGate(profile, access, AggregateObsState(profile, r), CurrentRequest.clearance)
                             /\ (MissingOps(profile, r) \subseteq RouteEnv.reviewed)
                      rem == IF ok THEN MissingOps(profile, r) ELSE {}
                  IN  /\ ctx' = "isolated-pub-primary"
                      /\ pluginSession' = {pre \in PrereqRootsOf(profile) : r[pre] = "established"}
                      /\ pubSubphase' = IF rem = {} THEN "pub-seal" ELSE "pub-write"
                      /\ pubRefreshed' = r /\ pending' = rem
                      /\ pubFinalObs' = UnobservedMap(profile) /\ pubReceiptStatus' = "present"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, pubCommands, verifyObs, history>>

FirstReadyIn(p, pendSet) ==
    p.ops[CHOOSE i \in 1..Len(p.ops) : p.ops[i] \in pendSet /\ \A j \in 1..(i - 1) : p.ops[j] \notin pendSet]

RightPortMutatePrecondition(p, targetEnv, routeEnv, refreshedMap, cmds, op, roleCap) ==
    /\ roleCap /\ ~LeftPortAllowedIn(ctx) /\ routeEnv.route = "publisher" /\ targetEnv.bound
    /\ op \in routeEnv.reviewed /\ op \notin IssuedOps(cmds)
    /\ \A i \in 1..Len(cmds) : cmds[i][2] = "acknowledged"
    /\ p.consumes[op] \subseteq DOMAIN targetEnv.selectedOriginals
    /\ p.prereq[op] \subseteq pluginSession
    /\ IF p.readTopology = "blind-receipt"
       THEN targetEnv.subject.clearance \in {"first", "owner-checked"}
       ELSE op \in MissingOps(p, refreshedMap)

PubInvoke(outcome, blindReceiptObs) ==
    /\ ctx = "isolated-pub-primary" /\ pubSubphase = "pub-write"
    /\ outcome \in {"acknowledged", "failed", "ambiguous"}
    /\ IF profile.readTopology # "blind-receipt" THEN blindReceiptObs = "unknown"
       ELSE blindReceiptObs \in CASE outcome = "acknowledged" -> {"established", "conflict", "unknown"}
                                  [] outcome = "failed"       -> {"conflict", "unknown"}
                                  [] OTHER                    -> {"unknown"}
    /\ LET op  == FirstReadyIn(profile, pending)
           rem == IF outcome = "acknowledged" THEN pending \ {op} ELSE {}
       IN  /\ pubCommands' = Append(pubCommands, <<op, outcome>>) /\ pending' = rem
           /\ pluginSession' = IF op \in PrereqRootsOf(profile) /\ outcome = "acknowledged"
                               THEN pluginSession \cup {op} ELSE pluginSession
           /\ pubFinalObs' = IF profile.readTopology = "blind-receipt"
                             THEN BlankObs(profile, blindReceiptObs) ELSE pubFinalObs
           /\ pubSubphase' = IF rem = {} THEN "pub-seal" ELSE "pub-write"
    /\ UNCHANGED <<reqVars, clearance, retained, ctx, preObs, pubRefreshed, pubReceiptStatus, verifyObs, history>>

PubSealAndExit(pubRead) ==
    /\ ctx = "isolated-pub-primary" /\ pubSubphase = "pub-seal"
    /\ IF profile.readTopology = "publisher-only" THEN
           /\ IF pubCommands = <<>> THEN pubRead = pubRefreshed ELSE pubRead \in ValidObsMaps(profile)
           /\ pubFinalObs' = pubRead
       ELSE IsUnobservedMap(profile, pubRead) /\ pubFinalObs' = pubFinalObs
    /\ ctx' = "verify" /\ pubSubphase' = "none" /\ pluginSession' = {}
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, pubRefreshed, pending, pubCommands, pubReceiptStatus, verifyObs, history>>

LosePubReceipt ==
    /\ RouteEnv.route = "publisher" /\ pubReceiptStatus \in {"awaited", "present"}
    /\ (ctx \in {"unpriv", "isolated-pub-primary"} \/ (ctx = "verify" /\ pubReceiptStatus = "present"))
    /\ ctx' = "verify" /\ pubSubphase' = "none" /\ pluginSession' = {} /\ pending' = {}
    /\ pubReceiptStatus' = "lost"
    /\ UNCHANGED <<reqVars, clearance, retained, preObs, pubRefreshed, pubCommands, pubFinalObs, verifyObs, history>>

VerifierShouldRead ==
    /\ TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest)
    /\ refEligible /\ mode = "live" /\ CompletionReader(profile) = "verifier"
    /\ (ProfileScope # "single-port-matrix" \/ RouteEnv.route # "blocked")

CanInjectVerifyFaults ==
    ProfileScope = "single-port-matrix" /\ channel = "official" /\ mode = "live" /\ TargetEnv.bound /\ refEligible

RetainedChoices == IF CanInjectVerifyFaults THEN BOOLEAN ELSE {TRUE}
VerifyObsChoices ==
    IF VerifierShouldRead
    THEN ValidObsMaps(profile) \cup (IF CanInjectVerifyFaults THEN {UnobservedMap(profile)} ELSE {})
    ELSE {UnobservedMap(profile)}

VerifyStep(vObs, retFlag) ==
    /\ ctx = "verify" /\ retFlag \in RetainedChoices /\ vObs \in VerifyObsChoices
    /\ verifyObs' = vObs /\ retained' = retFlag /\ ctx' = "done" /\ pluginSession' = {}
    /\ UNCHANGED <<reqVars, clearance, pubSubphase, preObs, pubRefreshed, pending, pubCommands, pubFinalObs, pubReceiptStatus, history>>

\* ---------------------------------------------------------------------------
\* Bounded 2-request manual recovery across all four single-port recovery seams
\* ---------------------------------------------------------------------------
CanRecoverBase ==
    ctx = "done" /\ Len(history) = 0 /\ retained /\ mode = "live" /\
    TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\ refEligible

CanRecoverOrderedPair ==
    /\ CanRecoverBase /\ profile = ByteOrderedPair /\ pubReceiptStatus = "present"
    /\ preObs = BlankObs(profile, "missing") /\ pubRefreshed = preObs
    /\ pubCommands = <<<<"op1", "ambiguous">>>> /\ verifyObs = BlankObs(profile, "missing")

CanRecoverContainerPartial ==
    /\ CanRecoverBase /\ ProfileScope = "single-port-matrix" /\ profile = TwoAssetContainer
    /\ preObs = BlankObs(profile, "missing") /\ pubRefreshed = preObs /\ pubReceiptStatus = "present"
    /\ pubCommands = <<<<"op0", "acknowledged">>, <<"op1", "acknowledged">>, <<"op2", "ambiguous">>>>
    /\ verifyObs = [op \in OpsOf(profile) |-> IF op = "op2" THEN "missing" ELSE "established"]

CanRecoverGatedStore ==
    CanRecoverBase /\ ProfileScope = "single-port-matrix" /\ profile = GatedReadBundledStore /\ CurrentReport.satisfaction = "unknown"

CanRecoverBlindStore ==
    CanRecoverBase /\ ProfileScope = "single-port-matrix" /\ profile = BlindReceiptStore /\
    CurrentRequest.clearance = "first" /\ CurrentReport.satisfaction = "unknown"

CanRecoverAny == CanRecoverOrderedPair \/ CanRecoverContainerPartial \/ CanRecoverGatedStore \/ CanRecoverBlindStore

SavedReportEntry(rep) ==
    IF ProfileScope # "single-port-matrix" THEN rep
    ELSE [rep EXCEPT !.initial = UnobservedMap(profile), !.publisherRead = UnobservedMap(profile),
                     !.observed = UnobservedMap(profile),
                     !.outcomes = IF Len(profile.ops) = 1 THEN rep.outcomes ELSE <<>>,
                     !.uncertainCalls = IF rep.uncertainCalls # {} THEN {profile.ops[1]} ELSE {}]

RecoveryPreObsChoices ==
    IF BeforeGateMode(profile, "live") = "unobserved" THEN {UnobservedMap(profile)} ELSE ValidObsMaps(profile)

NewManualDispatch(reproducesAll, nextClearance, nextPreObs) ==
    /\ CanRecoverAny
    /\ LET nextReq == [CurrentRequest EXCEPT !.execution = 2, !.clearance = nextClearance]
       IN  /\ ValidRecoveryDispatch(CurrentRequest, nextReq, "manual-dispatch", reproducesAll)
           /\ IF profile.readTopology = "blind-receipt"
              THEN nextClearance \in {"unresolved", "owner-checked"} /\ IsUnobservedMap(profile, nextPreObs)
              ELSE nextClearance = "first" /\ nextPreObs \in RecoveryPreObsChoices
           /\ LET nextRoute == SelectRoute(profile, nextReq, TRUE, EligibleChannels, AggregateObsState(profile, nextPreObs))
              IN  /\ history' = <<SavedReportEntry(CurrentReport)>> /\ clearance' = nextClearance
                  /\ retained' = TRUE /\ ctx' = "unpriv" /\ pubSubphase' = "none" /\ pluginSession' = {}
                  /\ preObs' = nextPreObs /\ pubRefreshed' = BlankObs(profile, "unknown")
                  /\ pending' = {} /\ pubCommands' = <<>> /\ pubFinalObs' = UnobservedMap(profile)
                  /\ pubReceiptStatus' = (IF nextRoute = "publisher" THEN "awaited" ELSE "not-required")
                  /\ verifyObs' = UnobservedMap(profile)
    /\ UNCHANGED reqVars

Next ==
    \/ (ctx = "unpriv" /\
        ( BypassGate \/ GateNoStart \/ LosePubReceipt
          \/ (profile.readTopology = "blind-receipt" /\ EnterPubAndRefresh(UnobservedMap(profile)))
          \/ (profile.readTopology # "blind-receipt" /\ \E r \in ValidObsMaps(profile) : EnterPubAndRefresh(r)) ))
    \/ (ctx = "isolated-pub-primary" /\
        ( LosePubReceipt
          \/ (pubSubphase = "pub-write" /\
              \E out \in {"acknowledged", "failed", "ambiguous"}, bro \in {"established", "conflict", "unknown"} :
                PubInvoke(out, bro))
          \/ (pubSubphase = "pub-seal" /\
              ( PubSealAndExit(UnobservedMap(profile))
                \/ (profile.readTopology = "publisher-only" /\ \E r \in ValidObsMaps(profile) : PubSealAndExit(r)) )) ))
    \/ (ctx = "verify" /\ (LosePubReceipt \/ \E vObs \in VerifyObsChoices, ret \in RetainedChoices : VerifyStep(vObs, ret)))
    \/ (ctx = "done" /\
        ( UNCHANGED vars
          \/ (CanRecoverAny /\ \E nc \in Clearances, np \in RecoveryPreObsChoices : NewManualDispatch(TRUE, nc, np)) ))

Spec == Init /\ [][Next]_vars

\* ---------------------------------------------------------------------------
\* Invariants & counterexample/witness predicates
\* ---------------------------------------------------------------------------
CapabilityAndSessionIsolation ==
    /\ (AnyWriteCapIn(ctx) => ~LeftPortAllowedIn(ctx)) /\ ~AnchorWriteCapIn(ctx)
    /\ (ctx # "isolated-pub-primary" => pluginSession = {} /\ ~AnyWriteCapIn(ctx))
    /\ (ctx = "isolated-pub-primary" => pluginSession \subseteq PrereqRootsOf(profile))
    /\ ((mode = "dry" \/ ~TargetEnv.bound) => ~AnyWriteCapIn(ctx) /\ pubCommands = <<>>)
    /\ ((mode = "live" /\ ~refEligible) => RouteEnv.route = "blocked" /\ ~AnyWriteCapIn(ctx) /\ pubCommands = <<>>)
    /\ ((profile.readTopology = "blind-receipt" /\ mode = "live" /\ TargetEnv.bound /\
         AdmittedRequest(profile, CurrentRequest) /\ refEligible /\ CurrentRequest.clearance = "unresolved") =>
          RouteEnv.route = "unresolved" /\ ctx # "isolated-pub-primary" /\ pubCommands = <<>>)
    /\ (BeforeGateMode(profile, mode) = "unobserved" => IsUnobservedMap(profile, preObs))
    /\ ((CompletionReader(profile) = "publisher" \/ mode = "dry") => IsUnobservedMap(profile, verifyObs))

CallerAssumeGuarantee ==
    /\ (ctx = "isolated-pub-primary" /\ pubSubphase = "pub-write" =>
          pending # {} /\ RightPortMutatePrecondition(profile, TargetEnv, RouteEnv, pubRefreshed,
                                                      pubCommands, FirstReadyIn(profile, pending), PrimaryWriteCapIn(ctx)))
    /\ IssuedOps(pubCommands) \subseteq RouteEnv.reviewed
    /\ Len(pubCommands) = Cardinality(IssuedOps(pubCommands))
    /\ \A i \in 1..(Len(pubCommands) - 1) : pubCommands[i][2] = "acknowledged"
    /\ (profile.readTopology # "blind-receipt" =>
          \A i, j \in 1..Len(profile.ops) :
            (i < j /\ profile.ops[i] \in MissingOps(profile, pubRefreshed) /\ profile.ops[j] \in IssuedOps(pubCommands)) =>
              \E a, b \in 1..Len(pubCommands) : a < b /\ pubCommands[a][1] = profile.ops[i] /\ pubCommands[b][1] = profile.ops[j])

PubReceiptGuarantee ==
    (pubSubphase = "none" /\ pubReceiptStatus = "present") =>
      IsValidSealedPubReceipt(profile, CurrentRequest, TargetEnv, RouteEnv, PubReceiptEnv)

PermittedProgress ==
    /\ (ctx = "isolated-pub-primary" /\ pubSubphase = "pub-write" =>
          ENABLED PubInvoke("acknowledged", IF profile.readTopology = "blind-receipt" THEN "established" ELSE "unknown"))
    /\ ~ValidRecoveryDispatch(CurrentRequest, [CurrentRequest EXCEPT !.execution = 2], "manual-dispatch", FALSE)

EnvelopeEvidenceDiscipline ==
    ctx = "done" =>
      LET rep == CurrentReport
      IN  /\ (mode = "dry" /\ TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\ retained =>
                rep.satisfaction = "simulation" /\ rep.attempted = "no")
          /\ (mode = "live" => rep.hypotheticalOps = {})
          /\ ((~TargetEnv.bound \/ ~AdmittedRequest(profile, CurrentRequest) \/ (mode = "live" /\ ~refEligible)) =>
                rep.satisfaction = "unsatisfied" /\ rep.attempted = "no")
          /\ (~retained /\ TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\ (mode = "dry" \/ refEligible) =>
                rep.satisfaction # "satisfied" /\ (pubReceiptStatus = "present" /\ pubCommands # <<>> => rep.attempted = "yes"))
          /\ (pubReceiptStatus = "lost" =>
                rep.attempted = "unknown" /\ ~rep.outcomesAvailable /\ rep.outcomes = <<>> /\ rep.satisfaction # "satisfied")
          /\ (retained /\ rep.attempted = "yes" /\ rep.satisfaction = "unknown" /\ profile.readTopology = "blind-receipt" =>
                rep.uncertainCalls # {})
          /\ (rep.satisfaction = "satisfied" =>
                retained /\ mode = "live" /\ refEligible /\ TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\
                AggregateObsState(profile, rep.observed) = ProofToken(profile.proofMode))

RecoveryHistoryDiscipline ==
    Len(history) = 1 => history[1].execution = 1 /\ history[1].satisfaction # "satisfied" /\ CurrentRequest.execution = 2

GatedReadAloneImpliesUnobservedWriteSafe ==
    (mode = "live" /\ refEligible /\ TargetEnv.bound /\ AdmittedRequest(profile, CurrentRequest) /\
     BeforeGateMode(profile, "live") = "unobserved") => MayMutatePostGate(profile, TRUE, "unobserved", "first")

PreGateObservationSufficesWithoutRefresh ==
    ~(ctx = "isolated-pub-primary" /\ pubSubphase = "pub-seal" /\ profile = TwoAssetContainer /\
      preObs = BlankObs(profile, "missing") /\ pubRefreshed["op0"] = "established" /\
      HasObsValue(profile, pubRefreshed, "conflict") /\ "op0" \in pluginSession /\ pubCommands = <<>>)

NoGatedStoreZeroWriteSatisfaction ==
    ~(ctx = "done" /\ profile = GatedReadBundledStore /\ mode = "live" /\ retained /\
      IsUnobservedMap(profile, preObs) /\ pubReceiptStatus = "present" /\ pubCommands = <<>> /\
      IsUnobservedMap(profile, verifyObs) /\ CurrentReport.attempted = "no" /\ CurrentReport.satisfaction = "satisfied")

NoBlindOwnerCheckedRecovery ==
    ~(ctx = "done" /\ Len(history) = 1 /\ retained /\ profile = BlindReceiptStore /\
      CurrentRequest.clearance = "owner-checked" /\ history[1].publisherRecord = "present" /\
      history[1].outcomes = <<<<"op1", "acknowledged">>>> /\ history[1].uncertainCalls # {} /\
      history[1].satisfaction = "unknown" /\ CurrentReport.outcomes = <<<<"op1", "acknowledged">>>> /\
      CurrentReport.satisfaction = "satisfied")

NoContainerRebuildByteConflictBlocksRecovery ==
    ~(ctx = "done" /\ Len(history) = 1 /\ retained /\ profile = TwoAssetContainer /\
      preObs["op0"] = "established" /\ preObs["op1"] = "conflict" /\ pubCommands = <<>> /\
      CurrentReport.attempted = "no" /\ CurrentReport.satisfaction = "unsatisfied")
=============================================================================
