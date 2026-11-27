------------------------ MODULE DeliveryPortAlgebra ------------------------
\* Stateless Narrow-Waist Algebra (Core): domain lattices, general finite
\* operation posets, minimal cross-context envelopes, multi-provider CI
\* planning/join, and single- or dual-RightPort Release laws (including
\* quotient bisimulation over observation maps and sealed publisher receipts).
\* Zero concrete ecosystem/destination names and zero state variables.
EXTENDS Naturals, Sequences, FiniteSets

Channels == {"buddy", "official"}
Modes == {"dry", "live"}
Contexts == {"unpriv", "isolated-pub-primary", "anchor-gate",
             "isolated-pub-anchor", "verify", "done", "recover"}

LeftPortAllowedIn(ctx) == ctx = "unpriv"
PrimaryWriteCapIn(ctx) == ctx = "isolated-pub-primary"
AnchorWriteCapIn(ctx) == ctx = "isolated-pub-anchor"
AnyWriteCapIn(ctx) == PrimaryWriteCapIn(ctx) \/ AnchorWriteCapIn(ctx)

\* ---------------------------------------------------------------------------
\* CI Narrow-Waist Algebra: self-contained multi-provider planning & 2-level join
\* ---------------------------------------------------------------------------
RECURSIVE ReachFix(_, _, _)
ReachFix(edges, frontier, budget) ==
    IF budget = 0 THEN frontier
    ELSE LET nxt == frontier \cup {e[2] : e \in {ed \in edges : ed[1] \in frontier}}
         IN  IF nxt = frontier THEN frontier ELSE ReachFix(edges, nxt, budget - 1)

PairedClosure(allNodes, basisEdges, candEdges, seeds) ==
    ReachFix(basisEdges \cup candEdges, seeds, Cardinality(allNodes))

ComposeCiPlan(mode, changedPaths, expectedSubject, expectedProviders, leftEnvs) ==
    LET provs == DOMAIN leftEnvs
        subjectsOk ==
            /\ expectedProviders # {} /\ provs = expectedProviders
            /\ \A m \in expectedProviders :
                 /\ leftEnvs[m].provider = m /\ leftEnvs[m].complete
                 /\ leftEnvs[m].subject = expectedSubject
                 /\ DOMAIN leftEnvs[m].pathConsumers = changedPaths
                 /\ leftEnvs[m].accountedPaths \subseteq changedPaths
                 /\ DOMAIN leftEnvs[m].nodeChecks = leftEnvs[m].candidateNodes
        pathsAccounted ==
            \A p \in changedPaths : \E m \in provs :
              leftEnvs[m].pathConsumers[p] # {} \/ p \in leftEnvs[m].accountedPaths
        allSpecs ==
            UNION {(UNION {leftEnvs[m].nodeChecks[u] : u \in leftEnvs[m].candidateNodes})
                   \cup leftEnvs[m].advisoryChecks : m \in provs}
        noSpecConflict == \A s1, s2 \in allSpecs : s1.id = s2.id => s1.partition = s2.partition
    IN  IF ~(subjectsOk /\ pathsAccounted /\ noSpecConflict) THEN
            [scope |-> "unresolved", projects |-> {}, work |-> {},
             advisory |-> {}, groups |-> {}, partitionMap |-> [k \in {} |-> "none"]]
        ELSE
            LET seeds == UNION {leftEnvs[m].pathConsumers[p] : m \in provs, p \in changedPaths}
                bEdges == UNION {leftEnvs[m].basisEdges : m \in provs}
                cEdges == UNION {leftEnvs[m].candEdges : m \in provs}
                candNodes == UNION {leftEnvs[m].candidateNodes : m \in provs}
                allNodes == candNodes \cup seeds
                            \cup {e[1] : e \in bEdges \cup cEdges}
                            \cup {e[2] : e \in bEdges \cup cEdges}
                SpecsOf(u) == UNION {IF u \in leftEnvs[m].candidateNodes
                                     THEN leftEnvs[m].nodeChecks[u] ELSE {} : m \in provs}
                ReqSpecsOf(u) == {sp \in SpecsOf(u) : sp.required}
                runnable == {u \in candNodes : ReqSpecsOf(u) # {}}
                selected == IF mode = "full" THEN runnable
                            ELSE PairedClosure(allNodes, bEdges, cEdges, seeds) \cap runnable
                selReq == UNION {ReqSpecsOf(u) : u \in selected}
                selAdv == UNION {leftEnvs[m].advisoryChecks : m \in provs}
                          \cup {sp \in UNION {SpecsOf(u) : u \in selected} : ~sp.required}
                reqWork == {sp.id : sp \in selReq}
                advWork == {sp.id : sp \in selAdv} \ reqWork
                combined == selReq \cup selAdv
                partMap == [k \in reqWork \cup advWork |->
                              (CHOOSE sp \in combined : sp.id = k).partition]
                groups == UNION {leftEnvs[m].baselineGroups : m \in provs}
                          \cup {partMap[k] : k \in reqWork}
            IN  [scope |-> mode, projects |-> selected, work |-> reqWork,
                 advisory |-> advWork, groups |-> groups, partitionMap |-> partMap]

AllowedChecksInGroup(pEnv, g) ==
    {k \in DOMAIN pEnv.partitionMap : pEnv.partitionMap[k] = g}

ValidProducerPartitionJoin(plannedGroups, planSubject, pEnv, groupEnvs) ==
    /\ \A g \in plannedGroups :
         LET matching == {ge \in groupEnvs : ge.group = g}
         IN  \E ge \in matching :
               matching = {ge} /\ ge.subject = planSubject /\
               \A r \in ge.receipts : r.check \in AllowedChecksInGroup(pEnv, g)
    /\ \A ge \in groupEnvs : ge.group \in plannedGroups

ValidRequiredCheckJoin(requiredWork, planSubject, groupEnvs) ==
    LET all == UNION {ge.receipts : ge \in groupEnvs}
    IN  \A k \in requiredWork :
          LET matching == {r \in all : r.check = k}
          IN  \E r \in matching :
                matching = {r} /\ r.subject = planSubject /\ r.outcome = "passed"

\* ---------------------------------------------------------------------------
\* General finite operation posets & observation lattices
\* ---------------------------------------------------------------------------
ReadTopologies == {"unpriv-always", "unpriv-live-only", "publisher-only", "blind-receipt"}
ProofModes == {"served-bytes", "version-available", "store-accepted"}
Clearances == {"first", "owner-checked", "unresolved"}
ChannelPolicies ==
    {[live |-> l, dry |-> d] : l \in (SUBSET Channels) \ {{}}, d \in SUBSET Channels}
ValidChannelPolicies == {cp \in ChannelPolicies : cp.live \subseteq cp.dry}

OpsOf(p) == {p.ops[i] : i \in 1..Len(p.ops)}
TargetArtifactsOf(p) == UNION {p.consumes[op] : op \in OpsOf(p)}
PrereqRootsOf(p) == UNION {p.prereq[op] : op \in OpsOf(p)}

MakeRightProfile(live, dry, rt, pm, ops, prereq, consumes, subsets) ==
    [liveChannels |-> live, dryChannels |-> dry, readTopology |-> rt,
     proofMode |-> pm, ops |-> ops, prereq |-> prereq,
     consumes |-> consumes, completableSubsets |-> subsets]

SingleOpProfile(live, dry, rt, pm, opId, arts) ==
    MakeRightProfile(live, dry, rt, pm, <<opId>>,
                     [op \in {opId} |-> {}], [op \in {opId} |-> arts], {{opId}})

IsValidRightProfile(p, unitArtifacts) ==
    LET ops == OpsOf(p)
    IN  /\ p.liveChannels # {} /\ p.liveChannels \subseteq p.dryChannels
        /\ p.dryChannels \subseteq Channels
        /\ p.readTopology \in ReadTopologies /\ p.proofMode \in ProofModes
        /\ Len(p.ops) >= 1 /\ Cardinality(ops) = Len(p.ops)
        /\ DOMAIN p.prereq = ops /\ DOMAIN p.consumes = ops
        /\ \A i \in 1..Len(p.ops) : p.prereq[p.ops[i]] \subseteq {p.ops[j] : j \in 1..(i - 1)}
        /\ \A op \in ops : p.consumes[op] \subseteq unitArtifacts
        /\ TargetArtifactsOf(p) # {}
        /\ ops \in p.completableSubsets /\ {} \notin p.completableSubsets
        /\ \A S \in p.completableSubsets :
             S \subseteq ops /\ \A op \in ops : (\E pre \in p.prereq[op] : pre \in S) => op \in S
        /\ (Len(p.ops) = 1 => p.completableSubsets = {ops})
        /\ (p.readTopology = "blind-receipt" => Len(p.ops) = 1 /\ p.proofMode # "served-bytes")
        /\ (PrereqRootsOf(p) # {} => p.readTopology # "blind-receipt" /\ p.proofMode = "served-bytes")

ProofToken(pm) ==
    CASE pm = "version-available" -> "version-available"
      [] pm = "store-accepted"    -> "version-received"
      [] OTHER                    -> "served-originals"

CompletionReader(p) ==
    IF p.readTopology \in {"publisher-only", "blind-receipt"} THEN "publisher" ELSE "verifier"

BeforeGateMode(p, mode) ==
    IF p.readTopology \in {"publisher-only", "blind-receipt"}
       \/ (p.readTopology = "unpriv-live-only" /\ mode = "dry")
    THEN "unobserved" ELSE "read-only"

EvaluatePositiveProof(pm, coordMatch, metadataMatch, bytesMatch, status) ==
    IF coordMatch /\
       CASE pm = "version-available" -> status = "available"
         [] pm = "store-accepted"    -> status \in {"received", "in-review", "published"}
         [] OTHER                    -> metadataMatch /\ bytesMatch
    THEN ProofToken(pm) ELSE "no-proof"

RawOpStatuses == {"absent", "available", "uploaded", "received",
                  "in-review", "published", "rejected", "unknown"}
RawOpEvidenceSpace ==
    [present : BOOLEAN, coordMatch : BOOLEAN, metadataMatch : BOOLEAN,
     bytesMatch : BOOLEAN, status : RawOpStatuses]

ClassifyRawOpObs(pm, ev) ==
    IF ~ev.coordMatch THEN IF ev.present THEN "conflict" ELSE "unknown"
    ELSE IF ~ev.present THEN IF ev.status = "absent" THEN "missing" ELSE "unknown"
    ELSE IF ev.status = "absent" THEN "unknown"
    ELSE IF EvaluatePositiveProof(pm, ev.coordMatch, ev.metadataMatch,
                                  ev.bytesMatch, ev.status) = ProofToken(pm)
         THEN "established"
    ELSE IF ev.status = "rejected" \/ (pm = "served-bytes" /\ ev.metadataMatch /\ ~ev.bytesMatch)
         THEN "conflict"
    ELSE "unknown"

OpObsValues == {"missing", "established", "conflict", "unknown"}
BlankObs(p, val) == [op \in OpsOf(p) |-> val]
UnobservedMap(p) == BlankObs(p, "unobserved")
IsUnobservedMap(p, obs) == obs = UnobservedMap(p)

ValidObsMaps(p) ==
    {r \in [OpsOf(p) -> OpObsValues] :
       \A op \in OpsOf(p) : (\E pre \in p.prereq[op] : r[pre] = "missing") => r[op] = "missing"}

MissingOps(p, obs) == IF IsUnobservedMap(p, obs) THEN {} ELSE {op \in OpsOf(p) : obs[op] = "missing"}
HasObsValue(p, obs, val) == ~IsUnobservedMap(p, obs) /\ \E op \in OpsOf(p) : obs[op] = val

AggregateObsState(p, obs) ==
    IF IsUnobservedMap(p, obs) THEN "unobserved"
    ELSE IF HasObsValue(p, obs, "conflict") THEN "conflict"
    ELSE IF HasObsValue(p, obs, "unknown") THEN "unknown"
    ELSE IF MissingOps(p, obs) = {} THEN ProofToken(p.proofMode)
    ELSE IF MissingOps(p, obs) \in p.completableSubsets THEN "missing"
    ELSE "unsupported-subset"

\* Exact congruence kernel of an observation map under all Core operators.
ObsKernel(p, m) ==
    LET st == AggregateObsState(p, m)
    IN  <<st, IF st = "missing" THEN MissingOps(p, m) ELSE {}>>

QuotientObsMaps(p) ==
    LET valid   == ValidObsMaps(p)
        kernels == {ObsKernel(p, m) : m \in valid}
    IN  {(CHOOSE m \in valid : ObsKernel(p, m) = k) : k \in kernels}

\* ---------------------------------------------------------------------------
\* Bilateral Release binding, routing, single-port completion & quotients
\* ---------------------------------------------------------------------------
LeftUnitSubject(req) ==
    [execution |-> req.execution, unit |-> req.unit, revision |-> req.revision,
     inputs |-> req.inputs, channel |-> req.channel, mode |-> req.mode]

ValidLeftPrepEnvelope(req, leftPrep, controlRev, unitArtifacts) ==
    /\ leftPrep.unitSubject = LeftUnitSubject(req) /\ controlRev = req.revision
    /\ leftPrep.origin = "release" /\ leftPrep.complete /\ leftPrep.clean
    /\ DOMAIN leftPrep.originals = unitArtifacts /\ unitArtifacts # {}

ResolveRightCoordinate(destId, channel, nativeIdentity) ==
    [feed |-> destId, pkg |-> <<destId, channel, nativeIdentity.pkgName>>,
     version |-> nativeIdentity.version]

BindTargetEnvelope(p, requiresNotes, destId, req, leftPrep, controlRev, unitArtifacts) ==
    LET tArts == TargetArtifactsOf(p)
        ok == destId = req.destination /\ ValidLeftPrepEnvelope(req, leftPrep, controlRev, unitArtifacts)
              /\ (~requiresNotes \/ leftPrep.nativeIdentity.notesReady)
              /\ (tArts \subseteq DOMAIN leftPrep.originals)
    IN  [subject     |-> req,
         destination |-> destId,
         coordinate  |-> ResolveRightCoordinate(destId, req.channel, leftPrep.nativeIdentity),
         bound       |-> ok,
         notesReady  |-> leftPrep.nativeIdentity.notesReady,
         selectedOriginals |->
           IF tArts \subseteq DOMAIN leftPrep.originals
           THEN [a \in tArts |-> leftPrep.originals[a]] ELSE [a \in {} |-> <<"none", "none">>]]

AdmittedRequest(p, req) ==
    /\ req.channel \in Channels /\ req.mode \in Modes /\ req.clearance \in Clearances
    /\ req.channel \in (IF req.mode = "live" THEN p.liveChannels ELSE p.dryChannels)

ValidRecoveryDispatch(oldReq, newReq, trigger, reproducesAllOriginals) ==
    /\ trigger = "manual-dispatch" /\ newReq.mode = "live" /\ oldReq.execution # newReq.execution
    /\ newReq.clearance \in Clearances /\ reproducesAllOriginals
    /\ [oldReq EXCEPT !.execution = newReq.execution, !.clearance = newReq.clearance] = newReq

SelectRoute(p, req, boundOk, eligibleRefs, initialState) ==
    IF ~boundOk \/ ~AdmittedRequest(p, req) THEN "blocked"
    ELSE IF req.mode = "dry" THEN
        IF BeforeGateMode(p, "dry") = "unobserved" /\ initialState # "unobserved"
        THEN "blocked" ELSE "simulation"
    ELSE IF req.channel \notin eligibleRefs THEN "blocked"
    ELSE IF p.readTopology = "blind-receipt" THEN
        IF initialState # "unobserved" THEN "blocked"
        ELSE IF req.clearance \in {"first", "owner-checked"} THEN "publisher" ELSE "unresolved"
    ELSE IF p.readTopology = "publisher-only" THEN
        IF initialState = "unobserved" THEN "publisher" ELSE "blocked"
    ELSE CASE initialState = ProofToken(p.proofMode)    -> "verify-without-write"
           [] initialState = "missing"                  -> "publisher"
           [] initialState \in {"unknown", "unobserved"} -> "unresolved"
           [] OTHER                                     -> "blocked"

ReviewedCeiling(p, route, preObs) ==
    IF route # "publisher" THEN {}
    ELSE IF IsUnobservedMap(p, preObs) THEN OpsOf(p) ELSE MissingOps(p, preObs)

HypotheticalOps(p, route, preObs) ==
    IF route # "simulation" THEN {}
    ELSE IF IsUnobservedMap(p, preObs) THEN OpsOf(p)
    ELSE IF AggregateObsState(p, preObs) = "missing" THEN MissingOps(p, preObs) ELSE {}

HasPublisherAccess(p, req, route, boundOk, eligibleRefs, nativeGrant, hasRoleWriteCap, ctx) ==
    /\ route = "publisher" /\ req.mode = "live" /\ AdmittedRequest(p, req)
    /\ boundOk /\ req.channel \in eligibleRefs /\ nativeGrant
    /\ hasRoleWriteCap /\ ~LeftPortAllowedIn(ctx)

MayMutatePostGate(p, access, freshState, clearance) ==
    /\ access
    /\ IF p.readTopology = "blind-receipt"
       THEN freshState = "unobserved" /\ clearance \in {"first", "owner-checked"}
       ELSE freshState = "missing"

IssuedOps(cmds) == {cmds[i][1] : i \in 1..Len(cmds)}
OpsWithOutcome(cmds, out) == {op \in IssuedOps(cmds) : \E i \in 1..Len(cmds) : cmds[i] = <<op, out>>}

EvaluateConclusion(p, route, retained, receiptStatus, readerRole, finalEvidence) ==
    IF route = "blocked" THEN "unsatisfied"
    ELSE IF ~retained THEN "unknown"
    ELSE IF route = "simulation" THEN "simulation"
    ELSE IF p.proofMode = "served-bytes" /\ readerRole = CompletionReader(p) /\ finalEvidence = "conflict"
         THEN "unsatisfied"
    ELSE IF route = "unresolved" THEN "unknown"
    ELSE IF route = "publisher" /\ receiptStatus # "present" THEN
        IF receiptStatus = "known-nonstart" THEN "unsatisfied" ELSE "unknown"
    ELSE IF readerRole # CompletionReader(p) THEN "unknown"
    ELSE CASE finalEvidence = ProofToken(p.proofMode)                  -> "satisfied"
           [] finalEvidence \in {"conflict", "established-incomplete"} -> "unsatisfied"
           [] OTHER                                                    -> "unknown"

EvaluateAttempted(route, receiptStatus, returnedCmds) ==
    IF route # "publisher" THEN "no"
    ELSE CASE receiptStatus = "known-nonstart" -> "no"
           [] receiptStatus = "present"        -> IF returnedCmds = <<>> THEN "no" ELSE "yes"
           [] OTHER                            -> "unknown"

CompleteFromEnvelopes(p, req, targetEnv, routeEnv, pubReceiptEnv, verifyObsEnv, retained) ==
    LET tBound  == targetEnv.subject = req
        rtBound == tBound /\ routeEnv.subject = req /\ routeEnv.coordinate = targetEnv.coordinate
        route   == IF ~targetEnv.bound /\ tBound THEN "blocked"
                   ELSE IF rtBound THEN routeEnv.route ELSE "unresolved"
        ceil    == IF rtBound THEN routeEnv.reviewed ELSE OpsOf(p)
        recSt   == IF tBound /\ pubReceiptEnv.subject = req /\
                      pubReceiptEnv.coordinate = targetEnv.coordinate
                   THEN pubReceiptEnv.status ELSE "lost"
        vBound  == tBound /\ verifyObsEnv.present /\ verifyObsEnv.subject = req /\
                   verifyObsEnv.coordinate = targetEnv.coordinate
        cmds    == IF recSt = "present" THEN pubReceiptEnv.commands ELSE <<>>
        att     == IF rtBound /\ retained THEN EvaluateAttempted(route, recSt, cmds)
                   ELSE CASE recSt = "present" /\ cmds # <<>> -> "yes"
                          [] rtBound /\ route # "publisher"   -> "no"
                          [] recSt = "known-nonstart"         -> "no"
                          [] OTHER                            -> "unknown"
        authObs == IF CompletionReader(p) = "verifier"
                   THEN IF vBound THEN verifyObsEnv.obs ELSE UnobservedMap(p)
                   ELSE IF recSt = "present" THEN pubReceiptEnv.pubFinalObs ELSE UnobservedMap(p)
        reader  == IF CompletionReader(p) = "verifier" /\ vBound THEN "verifier"
                   ELSE IF CompletionReader(p) = "publisher" /\ recSt = "present" THEN "publisher"
                   ELSE "none"
        rawSt   == AggregateObsState(p, authObs)
        finalEv == IF IsUnobservedMap(p, authObs) THEN "unknown"
                   ELSE IF rawSt # "missing" THEN rawSt
                   ELSE IF MissingOps(p, authObs) \cap IssuedOps(cmds) # {}
                        THEN "unknown" ELSE "established-incomplete"
        initObs == IF rtBound THEN routeEnv.initialObs ELSE UnobservedMap(p)
        blindUnc == IF p.readTopology # "blind-receipt" THEN {}
                    ELSE IF IsUnobservedMap(p, authObs) THEN IssuedOps(cmds)
                    ELSE {op \in IssuedOps(cmds) : authObs[op] # "established"}
        unc     == IF recSt = "lost"
                   THEN IF (rtBound /\ route = "publisher") \/ ~rtBound THEN ceil ELSE {}
                   ELSE (IssuedOps(cmds) \ OpsWithOutcome(cmds, "acknowledged")) \cup blindUnc
    IN  [execution         |-> req.execution,
         initial           |-> initObs,
         hypotheticalOps   |-> IF rtBound /\ retained THEN HypotheticalOps(p, route, initObs) ELSE {},
         observed          |-> authObs,
         publisherRead     |-> IF recSt = "present" THEN pubReceiptEnv.refreshed ELSE BlankObs(p, "unknown"),
         publisherRecord   |-> recSt,
         satisfaction      |-> EvaluateConclusion(p, route, retained /\ rtBound, recSt, reader, finalEv),
         attempted         |-> att,
         outcomesAvailable |-> recSt # "lost",
         outcomes          |-> cmds,
         uncertainCalls    |-> unc]

\* ---------------------------------------------------------------------------
\* Assume-Guarantee publisher receipt law, Official anchor gate & quotient
\* ---------------------------------------------------------------------------
ValidPublisherCmdSeqs(p, remSet) ==
    LET seq == SelectSeq(p.ops, LAMBDA op : op \in remSet)
        n   == Len(seq)
    IN  IF n = 0 THEN {<<>>}
        ELSE {[i \in 1..n |-> <<seq[i], "acknowledged">>]}
             \cup {[i \in 1..s |-> IF i < s THEN <<seq[i], "acknowledged">>
                                   ELSE <<seq[s], abnormal>>] :
                     s \in 1..n, abnormal \in {"failed", "ambiguous"}}

IsValidSealedPubReceipt(p, req, targetEnv, routeEnv, rec) ==
    /\ rec.status = "present" /\ rec.subject = req
    /\ rec.coordinate = targetEnv.coordinate /\ routeEnv.route = "publisher"
    /\ IF p.readTopology = "blind-receipt" THEN
           /\ rec.refreshed = BlankObs(p, "unknown")
           /\ rec.commands \in ValidPublisherCmdSeqs(p, OpsOf(p))
           /\ LET lastOut == rec.commands[Len(rec.commands)][2]
                  allowed == CASE lastOut = "acknowledged" -> {"established", "conflict", "unknown"}
                               [] lastOut = "failed"       -> {"conflict", "unknown"}
                               [] OTHER                    -> {"unknown"}
              IN  rec.pubFinalObs \in {BlankObs(p, v) : v \in allowed}
       ELSE
           /\ rec.refreshed \in ValidObsMaps(p)
           /\ LET canWrite == MayMutatePostGate(p, TRUE, AggregateObsState(p, rec.refreshed), req.clearance)
                              /\ (MissingOps(p, rec.refreshed) \subseteq routeEnv.reviewed)
              IN  IF ~canWrite THEN
                      /\ rec.commands = <<>>
                      /\ IF p.readTopology = "publisher-only"
                         THEN rec.pubFinalObs = rec.refreshed ELSE IsUnobservedMap(p, rec.pubFinalObs)
                  ELSE
                      /\ rec.commands \in ValidPublisherCmdSeqs(p, MissingOps(p, rec.refreshed))
                      /\ IF p.readTopology = "publisher-only"
                         THEN rec.pubFinalObs \in ValidObsMaps(p) ELSE IsUnobservedMap(p, rec.pubFinalObs)

SealedPubReceiptsFromPool(p, req, targetEnv, routeEnv, obsPool) ==
    IF p.readTopology = "blind-receipt" THEN
        {rec \in {[status |-> "present", subject |-> req, coordinate |-> targetEnv.coordinate,
                   refreshed |-> BlankObs(p, "unknown"), commands |-> cmds,
                   pubFinalObs |-> BlankObs(p, v)] :
                    cmds \in ValidPublisherCmdSeqs(p, OpsOf(p)),
                    v \in {"established", "conflict", "unknown"}} :
           IsValidSealedPubReceipt(p, req, targetEnv, routeEnv, rec)}
    ELSE
        UNION {LET canWrite == MayMutatePostGate(p, TRUE, AggregateObsState(p, r), req.clearance)
                               /\ (MissingOps(p, r) \subseteq routeEnv.reviewed)
               IN  IF ~canWrite THEN
                       {[status |-> "present", subject |-> req, coordinate |-> targetEnv.coordinate,
                         refreshed |-> r, commands |-> <<>>,
                         pubFinalObs |-> IF p.readTopology = "publisher-only" THEN r ELSE UnobservedMap(p)]}
                   ELSE
                       {[status |-> "present", subject |-> req, coordinate |-> targetEnv.coordinate,
                         refreshed |-> r, commands |-> cmds, pubFinalObs |-> fObs] :
                          cmds \in ValidPublisherCmdSeqs(p, MissingOps(p, r)),
                          fObs \in (IF p.readTopology = "publisher-only" THEN obsPool ELSE {UnobservedMap(p)})} :
                 r \in obsPool}

PrimaryPermitsAnchor(p, req, primaryTargetEnv, primaryRouteEnv, primaryPubReceiptEnv) ==
    /\ req.mode = "live" /\ primaryTargetEnv.bound
    /\ primaryTargetEnv.subject = req
    /\ primaryRouteEnv.subject = req /\ primaryRouteEnv.coordinate = primaryTargetEnv.coordinate
    /\ primaryPubReceiptEnv.subject = req /\ primaryPubReceiptEnv.coordinate = primaryTargetEnv.coordinate
    /\ \/ (primaryRouteEnv.route = "verify-without-write" /\ primaryPubReceiptEnv.status = "not-required")
       \/ /\ primaryRouteEnv.route = "publisher" /\ primaryPubReceiptEnv.status = "present"
          /\ IF CompletionReader(p) = "publisher"
             THEN AggregateObsState(p, primaryPubReceiptEnv.pubFinalObs) = ProofToken(p.proofMode)
             ELSE \/ (~IsUnobservedMap(p, primaryPubReceiptEnv.pubFinalObs) /\
                      AggregateObsState(p, primaryPubReceiptEnv.pubFinalObs) = ProofToken(p.proofMode))
                  \/ /\ ~IsUnobservedMap(p, primaryPubReceiptEnv.refreshed)
                     /\ AggregateObsState(p, primaryPubReceiptEnv.refreshed) \in {ProofToken(p.proofMode), "missing"}
                     /\ MissingOps(p, primaryPubReceiptEnv.refreshed) \subseteq primaryRouteEnv.reviewed
                     /\ \A i \in 1..Len(primaryPubReceiptEnv.commands) :
                          primaryPubReceiptEnv.commands[i][2] = "acknowledged"
                     /\ OpsWithOutcome(primaryPubReceiptEnv.commands, "acknowledged") =
                          MissingOps(p, primaryPubReceiptEnv.refreshed)

\* Exact behavioral congruence kernel of a sealed publisher receipt.
ReceiptKernel(p, req, targetEnv, routeEnv, rec) ==
    <<AggregateObsState(p, rec.refreshed) = ProofToken(p.proofMode),
      IssuedOps(rec.commands),
      OpsWithOutcome(rec.commands, "acknowledged"),
      IF IsUnobservedMap(p, rec.pubFinalObs) THEN <<"unobserved", {}>>
      ELSE ObsKernel(p, rec.pubFinalObs),
      PrimaryPermitsAnchor(p, req, targetEnv, routeEnv, rec)>>

QuotientPubReceipts(p, req, targetEnv, routeEnv) ==
    LET pool == SealedPubReceiptsFromPool(p, req, targetEnv, routeEnv, QuotientObsMaps(p))
        kernels == {ReceiptKernel(p, req, targetEnv, routeEnv, r) : r \in pool}
    IN  {(CHOOSE r \in pool : ReceiptKernel(p, req, targetEnv, routeEnv, r) = k) : k \in kernels}

CombineAttempts(a1, a2) ==
    IF a1 = "yes" \/ a2 = "yes" THEN "yes"
    ELSE IF a1 = "unknown" \/ a2 = "unknown" THEN "unknown" ELSE "no"

TagRoleOps(role, opSet) == {<<role, op>> : op \in opSet}

IsDefinitiveUnsatisfied(p, rep) ==
    /\ rep.satisfaction = "unsatisfied"
    /\ (rep.publisherRecord # "known-nonstart" \/ HasObsValue(p, rep.observed, "conflict"))

CombinePrimaryAndAnchor(req, primaryProf, primaryRep,
                        hasAnchor, anchorProf, anchorRep, retained) ==
    LET sim == hasAnchor /\ primaryRep.satisfaction = "simulation" /\ anchorRep.satisfaction = "simulation"
        sat == IF ~hasAnchor THEN primaryRep.satisfaction
               ELSE IF sim THEN "simulation"
               ELSE IF IsDefinitiveUnsatisfied(primaryProf, primaryRep) \/
                       IsDefinitiveUnsatisfied(anchorProf, anchorRep) THEN "unsatisfied"
               ELSE IF ~retained THEN
                   IF primaryRep.satisfaction = "unsatisfied" /\ anchorRep.satisfaction = "unsatisfied"
                   THEN "unsatisfied" ELSE "unknown"
               ELSE IF primaryRep.satisfaction = "satisfied" /\ anchorRep.satisfaction = "satisfied"
                    THEN "satisfied"
               ELSE IF primaryRep.satisfaction = "unknown" \/ anchorRep.satisfaction = "unknown"
                    THEN "unknown"
               ELSE "unsatisfied"
    IN  [execution           |-> req.execution,
         initial             |-> primaryRep.initial,
         hypotheticalOps     |-> TagRoleOps("primary", primaryRep.hypotheticalOps) \cup
                                 (IF sim THEN TagRoleOps("anchor", anchorRep.hypotheticalOps) ELSE {}),
         observed            |-> primaryRep.observed,
         publisherRead       |-> primaryRep.publisherRead,
         publisherRecord     |-> primaryRep.publisherRecord,
         satisfaction        |-> sat,
         primarySatisfaction |-> primaryRep.satisfaction,
         attempted           |-> IF ~hasAnchor THEN primaryRep.attempted
                                 ELSE IF sim THEN "no"
                                 ELSE CombineAttempts(primaryRep.attempted, anchorRep.attempted),
         primaryAttempted    |-> IF sim THEN "no" ELSE primaryRep.attempted,
         outcomesAvailable   |-> IF ~hasAnchor THEN primaryRep.outcomesAvailable
                                 ELSE IF sim THEN TRUE
                                 ELSE primaryRep.outcomesAvailable /\ anchorRep.outcomesAvailable,
         outcomes            |-> IF sim THEN <<>> ELSE primaryRep.outcomes,
         uncertainCalls      |-> IF sim THEN {}
                                 ELSE TagRoleOps("primary", primaryRep.uncertainCalls) \cup
                                      (IF hasAnchor THEN TagRoleOps("anchor", anchorRep.uncertainCalls) ELSE {}),
         primaryUncertain    |-> IF sim THEN {} ELSE primaryRep.uncertainCalls,
         anchorInitial       |-> IF hasAnchor THEN anchorRep.initial ELSE UnobservedMap(anchorProf),
         anchorObserved      |-> IF hasAnchor THEN anchorRep.observed ELSE UnobservedMap(anchorProf),
         anchorSatisfaction  |-> IF ~hasAnchor THEN "not-applicable"
                                 ELSE IF sim THEN "simulation" ELSE anchorRep.satisfaction,
         anchorAttempted     |-> IF ~hasAnchor \/ sim THEN "no" ELSE anchorRep.attempted,
         anchorOutcomes      |-> IF ~hasAnchor \/ sim THEN <<>> ELSE anchorRep.outcomes,
         anchorUncertain     |-> IF ~hasAnchor \/ sim THEN {} ELSE anchorRep.uncertainCalls]
=============================================================================
