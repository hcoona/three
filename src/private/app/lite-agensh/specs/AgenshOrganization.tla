------------------------- MODULE AgenshOrganization -------------------------
(* Plane 4: Unified TLA+ INSTANCE Composition over CausalStream,           *)
(* WorkspaceCas, and EpochLifecycle.                                       *)
(*   - Mechanically verifies THEOREM Spec => SubPlane!SafetySpec across all*)
(*     three planes in a single unified configuration covering both normal *)
(*     & immediate causal-log delivery, PeerSyncMerge + 3-step workspace   *)
(*     CAS, hierarchical scopes, sum-type metering, and crash recovery.    *)
EXTENDS Integers, Sequences, FiniteSets

CONSTANTS
    Peers, Keys, MaxLogItems, MaxChan,
    MaxPeerCommits, MaxProposals, AllowExternalAdvance, RequirePeerReview,
    MaxEpochs, SoftCostLimit, MaxCrashes

ASSUME
    /\ Peers # {} /\ Keys # {} /\ "none" \notin Keys
    /\ MaxLogItems \in Nat /\ MaxChan \in Nat /\ MaxChan >= 1
    /\ MaxPeerCommits \in Nat /\ MaxPeerCommits >= 2 /\ MaxProposals \in Nat /\ MaxProposals >= 1
    /\ AllowExternalAdvance \in BOOLEAN /\ RequirePeerReview \in BOOLEAN
    /\ MaxEpochs \in Nat /\ MaxEpochs >= 1 /\ SoftCostLimit \in Nat /\ SoftCostLimit >= 1
    /\ MaxCrashes \in Nat

VARIABLES
    controllerActive, runEpoch, phase, stopReason, scopeState,
    peerExec, meter, finalVerdict, cleaned, crashesLeft,
    needsBootstrap, chan,
    log, baseCursor, immCursor, turnSnap, inflightImm,
    ancestors, targetRef, peerRef, proposals, eval, externalAdvanced

lifecycleOnlyVars == <<controllerActive, runEpoch, phase, stopReason, scopeState,
                       peerExec, meter, finalVerdict, cleaned, crashesLeft>>
sharedFenceVars   == <<needsBootstrap, chan>>
streamOnlyVars    == <<log, baseCursor, immCursor, turnSnap, inflightImm>>
workspaceOnlyVars == <<ancestors, targetRef, peerRef, proposals, eval, externalAdvanced>>
vars              == <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars, workspaceOnlyVars>>

WsControllerUp == controllerActive \/ phase \in {"Idle", "Quiescent"}

\* Horizontal sub-plane instantiations:
StreamPlane    == INSTANCE CausalStream WITH AllActors <- Peers
WorkspacePlane == INSTANCE WorkspaceCas WITH controllerUp <- WsControllerUp
LifecyclePlane == INSTANCE EpochLifecycle

\* Vertical Tier-0 atomic instantiation via composite nontrivial refinement mapping:
HighOrg == INSTANCE AtomicOrganization WITH
    AllActors    <- Peers,
    acked        <- StreamPlane!MappedAcked,
    bootstrapped <- StreamPlane!MappedBootstrapped,
    proposals    <- WorkspacePlane!MappedProposals
-----------------------------------------------------------------------------
Init == LifecyclePlane!Init /\ StreamPlane!Init /\ WorkspacePlane!Init

ComposedActivateEpoch ==
    LET isResume    == (runEpoch > 0 /\ phase = "Quiescent" /\ stopReason = "Paused")
        isReconnect == (runEpoch > 0 /\ phase \in {"Open", "Draining", "Settling"})
        openSet     == {p \in Peers : StreamPlane!InTurn(p) \/ chan[p] > 0}
    IN
    /\ LifecyclePlane!ActivateEpoch
    /\ IF isResume THEN StreamPlane!RotateContexts(Peers)
       ELSE IF isReconnect /\ openSet # {} THEN StreamPlane!UnbindChannels(openSet)
       ELSE UNCHANGED streamOnlyVars
    /\ IF isReconnect THEN WorkspacePlane!RecoverAndReconcileStores ELSE UNCHANGED workspaceOnlyVars

ComposedReserveOrCheckSavedPeer(p, savedLoadOk) ==
    LET hasSaved   == LifecyclePlane!HasSavedSession(p)
        newCost    == IF peerExec[p] = "Unmeasured" /\ savedLoadOk /\ hasSaved
                      THEN LifecyclePlane!AddCost(meter, 1) ELSE meter
        willReset  == hasSaved /\ ~savedLoadOk /\ peerExec[p] # "Unmeasured"
        willSettle == (hasSaved /\ ~savedLoadOk /\ peerExec[p] = "Unmeasured")
                      \/ ((~hasSaved \/ savedLoadOk) /\ newCost >= SoftCostLimit)
        willBind   == ~willSettle
        openSet    == {q \in Peers : StreamPlane!InTurn(q) \/ chan[q] > 0}
    IN
    /\ LifecyclePlane!ReserveOrCheckSavedPeer(p, savedLoadOk)
    /\ IF willBind THEN StreamPlane!BindChannel(p, willReset)
       ELSE IF openSet # {} THEN StreamPlane!UnbindChannels(openSet)
       ELSE UNCHANGED streamOnlyVars
    /\ UNCHANGED workspaceOnlyVars

ComposedRegisterReservedChannelScope(p) ==
    /\ LifecyclePlane!RegisterReservedChannelScope(p)
    /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>

ComposedBeginTurn(p, idleWake) ==
    LifecyclePlane!BeginPeerTurn(p) /\ StreamPlane!BeginTurn(p, idleWake) /\ UNCHANGED workspaceOnlyVars

ComposedCompleteTurn(p) ==
    LET settleAll == (phase = "Open" /\ LifecyclePlane!AddCost(meter, 1) >= SoftCostLimit)
    IN
    /\ LifecyclePlane!CompletePeerTurn(p)
    /\ StreamPlane!CommitTurn(p, chan[p], settleAll)
    /\ UNCHANGED workspaceOnlyVars

ComposedAbortTurn(p, readOk) ==
    LET openSet == {q \in Peers : StreamPlane!InTurn(q) \/ chan[q] > 0}
    IN
    /\ LifecyclePlane!AbortPeerTurn(p, readOk)
    /\ StreamPlane!UnbindChannels(openSet)
    /\ UNCHANGED workspaceOnlyVars

ComposedSignalStop(reason) ==
    LET openSet == {q \in Peers : StreamPlane!InTurn(q) \/ chan[q] > 0}
    IN
    /\ LifecyclePlane!SignalStop(reason)
    /\ IF reason \in {"GoalConverged", "Paused"} /\ openSet # {}
       THEN StreamPlane!UnbindChannels(openSet) ELSE UNCHANGED streamOnlyVars
    /\ UNCHANGED workspaceOnlyVars

ComposedEndDrain ==
    LET openSet == {q \in Peers : StreamPlane!InTurn(q) \/ chan[q] > 0}
    IN
    /\ LifecyclePlane!EndDrain
    /\ IF openSet # {} THEN StreamPlane!UnbindChannels(openSet) ELSE UNCHANGED streamOnlyVars
    /\ UNCHANGED workspaceOnlyVars

ScopeAlive(p)     == scopeState[p] \in {"Active", "Ambiguous"}
WorktreeActive(p) == ScopeAlive(p) /\ peerExec[p] # "Idle"
McpLive(p, c)     == StreamPlane!InTurn(p) /\ chan[p] = c

FencedWsCall(p, c, Act) ==
    LET hasImm == StreamPlane!ImmSlice(p, inflightImm[p], Len(log)) # {}
    IN
    /\ c \in 1..MaxChan /\ McpLive(p, c) /\ ScopeAlive(p)
    /\ UNCHANGED lifecycleOnlyVars
    /\ IF hasImm
       THEN StreamPlane!PeerToolCall(p, c, FALSE, StreamPlane!AllTarget, FALSE, StreamPlane!NoKey)
       ELSE UNCHANGED <<sharedFenceVars, streamOnlyVars>>
    /\ Act

ComposedPeerTool(p, c, emit, target, imm, key) ==
    /\ McpLive(p, c) /\ ScopeAlive(p)
    /\ StreamPlane!PeerToolCall(p, c, emit, target, imm, key)
    /\ UNCHANGED <<lifecycleOnlyVars, workspaceOnlyVars>>

ComposedBeginEvaluation(id, mergeOk, touchesProtected) ==
    LET prop == proposals[id]
        ok   == prop.head \notin ancestors[targetRef] /\ mergeOk
                /\ WorkspacePlane!ApprovalSatisfied(prop, touchesProtected)
    IN
    /\ controllerActive /\ phase = "Open" /\ scopeState[LifecyclePlane!VerifyScope] = "Stopped"
    /\ id \in 1..Len(proposals)
    /\ IF ok THEN LifecyclePlane!BeginSubordinateVerify ELSE UNCHANGED lifecycleOnlyVars
    /\ UNCHANGED <<sharedFenceVars, streamOnlyVars>>
    /\ WorkspacePlane!BeginEvaluation(id, mergeOk, touchesProtected)

ComposedCompleteVerificationAndPreCommit(passed) ==
    /\ controllerActive /\ phase \in {"Open", "Draining", "Settling"} /\ (phase = "Settling" => ~passed)
    /\ LifecyclePlane!EndSubordinateVerify
    /\ UNCHANGED streamOnlyVars
    /\ WorkspacePlane!CompleteVerificationAndPreCommit(passed)

ComposedStopChannelInSettlement(p, readOk) ==
    /\ LifecyclePlane!StopPeerChannelInSettlement(p, readOk)
    /\ IF StreamPlane!InTurn(p) \/ chan[p] > 0
       THEN StreamPlane!UnbindChannels({p}) ELSE UNCHANGED streamOnlyVars
    /\ UNCHANGED workspaceOnlyVars

ComposedSettleRuntimeEpoch ==
    LET openSet == {p \in Peers : StreamPlane!InTurn(p) \/ chan[p] > 0}
    IN
    /\ eval.stage = "idle" /\ (\A id \in 1..Len(proposals) : proposals[id].status # "committing")
    /\ LifecyclePlane!SettleParentRuntimeAndEpoch
    /\ IF openSet # {} THEN StreamPlane!UnbindChannels(openSet) ELSE UNCHANGED streamOnlyVars
    /\ UNCHANGED workspaceOnlyVars

ComposedSettleCrashedOrganization ==
    LET openSet == {p \in Peers : StreamPlane!InTurn(p) \/ chan[p] > 0}
    IN
    /\ LifecyclePlane!SettleInterruptedEpochRecord
    /\ IF openSet # {} THEN StreamPlane!UnbindChannels(openSet) ELSE UNCHANGED streamOnlyVars
    /\ WorkspacePlane!RecoverAndReconcileStores

ComposedBeginVerifyFinal ==
    /\ eval.stage = "idle" /\ (\A id \in 1..Len(proposals) : proposals[id].status # "committing")
    /\ LifecyclePlane!BeginVerifyFinal /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>

ComposedCrashController ==
    /\ LifecyclePlane!CrashController
    /\ IF phase \in {"Open", "Draining", "Settling"}
       THEN WorkspacePlane!CrashController ELSE UNCHANGED workspaceOnlyVars
    /\ UNCHANGED streamOnlyVars

Next ==
    \/ ComposedActivateEpoch
    \/ \E p \in Peers, ok \in BOOLEAN : ComposedReserveOrCheckSavedPeer(p, ok)
    \/ \E p \in Peers : ComposedRegisterReservedChannelScope(p)
    \/ \E p \in Peers, w \in BOOLEAN : ComposedBeginTurn(p, w)
    \/ \E p \in Peers : ComposedCompleteTurn(p)
    \/ \E p \in Peers, ok \in BOOLEAN : ComposedAbortTurn(p, ok)
    \/ \E p \in Peers, c \in 1..MaxChan, imm \in BOOLEAN, k \in StreamPlane!LogKeys :
        ComposedPeerTool(p, c, TRUE, StreamPlane!AllTarget, imm, k)
    \/ \E p \in Peers, c \in 1..MaxChan :
        ComposedPeerTool(p, c, FALSE, StreamPlane!AllTarget, FALSE, StreamPlane!NoKey)
    \/ \E p \in Peers :
        WorktreeActive(p) /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
        /\ WorkspacePlane!AuthorCommit(p)
    \/ \E p \in Peers :
        WorktreeActive(p) /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
        /\ WorkspacePlane!PeerSyncMerge(p)
    \/ \E p \in Peers, c \in 1..MaxChan : FencedWsCall(p, c, WorkspacePlane!OpenProposal(p))
    \/ \E p \in Peers, c \in 1..MaxChan, id \in 1..MaxProposals :
        FencedWsCall(p, c, WorkspacePlane!UpdateProposalHead(p, id))
    \/ \E r \in Peers, c \in 1..MaxChan, id \in 1..MaxProposals :
        FencedWsCall(r, c, WorkspacePlane!ApproveProposal(r, id))
    \/ \E r \in Peers, c \in 1..MaxChan, id \in 1..MaxProposals :
        FencedWsCall(r, c, WorkspacePlane!RevokeApproval(r, id))
    \/ \E q \in Peers, c \in 1..MaxChan, id \in 1..MaxProposals :
        FencedWsCall(q, c, WorkspacePlane!RequestIntegration(q, id))
    \/ \E id \in 1..MaxProposals, mOk \in BOOLEAN, tp \in BOOLEAN : ComposedBeginEvaluation(id, mOk, tp)
    \/ \E ok \in BOOLEAN : ComposedCompleteVerificationAndPreCommit(ok)
    \/ \E sub \in BOOLEAN :
        UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
        /\ WorkspacePlane!OutOfBandTargetAdvance(sub)
    \/ (controllerActive /\ phase \in {"Open", "Draining", "Settling"}
        /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
        /\ WorkspacePlane!ExecuteArtifactCas)
    \/ (controllerActive /\ phase \in {"Open", "Draining", "Settling"}
        /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
        /\ WorkspacePlane!CompleteMetadataPostCas)
    \/ \E r \in {"GoalConverged", "Paused", "TimeLimit"} : ComposedSignalStop(r)
    \/ ComposedEndDrain
    \/ \E p \in Peers, ok \in BOOLEAN : ComposedStopChannelInSettlement(p, ok)
    \/ ComposedSettleRuntimeEpoch \/ ComposedSettleCrashedOrganization \/ ComposedBeginVerifyFinal
    \/ \E ok \in BOOLEAN : LifecyclePlane!CompleteVerifyFinal(ok) /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>
    \/ (LifecyclePlane!CleanupResources /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>)
    \/ ComposedCrashController
    \/ \E s \in LifecyclePlane!Scopes :
        LifecyclePlane!ObserveOrphanOrWitnessScopeStopped(s) /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>
    \/ (LifecyclePlane!TerminalHalted /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>)

Fairness ==
    /\ \A p \in Peers :
        WF_vars(ComposedCompleteTurn(p) \/ ComposedAbortTurn(p, FALSE))
    /\ \A p \in Peers : WF_vars(\E ok \in BOOLEAN : ComposedStopChannelInSettlement(p, ok))
    /\ WF_vars(\E ok \in BOOLEAN : ComposedCompleteVerificationAndPreCommit(ok))
    /\ WF_vars(controllerActive /\ phase \in {"Open", "Draining", "Settling"}
               /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
               /\ WorkspacePlane!ExecuteArtifactCas)
    /\ WF_vars(controllerActive /\ phase \in {"Open", "Draining", "Settling"}
               /\ UNCHANGED <<lifecycleOnlyVars, sharedFenceVars, streamOnlyVars>>
               /\ WorkspacePlane!CompleteMetadataPostCas)
    /\ WF_vars(ComposedSignalStop("TimeLimit"))
    /\ WF_vars(ComposedEndDrain)
    /\ WF_vars(ComposedSettleRuntimeEpoch)
    /\ WF_vars(\E ok \in BOOLEAN : LifecyclePlane!CompleteVerifyFinal(ok) /\ UNCHANGED <<streamOnlyVars, workspaceOnlyVars>>)

Spec == Init /\ [][Next]_vars /\ Fairness
-----------------------------------------------------------------------------
\* Horizontal Composition Theorems:
StreamSafetyRefinement    == StreamPlane!SafetySpec
WorkspaceSafetyRefinement == WorkspacePlane!SafetySpec
LifecycleSafetyRefinement == LifecyclePlane!SafetySpec

\* Vertical Tier-0 Atomic Refinement Theorem (Safety + Fairness + Liveness!):
AtomicOrgSpecRefinement   == HighOrg!Spec

THEOREM Spec => StreamSafetyRefinement
THEOREM Spec => WorkspaceSafetyRefinement
THEOREM Spec => LifecycleSafetyRefinement
THEOREM Spec => AtomicOrgSpecRefinement

CrossPlaneCoherence ==
    /\ HighOrg!AtomicCoherence
    /\ StreamPlane!TypeOK /\ StreamPlane!CursorCoherence
    /\ WorkspacePlane!TypeOK /\ WorkspacePlane!TargetAncestryPreservation
    /\ LifecyclePlane!TypeOK /\ LifecyclePlane!BottomUpScopeContainment
    /\ LifecyclePlane!FencedEpochAndMeterSafety
    /\ \A p \in Peers :
        StreamPlane!InTurn(p) <=> (peerExec[p] = "Running" /\ phase # "Settling")
    /\ (controllerActive /\ ~LifecyclePlane!VerifyingFinal) =>
        ((scopeState[LifecyclePlane!VerifyScope] = "Active") <=> (eval.stage = "verifying"))
    /\ (phase = "Quiescent") =>
        eval.stage = "idle" /\ \A id \in 1..Len(proposals) : proposals[id].status # "committing"

BoundedComposedSettlement == HighOrg!BoundedTermination
=============================================================================
