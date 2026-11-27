---------------------------- MODULE FreshRecovery ----------------------------
EXTENDS Naturals

CONSTANT ReadBudget
ASSUME ReadBudget \in (Nat \ {0})

LostBeforeCommit == "lost-before-commit"
LostAfterCommit  == "lost-after-commit"
Histories == {LostBeforeCommit, LostAfterCommit}
Active == {LostAfterCommit}

\* Sets contain histories, not files. In both histories the first request
\* ended with the same ambiguous response for the same qualified file.
VARIABLES listed, observedExact, reads
vars == <<listed, observedExact, reads>>

Init == /\ listed = {}
        /\ observedExact = {}
        /\ reads = 0

IndexCatchesUp == /\ listed = {}
                  /\ listed' = Active
                  /\ UNCHANGED <<observedExact, reads>>

ReadIndex == /\ reads < ReadBudget
             /\ reads' = reads + 1
             /\ observedExact' = listed
             /\ UNCHANGED listed

AtDecision == reads = ReadBudget
Next == IndexCatchesUp \/ ReadIndex \/ (AtDecision /\ UNCHANGED vars)
Spec == Init /\ [][Next]_vars

\* A policy uses only the fresh request's observations. An exact match needs
\* no invocation. On the same unlisted observation it must make the same choice.
InvokeIfUnlisted == Histories \ observedExact
DeferIfUnlisted  == {}
Choices == {InvokeIfUnlisted, DeferIfUnlisted}

AllowsNeededCreate(calls) == LostBeforeCommit \in calls
AvoidsCallOnActive(calls) == LostAfterCommit \notin calls
BothGoalsPossible ==
    \E calls \in Choices : AllowsNeededCreate(calls) /\ AvoidsCallOnActive(calls)

RecoveryOffered == AtDecision => AllowsNeededCreate(InvokeIfUnlisted)
DecisionBoundary == AtDecision => (BothGoalsPossible <=> observedExact = Active)

\* Deliberately stronger alternative: its configuration should find a trace.
AlwaysPossibleToDoBoth == AtDecision => BothGoalsPossible
=============================================================================
