---------------------- MODULE PhiAccrualFailureDetector ----------------------
\* Abstract discrete TLA+ protocol model for PhiAccrualFailureDetector v3:
\*   1. Lifecycle state machine (Idle -> Bootstrapping -> Active, plus Reset
\*      epochs) keeping the sliding window free of synthetic bootstrap samples.
\*   2. O(1) amortized two-stack queue (front + back) of Chan pairwise variance
\*      aggregates without subtraction of squares, checked for exact 1st- and
\*      2nd-moment agreement and outlier-free prefix isolation against an
\*      uncompressed bounded history sequence across flips, pops, and resets.
\*   3. Herlihy-Wing single-point linearizability of atomic (state, clock)
\*      snapshot acquisition (carrying both 1st and 2nd moments) followed by
\*      outside-lock evaluation under arbitrary reader preemption and races.
\*   4. Epoch-scoped finite-horizon completeness under an abstract integer
\*      mean-plus-variance deadline (transcendental Real/Float64 properties of
\*      the Exponential and Normal formulas are checked separately in Z3,
\*      without claiming end-to-end TLC refinement of IEEE-754 exp/log10).

EXTENDS Naturals, Integers, Sequences

CONSTANTS
    MaxTime,          \* Finite monotonic clock horizon (e.g., 5)
    CrashDeadline,    \* Finite-horizon bound on PeerCrash so MaxTime exceeds the suspicion deadline
    PauseMargin,      \* Discrete acceptable-pause + threshold margin above sample mean (>= 1)
    WindowCapacities, \* Non-empty set of sliding-window capacities W >= 1 (e.g., {1, 3})
    MaxHeartbeats,    \* Finite heartbeat budget >= (2 * W) + 2 exercising repeated stack flips
    MaxResets,        \* Finite Reset() budget exercising monitoring-epoch transitions
    BootstrapMean,    \* Discrete bootstrap deadline when isMonitoring /\ count = 0 (e.g., 2)
    Readers           \* Non-empty finite set of concurrent reader threads

ASSUME
    /\ MaxTime \in Nat /\ MaxTime >= 4
    /\ CrashDeadline \in Nat /\ CrashDeadline >= 1
    /\ PauseMargin \in Nat /\ PauseMargin >= 1
    /\ WindowCapacities # {}
    /\ MaxHeartbeats \in Nat
    /\ \A w \in WindowCapacities :
        /\ w \in Nat /\ w >= 1
        /\ MaxHeartbeats >= (2 * w) + 2
    /\ MaxResets \in Nat
    /\ BootstrapMean \in Nat /\ BootstrapMean >= 1
    \* Ensure the finite clock horizon MaxTime suffices to reach the discrete deadline
    \* after any crash at <= CrashDeadline (both Bootstrapping and Active with variance):
    /\ (CrashDeadline + BootstrapMean) < MaxTime
    /\ ((CrashDeadline * CrashDeadline) + CrashDeadline + PauseMargin) < MaxTime
    /\ Readers # {}

VARIABLES
    windowCapacity, \* Active sliding-window capacity chosen from WindowCapacities
    clock,          \* Monotonic TimeProvider timestamp in 0..MaxTime
    crashed,        \* TRUE once the monitored peer permanently stops sending heartbeats
    hbCount,        \* Total heartbeats emitted in 0..MaxHeartbeats
    resetCount,     \* Total Reset() calls emitted in 0..MaxResets
    isMonitoring,   \* TRUE iff at least one heartbeat has been recorded since init/Reset
    lastTimestamp,  \* Timestamp of most recent heartbeat when isMonitoring = TRUE
    front,          \* Front stack of Chan prefix aggregates (bottom = newest of front, top = oldest)
    backVals,       \* Back stack of raw intervals (1 = oldest in back, Len = newest)
    backAggs,       \* Back stack of Chan prefix aggregates matching prefixes of backVals
    history,        \* Ghost specification oracle: bounded sequence of observed intervals
    unitHealthy,    \* Ghost flag: TRUE iff heartbeats arrived every tick since monitoring began
    readers         \* Per-reader state modeling lock snapshot -> preempted outside-lock eval

vars == <<
    windowCapacity, clock, crashed, hbCount, resetCount, isMonitoring, lastTimestamp,
    front, backVals, backAggs,
    history, unitHealthy, readers
>>

-----------------------------------------------------------------------------
\* Mathematical reference helpers and exact-integer Chan pairwise combination

Max(a, b) == IF a >= b THEN a ELSE b

RECURSIVE SeqSum(_)
SeqSum(s) == IF s = <<>> THEN 0 ELSE Head(s) + SeqSum(Tail(s))

RECURSIVE SeqSumSq(_)
SeqSumSq(s) == IF s = <<>> THEN 0 ELSE (Head(s) * Head(s)) + SeqSumSq(Tail(s))

BoundedAppend(s, x, cap) ==
    IF Len(s) < cap
    THEN Append(s, x)
    ELSE Append(Tail(s), x)

EmptyAgg == [cnt |-> 0, sum |-> 0, varNum |-> 0]

SingletonAgg(x) == [cnt |-> 1, sum |-> x, varNum |-> 0]

\* Exact integer representation of SampleWindow.ChanAggregate.Combine:
\*   For aggregate [cnt |-> n, sum |-> S, varNum |-> V] where V = n * M2 = n * Sum(x^2) - S^2,
\*   combining A and B via Chan's parallel formula (without subtracting squares) yields:
\*     n_ab = n_a + n_b
\*     S_ab = S_a + S_b
\*     V_ab = ((n_ab * n_b * V_a) + (n_ab * n_a * V_b) + (n_a * S_b - n_b * S_a)^2) \div (n_a * n_b)
ChanNumerator(a, b) ==
    LET n    == a.cnt + b.cnt
        diff == (a.cnt * b.sum) - (b.cnt * a.sum)
    IN (n * b.cnt * a.varNum) + (n * a.cnt * b.varNum) + (diff * diff)

ChanCombine(a, b) ==
    IF a.cnt = 0 THEN b
    ELSE IF b.cnt = 0 THEN a
    ELSE [
        cnt    |-> a.cnt + b.cnt,
        sum    |-> a.sum + b.sum,
        varNum |-> ChanNumerator(a, b) \div (a.cnt * b.cnt)
    ]

\* Flip backVals[Len(backVals) .. 2] into front after discarding backVals[1] (the evicted oldest sample).
RECURSIVE BuildFrontFromBack(_, _)
BuildFrontFromBack(rem, acc) ==
    IF rem = <<>> THEN <<>>
    ELSE LET lastIdx == Len(rem)
             nextAcc == ChanCombine(acc, SingletonAgg(rem[lastIdx]))
             prefix  == SubSeq(rem, 1, lastIdx - 1)
         IN <<nextAcc>> \o BuildFrontFromBack(prefix, nextAcc)

\* Read current window aggregate in O(1) from front top and backAggs top (SampleWindow.Snapshot).
WindowSnapshot(fSeq, bAggSeq) ==
    IF Len(fSeq) = 0 /\ Len(bAggSeq) = 0 THEN EmptyAgg
    ELSE IF Len(fSeq) = 0 THEN bAggSeq[Len(bAggSeq)]
    ELSE IF Len(bAggSeq) = 0 THEN fSeq[Len(fSeq)]
    ELSE ChanCombine(fSeq[Len(fSeq)], bAggSeq[Len(bAggSeq)])

\* Scope & abstraction boundary of this TLA+ model:
\*   TLC verifies the discrete protocol over finite integers (lifecycle epochs, O(1)
\*   amortized two-stack Chan queue equivalence across stack flips/evictions/resets,
\*   Herlihy-Wing single-point reader linearizability under outside-lock preemption,
\*   and epoch-scoped finite-horizon liveness). It does NOT evaluate transcendental
\*   IEEE-754 floating-point exp/log10; floating-point range safety, softplus tail
\*   stability, post-outlier Float64 precision, and threshold-crossing bounds are
\*   verified separately in Z3 (scripts/verify_formal_spec.py).
SuspectedFromSnapshot(mon, cnt, totalSum, varNum, elapsed) ==
    IF ~mon THEN FALSE
    ELSE IF cnt = 0 THEN elapsed > BootstrapMean
    ELSE (elapsed * cnt * cnt) > ((totalSum * cnt) + (PauseMargin * cnt * cnt) + varNum)

IdleReaderRecord == [
    pc                |-> "Idle",
    mon               |-> FALSE,
    hasElapsed        |-> FALSE,
    elapsed           |-> 0,
    cnt               |-> 0,
    totalSum          |-> 0,
    varNum            |-> 0,
    healthy           |-> FALSE,
    expectedSuspected |-> FALSE,
    suspected         |-> FALSE,
    available         |-> TRUE
]

-----------------------------------------------------------------------------
\* Initial state and transitions

Init ==
    /\ windowCapacity \in WindowCapacities
    /\ clock = 0
    /\ crashed = FALSE
    /\ hbCount = 0
    /\ resetCount = 0
    /\ isMonitoring = FALSE
    /\ lastTimestamp = 0
    /\ front = <<>>
    /\ backVals = <<>>
    /\ backAggs = <<>>
    /\ history = <<>>
    /\ unitHealthy = FALSE
    /\ readers = [r \in Readers |-> IdleReaderRecord]

\* Monotonic clock advances one tick.
Tick ==
    /\ clock < MaxTime
    /\ clock' = clock + 1
    /\ unitHealthy' = (unitHealthy /\ isMonitoring /\ ((clock + 1) - lastTimestamp <= 1))
    /\ UNCHANGED <<
        windowCapacity, crashed, hbCount, resetCount, isMonitoring, lastTimestamp,
        front, backVals, backAggs,
        history, readers
       >>

\* Writer records a heartbeat at the current clock timestamp under the detector lock,
\* mirroring PhiAccrualFailureDetector.RecordHeartbeat / SampleWindow.Add's exact
\* two-stack eviction/flip and push transitions.
RecordHeartbeat ==
    /\ ~crashed
    /\ hbCount < MaxHeartbeats
    /\ hbCount' = hbCount + 1
    /\ IF ~isMonitoring
       THEN \* First heartbeat transitions Idle -> Bootstrapping without synthetic samples.
            /\ isMonitoring' = TRUE
            /\ lastTimestamp' = clock
            /\ unitHealthy' = TRUE
            /\ UNCHANGED <<front, backVals, backAggs, history>>
       ELSE LET interval == Max(0, clock - lastTimestamp)
                single   == SingletonAgg(interval)
                full     == (Len(front) + Len(backVals)) = windowCapacity
                evictedFront ==
                    IF ~full THEN front
                    ELSE IF Len(front) > 0 THEN SubSeq(front, 1, Len(front) - 1)
                    ELSE BuildFrontFromBack(Tail(backVals), EmptyAgg)
                evictedBackVals ==
                    IF full /\ Len(front) = 0 THEN <<>> ELSE backVals
                evictedBackAggs ==
                    IF full /\ Len(front) = 0 THEN <<>> ELSE backAggs
                nextBackAgg ==
                    IF Len(evictedBackAggs) = 0 THEN single
                    ELSE ChanCombine(evictedBackAggs[Len(evictedBackAggs)], single)
            IN /\ isMonitoring' = TRUE
               /\ lastTimestamp' = clock
               /\ front' = evictedFront
               /\ backVals' = Append(evictedBackVals, interval)
               /\ backAggs' = Append(evictedBackAggs, nextBackAgg)
               /\ history' = BoundedAppend(history, interval, windowCapacity)
               /\ unitHealthy' = (unitHealthy /\ interval <= 1)
    /\ UNCHANGED <<windowCapacity, clock, crashed, resetCount, readers>>

\* Explicit application reset starts a new monitoring epoch (resetCount + 1)
\* and returns the detector to Idle. Notably, ResetDetector is NOT gated on ~crashed:
\* the caller has no crash oracle and may reset even after a peer has crashed.
ResetDetector ==
    /\ isMonitoring
    /\ resetCount < MaxResets
    /\ resetCount' = resetCount + 1
    /\ isMonitoring' = FALSE
    /\ lastTimestamp' = 0
    /\ front' = <<>>
    /\ backVals' = <<>>
    /\ backAggs' = <<>>
    /\ history' = <<>>
    /\ unitHealthy' = FALSE
    /\ UNCHANGED <<windowCapacity, clock, crashed, hbCount, readers>>

\* Monitored peer crashes permanently within the modeled pre-horizon window CrashDeadline
\* (bounded so MaxTime - CrashDeadline suffices for the suspicion deadline to elapse
\* before HorizonStutter stops the finite model clock at MaxTime).
PeerCrash ==
    /\ ~crashed
    /\ isMonitoring
    /\ clock <= CrashDeadline
    /\ crashed' = TRUE
    /\ UNCHANGED <<
        windowCapacity, clock, hbCount, resetCount, isMonitoring, lastTimestamp,
        front, backVals, backAggs,
        history, unitHealthy, readers
       >>

\* Reader step 1 (inside lock — the Herlihy-Wing linearization point):
\*   Atomically captures the two-stack Chan snapshot AND current clock.
\*   Simultaneously records expectedSuspected from the uncompressed ghost history
\*   oracle at this exact instant so LinearizableReaderInvariant verifies true
\*   single-point linearizability after arbitrary outside-lock preemption.
AcquireSnapshot(r) ==
    /\ readers[r].pc = "Idle"
    /\ LET elapsedSnap == IF isMonitoring THEN Max(0, clock - lastTimestamp) ELSE 0
           winSnap     == WindowSnapshot(front, backAggs)
           histCnt     == Len(history)
           histSum     == SeqSum(history)
           histVarNum  == (histCnt * SeqSumSq(history)) - (histSum * histSum)
           oracleSusp  == SuspectedFromSnapshot(isMonitoring, histCnt, histSum, histVarNum, elapsedSnap)
       IN readers' = [readers EXCEPT ![r] = [
              pc                |-> "Captured",
              mon               |-> isMonitoring,
              hasElapsed        |-> isMonitoring,
              elapsed           |-> elapsedSnap,
              cnt               |-> winSnap.cnt,
              totalSum          |-> winSnap.sum,
              varNum            |-> winSnap.varNum,
              healthy           |-> unitHealthy,
              expectedSuspected |-> oracleSusp,
              suspected         |-> FALSE,
              available         |-> TRUE
          ]]
    /\ UNCHANGED <<
        windowCapacity, clock, crashed, hbCount, resetCount, isMonitoring, lastTimestamp,
        front, backVals, backAggs,
        history, unitHealthy
       >>

\* Reader step 2 (outside lock, after arbitrary preemption/ticks/heartbeats):
\* evaluates phi and IsAvailable purely from the captured snapshot.
EvaluateOutsideLock(r) ==
    /\ readers[r].pc = "Captured"
    /\ LET snap == readers[r]
           susp == SuspectedFromSnapshot(snap.mon, snap.cnt, snap.totalSum, snap.varNum, snap.elapsed)
       IN readers' = [readers EXCEPT ![r] = [
              snap EXCEPT
                  !.pc = "Done",
                  !.suspected = susp,
                  !.available = ~susp
          ]]
    /\ UNCHANGED <<
        windowCapacity, clock, crashed, hbCount, resetCount, isMonitoring, lastTimestamp,
        front, backVals, backAggs,
        history, unitHealthy
       >>

\* Reader consumes its result and may start a new query.
CompleteRead(r) ==
    /\ readers[r].pc = "Done"
    /\ readers' = [readers EXCEPT ![r] = IdleReaderRecord]
    /\ UNCHANGED <<
        windowCapacity, clock, crashed, hbCount, resetCount, isMonitoring, lastTimestamp,
        front, backVals, backAggs,
        history, unitHealthy
       >>

\* Terminal stuttering at horizon keeps behaviors infinite for temporal checking.
HorizonStutter ==
    /\ clock = MaxTime
    /\ UNCHANGED vars

Next ==
    \/ Tick
    \/ RecordHeartbeat
    \/ ResetDetector
    \/ PeerCrash
    \/ \E r \in Readers : AcquireSnapshot(r) \/ EvaluateOutsideLock(r) \/ CompleteRead(r)
    \/ HorizonStutter

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Tick)
    /\ \A r \in Readers : WF_vars(EvaluateOutsideLock(r))

-----------------------------------------------------------------------------
\* Safety Invariants

TypeOK ==
    /\ windowCapacity \in WindowCapacities
    /\ clock \in 0..MaxTime
    /\ crashed \in BOOLEAN
    /\ hbCount \in 0..MaxHeartbeats
    /\ resetCount \in 0..MaxResets
    /\ isMonitoring \in BOOLEAN
    /\ lastTimestamp \in 0..MaxTime
    /\ Len(front) <= windowCapacity
    /\ Len(backVals) <= windowCapacity
    /\ Len(backAggs) = Len(backVals)
    /\ (Len(front) + Len(backVals)) <= windowCapacity
    /\ \A i \in 1..Len(backVals) : backVals[i] \in 0..MaxTime
    /\ \A i \in 1..Len(front) :
        /\ front[i].cnt = i
        /\ front[i].sum \in Nat
        /\ front[i].varNum \in Nat
    /\ \A i \in 1..Len(backAggs) :
        /\ backAggs[i].cnt = i
        /\ backAggs[i].sum \in Nat
        /\ backAggs[i].varNum \in Nat
    /\ Len(history) <= windowCapacity
    /\ unitHealthy \in BOOLEAN
    /\ \A r \in Readers :
        /\ readers[r].pc \in {"Idle", "Captured", "Done"}
        /\ readers[r].hasElapsed \in BOOLEAN
        /\ readers[r].elapsed \in 0..MaxTime
        /\ readers[r].cnt \in 0..windowCapacity
        /\ readers[r].varNum \in Nat
        /\ readers[r].expectedSuspected \in BOOLEAN
        /\ readers[r].suspected \in BOOLEAN
        /\ readers[r].available \in BOOLEAN

\* Idle and Bootstrapping never contaminate the sliding window with synthetic intervals.
BootstrapPurityInvariant ==
    /\ (~isMonitoring) =>
           (Len(front) = 0 /\ Len(backVals) = 0 /\ Len(backAggs) = 0 /\ Len(history) = 0 /\ lastTimestamp = 0)
    /\ (isMonitoring /\ Len(history) = 0) =>
           (Len(front) = 0 /\ Len(backVals) = 0 /\ Len(backAggs) = 0)
    /\ isMonitoring => (lastTimestamp <= clock)

\* Two-stack Chan queue preserves exact FIFO order, every stack-slot prefix aggregate,
\* and combined 1st & 2nd moments of bounded history across flips, pops, and resets.
SlidingWindowMatchesHistory ==
    LET fLen       == Len(front)
        bLen       == Len(backVals)
        snap       == WindowSnapshot(front, backAggs)
        histCnt    == Len(history)
        histSum    == SeqSum(history)
        histVarNum == (histCnt * SeqSumSq(history)) - (histSum * histSum)
    IN /\ (fLen + bLen) = histCnt
       /\ bLen = Len(backAggs)
       /\ backVals = SubSeq(history, fLen + 1, histCnt)
       /\ \A k \in 1..fLen :
              LET slice == SubSeq(history, (fLen - k) + 1, fLen)
                  sSum  == SeqSum(slice)
              IN /\ front[k].cnt = k
                 /\ front[k].sum = sSum
                 /\ front[k].varNum = (k * SeqSumSq(slice)) - (sSum * sSum)
       /\ \A j \in 1..bLen :
              LET slice == SubSeq(history, fLen + 1, fLen + j)
                  sSum  == SeqSum(slice)
              IN /\ backAggs[j].cnt = j
                 /\ backAggs[j].sum = sSum
                 /\ backAggs[j].varNum = (j * SeqSumSq(slice)) - (sSum * sSum)
       /\ snap.cnt = histCnt
       /\ snap.sum = histSum
       /\ snap.varNum = histVarNum
       /\ snap.varNum >= 0
       /\ (fLen > 0 /\ bLen > 0) =>
              LET fTop == front[fLen]
                  bTop == backAggs[bLen]
              IN (ChanNumerator(fTop, bTop) % (fTop.cnt * bTop.cnt)) = 0

CurrentlySuspected ==
    LET snap    == WindowSnapshot(front, backAggs)
        elapsed == IF isMonitoring THEN Max(0, clock - lastTimestamp) ELSE 0
    IN SuspectedFromSnapshot(isMonitoring, snap.cnt, snap.sum, snap.varNum, elapsed)

\* Higher sample variance strictly cushions suspicion beyond the zero-variance
\* deadline (mean + PauseMargin). Reachable under MaxTime = 5 at history = <<0, 2>>,
\* lastTimestamp = 2, clock = 5 (elapsed = 3): elapsed * cnt^2 = 12 exceeds the
\* zero-variance bound 8 yet stays within 8 + varNum = 12, so any formula or
\* snapshot that ignored 2nd-moment variance would violate this invariant.
VarianceCushionInvariant ==
    LET snap         == WindowSnapshot(front, backAggs)
        elapsed      == clock - lastTimestamp
        zeroVarBound == (snap.sum * snap.cnt) + (PauseMargin * snap.cnt * snap.cnt)
    IN /\ ( /\ isMonitoring
            /\ snap.cnt >= 1
            /\ snap.varNum > 0
            /\ (elapsed * snap.cnt * snap.cnt) > zeroVarBound
            /\ (elapsed * snap.cnt * snap.cnt) <= (zeroVarBound + snap.varNum) )
          => ~CurrentlySuspected
       /\ \A r \in Readers :
            LET rZeroVar == (readers[r].totalSum * readers[r].cnt)
                            + (PauseMargin * readers[r].cnt * readers[r].cnt)
                rLhs == readers[r].elapsed * readers[r].cnt * readers[r].cnt
            IN ( /\ readers[r].pc = "Done"
                 /\ readers[r].mon
                 /\ readers[r].cnt >= 1
                 /\ readers[r].varNum > 0
                 /\ rLhs > rZeroVar
                 /\ rLhs <= (rZeroVar + readers[r].varNum) )
               => (~readers[r].suspected /\ readers[r].available)

\* Herlihy-Wing single-point linearizability, PhiSnapshot cross-field coherence,
\* & preemption safety:
\*   1. Every completed outside-lock Evaluate() / GetPhi() / IsAvailable() read
\*      matches the uncompressed history oracle captured atomically at AcquireSnapshot
\*      AND preserves full internal cross-field coherence across (mon, hasElapsed,
\*      elapsed, cnt, totalSum, varNum, suspected, available), despite arbitrary
\*      concurrent heartbeats, ring-buffer wraparounds, Reset() calls, or clock advances.
\*   2. A reader whose snapshot observed a unit-healthy peer never fabricates a
\*      false suspicion when preempted before outside-lock evaluation.
LinearizableReaderInvariant ==
    \A r \in Readers :
        /\ readers[r].hasElapsed = readers[r].mon
        /\ (readers[r].pc = "Captured" /\ readers[r].healthy) => readers[r].elapsed <= 1
        /\ (readers[r].pc = "Done") =>
               /\ readers[r].suspected = readers[r].expectedSuspected
               /\ readers[r].suspected = SuspectedFromSnapshot(
                      readers[r].mon,
                      readers[r].cnt,
                      readers[r].totalSum,
                      readers[r].varNum,
                      readers[r].elapsed)
               /\ readers[r].available = ~readers[r].expectedSuspected
               /\ readers[r].available = ~readers[r].suspected
        /\ (readers[r].pc = "Done" /\ readers[r].healthy) =>
               (readers[r].hasElapsed /\ readers[r].elapsed <= 1 /\ ~readers[r].suspected /\ readers[r].available)
        /\ (readers[r].pc = "Done" /\ ~readers[r].mon) =>
               (~readers[r].hasElapsed /\ readers[r].elapsed = 0 /\ readers[r].cnt = 0
                /\ readers[r].totalSum = 0 /\ readers[r].varNum = 0
                /\ ~readers[r].suspected /\ readers[r].available)

-----------------------------------------------------------------------------
\* Temporal Liveness Properties

\* Epoch-scoped Eventual Strong Completeness (within the finite-horizon crash model):
\*   If a monitored peer has crashed in monitoring epoch e (resetCount = e) and the
\*   caller does not subsequently call Reset() to discard epoch e ([](resetCount = e)),
\*   advancing time eventually suspects the peer permanently — both when the crash
\*   occurred right after Heartbeat #1 (Bootstrapping) and in Active steady state.
EventualStrongCompleteness ==
    \A e \in 0..MaxResets :
        []((crashed /\ isMonitoring /\ resetCount = e /\ [](resetCount = e)) => <>[]CurrentlySuspected)

\* Preempted readers that captured a snapshot always finish pure evaluation.
PreemptedReadersComplete ==
    \A r \in Readers : []((readers[r].pc = "Captured") => <>(readers[r].pc # "Captured"))

=============================================================================
