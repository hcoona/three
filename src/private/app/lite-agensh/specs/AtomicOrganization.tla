------------------------- MODULE AtomicOrganization -------------------------
(* Tier 0 (High-Level Atomic Specification — Pure "What"):                 *)
(* Defines the externally observable contract of a self-organized peer     *)
(* cohort WITHOUT protocol implementation mechanics:                       *)
(*   - Observation slice: append-only `log` + per-peer acknowledged index  *)
(*     sets `acked[p]` and `bootstrapped[p]` (no cursors, no chan tokens). *)
(*   - Workspace slice: `ancestors` DAG + `proposals` with ONLY            *)
(*     {"open", "integrated"} statuses; `AtomicIntegrate` advances         *)
(*     `targetRef` and marks `"integrated"` in ONE atomic step (no         *)
(*     `"committing"` state, no `eval` stages, no ghost variables).        *)
(*   - Lifecycle slice: abstract `runEpoch`, `phase`, `stopReason`,        *)
(*     `peerExec`, sum-type `meter`, and `finalVerdict` (no OS scopes).    *)
(* Tier 1 (`CausalStream`, `WorkspaceCas`, `EpochLifecycle`, and composed  *)
(* `AgenshOrganization`) refines this module via nontrivial mappings.      *)
EXTENDS Integers, Sequences, FiniteSets

CONSTANTS
    Peers, AllActors, Keys, MaxLogItems,
    MaxPeerCommits, MaxProposals, AllowExternalAdvance, RequirePeerReview,
    MaxEpochs, SoftCostLimit

ASSUME
    /\ Peers # {} /\ Peers \subseteq AllActors /\ Keys # {} /\ "none" \notin Keys
    /\ MaxLogItems \in Nat /\ MaxPeerCommits \in Nat /\ MaxPeerCommits >= 2
    /\ MaxProposals \in Nat /\ MaxProposals >= 1
    /\ AllowExternalAdvance \in BOOLEAN /\ RequirePeerReview \in BOOLEAN
    /\ MaxEpochs \in Nat /\ MaxEpochs >= 1 /\ SoftCostLimit \in Nat /\ SoftCostLimit >= 1

NoKey         == "none"
LogKeys       == Keys \cup {NoKey}
AllTarget     == "all"
Targets       == AllActors \cup {AllTarget}
BaseCid       == 1
MaxDagNodes   == MaxPeerCommits + MaxProposals + (IF AllowExternalAdvance THEN 1 ELSE 0)
Phases        == {"Idle", "Open", "Draining", "Settling", "Quiescent"}
StopReasons   == {"None", "GoalConverged", "Paused", "TimeLimit", "SoftCostLimit", "FaultedClosed"}
PeerExecs     == {"Idle", "Running", "Unmeasured"}
Verdicts      == {"Unverified", "Passed", "Failed"}
Indeterminate == -1
MaxCostBound  == SoftCostLimit + Cardinality(Peers)
MeterValues   == Indeterminate..MaxCostBound

VisibleTo(p, msg)               == msg.author # p /\ (msg.target = AllTarget \/ msg.target = p)
ApprovalSatisfied(prop, prot)   == (~(RequirePeerReview \/ prot)) \/ prop.approved
AddCost(m, d)                   == IF m = Indeterminate THEN Indeterminate ELSE m + d

VARIABLES
    \* --- Atomic Stream Slice ---
    log, acked, bootstrapped,
    \* --- Atomic Workspace Slice ---
    ancestors, targetRef, peerRef, proposals, externalAdvanced,
    \* --- Atomic Lifecycle Slice ---
    controllerActive, runEpoch, phase, stopReason, peerExec, meter, finalVerdict, cleaned

streamVars    == <<log, acked, bootstrapped>>
workspaceVars == <<ancestors, targetRef, peerRef, proposals, externalAdvanced>>
lifecycleVars == <<controllerActive, runEpoch, phase, stopReason, peerExec, meter, finalVerdict, cleaned>>
vars          == <<streamVars, workspaceVars, lifecycleVars>>

VerifyingFinal == phase = "Quiescent" /\ controllerActive
HasUnmeasured  == \E p \in Peers : peerExec[p] # "Idle"

\* Every visible NoKey item in acked[p] was delivered verbatim; keyed items in acked[p] were
\* either delivered directly or superseded by a strictly newer item of the same key in acked[p].
UpToDateOn(p, i) ==
    /\ (log[i].key = NoKey => i \in acked[p])
    /\ (log[i].key # NoKey => \E j \in acked[p] : j >= i /\ log[j].key = log[i].key)
-----------------------------------------------------------------------------
StreamInit ==
    /\ log          = <<>>
    /\ acked        = [p \in Peers |-> {}]
    /\ bootstrapped = [p \in Peers |-> FALSE]

AtomicAppendLog(author, target, imm, key) ==
    /\ author \in AllActors /\ target \in Targets \ {author}
    /\ imm \in BOOLEAN /\ key \in LogKeys /\ Len(log) < MaxLogItems
    /\ log' = Append(log, [author |-> author, target |-> target, imm |-> imm, key |-> key])
    /\ UNCHANGED <<acked, bootstrapped>>

\* Atomic turn acknowledgment: accounts for all visible items up to turn-start T (folding superseded
\* keyed updates to their latest index <= T) plus visible imm items up to I >= T.
AtomicObserveTurn(p, T, I) ==
    /\ p \in Peers /\ T \in 0..Len(log) /\ I \in T..Len(log)
    /\ \A j \in acked[p] : IF log[j].imm THEN j <= I ELSE j <= T
    /\ acked'        = [acked EXCEPT ![p] = @ \cup {i \in 1..I : VisibleTo(p, log[i]) /\ (i <= T \/ log[i].imm)}]
    /\ bootstrapped' = [bootstrapped EXCEPT ![p] = TRUE]
    /\ UNCHANGED log

AtomicResetBootstrap(S) ==
    /\ S \subseteq Peers /\ S # {}
    /\ bootstrapped' = [p \in Peers |-> IF p \in S THEN FALSE ELSE bootstrapped[p]]
    /\ UNCHANGED <<log, acked>>

StreamNext ==
    \/ \E a \in AllActors, imm \in BOOLEAN, k \in LogKeys :
        \E t \in Targets \ {a} : AtomicAppendLog(a, t, imm, k)
    \/ \E p \in Peers : \E T \in 0..Len(log) : \E I \in T..Len(log) : AtomicObserveTurn(p, T, I)
    \/ \E S \in (SUBSET Peers) \ {{}} : AtomicResetBootstrap(S)

StreamSafetySpec == StreamInit /\ [][StreamNext]_streamVars
StreamLiveness   == <>[] (\A p \in Peers :
                            bootstrapped[p] /\ \A i \in 1..Len(log) : VisibleTo(p, log[i]) => UpToDateOn(p, i))
StreamSpec       == StreamSafetySpec /\ StreamLiveness
-----------------------------------------------------------------------------
WorkspaceInit ==
    /\ ancestors = <<{BaseCid}>> /\ targetRef = BaseCid
    /\ peerRef   = [p \in Peers |-> BaseCid] /\ proposals = <<>> /\ externalAdvanced = FALSE

AtomicAuthorCommit(p) ==
    LET newCid == Len(ancestors) + 1 IN
    /\ p \in Peers /\ Len(ancestors) < MaxPeerCommits
    /\ ancestors' = Append(ancestors, ancestors[peerRef[p]] \cup {newCid})
    /\ peerRef'   = [peerRef EXCEPT ![p] = newCid]
    /\ UNCHANGED <<targetRef, proposals, externalAdvanced>>

AtomicPeerSyncMerge(p) ==
    LET pCid == peerRef[p]  newCid == Len(ancestors) + 1 IN
    /\ p \in Peers /\ targetRef \notin ancestors[pCid]
    /\ IF pCid \in ancestors[targetRef]
       THEN peerRef' = [peerRef EXCEPT ![p] = targetRef] /\ UNCHANGED ancestors
       ELSE /\ Len(ancestors) < MaxPeerCommits
            /\ ancestors' = Append(ancestors, ancestors[pCid] \cup ancestors[targetRef] \cup {newCid})
            /\ peerRef'   = [peerRef EXCEPT ![p] = newCid]
    /\ UNCHANGED <<targetRef, proposals, externalAdvanced>>

AtomicOpenProposal(p) ==
    /\ p \in Peers /\ Len(proposals) < MaxProposals /\ peerRef[p] # BaseCid
    /\ ~\E id \in 1..Len(proposals) : proposals[id].head = peerRef[p]
    /\ proposals' = Append(proposals,
        [author |-> p, head |-> peerRef[p], status |-> "open", requested |-> FALSE, approved |-> FALSE])
    /\ UNCHANGED <<ancestors, targetRef, peerRef, externalAdvanced>>

AtomicUpdateProposalHead(p, id) ==
    /\ p \in Peers /\ id \in 1..Len(proposals)
    /\ proposals[id].author = p /\ proposals[id].status = "open"
    /\ peerRef[p] # BaseCid /\ proposals[id].head # peerRef[p]
    /\ proposals' = [proposals EXCEPT ![id].head = peerRef[p], ![id].requested = FALSE, ![id].approved = FALSE]
    /\ UNCHANGED <<ancestors, targetRef, peerRef, externalAdvanced>>

AtomicSetApproval(reviewer, id, val) ==
    /\ reviewer \in Peers /\ id \in 1..Len(proposals) /\ val \in BOOLEAN
    /\ proposals[id].status = "open" /\ reviewer # proposals[id].author /\ proposals[id].approved # val
    /\ proposals' = [proposals EXCEPT ![id].approved = val]
    /\ UNCHANGED <<ancestors, targetRef, peerRef, externalAdvanced>>

AtomicRequestIntegration(requester, id) ==
    /\ requester \in Peers /\ id \in 1..Len(proposals)
    /\ proposals[id].status = "open" /\ ~proposals[id].requested
    /\ proposals[id].head \notin ancestors[targetRef]
    /\ proposals' = [proposals EXCEPT ![id].requested = TRUE]
    /\ UNCHANGED <<ancestors, targetRef, peerRef, externalAdvanced>>

AtomicRejectRequest(id) ==
    /\ id \in 1..Len(proposals) /\ proposals[id].status = "open" /\ proposals[id].requested
    /\ proposals' = [proposals EXCEPT ![id].requested = FALSE]
    /\ UNCHANGED <<ancestors, targetRef, peerRef, externalAdvanced>>

\* Single-step atomic integration: targetRef and status := "integrated" advance together!
AtomicIntegrate(id, touchesProtected) ==
    LET prop            == proposals[id]
        isFf            == targetRef \in ancestors[prop.head]
        mCid            == Len(ancestors) + 1
        mergedAncestors == ancestors[targetRef] \cup ancestors[prop.head] \cup {mCid}
    IN
    /\ id \in 1..Len(proposals) /\ prop.status = "open" /\ prop.requested
    /\ prop.head \notin ancestors[targetRef] /\ touchesProtected \in BOOLEAN
    /\ ApprovalSatisfied(prop, touchesProtected)
    /\ IF isFf
       THEN targetRef' = prop.head /\ UNCHANGED ancestors
       ELSE ancestors' = Append(ancestors, mergedAncestors) /\ targetRef' = mCid
    /\ proposals' = [proposals EXCEPT ![id].status = "integrated", ![id].requested = FALSE]
    /\ UNCHANGED <<peerRef, externalAdvanced>>

AtomicExternalAdvance(subsumedId) ==
    LET newCid       == Len(ancestors) + 1
        extAncestors == IF subsumedId = 0
                        THEN ancestors[targetRef] \cup {newCid}
                        ELSE ancestors[targetRef] \cup ancestors[proposals[subsumedId].head] \cup {newCid}
    IN
    /\ AllowExternalAdvance /\ ~externalAdvanced /\ subsumedId \in 0..Len(proposals)
    /\ (subsumedId > 0 => proposals[subsumedId].status = "open" /\ proposals[subsumedId].requested)
    /\ externalAdvanced' = TRUE /\ ancestors' = Append(ancestors, extAncestors) /\ targetRef' = newCid
    /\ proposals' = IF subsumedId = 0 THEN proposals
                    ELSE [proposals EXCEPT ![subsumedId].status = "integrated", ![subsumedId].requested = FALSE]
    /\ UNCHANGED peerRef

WorkspaceNext ==
    \/ \E p \in Peers : AtomicAuthorCommit(p) \/ AtomicPeerSyncMerge(p) \/ AtomicOpenProposal(p)
    \/ \E p \in Peers, id \in 1..MaxProposals : AtomicUpdateProposalHead(p, id)
    \/ \E r \in Peers, id \in 1..MaxProposals, v \in BOOLEAN : AtomicSetApproval(r, id, v)
    \/ \E q \in Peers, id \in 1..MaxProposals : AtomicRequestIntegration(q, id)
    \/ \E id \in 1..MaxProposals : AtomicRejectRequest(id)
    \/ \E id \in 1..MaxProposals, tp \in BOOLEAN : AtomicIntegrate(id, tp)
    \/ \E sid \in 0..MaxProposals : AtomicExternalAdvance(sid)

WorkspaceSafetySpec == WorkspaceInit /\ [][WorkspaceNext]_workspaceVars
WorkspaceLiveness   == <>[] (\A id \in 1..Len(proposals) : ~proposals[id].requested)
WorkspaceSpec       == WorkspaceSafetySpec /\ WorkspaceLiveness
-----------------------------------------------------------------------------
LifecycleInit ==
    /\ controllerActive = FALSE /\ runEpoch = 0 /\ phase = "Idle" /\ stopReason = "None"
    /\ peerExec = [p \in Peers |-> "Idle"] /\ meter = 0 /\ finalVerdict = "Unverified" /\ cleaned = FALSE

AtomicActivateEpoch ==
    LET isFresh     == (runEpoch = 0 /\ phase = "Idle")
        isResume    == (runEpoch > 0 /\ phase = "Quiescent" /\ stopReason = "Paused")
        isReconnect == (runEpoch > 0 /\ phase \in {"Open", "Draining", "Settling"})
    IN
    /\ ~controllerActive /\ ~cleaned /\ finalVerdict = "Unverified" /\ runEpoch < MaxEpochs
    /\ meter \in 0..(SoftCostLimit - 1) /\ (isFresh \/ (isResume /\ ~HasUnmeasured) \/ isReconnect)
    /\ controllerActive' = TRUE /\ runEpoch' = runEpoch + 1 /\ phase' = "Open" /\ stopReason' = "None"
    /\ peerExec'         = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ bootstrapped'     = IF isResume THEN [p \in Peers |-> FALSE] ELSE bootstrapped
    /\ UNCHANGED <<log, acked, workspaceVars, meter, finalVerdict, cleaned>>

AtomicReconcileOrFallback(p, loadOk) ==
    LET hasSaved == bootstrapped[p] \/ peerExec[p] # "Idle"
        newCost  == IF peerExec[p] = "Unmeasured" /\ loadOk /\ hasSaved THEN AddCost(meter, 1) ELSE meter
    IN
    /\ controllerActive /\ phase = "Open" /\ peerExec[p] # "Running"
    /\ meter \in 0..(SoftCostLimit - 1) /\ loadOk \in BOOLEAN
    /\ IF hasSaved /\ ~loadOk
       THEN IF peerExec[p] = "Unmeasured"
            THEN meter' = Indeterminate /\ phase' = "Settling" /\ stopReason' = "FaultedClosed"
                 /\ UNCHANGED <<bootstrapped, peerExec>>
            ELSE bootstrapped' = [bootstrapped EXCEPT ![p] = FALSE]
                 /\ UNCHANGED <<meter, phase, stopReason, peerExec>>
       ELSE /\ peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
            /\ IF newCost >= SoftCostLimit
               THEN phase' = "Settling" /\ stopReason' = "SoftCostLimit"
               ELSE UNCHANGED <<phase, stopReason>>
            /\ UNCHANGED bootstrapped
    /\ UNCHANGED <<log, acked, workspaceVars, controllerActive, runEpoch, finalVerdict, cleaned>>

AtomicBeginTurn(p) ==
    /\ controllerActive /\ phase = "Open" /\ peerExec[p] = "Idle" /\ meter \in 0..(SoftCostLimit - 1)
    /\ peerExec' = [peerExec EXCEPT ![p] = "Running"]
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, phase, stopReason, meter, finalVerdict, cleaned>>

AtomicCompleteTurn(p, T, I) ==
    LET newCost == AddCost(meter, 1) IN
    /\ controllerActive /\ phase \in {"Open", "Draining"} /\ peerExec[p] = "Running"
    /\ AtomicObserveTurn(p, T, I)
    /\ peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
    /\ IF phase = "Open" /\ newCost >= SoftCostLimit
       THEN phase' = "Settling" /\ stopReason' = "SoftCostLimit"
       ELSE UNCHANGED <<phase, stopReason>>
    /\ UNCHANGED <<workspaceVars, controllerActive, runEpoch, finalVerdict, cleaned>>

AtomicAbortTurn(p, readOk) ==
    LET newCost == AddCost(meter, 1) IN
    /\ controllerActive /\ phase \in {"Open", "Draining"} /\ peerExec[p] = "Running" /\ readOk \in BOOLEAN
    /\ phase' = "Settling"
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
            /\ stopReason' = IF phase = "Open" /\ newCost >= SoftCostLimit THEN "SoftCostLimit"
                             ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"] /\ meter' = Indeterminate
            /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, finalVerdict, cleaned>>

AtomicSignalStop(reason) ==
    /\ controllerActive /\ reason \in {"GoalConverged", "Paused", "TimeLimit"}
    /\ IF reason = "TimeLimit"
       THEN phase = "Open" /\ phase' = "Draining" /\ stopReason' = "TimeLimit"
       ELSE /\ phase \in {"Open", "Draining"} /\ phase' = "Settling"
            /\ stopReason' = IF meter = Indeterminate THEN "FaultedClosed" ELSE reason
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, peerExec, meter, finalVerdict, cleaned>>

AtomicEndDrain ==
    /\ controllerActive /\ phase = "Draining" /\ phase' = "Settling"
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, stopReason, peerExec, meter, finalVerdict, cleaned>>

AtomicSettlePeer(p, readOk) ==
    /\ controllerActive /\ phase = "Settling" /\ peerExec[p] # "Idle" /\ readOk \in BOOLEAN
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = AddCost(meter, 1) /\ UNCHANGED stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"] /\ meter' = Indeterminate /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, phase, finalVerdict, cleaned>>

AtomicSettleEpoch ==
    /\ controllerActive /\ phase = "Settling" /\ (\A p \in Peers : peerExec[p] # "Running")
    /\ phase' = "Quiescent" /\ controllerActive' = FALSE
    /\ meter' = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ stopReason' = IF meter = Indeterminate \/ HasUnmeasured THEN "FaultedClosed"
                     ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
    /\ UNCHANGED <<streamVars, workspaceVars, runEpoch, peerExec, finalVerdict, cleaned>>

AtomicSettleCrashed ==
    /\ ~controllerActive /\ ~cleaned /\ phase \in {"Open", "Draining", "Settling"}
    /\ phase' = "Quiescent" /\ stopReason' = "FaultedClosed"
    /\ peerExec' = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ meter' = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, finalVerdict, cleaned>>

AtomicBeginVerifyFinal ==
    /\ ~controllerActive /\ ~cleaned /\ runEpoch > 0 /\ phase = "Quiescent"
    /\ controllerActive' = TRUE /\ finalVerdict' = "Unverified"
    /\ UNCHANGED <<streamVars, workspaceVars, runEpoch, phase, stopReason, peerExec, meter, cleaned>>

AtomicCompleteVerifyFinal(ok) ==
    /\ VerifyingFinal /\ ok \in BOOLEAN /\ controllerActive' = FALSE
    /\ finalVerdict' = IF ok /\ meter >= 0 /\ ~HasUnmeasured /\ stopReason # "FaultedClosed"
                       THEN "Passed" ELSE "Failed"
    /\ UNCHANGED <<streamVars, workspaceVars, runEpoch, phase, stopReason, peerExec, meter, cleaned>>

AtomicCleanup ==
    /\ ~controllerActive /\ ~cleaned /\ phase = "Quiescent" /\ finalVerdict # "Unverified"
    /\ cleaned' = TRUE
    /\ UNCHANGED <<streamVars, workspaceVars, controllerActive, runEpoch, phase, stopReason, peerExec, meter, finalVerdict>>

AtomicCrash ==
    /\ controllerActive /\ controllerActive' = FALSE
    /\ \/ proposals' = proposals
       \/ \E id \in 1..Len(proposals) :
            /\ proposals[id].status = "open" /\ proposals[id].requested
            /\ proposals' = [proposals EXCEPT ![id].requested = FALSE]
    /\ UNCHANGED <<streamVars, ancestors, targetRef, peerRef, externalAdvanced,
                   runEpoch, phase, stopReason, peerExec, meter, finalVerdict, cleaned>>

AtomicTerminalStutter ==
    /\ ~controllerActive
    /\ (cleaned \/ (phase = "Quiescent" /\ finalVerdict # "Unverified") \/ runEpoch = MaxEpochs)
    /\ UNCHANGED vars

LifecycleNext ==
    \/ AtomicActivateEpoch
    \/ \E p \in Peers, ok \in BOOLEAN : AtomicReconcileOrFallback(p, ok)
    \/ \E p \in Peers : AtomicBeginTurn(p)
    \/ \E p \in Peers : \E T \in 0..Len(log) : \E I \in T..Len(log) : AtomicCompleteTurn(p, T, I)
    \/ \E p \in Peers, ok \in BOOLEAN : AtomicAbortTurn(p, ok)
    \/ \E r \in {"GoalConverged", "Paused", "TimeLimit"} : AtomicSignalStop(r)
    \/ AtomicEndDrain
    \/ \E p \in Peers, ok \in BOOLEAN : AtomicSettlePeer(p, ok)
    \/ AtomicSettleEpoch \/ AtomicSettleCrashed
    \/ AtomicBeginVerifyFinal \/ \E ok \in BOOLEAN : AtomicCompleteVerifyFinal(ok)
    \/ AtomicCleanup \/ AtomicCrash \/ AtomicTerminalStutter

LifecycleOnlyVars   == <<bootstrapped, lifecycleVars>>
LifecycleSafetySpec == (StreamInit /\ WorkspaceInit /\ LifecycleInit)
                       /\ [][LifecycleNext \/ (StreamNext /\ UNCHANGED <<workspaceVars, lifecycleVars>>)
                             \/ (WorkspaceNext /\ UNCHANGED <<streamVars, lifecycleVars>>)]_vars
BoundedTermination  == [](controllerActive => <> (~controllerActive))

Init == StreamInit /\ WorkspaceInit /\ LifecycleInit
Next ==
    \/ LifecycleNext
    \/ (StreamNext /\ UNCHANGED <<workspaceVars, lifecycleVars>>)
    \/ (WorkspaceNext /\ UNCHANGED <<streamVars, lifecycleVars>>)

Fairness ==
    /\ \A p \in Peers :
        WF_vars((\E T \in 0..Len(log) : \E I \in T..Len(log) : AtomicCompleteTurn(p, T, I))
                \/ AtomicAbortTurn(p, FALSE))
    /\ \A p \in Peers : WF_vars(\E ok \in BOOLEAN : AtomicSettlePeer(p, ok))
    /\ WF_vars(AtomicSignalStop("TimeLimit")) /\ WF_vars(AtomicEndDrain)
    /\ WF_vars(AtomicSettleEpoch) /\ WF_vars(\E ok \in BOOLEAN : AtomicCompleteVerifyFinal(ok))

SafetySpec == Init /\ [][Next]_vars
Spec       == SafetySpec /\ Fairness
-----------------------------------------------------------------------------
AtomicCoherence ==
    /\ Len(log) <= MaxLogItems /\ acked \in [Peers -> SUBSET (1..MaxLogItems)]
    /\ bootstrapped \in [Peers -> BOOLEAN]
    /\ \A p \in Peers : \A i \in acked[p] : i <= Len(log) /\ VisibleTo(p, log[i])
    /\ ancestors[BaseCid] = {BaseCid} /\ BaseCid \in ancestors[targetRef]
    /\ \A id \in 1..Len(proposals) :
        /\ proposals[id].status \in {"open", "integrated"}
        /\ (proposals[id].status = "integrated") => (proposals[id].head \in ancestors[targetRef])
    /\ (phase = "Quiescent" /\ HasUnmeasured) => (meter = Indeterminate)
    /\ (finalVerdict = "Passed") => (meter >= 0 /\ ~HasUnmeasured /\ stopReason # "FaultedClosed")
=============================================================================
