--------------------------- MODULE EpochLifecycle ---------------------------
(* Plane 3: Fenced Epoch Ownership, Hierarchical Scopes, and Sum-Type      *)
(* Epistemic Meter.                                                        *)
(*   - VerifyingFinal == (phase = "Quiescent" /\ controllerActive);        *)
(*   - Admitted peers in Open equal {p \in Peers : scopeState[p]="Active"};*)
(*   - Signed chan[p] \in -MaxChan..MaxChan (+k bound, -k unbound after k);*)
(*   - peerExec[p] \in {"Idle", "Running", "Unmeasured"} (3-state sum);    *)
(*   - meter \in -1..MaxCostBound (-1 = Indeterminate, >=0 = Exact(cost)). *)
EXTENDS Integers, FiniteSets

CONSTANTS Peers, MaxEpochs, MaxChan, SoftCostLimit, MaxCrashes

ASSUME
    /\ Peers # {} /\ MaxEpochs \in Nat /\ MaxEpochs >= 1
    /\ MaxChan \in Nat /\ MaxChan >= 1 /\ SoftCostLimit \in Nat /\ SoftCostLimit >= 1
    /\ MaxCrashes \in Nat

RuntimeScope  == "runtime"
VerifyScope   == "verify"
RootScope     == "root"
Scopes        == {RuntimeScope, VerifyScope} \cup Peers
ScopeStates   == {"Stopped", "Active", "Ambiguous"}
Phases        == {"Idle", "Open", "Draining", "Settling", "Quiescent"}
StopReasons   == {"None", "GoalConverged", "Paused", "TimeLimit", "SoftCostLimit", "FaultedClosed"}
PeerExecs     == {"Idle", "Running", "Unmeasured"}
Verdicts      == {"Unverified", "Passed", "Failed"}
Indeterminate == -1
MaxCostBound  == SoftCostLimit + Cardinality(Peers)
MeterValues   == Indeterminate..MaxCostBound

ParentScope(s) == IF s \in Peers THEN RuntimeScope ELSE RootScope
LastChan(c)    == IF c < 0 THEN -c ELSE c
UnbindVal(c)   == IF c > 0 THEN -c ELSE c
AddCost(m, d)  == IF m = Indeterminate THEN Indeterminate ELSE m + d

VARIABLES
    controllerActive, runEpoch, phase, stopReason, scopeState,
    needsBootstrap, chan, peerExec, meter, finalVerdict, cleaned, crashesLeft

endVars == <<finalVerdict, cleaned, crashesLeft>>
vars    == <<controllerActive, runEpoch, phase, stopReason, scopeState,
             needsBootstrap, chan, peerExec, meter, endVars>>

VerifyingFinal     == phase = "Quiescent" /\ controllerActive
HasUnmeasured      == \E p \in Peers : peerExec[p] # "Idle"
HasSavedSession(p) == (~needsBootstrap[p]) \/ (peerExec[p] # "Idle")
ChildrenStopped(s) == \A c \in Scopes : (ParentScope(c) = s) => (scopeState[c] = "Stopped")
AllScopesStopped   == \A s \in Scopes : scopeState[s] = "Stopped"
BottomUpValid(st)  == \A c \in Scopes :
    (ParentScope(c) # RootScope /\ st[c] # "Stopped") => (st[ParentScope(c)] # "Stopped")

Init ==
    /\ controllerActive = FALSE /\ runEpoch = 0 /\ phase = "Idle" /\ stopReason = "None"
    /\ scopeState = [s \in Scopes |-> "Stopped"]
    /\ needsBootstrap = [p \in Peers |-> TRUE] /\ chan = [p \in Peers |-> 0]
    /\ peerExec = [p \in Peers |-> "Idle"]
    /\ meter = 0 /\ finalVerdict = "Unverified" /\ cleaned = FALSE /\ crashesLeft = MaxCrashes

ActivateEpoch ==
    LET isFresh     == (runEpoch = 0 /\ phase = "Idle")
        isResume    == (runEpoch > 0 /\ phase = "Quiescent" /\ stopReason = "Paused")
        isReconnect == (runEpoch > 0 /\ phase \in {"Open", "Draining", "Settling"})
    IN
    /\ ~controllerActive /\ ~cleaned /\ finalVerdict = "Unverified" /\ runEpoch < MaxEpochs
    /\ AllScopesStopped /\ meter \in 0..(SoftCostLimit - 1)
    /\ (isFresh \/ (isResume /\ ~HasUnmeasured) \/ isReconnect)
    /\ controllerActive' = TRUE /\ runEpoch' = runEpoch + 1 /\ phase' = "Open" /\ stopReason' = "None"
    /\ scopeState'     = [scopeState EXCEPT ![RuntimeScope] = "Active"]
    /\ needsBootstrap' = IF isResume THEN [p \in Peers |-> TRUE] ELSE needsBootstrap
    /\ chan'           = [p \in Peers |-> UnbindVal(chan[p])]
    /\ peerExec'       = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ UNCHANGED <<meter, endVars>>

\* Step 1 of channel setup: reserve monotonic `+chan` token (or evaluate saved-session load) before `mcp` reaches `Main`.
ReserveOrCheckSavedPeer(p, savedLoadOk) ==
    LET hasSaved == HasSavedSession(p)
        newCost  == IF peerExec[p] = "Unmeasured" /\ savedLoadOk /\ hasSaved
                    THEN AddCost(meter, 1) ELSE meter
    IN
    /\ controllerActive /\ phase = "Open" /\ scopeState[RuntimeScope] = "Active" /\ scopeState[p] = "Stopped"
    /\ peerExec[p] # "Running" /\ chan[p] <= 0 /\ LastChan(chan[p]) < MaxChan
    /\ meter \in 0..(SoftCostLimit - 1) /\ savedLoadOk \in BOOLEAN
    /\ IF hasSaved /\ ~savedLoadOk
       THEN IF peerExec[p] = "Unmeasured"
            THEN meter' = Indeterminate /\ phase' = "Settling" /\ stopReason' = "FaultedClosed"
                 /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
                 /\ UNCHANGED <<scopeState, needsBootstrap, peerExec>>
            ELSE chan' = [chan EXCEPT ![p] = LastChan(@) + 1]
                 /\ needsBootstrap' = [needsBootstrap EXCEPT ![p] = TRUE]
                 /\ UNCHANGED <<scopeState, meter, phase, stopReason, peerExec>>
       ELSE /\ peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
            /\ IF newCost >= SoftCostLimit
               THEN phase' = "Settling" /\ stopReason' = "SoftCostLimit"
                    /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
                    /\ UNCHANGED scopeState
               ELSE chan' = [chan EXCEPT ![p] = LastChan(@) + 1]
                    /\ UNCHANGED <<scopeState, phase, stopReason>>
            /\ UNCHANGED needsBootstrap
    /\ UNCHANGED <<controllerActive, runEpoch, endVars>>

\* Step 2 of channel setup: `mcp Main` self-registers `effect_scopes[p]` under its reserved `chan[p] > 0`
\* (covering both live Coordinator `"Active"` and surviving POSIX orphan `"Ambiguous"` after Coordinator crash).
RegisterReservedChannelScope(p) ==
    /\ phase = "Open" /\ scopeState[RuntimeScope] \in {"Active", "Ambiguous"} /\ scopeState[p] = "Stopped"
    /\ peerExec[p] = "Idle" /\ chan[p] > 0
    /\ scopeState' = [scopeState EXCEPT ![p] = IF controllerActive THEN "Active" ELSE "Ambiguous"]
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, needsBootstrap,
                   chan, peerExec, meter, endVars>>

BeginPeerTurn(p) ==
    /\ controllerActive /\ phase = "Open" /\ scopeState[RuntimeScope] = "Active" /\ scopeState[p] = "Active"
    /\ chan[p] > 0 /\ peerExec[p] = "Idle" /\ meter \in 0..(SoftCostLimit - 1)
    /\ peerExec' = [peerExec EXCEPT ![p] = "Running"]
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, scopeState, needsBootstrap,
                   chan, meter, endVars>>

CompletePeerTurn(p) ==
    LET newCost == AddCost(meter, 1)
    IN
    /\ controllerActive /\ phase \in {"Open", "Draining"} /\ scopeState[p] = "Active"
    /\ chan[p] > 0 /\ peerExec[p] = "Running"
    /\ peerExec' = [peerExec EXCEPT ![p] = "Idle"]
    /\ needsBootstrap' = [needsBootstrap EXCEPT ![p] = FALSE] /\ meter' = newCost
    /\ IF phase = "Open" /\ newCost >= SoftCostLimit
       THEN phase' = "Settling" /\ stopReason' = "SoftCostLimit"
            /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
       ELSE UNCHANGED <<phase, stopReason, chan>>
    /\ UNCHANGED <<controllerActive, runEpoch, scopeState, endVars>>

\* Immediate cohort-wide SQLite fence (`chan := UnbindVal(chan)`) + transition to `Settling` on any turn abort:
\* `scopeState` stays `"Active"` until the `runtime` container tears down all helpers and `"trusted"` tools.
AbortPeerTurn(p, readOk) ==
    LET newCost == AddCost(meter, 1)
    IN
    /\ controllerActive /\ phase \in {"Open", "Draining"} /\ scopeState[p] = "Active"
    /\ chan[p] > 0 /\ peerExec[p] = "Running" /\ readOk \in BOOLEAN
    /\ chan'  = [q \in Peers |-> UnbindVal(chan[q])]
    /\ phase' = "Settling"
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
            /\ stopReason' = IF phase = "Open" /\ newCost >= SoftCostLimit THEN "SoftCostLimit"
                             ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"]
            /\ meter' = Indeterminate /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<controllerActive, runEpoch, scopeState, needsBootstrap, endVars>>

BeginSubordinateVerify ==
    /\ controllerActive /\ phase = "Open" /\ scopeState[VerifyScope] = "Stopped"
    /\ scopeState' = [scopeState EXCEPT ![VerifyScope] = "Active"]
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, needsBootstrap,
                   chan, peerExec, meter, endVars>>

EndSubordinateVerify ==
    /\ controllerActive /\ phase \in {"Open", "Draining", "Settling"} /\ scopeState[VerifyScope] = "Active"
    /\ scopeState' = [scopeState EXCEPT ![VerifyScope] = "Stopped"]
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, needsBootstrap,
                   chan, peerExec, meter, endVars>>

SignalStop(reason) ==
    /\ controllerActive /\ reason \in {"GoalConverged", "Paused", "TimeLimit"}
    /\ IF reason = "TimeLimit"
       THEN phase = "Open" /\ phase' = "Draining" /\ stopReason' = "TimeLimit" /\ UNCHANGED chan
       ELSE /\ phase \in {"Open", "Draining"} /\ phase' = "Settling"
            /\ stopReason' = IF meter = Indeterminate THEN "FaultedClosed" ELSE reason
            /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
    /\ UNCHANGED <<controllerActive, runEpoch, scopeState, needsBootstrap,
                   peerExec, meter, endVars>>

EndDrain ==
    /\ controllerActive /\ phase = "Draining" /\ phase' = "Settling"
    /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
    /\ UNCHANGED <<controllerActive, runEpoch, stopReason, scopeState, needsBootstrap,
                   peerExec, meter, endVars>>

StopPeerChannelInSettlement(p, readOk) ==
    LET d == IF peerExec[p] # "Idle" /\ readOk THEN 1 ELSE 0
    IN
    /\ controllerActive /\ phase = "Settling" /\ scopeState[p] = "Active"
    /\ readOk \in IF peerExec[p] # "Idle" THEN BOOLEAN ELSE {TRUE}
    /\ scopeState' = [scopeState EXCEPT ![p] = "Stopped"] /\ chan' = [chan EXCEPT ![p] = UnbindVal(@)]
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = AddCost(meter, d)
            /\ UNCHANGED stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"] /\ meter' = Indeterminate
            /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<controllerActive, runEpoch, phase, needsBootstrap, endVars>>

SettleParentRuntimeAndEpoch ==
    /\ controllerActive /\ phase = "Settling"
    /\ ChildrenStopped(RuntimeScope) /\ scopeState[VerifyScope] = "Stopped"
    /\ scopeState' = [scopeState EXCEPT ![RuntimeScope] = "Stopped"]
    /\ chan' = [p \in Peers |-> UnbindVal(chan[p])]
    /\ phase' = "Quiescent" /\ controllerActive' = FALSE
    /\ meter' = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ stopReason' = IF meter = Indeterminate \/ HasUnmeasured THEN "FaultedClosed"
                     ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
    /\ UNCHANGED <<runEpoch, needsBootstrap, peerExec, endVars>>

SettleInterruptedEpochRecord ==
    /\ ~controllerActive /\ ~cleaned /\ AllScopesStopped /\ phase \in {"Open", "Draining", "Settling"}
    /\ phase' = "Quiescent" /\ stopReason' = "FaultedClosed"
    /\ chan' = [p \in Peers |-> UnbindVal(chan[p])]
    /\ peerExec' = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ meter' = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ UNCHANGED <<controllerActive, runEpoch, scopeState, needsBootstrap, endVars>>

BeginVerifyFinal ==
    /\ ~controllerActive /\ ~cleaned /\ runEpoch > 0 /\ phase = "Quiescent" /\ AllScopesStopped
    /\ controllerActive' = TRUE /\ finalVerdict' = "Unverified"
    /\ scopeState' = [scopeState EXCEPT ![VerifyScope] = "Active"]
    /\ UNCHANGED <<runEpoch, phase, stopReason, needsBootstrap, chan,
                   peerExec, meter, cleaned, crashesLeft>>

CompleteVerifyFinal(verifyPassed) ==
    /\ VerifyingFinal /\ scopeState[VerifyScope] = "Active" /\ verifyPassed \in BOOLEAN
    /\ scopeState' = [scopeState EXCEPT ![VerifyScope] = "Stopped"] /\ controllerActive' = FALSE
    /\ finalVerdict' = IF verifyPassed /\ meter >= 0 /\ ~HasUnmeasured /\ stopReason # "FaultedClosed"
                       THEN "Passed" ELSE "Failed"
    /\ UNCHANGED <<runEpoch, phase, stopReason, needsBootstrap, chan,
                   peerExec, meter, cleaned, crashesLeft>>

CleanupResources ==
    /\ ~controllerActive /\ ~cleaned /\ phase = "Quiescent"
    /\ finalVerdict # "Unverified" /\ AllScopesStopped /\ cleaned' = TRUE
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, scopeState, needsBootstrap,
                   chan, peerExec, meter, finalVerdict, crashesLeft>>

CrashController ==
    /\ controllerActive /\ crashesLeft > 0
    /\ scopeState' = [s \in Scopes |-> IF scopeState[s] = "Active" THEN "Ambiguous" ELSE scopeState[s]]
    /\ controllerActive' = FALSE /\ crashesLeft' = crashesLeft - 1
    /\ UNCHANGED <<runEpoch, phase, stopReason, needsBootstrap, chan,
                   peerExec, meter, finalVerdict, cleaned>>

ObserveOrphanOrWitnessScopeStopped(s) ==
    /\ ~controllerActive /\ scopeState[s] \in {"Active", "Ambiguous"} /\ ChildrenStopped(s)
    /\ scopeState' = [scopeState EXCEPT ![s] = "Stopped"]
    /\ UNCHANGED <<controllerActive, runEpoch, phase, stopReason, needsBootstrap,
                   chan, peerExec, meter, endVars>>

TerminalHalted ==
    /\ ~controllerActive
    /\ (cleaned \/ ~AllScopesStopped \/ (phase = "Quiescent" /\ finalVerdict # "Unverified"))
    /\ UNCHANGED vars

Next ==
    \/ ActivateEpoch
    \/ \E p \in Peers, ok \in BOOLEAN : ReserveOrCheckSavedPeer(p, ok)
    \/ \E p \in Peers : RegisterReservedChannelScope(p)
    \/ \E p \in Peers : BeginPeerTurn(p)
    \/ \E p \in Peers : CompletePeerTurn(p)
    \/ \E p \in Peers, ok \in BOOLEAN : AbortPeerTurn(p, ok)
    \/ BeginSubordinateVerify \/ EndSubordinateVerify
    \/ \E r \in {"GoalConverged", "Paused", "TimeLimit"} : SignalStop(r)
    \/ EndDrain
    \/ \E p \in Peers, ok \in BOOLEAN : StopPeerChannelInSettlement(p, ok)
    \/ SettleParentRuntimeAndEpoch \/ SettleInterruptedEpochRecord
    \/ BeginVerifyFinal \/ \E ok \in BOOLEAN : CompleteVerifyFinal(ok)
    \/ CleanupResources \/ CrashController
    \/ \E s \in Scopes : ObserveOrphanOrWitnessScopeStopped(s)
    \/ TerminalHalted

Fairness ==
    /\ \A p \in Peers : WF_vars(CompletePeerTurn(p) \/ AbortPeerTurn(p, FALSE))
    /\ \A p \in Peers : WF_vars(\E ok \in BOOLEAN : StopPeerChannelInSettlement(p, ok))
    /\ WF_vars(EndSubordinateVerify) /\ WF_vars(SignalStop("TimeLimit"))
    /\ WF_vars(EndDrain) /\ WF_vars(SettleParentRuntimeAndEpoch)
    /\ WF_vars(\E ok \in BOOLEAN : CompleteVerifyFinal(ok))

SafetySpec == Init /\ [][Next]_vars
Spec       == SafetySpec /\ Fairness
-----------------------------------------------------------------------------
TypeOK ==
    /\ controllerActive \in BOOLEAN /\ runEpoch \in 0..MaxEpochs
    /\ phase \in Phases /\ stopReason \in StopReasons /\ scopeState \in [Scopes -> ScopeStates]
    /\ needsBootstrap \in [Peers -> BOOLEAN] /\ chan \in [Peers -> -MaxChan..MaxChan]
    /\ peerExec \in [Peers -> PeerExecs]
    /\ meter \in MeterValues /\ finalVerdict \in Verdicts /\ cleaned \in BOOLEAN
    /\ crashesLeft \in 0..MaxCrashes

BottomUpScopeContainment == BottomUpValid(scopeState)

FencedEpochAndMeterSafety ==
    /\ controllerActive =>
        /\ ~cleaned /\ \A s \in Scopes : scopeState[s] # "Ambiguous"
        /\ VerifyingFinal => (scopeState[RuntimeScope] = "Stopped" /\ ChildrenStopped(RuntimeScope))
        /\ (~VerifyingFinal) =>
            /\ (phase = "Idle" => scopeState[VerifyScope] = "Stopped")
            /\ (phase = "Settling" => \A p \in Peers : chan[p] <= 0)
            /\ \A p \in Peers : (peerExec[p] = "Running" /\ phase # "Settling") => (scopeState[p] = "Active" /\ chan[p] > 0)
    /\ (phase = "Quiescent" /\ HasUnmeasured) => (meter = Indeterminate)
    /\ (phase = "Quiescent" /\ stopReason # "FaultedClosed") => (meter >= 0 /\ ~HasUnmeasured)
    /\ (meter = Indeterminate) =>
        /\ ~(controllerActive /\ phase = "Open") /\ finalVerdict # "Passed"
        /\ (phase = "Quiescent" => stopReason = "FaultedClosed")
    /\ (finalVerdict = "Passed") =>
        /\ AllScopesStopped /\ meter >= 0 /\ ~HasUnmeasured
        /\ stopReason \in {"GoalConverged", "Paused", "TimeLimit", "SoftCostLimit"}
    /\ cleaned => (~controllerActive /\ AllScopesStopped /\ finalVerdict # "Unverified")

AdmissionAndFallbackFenceStep ==
    [][/\ ((~controllerActive) \/ phase # "Open") =>
            \A p \in Peers : ~(scopeState[p] = "Stopped" /\ scopeState'[p] = "Active")
       /\ \A p \in Peers :
            /\ LastChan(chan'[p]) >= LastChan(chan[p])
            /\ (chan[p] <= 0 /\ chan'[p] > 0) => (chan'[p] = LastChan(chan[p]) + 1)
            /\ (scopeState[p] = "Stopped" /\ scopeState'[p] # "Stopped") => (chan'[p] > 0)
            /\ (~needsBootstrap[p] /\ needsBootstrap'[p] /\ peerExec[p] # "Idle") =>
                (meter' = Indeterminate)]_vars

\* Nontrivial Refinement Mapping to Tier-0 AtomicOrganization lifecycle slice (hiding scopes, chan, crashesLeft):
HighLifecycle == INSTANCE AtomicOrganization WITH
    bootstrapped <- [p \in Peers |-> ~needsBootstrap[p]],
    AllActors <- Peers, Keys <- {"k1"}, MaxLogItems <- 0,
    MaxPeerCommits <- 2, MaxProposals <- 1, AllowExternalAdvance <- FALSE, RequirePeerReview <- FALSE,
    log <- <<>>, acked <- [p \in Peers |-> {}],
    ancestors <- <<{1}>>, targetRef <- 1, peerRef <- [p \in Peers |-> 1],
    proposals <- <<>>, externalAdvanced <- FALSE

AtomicLifecycleSpecRefinement == HighLifecycle!Spec
THEOREM Spec => AtomicLifecycleSpecRefinement

BoundedControllerTermination == HighLifecycle!BoundedTermination
=============================================================================
