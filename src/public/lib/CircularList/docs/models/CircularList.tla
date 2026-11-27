---------------------------- MODULE CircularList ----------------------------
EXTENDS Naturals, Integers, Sequences, FiniteSets

\* Finite-state protocol specification for IO.Github.Hcoona.Collections.CircularList.
\* Verifies ring-buffer refinement against a bounded FIFO sequence, post-mutation
\* synchronous OnOverflow callback coherence (including reentrant Add/Clear/read
\* inside OnOverflow), live-slot reference clearing (zero stale retention in dead
\* backing-array slots), dual-segment GetSegments() partition equivalence, and
\* version-stamped fail-fast Enumerator invalidation.

CONSTANTS MaxCapacity, Values, Cleared, MaxSteps, NumEnumerators

ASSUME MaxCapacity \in Nat /\ MaxCapacity >= 1
ASSUME Values # {} /\ Cleared \notin Values
ASSUME MaxSteps \in Nat /\ MaxSteps >= 1
ASSUME NumEnumerators \in Nat /\ NumEnumerators >= 1

VARIABLES
    cap,
    buf,
    startIndex,
    count,
    version,
    specSeq,
    evictedLog,
    specEvictedLog,
    callbackDepth,
    pendingEvicted,
    enums,
    lastOp,
    steps

vars == <<
    cap,
    buf,
    startIndex,
    count,
    version,
    specSeq,
    evictedLog,
    specEvictedLog,
    callbackDepth,
    pendingEvicted,
    enums,
    lastOp,
    steps
>>

OpTags == {"Init", "Collection", "Open", "Step", "Reset", "Dispose"}

TailLength == cap - startIndex

PhysIndex(logicalIdx) ==
    IF logicalIdx < TailLength
    THEN startIndex + logicalIdx
    ELSE logicalIdx - TailLength

RingSeq ==
    [k \in 1..count |-> buf[PhysIndex(k - 1)]]

FirstSegment ==
    IF count = 0 THEN <<>>
    ELSE IF count <= TailLength
         THEN [k \in 1..count |-> buf[startIndex + (k - 1)]]
         ELSE [k \in 1..TailLength |-> buf[startIndex + (k - 1)]]

SecondSegment ==
    IF count <= TailLength THEN <<>>
    ELSE LET headLen == count - TailLength
         IN [k \in 1..headLen |-> buf[k - 1]]

IsLivePhysicalSlot(p) ==
    \E i \in 0..(count - 1) : PhysIndex(i) = p

BlankEnumerator ==
    [active |-> FALSE, ver |-> 0, idx |-> -1, current |-> Cleared, trapped |-> FALSE]

Init ==
    /\ cap \in 1..MaxCapacity
    /\ buf = [p \in 0..(cap - 1) |-> Cleared]
    /\ startIndex = 0
    /\ count = 0
    /\ version = 0
    /\ specSeq = <<>>
    /\ evictedLog = <<>>
    /\ specEvictedLog = <<>>
    /\ callbackDepth = 0
    /\ pendingEvicted = Cleared
    /\ enums = [e \in 1..NumEnumerators |-> BlankEnumerator]
    /\ lastOp = [op |-> "Init", enum |-> 0]
    /\ steps = 0

\* Exact state update of CircularList<T>.AddCore(item, out overwrittenItem).
\* Crucially, ring state is committed BEFORE any OnOverflow handler runs.
AddCoreTransition(v, enterCallback) ==
    IF count < cap THEN
        LET writeIdx == IF count < TailLength
                        THEN startIndex + count
                        ELSE count - TailLength
        IN  /\ cap' = cap
            /\ buf' = [buf EXCEPT ![writeIdx] = v]
            /\ startIndex' = startIndex
            /\ count' = count + 1
            /\ version' = version + 1
            /\ specSeq' = Append(specSeq, v)
            /\ evictedLog' = evictedLog
            /\ specEvictedLog' = specEvictedLog
            /\ callbackDepth' = 0
            /\ pendingEvicted' = Cleared
    ELSE
        LET evicted == buf[startIndex]
            nextStart == IF startIndex = cap - 1 THEN 0 ELSE startIndex + 1
            nextTail == IF cap = 1 THEN <<>> ELSE SubSeq(specSeq, 2, cap)
        IN  /\ cap' = cap
            /\ buf' = [buf EXCEPT ![startIndex] = v]
            /\ startIndex' = nextStart
            /\ count' = cap
            /\ version' = version + 1
            /\ specSeq' = Append(nextTail, v)
            /\ evictedLog' = Append(evictedLog, evicted)
            /\ specEvictedLog' = Append(specEvictedLog, Head(specSeq))
            /\ callbackDepth' = IF enterCallback THEN 1 ELSE 0
            /\ pendingEvicted' = IF enterCallback THEN evicted ELSE Cleared

ClearTransition(nextDepth, nextPending) ==
    LET clearedBuf ==
        IF count = 0 THEN buf
        ELSE IF count <= TailLength
             THEN [p \in 0..(cap - 1) |->
                     IF startIndex <= p /\ p < startIndex + count
                     THEN Cleared
                     ELSE buf[p]]
             ELSE [p \in 0..(cap - 1) |->
                     IF (startIndex <= p /\ p < cap) \/ (0 <= p /\ p < count - TailLength)
                     THEN Cleared
                     ELSE buf[p]]
    IN  /\ cap' = cap
        /\ buf' = clearedBuf
        /\ startIndex' = 0
        /\ count' = 0
        /\ version' = version + 1
        /\ specSeq' = <<>>
        /\ evictedLog' = evictedLog
        /\ specEvictedLog' = specEvictedLog
        /\ callbackDepth' = nextDepth
        /\ pendingEvicted' = nextPending

CallerAdd(v, hasSubscriber) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ AddCoreTransition(v, hasSubscriber)
    /\ enums' = enums
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ steps' = steps + 1

CallerSetItem(i, v) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ i \in 0..(count - 1)
    /\ cap' = cap
    /\ buf' = [buf EXCEPT ![PhysIndex(i)] = v]
    /\ startIndex' = startIndex
    /\ count' = count
    /\ version' = version + 1
    /\ specSeq' = [specSeq EXCEPT ![i + 1] = v]
    /\ evictedLog' = evictedLog
    /\ specEvictedLog' = specEvictedLog
    /\ callbackDepth' = 0
    /\ pendingEvicted' = Cleared
    /\ enums' = enums
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ steps' = steps + 1

CallerClear ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ ClearTransition(0, Cleared)
    /\ enums' = enums
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ steps' = steps + 1

\* Synchronous OnOverflow handler actions: execute while callbackDepth = 1
\* AFTER AddCore committed the post-eviction ring state.
OverflowHandlerReentrantAdd(v) ==
    /\ callbackDepth = 1
    /\ steps < MaxSteps
    /\ AddCoreTransition(v, FALSE)
    /\ enums' = enums
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ steps' = steps + 1

OverflowHandlerReentrantClear ==
    /\ callbackDepth = 1
    /\ steps < MaxSteps
    /\ ClearTransition(0, Cleared)
    /\ enums' = enums
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ steps' = steps + 1

OverflowHandlerReturn ==
    /\ callbackDepth = 1
    /\ callbackDepth' = 0
    /\ pendingEvicted' = Cleared
    /\ lastOp' = [op |-> "Collection", enum |-> 0]
    /\ UNCHANGED <<cap, buf, startIndex, count, version, specSeq, evictedLog, specEvictedLog, enums, steps>>

\* Value-type Enumerator lifecycle and version-checked MoveNext / Reset / Dispose.
OpenEnumerator(e) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ enums' = [enums EXCEPT ![e] =
                    [active |-> TRUE, ver |-> version, idx |-> 0, current |-> Cleared, trapped |-> FALSE]]
    /\ lastOp' = [op |-> "Open", enum |-> e]
    /\ UNCHANGED <<cap, buf, startIndex, count, version, specSeq, evictedLog, specEvictedLog, callbackDepth, pendingEvicted>>
    /\ steps' = steps + 1

StepEnumerator(e) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ enums[e].active
    /\ ~enums[e].trapped
    /\ IF enums[e].ver # version THEN
           enums' = [enums EXCEPT ![e].trapped = TRUE]
       ELSE IF enums[e].idx >= 0 /\ enums[e].idx < count THEN
           LET item == buf[PhysIndex(enums[e].idx)]
           IN enums' = [enums EXCEPT ![e].idx = enums[e].idx + 1,
                                     ![e].current = item]
       ELSE
           enums' = [enums EXCEPT ![e].idx = -1,
                                  ![e].current = Cleared]
    /\ lastOp' = [op |-> "Step", enum |-> e]
    /\ UNCHANGED <<cap, buf, startIndex, count, version, specSeq, evictedLog, specEvictedLog, callbackDepth, pendingEvicted>>
    /\ steps' = steps + 1

ResetEnumerator(e) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ enums[e].active
    /\ ~enums[e].trapped
    /\ IF enums[e].ver # version THEN
           enums' = [enums EXCEPT ![e].trapped = TRUE]
       ELSE
           enums' = [enums EXCEPT ![e].idx = 0,
                                  ![e].current = Cleared]
    /\ lastOp' = [op |-> "Reset", enum |-> e]
    /\ UNCHANGED <<cap, buf, startIndex, count, version, specSeq, evictedLog, specEvictedLog, callbackDepth, pendingEvicted>>
    /\ steps' = steps + 1

DisposeEnumerator(e) ==
    /\ callbackDepth = 0
    /\ steps < MaxSteps
    /\ enums[e].active
    /\ enums' = [enums EXCEPT ![e] = BlankEnumerator]
    /\ lastOp' = [op |-> "Dispose", enum |-> e]
    /\ UNCHANGED <<cap, buf, startIndex, count, version, specSeq, evictedLog, specEvictedLog, callbackDepth, pendingEvicted>>
    /\ steps' = steps + 1

Done ==
    /\ callbackDepth = 0
    /\ steps = MaxSteps
    /\ UNCHANGED vars

Next ==
    \/ \E v \in Values, sub \in BOOLEAN : CallerAdd(v, sub)
    \/ \E v \in Values : count > 0 /\ \E i \in 0..(count - 1) : CallerSetItem(i, v)
    \/ CallerClear
    \/ \E v \in Values : OverflowHandlerReentrantAdd(v)
    \/ OverflowHandlerReentrantClear
    \/ OverflowHandlerReturn
    \/ \E e \in 1..NumEnumerators :
           OpenEnumerator(e) \/ StepEnumerator(e) \/ ResetEnumerator(e) \/ DisposeEnumerator(e)
    \/ Done

Spec == Init /\ [][Next]_vars

TypeAndBoundsInvariant ==
    /\ cap \in 1..MaxCapacity
    /\ DOMAIN buf = 0..(cap - 1)
    /\ \A p \in 0..(cap - 1) : buf[p] \in Values \cup {Cleared}
    /\ startIndex \in 0..(cap - 1)
    /\ count \in 0..cap
    /\ version \in Nat
    /\ callbackDepth \in 0..1
    /\ pendingEvicted \in Values \cup {Cleared}
    /\ lastOp \in [op : OpTags, enum : 0..NumEnumerators]
    /\ steps \in 0..MaxSteps
    /\ \A i \in 0..(count - 1) : PhysIndex(i) \in 0..(cap - 1)
    /\ \A i, j \in 0..(count - 1) : i # j => PhysIndex(i) # PhysIndex(j)

SequenceRefinementInvariant ==
    /\ Len(specSeq) = count
    /\ RingSeq = specSeq
    /\ evictedLog = specEvictedLog

SegmentsPartitionInvariant ==
    /\ (FirstSegment \o SecondSegment) = specSeq
    /\ (count <= TailLength => SecondSegment = <<>>)
    /\ (count > TailLength =>
           Len(FirstSegment) = TailLength /\ Len(SecondSegment) = count - TailLength)

OverflowCallbackCoherenceInvariant ==
    (callbackDepth = 1) =>
        /\ count = cap
        /\ Len(evictedLog) >= 1
        /\ pendingEvicted = evictedLog[Len(evictedLog)]
        /\ RingSeq = specSeq

NoStaleReferencesInvariant ==
    \A p \in 0..(cap - 1) : ~IsLivePhysicalSlot(p) => buf[p] = Cleared

EnumeratorFailFastInvariant ==
    \A e \in 1..NumEnumerators :
        /\ ((callbackDepth = 0 /\ steps < MaxSteps /\ enums[e].active /\ ~enums[e].trapped) =>
                /\ ENABLED (Next /\ lastOp' = [op |-> "Step", enum |-> e])
                /\ ENABLED (Next /\ lastOp' = [op |-> "Reset", enum |-> e]))
        /\ (enums[e].active =>
                /\ (enums[e].trapped => enums[e].ver # version)
                /\ (~enums[e].trapped /\ enums[e].ver = version /\ enums[e].idx > 0 =>
                        /\ enums[e].idx <= count
                        /\ enums[e].current = specSeq[enums[e].idx]))

EnumeratorFailFastActionProperty ==
    [][\A e \in 1..NumEnumerators :
        /\ (lastOp'.enum # e => enums'[e] = enums[e])
        /\ (lastOp' = [op |-> "Step", enum |-> e] =>
                /\ enums[e].active /\ ~enums[e].trapped
                /\ enums'[e].active /\ enums'[e].ver = enums[e].ver
                /\ IF enums[e].ver # version THEN
                       /\ enums'[e].trapped
                       /\ enums'[e].idx = enums[e].idx
                       /\ enums'[e].current = enums[e].current
                   ELSE
                       /\ ~enums'[e].trapped
                       /\ IF enums[e].idx >= 0 /\ enums[e].idx < count THEN
                              /\ enums'[e].idx = enums[e].idx + 1
                              /\ enums'[e].current = specSeq[enums[e].idx + 1]
                          ELSE
                              /\ enums'[e].idx = -1
                              /\ enums'[e].current = Cleared)
        /\ (lastOp' = [op |-> "Reset", enum |-> e] =>
                /\ enums[e].active /\ ~enums[e].trapped
                /\ enums'[e].active /\ enums'[e].ver = enums[e].ver
                /\ IF enums[e].ver # version THEN
                       /\ enums'[e].trapped
                       /\ enums'[e].idx = enums[e].idx
                       /\ enums'[e].current = enums[e].current
                   ELSE
                       /\ ~enums'[e].trapped
                       /\ enums'[e].idx = 0
                       /\ enums'[e].current = Cleared)]_vars
=============================================================================
