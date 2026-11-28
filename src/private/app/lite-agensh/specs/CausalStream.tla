---------------------------- MODULE CausalStream ----------------------------
(* Plane 1: Single Causal Log with Co-Axial Cursors & Signed Channel Fence.*)
(*   - Single append-only causal `log` of [author, target, imm, key].      *)
(*   - Co-axial durable cursors 0 <= baseCursor[p] <= immCursor[p] <= |log|*)
(*     plus ephemeral turnSnap[p] (-1 = idle) and inflightImm[p]; aborts   *)
(*     discard ephemeral cursors with zero durable cursor writes.          *)
(*   - Minimal per-peer fencing: needsBootstrap[p] \in BOOLEAN, signed     *)
(*     chan[p] \in -MaxChan..MaxChan (+k = bound at k, -k = unbound after  *)
(*     k, 0 = never bound), and InTurn(p) == (turnSnap[p] >= 0).           *)
EXTENDS Integers, Sequences, FiniteSets

CONSTANTS Peers, AllActors, Keys, MaxLogItems, MaxChan

ASSUME
    /\ Peers # {} /\ Peers \subseteq AllActors
    /\ Keys # {} /\ "none" \notin Keys
    /\ MaxLogItems \in Nat /\ MaxChan \in Nat /\ MaxChan >= 1

NoKey        == "none"
LogKeys      == Keys \cup {NoKey}
AllTarget    == "all"
Targets      == AllActors \cup {AllTarget}
LastChan(c)  == IF c < 0 THEN -c ELSE c
UnbindVal(c) == IF c > 0 THEN -c ELSE c

VARIABLES log, needsBootstrap, chan, baseCursor, immCursor, turnSnap, inflightImm

fenceVars  == <<needsBootstrap, chan>>
cursorVars == <<baseCursor, immCursor, turnSnap, inflightImm>>
vars       == <<log, fenceVars, cursorVars>>

InTurn(p)         == turnSnap[p] >= 0
VisibleTo(p, msg) == msg.author # p /\ (msg.target = AllTarget \/ msg.target = p)

\* Turn-start projection over (baseCursor[p]..T]: excludes already-piggybacked imm items in
\* (baseCursor..immCursor], keeps NoKey items verbatim, and folds each key over (baseCursor..T]
\* (including newer imm items <= immCursor so stale ~imm items never overwrite newer imm ones).
UnreadTurnIdx(p, T) ==
    {i \in (baseCursor[p] + 1)..T :
        /\ i <= Len(log) /\ VisibleTo(p, log[i])
        /\ (i > immCursor[p] \/ ~log[i].imm)
        /\ (log[i].key = NoKey
            \/ ~\E j \in (i + 1)..T :
                j <= Len(log) /\ VisibleTo(p, log[j]) /\ log[j].key = log[i].key)}

ImmSlice(p, low, high) ==
    {i \in (low + 1)..high : i <= Len(log) /\ VisibleTo(p, log[i]) /\ log[i].imm}

LatestVisibleKeyUpTo(p, k, bound) ==
    LET M == {i \in 1..bound : i <= Len(log) /\ VisibleTo(p, log[i]) /\ log[i].key = k}
    IN IF M = {} THEN 0 ELSE CHOOSE m \in M : \A n \in M : m >= n

HasUnread(p) == needsBootstrap[p] \/ UnreadTurnIdx(p, Len(log)) # {}

Init ==
    /\ log = <<>>
    /\ needsBootstrap = [p \in Peers |-> TRUE] /\ chan        = [p \in Peers |-> 0]
    /\ baseCursor     = [p \in Peers |-> 0]    /\ immCursor   = [p \in Peers |-> 0]
    /\ turnSnap       = [p \in Peers |-> -1]   /\ inflightImm = [p \in Peers |-> 0]

AppendLogItem(author, target, imm, key) ==
    /\ imm \in BOOLEAN /\ key \in LogKeys /\ Len(log) < MaxLogItems
    /\ log' = Append(log, [author |-> author, target |-> target, imm |-> imm, key |-> key])
    /\ UNCHANGED <<fenceVars, cursorVars>>

PeerToolCall(p, callerChan, emit, target, imm, key) ==
    LET hasImm  == ImmSlice(p, inflightImm[p], Len(log)) # {}
        nextLen == IF emit THEN Len(log) + 1 ELSE Len(log)
    IN
    /\ InTurn(p) /\ callerChan \in 1..MaxChan /\ chan[p] = callerChan
    /\ emit \in BOOLEAN /\ imm \in BOOLEAN /\ key \in LogKeys
    /\ (emit => Len(log) < MaxLogItems /\ target \in Targets \ {p})
    /\ ((~emit) => hasImm)
    /\ log'         = IF emit THEN Append(log, [author |-> p, target |-> target, imm |-> imm, key |-> key]) ELSE log
    /\ inflightImm' = IF hasImm THEN [inflightImm EXCEPT ![p] = nextLen] ELSE inflightImm
    /\ UNCHANGED <<fenceVars, baseCursor, immCursor, turnSnap>>

BindChannel(p, resetContext) ==
    /\ ~InTurn(p) /\ LastChan(chan[p]) < MaxChan /\ resetContext \in BOOLEAN
    /\ chan'           = [chan EXCEPT ![p] = LastChan(@) + 1]
    /\ needsBootstrap' = IF resetContext THEN [needsBootstrap EXCEPT ![p] = TRUE] ELSE needsBootstrap
    /\ UNCHANGED <<log, cursorVars>>

RotateContexts(S) ==
    /\ S \subseteq Peers /\ S # {} /\ \A p \in S : ~InTurn(p)
    /\ needsBootstrap' = [p \in Peers |-> IF p \in S THEN TRUE ELSE needsBootstrap[p]]
    /\ UNCHANGED <<log, chan, cursorVars>>

BeginTurn(p, idleWake) ==
    /\ ~InTurn(p) /\ chan[p] > 0 /\ idleWake \in BOOLEAN
    /\ IF idleWake THEN ~HasUnread(p) ELSE HasUnread(p)
    /\ turnSnap'    = [turnSnap    EXCEPT ![p] = Len(log)]
    /\ inflightImm' = [inflightImm EXCEPT ![p] = Len(log)]
    /\ UNCHANGED <<log, fenceVars, baseCursor, immCursor>>

CommitTurn(p, callerChan, settleAll) ==
    /\ InTurn(p) /\ chan[p] = callerChan /\ settleAll \in BOOLEAN
    /\ baseCursor'     = [baseCursor     EXCEPT ![p] = turnSnap[p]]
    /\ immCursor'      = [immCursor      EXCEPT ![p] = inflightImm[p]]
    /\ needsBootstrap' = [needsBootstrap EXCEPT ![p] = FALSE]
    /\ IF settleAll
       THEN chan'        = [q \in Peers |-> UnbindVal(chan[q])]
            /\ turnSnap'    = [q \in Peers |-> -1]
            /\ inflightImm' = [q \in Peers |-> IF q = p THEN inflightImm[p] ELSE immCursor[q]]
       ELSE UNCHANGED <<chan, inflightImm>>
            /\ turnSnap' = [turnSnap EXCEPT ![p] = -1]
    /\ UNCHANGED log

UnbindChannels(S) ==
    /\ S \subseteq Peers /\ S # {} /\ \E p \in S : InTurn(p) \/ chan[p] > 0
    /\ chan'        = [p \in Peers |-> IF p \in S THEN UnbindVal(chan[p]) ELSE chan[p]]
    /\ turnSnap'    = [p \in Peers |-> IF p \in S THEN -1 ELSE turnSnap[p]]
    /\ inflightImm' = [p \in Peers |-> IF p \in S THEN immCursor[p] ELSE inflightImm[p]]
    /\ UNCHANGED <<log, needsBootstrap, baseCursor, immCursor>>

TerminalQuiescent ==
    /\ \A p \in Peers : ~InTurn(p) /\ ((chan[p] <= 0 /\ LastChan(chan[p]) = MaxChan) \/ ~HasUnread(p))
    /\ (Len(log) = MaxLogItems \/ \A p \in Peers : chan[p] <= 0 /\ LastChan(chan[p]) = MaxChan)
    /\ UNCHANGED vars

Next ==
    \/ \E a \in AllActors \ Peers, imm \in BOOLEAN, k \in LogKeys :
        \E t \in Targets \ {a} : AppendLogItem(a, t, imm, k)
    \/ \E p \in Peers, c \in 1..MaxChan, imm \in BOOLEAN, k \in LogKeys :
        \E t \in Targets \ {p} : PeerToolCall(p, c, TRUE, t, imm, k)
    \/ \E p \in Peers, c \in 1..MaxChan :
        PeerToolCall(p, c, FALSE, AllTarget, FALSE, NoKey)
    \/ \E p \in Peers, r \in BOOLEAN : BindChannel(p, r)
    \/ \E S \in (SUBSET Peers) \ {{}} : RotateContexts(S)
    \/ \E p \in Peers, w \in BOOLEAN : BeginTurn(p, w)
    \/ \E p \in Peers, c \in 1..MaxChan, sa \in BOOLEAN : CommitTurn(p, c, sa)
    \/ \E S \in (SUBSET Peers) \ {{}} : UnbindChannels(S)
    \/ TerminalQuiescent

Fairness ==
    \A p \in Peers :
        /\ WF_vars(BindChannel(p, FALSE)) /\ WF_vars(BeginTurn(p, FALSE))
        /\ \A c \in 1..MaxChan : WF_vars(CommitTurn(p, c, FALSE))

SafetySpec == Init /\ [][Next]_vars
Spec       == SafetySpec /\ Fairness
-----------------------------------------------------------------------------
TypeOK ==
    /\ Len(log) <= MaxLogItems /\ needsBootstrap \in [Peers -> BOOLEAN]
    /\ chan \in [Peers -> -MaxChan..MaxChan]
    /\ baseCursor \in [Peers -> 0..MaxLogItems] /\ immCursor \in [Peers -> 0..MaxLogItems]
    /\ turnSnap \in [Peers -> -1..MaxLogItems] /\ inflightImm \in [Peers -> 0..MaxLogItems]

CursorCoherence ==
    \A p \in Peers :
        /\ baseCursor[p] <= immCursor[p] /\ immCursor[p] <= Len(log)
        /\ IF InTurn(p)
           THEN chan[p] > 0 /\ immCursor[p] <= turnSnap[p]
                /\ turnSnap[p] <= inflightImm[p] /\ inflightImm[p] <= Len(log)
           ELSE inflightImm[p] = immCursor[p]

CausalFenceAndFoldingStep ==
    [][/\ \A i \in 1..Len(log) : log'[i] = log[i]
       /\ (Len(log') > Len(log) /\ log'[Len(log')].author \in Peers) =>
            LET p == log'[Len(log')].author IN InTurn(p) /\ chan[p] > 0
       /\ \A p \in Peers :
            /\ LastChan(chan'[p]) >= LastChan(chan[p])
            /\ baseCursor'[p] >= baseCursor[p] /\ immCursor'[p] >= immCursor[p]
            /\ (chan'[p] > 0 /\ chan'[p] # chan[p]) => (chan'[p] = LastChan(chan[p]) + 1)
            /\ (InTurn(p) /\ ~InTurn(p)') =>
                \/ (baseCursor'[p] = turnSnap[p] /\ immCursor'[p] = inflightImm[p]
                    /\ ~needsBootstrap'[p] /\ chan'[p] \in {chan[p], -chan[p]})
                \/ (baseCursor'[p] = baseCursor[p] /\ immCursor'[p] = immCursor[p]
                    /\ needsBootstrap'[p] = needsBootstrap[p] /\ chan'[p] # chan[p])
            /\ (baseCursor'[p] > baseCursor[p]) =>
                LET T    == turnSnap[p]
                    proj == UnreadTurnIdx(p, T)
                    raw  == {i \in (baseCursor[p] + 1)..T :
                                VisibleTo(p, log[i]) /\ (i > immCursor[p] \/ ~log[i].imm)
                                /\ log[i].key = NoKey}
                IN /\ InTurn(p) /\ ~InTurn(p)' /\ raw \subseteq proj
                   /\ \A k \in Keys :
                        LET next == LatestVisibleKeyUpTo(p, k, T)
                            hit  == {i \in proj : log[i].key = k}
                        IN IF next > immCursor[p] \/ (next > baseCursor[p] /\ ~log[next].imm)
                           THEN hit = {next}
                           ELSE hit = {}]_vars

\* Nontrivial Refinement Mapping to Tier-0 AtomicOrganization stream slice:
MappedAcked ==
    [p \in Peers |->
        {i \in 1..immCursor[p] :
            i <= Len(log) /\ VisibleTo(p, log[i]) /\ (i <= baseCursor[p] \/ log[i].imm)}]
MappedBootstrapped == [p \in Peers |-> ~needsBootstrap[p]]

HighStream == INSTANCE AtomicOrganization WITH
    acked <- MappedAcked, bootstrapped <- MappedBootstrapped,
    MaxPeerCommits <- 2, MaxProposals <- 1, AllowExternalAdvance <- FALSE, RequirePeerReview <- FALSE,
    MaxEpochs <- 1, SoftCostLimit <- 1,
    ancestors <- <<{1}>>, targetRef <- 1, peerRef <- [p \in Peers |-> 1],
    proposals <- <<>>, externalAdvanced <- FALSE,
    controllerActive <- FALSE, runEpoch <- 0, phase <- "Idle", stopReason <- "None",
    peerExec <- [p \in Peers |-> "Idle"], meter <- 0, finalVerdict <- "Unverified", cleaned <- FALSE

AtomicStreamSafetyRefinement == HighStream!StreamSafetySpec
THEOREM Spec => AtomicStreamSafetyRefinement

EventualCausalConvergence ==
    (/\ \A p \in Peers : <> [][~BeginTurn(p, TRUE)]_vars
     /\ \A S \in (SUBSET Peers) \ {{}} : <> [][~RotateContexts(S)]_vars /\ <> [][~UnbindChannels(S)]_vars)
    => <>[] (\A p \in Peers :
                /\ ~InTurn(p)
                /\ ((chan[p] > 0 \/ LastChan(chan[p]) < MaxChan) =>
                        (~HasUnread(p) /\ MappedBootstrapped[p]
                         /\ \A i \in 1..Len(log) : VisibleTo(p, log[i]) => HighStream!UpToDateOn(p, i))))
=============================================================================
