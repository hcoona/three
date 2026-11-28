---------------------------- MODULE WorkspaceCas ----------------------------
(* Plane 2: Two-Phase Cross-Store CAS over an Abstract Partial-Order DAG.  *)
(*   - Trusts Git for merge-base and 3-way tree merging via nondeterministic*)
(*     merge + ProtectedDiff oracle on `(targetRef, head)`.                *)
(*   - Proves 3-step cross-store CAS (`open -> committing -> CAS(targetRef)*)
(*     -> integrated | open`), head/review races, Peer Integrator requests,*)
(*     and crash reconciliation via `head \in ancestors[targetRef]`.       *)
EXTENDS Integers, Sequences, FiniteSets

CONSTANTS Peers, MaxPeerCommits, MaxProposals, AllowExternalAdvance, RequirePeerReview

ASSUME
    /\ Peers # {} /\ MaxPeerCommits \in Nat /\ MaxPeerCommits >= 2
    /\ MaxProposals \in Nat /\ MaxProposals >= 1
    /\ AllowExternalAdvance \in BOOLEAN /\ RequirePeerReview \in BOOLEAN

\* Self-contained net-state ProtectedDiff lemma: reverts cancel; distinct paths do not.
ProtectedDiff(ProtectedPaths, baseTree, candTree) ==
    \E path \in ProtectedPaths : candTree[path] # baseTree[path]
ASSUME
    /\ ~ProtectedDiff({"p1", "p2"}, [p1 |-> 0, p2 |-> 0], [p1 |-> 0, p2 |-> 0])
    /\ ProtectedDiff({"p1", "p2"}, [p1 |-> 0, p2 |-> 0], [p1 |-> 1, p2 |-> 0])

BaseCid          == 1
MaxDagNodes      == MaxPeerCommits + MaxProposals + (IF AllowExternalAdvance THEN 1 ELSE 0)
ProposalStatuses == {"open", "committing", "integrated"}
EvalStages       == {"idle", "verifying", "cas_pending", "post_cas"}
IdleEval         == [stage |-> "idle", propId |-> 0, snapTarget |-> BaseCid,
                     snapHead |-> BaseCid, touchesProtected |-> FALSE]

VARIABLES
    controllerUp, ancestors, targetRef, peerRef, proposals, eval, externalAdvanced

dagVars == <<ancestors, targetRef, peerRef>>
vars    == <<controllerUp, dagVars, proposals, eval, externalAdvanced>>

ApprovalSatisfied(prop, touchesProtected) ==
    (~(RequirePeerReview \/ touchesProtected)) \/ prop.approved

Init ==
    /\ controllerUp = TRUE /\ ancestors = <<{BaseCid}>>
    /\ targetRef = BaseCid /\ peerRef = [p \in Peers |-> BaseCid]
    /\ proposals = <<>> /\ eval = IdleEval /\ externalAdvanced = FALSE

AuthorCommit(p) ==
    LET newCid == Len(ancestors) + 1
    IN
    /\ Len(ancestors) < MaxPeerCommits
    /\ ancestors' = Append(ancestors, ancestors[peerRef[p]] \cup {newCid})
    /\ peerRef'   = [peerRef EXCEPT ![p] = newCid]
    /\ UNCHANGED <<controllerUp, targetRef, proposals, eval, externalAdvanced>>

PeerSyncMerge(p) ==
    LET pCid == peerRef[p]  newCid == Len(ancestors) + 1
    IN
    /\ targetRef \notin ancestors[pCid]
    /\ IF pCid \in ancestors[targetRef]
       THEN peerRef' = [peerRef EXCEPT ![p] = targetRef] /\ UNCHANGED ancestors
       ELSE /\ Len(ancestors) < MaxPeerCommits
            /\ ancestors' = Append(ancestors, ancestors[pCid] \cup ancestors[targetRef] \cup {newCid})
            /\ peerRef'   = [peerRef EXCEPT ![p] = newCid]
    /\ UNCHANGED <<controllerUp, targetRef, proposals, eval, externalAdvanced>>

OpenProposal(p) ==
    /\ Len(proposals) < MaxProposals /\ peerRef[p] # BaseCid
    /\ ~\E id \in 1..Len(proposals) : proposals[id].head = peerRef[p]
    /\ proposals' = Append(proposals,
        [author |-> p, head |-> peerRef[p], status |-> "open", requested |-> FALSE, approved |-> FALSE])
    /\ UNCHANGED <<controllerUp, dagVars, eval, externalAdvanced>>

UpdateProposalHead(p, id) ==
    /\ id \in 1..Len(proposals) /\ proposals[id].author = p /\ proposals[id].status = "open"
    /\ peerRef[p] # BaseCid /\ proposals[id].head # peerRef[p]
    /\ proposals' = [proposals EXCEPT ![id].head = peerRef[p],
                                      ![id].requested = FALSE, ![id].approved = FALSE]
    /\ UNCHANGED <<controllerUp, dagVars, eval, externalAdvanced>>

ApproveProposal(reviewer, id) ==
    /\ id \in 1..Len(proposals) /\ proposals[id].status = "open"
    /\ reviewer # proposals[id].author /\ ~proposals[id].approved
    /\ proposals' = [proposals EXCEPT ![id].approved = TRUE]
    /\ UNCHANGED <<controllerUp, dagVars, eval, externalAdvanced>>

RevokeApproval(reviewer, id) ==
    /\ id \in 1..Len(proposals) /\ proposals[id].status = "open"
    /\ reviewer # proposals[id].author /\ proposals[id].approved
    /\ proposals' = [proposals EXCEPT ![id].approved = FALSE]
    /\ UNCHANGED <<controllerUp, dagVars, eval, externalAdvanced>>

RequestIntegration(requester, id) ==
    /\ requester \in Peers /\ id \in 1..Len(proposals)
    /\ proposals[id].status = "open" /\ ~proposals[id].requested
    /\ proposals[id].head \notin ancestors[targetRef]
    /\ proposals' = [proposals EXCEPT ![id].requested = TRUE]
    /\ UNCHANGED <<controllerUp, dagVars, eval, externalAdvanced>>

BeginEvaluation(id, mergeOk, touchesProtected) ==
    LET prop == proposals[id]
        ok   == prop.head \notin ancestors[targetRef] /\ mergeOk
                /\ ApprovalSatisfied(prop, touchesProtected)
    IN
    /\ controllerUp /\ eval.stage = "idle" /\ id \in 1..Len(proposals)
    /\ prop.status = "open" /\ prop.requested
    /\ mergeOk \in BOOLEAN /\ touchesProtected \in BOOLEAN
    /\ ((targetRef \in ancestors[prop.head]) => mergeOk) /\ ((~mergeOk) => ~touchesProtected)
    /\ IF ok
       THEN eval' = [stage |-> "verifying", propId |-> id, snapTarget |-> targetRef,
                     snapHead |-> prop.head, touchesProtected |-> touchesProtected]
            /\ UNCHANGED proposals
       ELSE proposals' = [proposals EXCEPT ![id].requested = FALSE] /\ UNCHANGED eval
    /\ UNCHANGED <<controllerUp, dagVars, externalAdvanced>>

CompleteVerificationAndPreCommit(passed) ==
    LET id   == eval.propId  prop == proposals[id]
        lock == passed /\ targetRef = eval.snapTarget /\ prop.status = "open"
                /\ prop.head = eval.snapHead /\ ApprovalSatisfied(prop, eval.touchesProtected)
    IN
    /\ controllerUp /\ eval.stage = "verifying" /\ passed \in BOOLEAN
    /\ IF lock
       THEN proposals' = [proposals EXCEPT ![id].status = "committing"]
            /\ eval' = [eval EXCEPT !.stage = "cas_pending"]
       ELSE /\ proposals' = IF prop.head = eval.snapHead
                            THEN [proposals EXCEPT ![id].requested = FALSE]
                            ELSE proposals
            /\ eval' = IdleEval
    /\ UNCHANGED <<controllerUp, dagVars, externalAdvanced>>

OutOfBandTargetAdvance(subsumesHead) ==
    LET newCid       == Len(ancestors) + 1
        extAncestors == IF subsumesHead
                        THEN ancestors[eval.snapTarget] \cup ancestors[eval.snapHead] \cup {newCid}
                        ELSE ancestors[targetRef] \cup {newCid}
    IN
    /\ AllowExternalAdvance /\ eval.stage \in {"verifying", "cas_pending"} /\ ~externalAdvanced
    /\ subsumesHead \in BOOLEAN /\ (subsumesHead => eval.stage = "cas_pending")
    /\ externalAdvanced' = TRUE
    /\ ancestors' = Append(ancestors, extAncestors) /\ targetRef' = newCid
    /\ UNCHANGED <<controllerUp, peerRef, proposals, eval>>

ExecuteArtifactCas ==
    LET id             == eval.propId  isFf == eval.snapTarget \in ancestors[eval.snapHead]
        mergeCid       == Len(ancestors) + 1
        mergeAncestors == ancestors[eval.snapTarget] \cup ancestors[eval.snapHead] \cup {mergeCid}
    IN
    /\ controllerUp /\ eval.stage = "cas_pending"
    /\ IF targetRef = eval.snapTarget
       THEN IF isFf
            THEN targetRef' = eval.snapHead /\ UNCHANGED ancestors
            ELSE ancestors' = Append(ancestors, mergeAncestors) /\ targetRef' = mergeCid
       ELSE UNCHANGED <<ancestors, targetRef>>
    /\ eval' = [IdleEval EXCEPT !.stage = "post_cas", !.propId = id]
    /\ UNCHANGED <<controllerUp, peerRef, proposals, externalAdvanced>>

CompleteMetadataPostCas ==
    LET id == eval.propId  landed == proposals[id].head \in ancestors[targetRef]
    IN
    /\ controllerUp /\ eval.stage = "post_cas"
    /\ proposals' = [proposals EXCEPT ![id].status = IF landed THEN "integrated" ELSE "open",
                                      ![id].requested = FALSE]
    /\ eval' = IdleEval
    /\ UNCHANGED <<controllerUp, dagVars, externalAdvanced>>

CrashController ==
    /\ controllerUp /\ controllerUp' = FALSE /\ eval' = IdleEval
    /\ UNCHANGED <<dagVars, proposals, externalAdvanced>>

RecoverAndReconcileStores ==
    /\ ~controllerUp /\ controllerUp' = TRUE
    /\ proposals' = [id \in 1..Len(proposals) |->
        IF proposals[id].status = "committing"
        THEN [proposals[id] EXCEPT !.status = IF proposals[id].head \in ancestors[targetRef]
                                              THEN "integrated" ELSE "open",
                                   !.requested = FALSE]
        ELSE proposals[id]]
    /\ UNCHANGED <<dagVars, eval, externalAdvanced>>

TerminalQuiescent ==
    /\ controllerUp /\ eval.stage = "idle"
    /\ \A id \in 1..Len(proposals) : proposals[id].status # "committing" /\ ~proposals[id].requested
    /\ UNCHANGED vars

Next ==
    \/ \E p \in Peers : AuthorCommit(p)
    \/ \E p \in Peers : PeerSyncMerge(p)
    \/ \E p \in Peers : OpenProposal(p)
    \/ \E p \in Peers, id \in 1..MaxProposals : UpdateProposalHead(p, id)
    \/ \E r \in Peers, id \in 1..MaxProposals : ApproveProposal(r, id)
    \/ \E r \in Peers, id \in 1..MaxProposals : RevokeApproval(r, id)
    \/ \E q \in Peers, id \in 1..MaxProposals : RequestIntegration(q, id)
    \/ \E id \in 1..MaxProposals, mOk \in BOOLEAN, tp \in BOOLEAN : BeginEvaluation(id, mOk, tp)
    \/ \E ok \in BOOLEAN : CompleteVerificationAndPreCommit(ok)
    \/ \E sub \in BOOLEAN : OutOfBandTargetAdvance(sub)
    \/ ExecuteArtifactCas \/ CompleteMetadataPostCas
    \/ CrashController \/ RecoverAndReconcileStores \/ TerminalQuiescent

Fairness ==
    /\ WF_vars(RecoverAndReconcileStores)
    /\ \A id \in 1..MaxProposals : WF_vars(\E mOk \in BOOLEAN, tp \in BOOLEAN : BeginEvaluation(id, mOk, tp))
    /\ WF_vars(\E ok \in BOOLEAN : CompleteVerificationAndPreCommit(ok))
    /\ WF_vars(ExecuteArtifactCas) /\ WF_vars(CompleteMetadataPostCas)

SafetySpec == Init /\ [][Next]_vars
Spec       == SafetySpec /\ Fairness
-----------------------------------------------------------------------------
TypeOK ==
    /\ controllerUp \in BOOLEAN /\ ancestors[BaseCid] = {BaseCid}
    /\ Len(ancestors) <= MaxDagNodes /\ targetRef \in 1..Len(ancestors)
    /\ \A p \in Peers : peerRef[p] \in 1..Len(ancestors)
    /\ Len(proposals) <= MaxProposals /\ eval.stage \in EvalStages /\ externalAdvanced \in BOOLEAN
    /\ \A id \in 1..Len(proposals) :
        proposals[id].author \in Peers /\ proposals[id].head \in 1..Len(ancestors)
        /\ proposals[id].status \in ProposalStatuses /\ proposals[id].requested \in BOOLEAN
        /\ proposals[id].approved \in BOOLEAN

TargetAncestryPreservation ==
    /\ ancestors[BaseCid] = {BaseCid} /\ BaseCid \in ancestors[targetRef]
    /\ \A id \in 1..Len(proposals) :
        (proposals[id].status = "integrated") => (proposals[id].head \in ancestors[targetRef])

\* Nontrivial Lamport-Abadi Refinement Mapping to Tier-0 AtomicOrganization workspace slice:
\* linearizes 3-step cross-store CAS + crash recovery at the exact ExecuteArtifactCas step!
MappedProposals ==
    [id \in 1..Len(proposals) |->
        [proposals[id] EXCEPT
            !.status    = IF @ = "committing"
                          THEN IF proposals[id].head \in ancestors[targetRef]
                               THEN "integrated" ELSE "open"
                          ELSE @,
            !.requested = IF proposals[id].status = "committing"
                          THEN (controllerUp /\ proposals[id].head \notin ancestors[targetRef])
                          ELSE @]]

HighWorkspace == INSTANCE AtomicOrganization WITH
    proposals <- MappedProposals,
    AllActors <- Peers, Keys <- {"k1"}, MaxLogItems <- 0, MaxEpochs <- 1, SoftCostLimit <- 1,
    log <- <<>>, acked <- [p \in Peers |-> {}], bootstrapped <- [p \in Peers |-> FALSE],
    controllerActive <- FALSE, runEpoch <- 0, phase <- "Idle", stopReason <- "None",
    peerExec <- [p \in Peers |-> "Idle"], meter <- 0, finalVerdict <- "Unverified", cleaned <- FALSE

AtomicWorkspaceSafetyRefinement == HighWorkspace!WorkspaceSafetySpec
THEOREM Spec => AtomicWorkspaceSafetyRefinement

MonotonicVerifiedCasStep ==
    [][/\ ancestors[targetRef] \subseteq ancestors'[targetRef']
       /\ ((targetRef' # targetRef) /\ (externalAdvanced' = externalAdvanced)) =>
            LET id == eval.propId
            IN eval.stage = "cas_pending" /\ proposals[id].status = "committing"
               /\ ApprovalSatisfied(proposals[id], eval.touchesProtected)]_vars

RequestResolutionLiveness ==
    (/\ <> [][~CrashController]_vars
     /\ \A q \in Peers, id \in 1..MaxProposals : <> [][~RequestIntegration(q, id)]_vars)
    => (/\ <>[] (controllerUp /\ eval.stage = "idle"
                 /\ \A id \in 1..Len(proposals) : ~proposals[id].requested /\ proposals[id].status # "committing")
        /\ HighWorkspace!WorkspaceLiveness)
=============================================================================
