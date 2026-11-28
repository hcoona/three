------------------------- MODULE OsScopeRefinement -------------------------
(* Tier-2 Downward Protocol Refinement of Plane 3 (`EpochLifecycle`):      *)
(* Refines Tier-1 `EpochLifecycle` (`THEOREM Spec => Tier1Lifecycle!...`)  *)
(* down to concrete SQLite `effect_scopes` rows and real OS process-tree   *)
(* mechanics:                                                              *)
(*   1. Root scopes (`runtime`, `verify`) spawned directly by Coordinator  *)
(*      write-ahead `PrePidAmbiguous` before OS `CreateProcess` returns    *)
(*      `(pid, start_ticks)` (`SpawnOsRootProcess` -> `PersistRootPid`);   *)
(*      a crash before `PersistRootPid` has no trustworthy post-exit       *)
(*      witness without a PID and therefore fails closed (`Ambiguous`);    *)
(*   2. Coordinator reserves strictly monotonic `chan_token = ABS(c) + 1`  *)
(*      in SQLite before SDK forks `mcp --channel <K>`; `mcp` never        *)
(*      pre-writes `PrePidAmbiguous` and self-registers `(pid, 'Active')`  *)
(*      in `Main` iff `chan_token = K` (even if Coordinator or `runtime`   *)
(*      already exited on POSIX); on any turn abort or helper failure,     *)
(*      Coordinator IMMEDIATELY negates `chan := -K` and enters `Settling` *)
(*      so the root `runtime` container tears down all helpers and         *)
(*      `"trusted"` tools (`RuntimeGroupEmpty`) before Epoch `e + 1`       *)
(*      re-attaches under `+(K + 1)`;                                      *)
(*   3. `"trusted"` SDK shell tools (`runtimeSiblingAlive`) and check      *)
(*      descendants (`verifyDescAlive`) belong to the root Windows Job     *)
(*      Object (`KILL_ON_JOB_CLOSE`, breakaway disabled) or cooperative    *)
(*      POSIX `setsid` process group (explicitly scoped to non-daemonizing *)
(*      descendants in `pgid == rootPid`, so `kill(-pid, 0) == ESRCH`      *)
(*      proves group emptiness even when group leaders exit first).        *)
EXTENDS Integers, FiniteSets

CONSTANTS Peers, MaxEpochs, MaxChan, SoftCostLimit, MaxCrashes

ASSUME
    /\ Peers # {} /\ MaxEpochs \in Nat /\ MaxEpochs >= 1
    /\ MaxChan \in Nat /\ MaxChan >= 1
    /\ SoftCostLimit \in Nat /\ SoftCostLimit >= 1
    /\ MaxCrashes \in Nat

RuntimeScope  == "runtime"
VerifyScope   == "verify"
RootScopes    == {RuntimeScope, VerifyScope}
Scopes        == RootScopes \cup Peers
DbStates      == {"Stopped", "PrePidAmbiguous", "Active"}
Phases        == {"Idle", "Open", "Settling", "Quiescent"}
StopReasons   == {"None", "Paused", "SoftCostLimit", "FaultedClosed"}
PeerExecs     == {"Idle", "Running", "Unmeasured"}
Indeterminate == -1
MaxCostBound  == SoftCostLimit + Cardinality(Peers)

ParentScope(s) == IF s \in Peers THEN RuntimeScope ELSE "root"
LastChan(c)    == IF c < 0 THEN -c ELSE c
UnbindVal(c)   == IF c > 0 THEN -c ELSE c
AddCost(m, d)  == IF m = Indeterminate THEN Indeterminate ELSE m + d

VARIABLES
    coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
    dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
    rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive

vars == <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
          dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
          rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

AllDbScopesStopped == \A s \in Scopes : dbState[s] = "Stopped"
HasUnmeasured      == \E p \in Peers : peerExec[p] # "Idle"

\* Nontrivial Refinement Mapping from SQLite + live Coordinator to Tier-1 `EpochLifecycle!scopeState`:
AbstractScope ==
    [s \in Scopes |->
        IF dbState[s] = "Stopped" THEN "Stopped"
        ELSE IF coordUp THEN "Active"
        ELSE "Ambiguous"]

AbstractChildrenStopped(s) == \A c \in Scopes : (ParentScope(c) = s) => (AbstractScope[c] = "Stopped")

RuntimeGroupEmpty ==
    /\ ~rootAlive[RuntimeScope] /\ ~runtimeSiblingAlive
    /\ \A p \in Peers : dbState[p] = "Stopped" /\ liveMcpTokens[p] = {}

VerifyGroupEmpty == ~rootAlive[VerifyScope] /\ ~verifyDescAlive
-----------------------------------------------------------------------------
Init ==
    /\ coordUp             = FALSE
    /\ runEpoch            = 0
    /\ phase               = "Idle"
    /\ stopReason          = "None"
    /\ chan                = [p \in Peers |-> 0]
    /\ peerExec            = [p \in Peers |-> "Idle"]
    /\ meter               = 0
    /\ crashesLeft         = MaxCrashes
    /\ dbState             = [s \in Scopes |-> "Stopped"]
    /\ rootSpawned         = [r \in RootScopes |-> FALSE]
    /\ mcpSpawned          = [p \in Peers |-> FALSE]
    /\ hasPid              = [s \in Scopes |-> FALSE]
    /\ registeredToken     = [p \in Peers |-> 0]
    /\ rootAlive           = [r \in RootScopes |-> FALSE]
    /\ liveMcpTokens       = [p \in Peers |-> {}]
    /\ runtimeSiblingAlive = FALSE
    /\ verifyDescAlive     = FALSE

\* 1. Epoch activation writes-ahead `PrePidAmbiguous` on `runtime` (refines `EpochLifecycle!ActivateEpoch`).
ActivateEpochWriteAheadRuntime ==
    LET isFresh     == (runEpoch = 0 /\ phase = "Idle")
        isResume    == (runEpoch > 0 /\ phase = "Quiescent" /\ stopReason = "Paused" /\ ~HasUnmeasured)
        isReconnect == (runEpoch > 0 /\ phase \in {"Open", "Settling"})
    IN
    /\ ~coordUp /\ runEpoch < MaxEpochs /\ AllDbScopesStopped /\ meter \in 0..(SoftCostLimit - 1)
    /\ (isFresh \/ isResume \/ isReconnect)
    /\ coordUp'     = TRUE
    /\ runEpoch'    = runEpoch + 1
    /\ phase'       = "Open"
    /\ stopReason'  = "None"
    /\ chan'        = [p \in Peers |-> UnbindVal(chan[p])]
    /\ peerExec'    = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ dbState'     = [dbState EXCEPT ![RuntimeScope] = "PrePidAmbiguous"]
    /\ rootSpawned' = [rootSpawned EXCEPT ![RuntimeScope] = FALSE]
    /\ hasPid'      = [hasPid EXCEPT ![RuntimeScope] = FALSE]
    /\ UNCHANGED <<meter, crashesLeft, mcpSpawned, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

SpawnOsRootProcess(r) ==
    /\ coordUp /\ r \in RootScopes /\ dbState[r] = "PrePidAmbiguous" /\ ~rootSpawned[r] /\ ~rootAlive[r]
    /\ rootSpawned' = [rootSpawned EXCEPT ![r] = TRUE]
    /\ rootAlive'   = [rootAlive EXCEPT ![r] = TRUE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, mcpSpawned, hasPid, registeredToken,
                   liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

PersistRootPid(r) ==
    /\ coordUp /\ r \in RootScopes /\ dbState[r] = "PrePidAmbiguous" /\ rootSpawned[r]
    /\ dbState' = [dbState EXCEPT ![r] = "Active"]
    /\ hasPid'  = [hasPid EXCEPT ![r] = TRUE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   rootSpawned, mcpSpawned, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

WriteAheadSubordinateVerify ==
    /\ coordUp /\ phase = "Open" /\ dbState[VerifyScope] = "Stopped" /\ VerifyGroupEmpty
    /\ dbState'     = [dbState EXCEPT ![VerifyScope] = "PrePidAmbiguous"]
    /\ rootSpawned' = [rootSpawned EXCEPT ![VerifyScope] = FALSE]
    /\ hasPid'      = [hasPid EXCEPT ![VerifyScope] = FALSE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   mcpSpawned, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

SpawnVerifyDescendant ==
    /\ coordUp /\ dbState[VerifyScope] = "Active" /\ rootAlive[VerifyScope] /\ ~verifyDescAlive
    /\ verifyDescAlive' = TRUE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive>>

EndSubordinateVerifyClean ==
    /\ coordUp /\ phase \in {"Open", "Settling"}
    /\ (dbState[VerifyScope] = "Active" \/ (dbState[VerifyScope] = "PrePidAmbiguous" /\ ~rootSpawned[VerifyScope]))
    /\ ~verifyDescAlive
    /\ dbState'   = [dbState EXCEPT ![VerifyScope] = "Stopped"]
    /\ hasPid'    = [hasPid EXCEPT ![VerifyScope] = FALSE]
    /\ rootAlive' = [rootAlive EXCEPT ![VerifyScope] = FALSE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   rootSpawned, mcpSpawned, registeredToken,
                   liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

\* 2. Channel reservation, SDK session launch, and surviving-POSIX `mcp Main` registration.
ReserveChannelInDb(p) ==
    LET nextToken == LastChan(chan[p]) + 1
        newCost   == IF peerExec[p] = "Unmeasured" THEN AddCost(meter, 1) ELSE meter
    IN
    /\ coordUp /\ phase = "Open" /\ dbState[RuntimeScope] = "Active"
    /\ p \in Peers /\ dbState[p] = "Stopped" /\ peerExec[p] # "Running"
    /\ chan[p] <= 0 /\ LastChan(chan[p]) < MaxChan /\ meter \in 0..(SoftCostLimit - 1)
    /\ peerExec' = [peerExec EXCEPT ![p] = "Idle"]
    /\ meter'    = newCost
    /\ IF newCost >= SoftCostLimit
       THEN phase' = "Settling" /\ stopReason' = "SoftCostLimit"
            /\ chan' = [q \in Peers |-> UnbindVal(chan[q])]
            /\ UNCHANGED mcpSpawned
       ELSE chan'          = [chan EXCEPT ![p] = nextToken]
            /\ mcpSpawned' = [mcpSpawned EXCEPT ![p] = FALSE]
            /\ UNCHANGED <<phase, stopReason>>
    /\ UNCHANGED <<coordUp, runEpoch, crashesLeft, dbState, rootSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

SpawnMcpProcess(p) ==
    /\ coordUp /\ phase = "Open" /\ dbState[RuntimeScope] = "Active" /\ rootAlive[RuntimeScope]
    /\ p \in Peers /\ dbState[p] = "Stopped" /\ ~mcpSpawned[p] /\ chan[p] > 0
    /\ mcpSpawned'    = [mcpSpawned EXCEPT ![p] = TRUE]
    /\ liveMcpTokens' = [liveMcpTokens EXCEPT ![p] = @ \cup {chan[p]}]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, hasPid, registeredToken,
                   rootAlive, runtimeSiblingAlive, verifyDescAlive>>

McpMainSelfRegister(p, k) ==
    /\ p \in Peers /\ k \in liveMcpTokens[p]
    /\ phase = "Open" /\ dbState[RuntimeScope] = "Active" /\ dbState[p] = "Stopped"
    /\ peerExec[p] = "Idle" /\ chan[p] = k
    /\ dbState'         = [dbState EXCEPT ![p] = "Active"]
    /\ hasPid'          = [hasPid EXCEPT ![p] = TRUE]
    /\ registeredToken' = [registeredToken EXCEPT ![p] = k]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   rootSpawned, mcpSpawned,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

\* Active turn execution + immediate SQLite fence + transition to `Settling` (`AbortPeerTurn`):
\* Epoch `e + 1` re-attaches under `+(K + 1)` ONLY AFTER `RuntimeGroupEmpty` proves all helpers and `"trusted"` shells exited!
BeginPeerTurnInOpen(p) ==
    /\ coordUp /\ phase = "Open" /\ dbState[RuntimeScope] = "Active" /\ dbState[p] = "Active"
    /\ chan[p] > 0 /\ peerExec[p] = "Idle" /\ meter \in 0..(SoftCostLimit - 1)
    /\ peerExec' = [peerExec EXCEPT ![p] = "Running"]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

FenceAbortTurnToSettling(p, readOk) ==
    LET newCost == AddCost(meter, 1)
    IN
    /\ coordUp /\ phase = "Open" /\ dbState[p] = "Active" /\ chan[p] > 0
    /\ peerExec[p] = "Running" /\ readOk \in BOOLEAN
    /\ chan'  = [q \in Peers |-> UnbindVal(chan[q])]
    /\ phase' = "Settling"
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = newCost
            /\ stopReason' = IF newCost >= SoftCostLimit THEN "SoftCostLimit"
                             ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"]
            /\ meter' = Indeterminate /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<coordUp, runEpoch, crashesLeft, dbState, rootSpawned, mcpSpawned,
                   hasPid, registeredToken, rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

\* 3. `"trusted"` SDK shell subprocess runs inside `runtime`'s root Job Object / `setsid` group.
SpawnTrustedRuntimeSibling ==
    /\ coordUp /\ phase = "Open" /\ dbState[RuntimeScope] = "Active" /\ rootAlive[RuntimeScope]
    /\ (\E p \in Peers : dbState[p] = "Active") /\ ~runtimeSiblingAlive
    /\ runtimeSiblingAlive' = TRUE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, verifyDescAlive>>

\* Entering `Settling` (including pre-`CreateProcess` root failure or pre-`Main` helper failure) atomically
\* negates ALL positive `chan` tokens across the cohort (`\A q \in Peers : chan'[q] <= 0`).
BeginSettlement ==
    /\ coordUp /\ phase = "Open"
    /\ (dbState[RuntimeScope] = "Active" \/ ~rootSpawned[RuntimeScope])
    /\ phase'      = "Settling"
    /\ stopReason' = IF meter = Indeterminate THEN "FaultedClosed" ELSE "Paused"
    /\ chan'       = [q \in Peers |-> UnbindVal(chan[q])]
    /\ UNCHANGED <<coordUp, runEpoch, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

TerminateRuntimeGroupInSettlement ==
    /\ coordUp /\ phase = "Settling" /\ hasPid[RuntimeScope]
    /\ (rootAlive[RuntimeScope] \/ runtimeSiblingAlive \/ \E p \in Peers : liveMcpTokens[p] # {})
    /\ rootAlive'           = [rootAlive EXCEPT ![RuntimeScope] = FALSE]
    /\ liveMcpTokens'       = [p \in Peers |-> {}]
    /\ runtimeSiblingAlive' = FALSE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken, verifyDescAlive>>

TerminateVerifyGroupInSettlement ==
    /\ coordUp /\ phase = "Settling" /\ hasPid[VerifyScope] /\ (rootAlive[VerifyScope] \/ verifyDescAlive)
    /\ rootAlive'       = [rootAlive EXCEPT ![VerifyScope] = FALSE]
    /\ verifyDescAlive' = FALSE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken, liveMcpTokens, runtimeSiblingAlive>>

StopPeerChannelClean(p, readOk) ==
    LET d == IF peerExec[p] # "Idle" /\ readOk THEN 1 ELSE 0
    IN
    /\ coordUp /\ phase = "Settling" /\ p \in Peers /\ dbState[p] = "Active" /\ hasPid[p]
    /\ registeredToken[p] \notin liveMcpTokens[p]
    /\ readOk \in IF peerExec[p] # "Idle" THEN BOOLEAN ELSE {TRUE}
    /\ dbState'         = [dbState EXCEPT ![p] = "Stopped"]
    /\ hasPid'          = [hasPid EXCEPT ![p] = FALSE]
    /\ registeredToken' = [registeredToken EXCEPT ![p] = 0]
    /\ chan'            = [chan EXCEPT ![p] = UnbindVal(@)]
    /\ IF readOk
       THEN peerExec' = [peerExec EXCEPT ![p] = "Idle"] /\ meter' = AddCost(meter, d)
            /\ UNCHANGED stopReason
       ELSE peerExec' = [peerExec EXCEPT ![p] = "Unmeasured"] /\ meter' = Indeterminate
            /\ stopReason' = "FaultedClosed"
    /\ UNCHANGED <<coordUp, runEpoch, phase, crashesLeft,
                   rootSpawned, mcpSpawned, rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

SettleRuntimeAndEpochClean ==
    /\ coordUp /\ phase = "Settling" /\ dbState[VerifyScope] = "Stopped" /\ VerifyGroupEmpty
    /\ RuntimeGroupEmpty
    /\ dbState'    = [dbState EXCEPT ![RuntimeScope] = "Stopped"]
    /\ hasPid'     = [hasPid EXCEPT ![RuntimeScope] = FALSE]
    /\ chan'       = [p \in Peers |-> UnbindVal(chan[p])]
    /\ phase'      = "Quiescent"
    /\ coordUp'    = FALSE
    /\ meter'      = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ stopReason' = IF meter = Indeterminate \/ HasUnmeasured THEN "FaultedClosed"
                     ELSE IF stopReason = "None" THEN "Paused" ELSE stopReason
    /\ UNCHANGED <<runEpoch, peerExec, crashesLeft, rootSpawned, mcpSpawned,
                   registeredToken, rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

\* 4. Coordinator crash & independent OS process exits.
CrashCoordinator(killJobOnClose) ==
    /\ coordUp /\ crashesLeft > 0 /\ killJobOnClose \in BOOLEAN
    /\ coordUp'     = FALSE
    /\ crashesLeft' = crashesLeft - 1
    /\ IF killJobOnClose
       THEN rootAlive'            = [r \in RootScopes |-> FALSE]
            /\ liveMcpTokens'     = [p \in Peers |-> {}]
            /\ runtimeSiblingAlive' = FALSE
            /\ verifyDescAlive'   = FALSE
       ELSE UNCHANGED <<rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>
    /\ UNCHANGED <<runEpoch, phase, stopReason, chan, peerExec, meter,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken>>

RootProcessExit(r) ==
    /\ r \in RootScopes /\ rootAlive[r]
    /\ rootAlive' = [rootAlive EXCEPT ![r] = FALSE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

McpProcessExit(p, k) ==
    /\ p \in Peers /\ k \in liveMcpTokens[p]
    /\ liveMcpTokens' = [liveMcpTokens EXCEPT ![p] = @ \ {k}]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, runtimeSiblingAlive, verifyDescAlive>>

RuntimeSiblingExit ==
    /\ runtimeSiblingAlive
    /\ runtimeSiblingAlive' = FALSE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, verifyDescAlive>>

VerifyDescendantExit ==
    /\ verifyDescAlive
    /\ verifyDescAlive' = FALSE
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   dbState, rootSpawned, mcpSpawned, hasPid, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive>>

\* 5. Recovery probes & interrupted epoch settlement.
ReapExitedChild(p) ==
    /\ ~coordUp /\ p \in Peers /\ dbState[p] = "Active" /\ hasPid[p] /\ registeredToken[p] \notin liveMcpTokens[p]
    /\ dbState'         = [dbState EXCEPT ![p] = "Stopped"]
    /\ hasPid'          = [hasPid EXCEPT ![p] = FALSE]
    /\ registeredToken' = [registeredToken EXCEPT ![p] = 0]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   rootSpawned, mcpSpawned,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

ReapExitedRoot(r) ==
    /\ ~coordUp /\ r \in RootScopes /\ dbState[r] = "Active" /\ hasPid[r] /\ ~rootAlive[r]
    /\ AbstractChildrenStopped(r)
    /\ (r = RuntimeScope => RuntimeGroupEmpty)
    /\ (r = VerifyScope  => VerifyGroupEmpty)
    /\ dbState' = [dbState EXCEPT ![r] = "Stopped"]
    /\ hasPid'  = [hasPid EXCEPT ![r] = FALSE]
    /\ UNCHANGED <<coordUp, runEpoch, phase, stopReason, chan, peerExec, meter, crashesLeft,
                   rootSpawned, mcpSpawned, registeredToken,
                   rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

SettleInterruptedEpoch ==
    /\ ~coordUp /\ AllDbScopesStopped /\ phase \in {"Open", "Settling"}
    /\ phase'      = "Quiescent"
    /\ stopReason' = "FaultedClosed"
    /\ chan'       = [p \in Peers |-> UnbindVal(chan[p])]
    /\ peerExec'   = [p \in Peers |-> IF peerExec[p] = "Idle" THEN "Idle" ELSE "Unmeasured"]
    /\ meter'      = IF HasUnmeasured THEN Indeterminate ELSE meter
    /\ UNCHANGED <<coordUp, runEpoch, crashesLeft, dbState, rootSpawned, mcpSpawned,
                   hasPid, registeredToken, rootAlive, liveMcpTokens, runtimeSiblingAlive, verifyDescAlive>>

TerminalStutter ==
    /\ ~coordUp
    /\ (~AllDbScopesStopped \/ runEpoch = MaxEpochs \/ meter = Indeterminate
        \/ meter >= SoftCostLimit \/ HasUnmeasured
        \/ (phase = "Quiescent" /\ stopReason # "Paused"))
    /\ UNCHANGED vars

Next ==
    \/ ActivateEpochWriteAheadRuntime
    \/ \E r \in RootScopes : SpawnOsRootProcess(r)
    \/ \E r \in RootScopes : PersistRootPid(r)
    \/ WriteAheadSubordinateVerify \/ SpawnVerifyDescendant \/ EndSubordinateVerifyClean
    \/ \E p \in Peers : ReserveChannelInDb(p)
    \/ \E p \in Peers : SpawnMcpProcess(p)
    \/ \E p \in Peers, k \in 1..MaxChan : McpMainSelfRegister(p, k)
    \/ \E p \in Peers : BeginPeerTurnInOpen(p)
    \/ \E p \in Peers, ok \in BOOLEAN : FenceAbortTurnToSettling(p, ok)
    \/ SpawnTrustedRuntimeSibling
    \/ BeginSettlement
    \/ TerminateRuntimeGroupInSettlement \/ TerminateVerifyGroupInSettlement
    \/ \E p \in Peers, ok \in BOOLEAN : StopPeerChannelClean(p, ok)
    \/ SettleRuntimeAndEpochClean
    \/ \E kt \in BOOLEAN : CrashCoordinator(kt)
    \/ \E r \in RootScopes : RootProcessExit(r)
    \/ \E p \in Peers, k \in 1..MaxChan : McpProcessExit(p, k)
    \/ RuntimeSiblingExit
    \/ VerifyDescendantExit
    \/ \E p \in Peers : ReapExitedChild(p)
    \/ \E r \in RootScopes : ReapExitedRoot(r)
    \/ SettleInterruptedEpoch
    \/ TerminalStutter

Fairness ==
    /\ \A r \in RootScopes : WF_vars(PersistRootPid(r))
    /\ WF_vars(BeginSettlement)
    /\ WF_vars(TerminateRuntimeGroupInSettlement) /\ WF_vars(TerminateVerifyGroupInSettlement)
    /\ WF_vars(EndSubordinateVerifyClean)
    /\ \A p \in Peers : WF_vars(\E ok \in BOOLEAN : StopPeerChannelClean(p, ok))
    /\ WF_vars(SettleRuntimeAndEpochClean)
    /\ \A r \in RootScopes : WF_vars(RootProcessExit(r)) /\ WF_vars(ReapExitedRoot(r))
    /\ \A p \in Peers, k \in 1..MaxChan : WF_vars(McpProcessExit(p, k))
    /\ WF_vars(RuntimeSiblingExit) /\ WF_vars(VerifyDescendantExit)
    /\ \A p \in Peers : WF_vars(ReapExitedChild(p))
    /\ WF_vars(SettleInterruptedEpoch)

SafetySpec == Init /\ [][Next]_vars
Spec       == SafetySpec /\ Fairness
-----------------------------------------------------------------------------
TypeOK ==
    /\ coordUp \in BOOLEAN /\ runEpoch \in 0..MaxEpochs
    /\ phase \in Phases /\ stopReason \in StopReasons /\ chan \in [Peers -> -MaxChan..MaxChan]
    /\ peerExec \in [Peers -> PeerExecs] /\ meter \in Indeterminate..MaxCostBound
    /\ crashesLeft \in 0..MaxCrashes /\ dbState \in [Scopes -> DbStates]
    /\ rootSpawned \in [RootScopes -> BOOLEAN] /\ mcpSpawned \in [Peers -> BOOLEAN]
    /\ hasPid \in [Scopes -> BOOLEAN] /\ registeredToken \in [Peers -> 0..MaxChan]
    /\ rootAlive \in [RootScopes -> BOOLEAN] /\ liveMcpTokens \in [Peers -> SUBSET (1..MaxChan)]
    /\ runtimeSiblingAlive \in BOOLEAN /\ verifyDescAlive \in BOOLEAN

NoPrePidAmbiguousOnPeerChannel ==
    \A p \in Peers : dbState[p] # "PrePidAmbiguous" /\ (dbState[p] = "Active" <=> (hasPid[p] /\ registeredToken[p] > 0))

ZeroOrphanWhenAbstractStopped ==
    /\ (AbstractScope[RuntimeScope] = "Stopped") => RuntimeGroupEmpty
    /\ (AbstractScope[VerifyScope] = "Stopped") => VerifyGroupEmpty
    /\ \A p \in Peers : (AbstractScope[p] = "Stopped" /\ AbstractScope[RuntimeScope] = "Stopped")
                        => (liveMcpTokens[p] = {})

PrePidRootFailsClosed ==
    [][\A r \in RootScopes :
        (~coordUp /\ dbState[r] = "PrePidAmbiguous" /\ ~hasPid[r]) =>
            (AbstractScope'[r] = "Ambiguous")]_vars

\* Direct Tier-2 -> Tier-1 `INSTANCE` Refinement Mapping into `EpochLifecycle` (including active `peerExec` & `meter`!):
Tier1Lifecycle == INSTANCE EpochLifecycle WITH
    controllerActive <- coordUp,
    scopeState       <- AbstractScope,
    needsBootstrap   <- [p \in Peers |-> TRUE],
    finalVerdict     <- "Unverified",
    cleaned          <- FALSE

Tier1LifecycleSafetyRefinement == Tier1Lifecycle!SafetySpec
THEOREM Spec => Tier1LifecycleSafetyRefinement

\* Every execution that avoids the fail-closed pre-PID root crash window eventually settles to `Quiescent`
\* with all SQLite scopes `Stopped` and both OS process groups (`RuntimeGroupEmpty`, `VerifyGroupEmpty`) empty.
BoundedLiveEpochSettlement ==
    ([] (\A r \in RootScopes : ~(~coordUp /\ dbState[r] = "PrePidAmbiguous" /\ ~hasPid[r])))
    => <>[] (~coordUp /\ (runEpoch = 0 \/ phase = "Quiescent")
             /\ AllDbScopesStopped /\ RuntimeGroupEmpty /\ VerifyGroupEmpty)
=============================================================================
