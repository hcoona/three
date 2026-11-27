-------------------------- MODULE PollingChangeToken --------------------------
EXTENDS WebHdfsRules

CONSTANT MaxMutations
ASSUME MaxMutations \in Nat

Tokens    == {1, 2}
Callbacks == {"cb1", "cb2"}

WatchTrailingSlash == [validSyntax |-> TRUE, dirSegments |-> <<"a">>, trailingSlash |-> TRUE,  leafToken |-> ""]
WatchExactA        == [validSyntax |-> TRUE, dirSegments |-> <<>>,    trailingSlash |-> FALSE, leafToken |-> "a"]
WatchRootFlatSub   == [validSyntax |-> TRUE, dirSegments |-> <<>>,    trailingSlash |-> FALSE, leafToken |-> "a*"]
WatchNestedFlat    == [validSyntax |-> TRUE, dirSegments |-> <<"a">>, trailingSlash |-> FALSE, leafToken |-> "*"]

WatchCandidates ==
  {WatchTrailingSlash, WatchExactA, WatchRootFlatSub, WatchNestedFlat}

InitialTrees ==
  { [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> AbsentNode
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> FileNode(3, 2, 30, <<7, 8>>)
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> DirNode(3, 30)
         [] p = PathAppAx -> FileNode(4, 1, 35, <<6>>)
         [] OTHER -> AbsentNode] }

EmptyOracle == [tag |-> "none", item |-> <<>>, items |-> {}]

\* Independent HDFS ground-truth projection (does NOT call WebHdfsStat,
\* WebHdfsList, or ObserveWatchSignature) used to verify FalsePositiveIsolation.
CleanDirAncestors(tree, q) ==
  q \in Paths /\ \A ancestor \in Ancestors(q) : tree[ancestor].kind = "DIRECTORY"

OracleProjection(tree, useFileId, spec) ==
  IF spec.mode = "null-token" THEN EmptyOracle
  ELSE IF spec.mode = "exact" THEN
    IF CleanDirAncestors(tree, spec.target) /\
       tree[spec.target].kind \in {"FILE", "DIRECTORY"} THEN
      [tag |-> "exact-present",
       item |-> <<tree[spec.target].kind,
                  IF useFileId THEN tree[spec.target].id ELSE 0,
                  tree[spec.target].length,
                  tree[spec.target].mtime>>,
       items |-> {}]
    ELSE
      [tag |-> "exact-absent", item |-> <<>>, items |-> {}]
  ELSE
    IF CleanDirAncestors(tree, spec.target) /\
       tree[spec.target].kind = "DIRECTORY" THEN
      [tag |-> "flat",
       item |-> <<>>,
       items |-> {<<ChildNameOf(c),
                    IF useFileId THEN tree[c].id ELSE 0,
                    tree[c].length,
                    tree[c].mtime>> :
                  c \in {x \in DirectChildren(spec.target) :
                         tree[x].kind = "FILE" /\
                         MatchesFlatWildcard(spec.pattern, ChildNameOf(x))}}]
    ELSE
      [tag |-> "flat", item |-> <<>>, items |-> {}]

UncreatedToken ==
  [created |-> FALSE,
   spec |-> NullSpec,
   baseline |-> AbsentSignature,
   initOracle |-> EmptyOracle,
   oracleDiverged |-> FALSE,
   hasChanged |-> FALSE,
   cbState |-> [c \in Callbacks |-> "unregistered"],
   cbCount |-> [c \in Callbacks |-> 0]]

VARIABLES
  fs,
  nextId,
  mutations,
  fault,
  providerDisposed,
  watchReq,
  hasFileId,
  activeCallbacks,
  tok

vars ==
  <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks, tok>>

TokenIsNull(t) == tok[t].spec.mode = "null-token"
TokenActive(t) == tok[t].created /\ ~TokenIsNull(t) /\ activeCallbacks

CaptureBaseline(tree, curFault, useFileId, spec) ==
  IF spec.mode = "null-token" THEN AbsentSignature
  ELSE LET sig == ObserveWatchSignature(tree, curFault, "namenode-user", useFileId, spec)
       IN  IF sig = UnknownSignature THEN UnobservedSignature ELSE sig

NewToken(tree, curFault, disposed, useFileId, req) ==
  LET spec == ClassifyFilter(PathApp, TRUE, disposed, req)
  IN  [created |-> TRUE,
       spec |-> spec,
       baseline |-> CaptureBaseline(tree, curFault, useFileId, spec),
       initOracle |-> OracleProjection(tree, useFileId, spec),
       oracleDiverged |-> FALSE,
       hasChanged |-> FALSE,
       cbState |-> [c \in Callbacks |-> "unregistered"],
       cbCount |-> [c \in Callbacks |-> 0]]

Init ==
  /\ fs \in InitialTrees
  /\ nextId = 5
  /\ mutations = 0
  /\ fault \in {"none", "transport"}
  /\ providerDisposed = FALSE
  /\ watchReq \in WatchCandidates
  /\ hasFileId \in BOOLEAN
  /\ activeCallbacks \in BOOLEAN
  /\ tok = [t \in Tokens |->
              IF t = 1
              THEN NewToken(fs, fault, FALSE, hasFileId, watchReq)
              ELSE UncreatedToken]

RegisterCallback(t, c) ==
  /\ tok[t].created
  /\ (t = 2 => c = "cb1")
  /\ tok[t].cbState[c] = "unregistered"
  /\ IF TokenActive(t) /\ tok[t].hasChanged THEN
       tok' = [tok EXCEPT ![t].cbState[c] = "invoked",
                          ![t].cbCount[c] = tok[t].cbCount[c] + 1]
     ELSE
       tok' = [tok EXCEPT ![t].cbState[c] = "registered"]
  /\ UNCHANGED <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

UnregisterCallback(t, c) ==
  /\ tok[t].created
  /\ tok[t].cbState[c] \in {"registered", "invoked"}
  /\ tok' = [tok EXCEPT ![t].cbState[c] = "disposed"]
  /\ UNCHANGED <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

Poll(t) ==
  /\ tok[t].created
  /\ ~TokenIsNull(t)
  /\ ~tok[t].hasChanged
  /\ ~providerDisposed
  /\ LET cur == ObserveWatchSignature(fs, fault, "namenode-user", hasFileId, tok[t].spec)
     IN  IF cur = UnknownSignature THEN
           UNCHANGED tok
         ELSE IF tok[t].baseline = UnobservedSignature \/ cur # tok[t].baseline THEN
           tok' = [tok EXCEPT ![t].hasChanged = TRUE]
         ELSE
           UNCHANGED tok
  /\ UNCHANGED <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

DeliverCallback(t, c) ==
  /\ TokenActive(t)
  /\ tok[t].hasChanged
  /\ tok[t].cbState[c] = "registered"
  /\ tok' = [tok EXCEPT ![t].cbState[c] = "invoked",
                        ![t].cbCount[c] = tok[t].cbCount[c] + 1]
  /\ UNCHANGED <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

OnChangeCreateSuccessor ==
  /\ ~tok[2].created
  /\ tok[1].cbState["cb1"] = "invoked"
  /\ tok' = [tok EXCEPT ![2] = NewToken(fs, fault, providerDisposed, hasFileId, watchReq)]
  /\ UNCHANGED <<fs, nextId, mutations, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

DisposeProvider ==
  /\ ~providerDisposed
  /\ providerDisposed' = TRUE
  /\ UNCHANGED <<fs, nextId, mutations, fault, watchReq, hasFileId, activeCallbacks, tok>>

SetFault(f) ==
  /\ f \in {"none", "transport"}
  /\ f # fault
  /\ fault' = f
  /\ UNCHANGED <<fs, nextId, mutations, providerDisposed, watchReq, hasFileId, activeCallbacks, tok>>

UpdateOracleDivergence(tokensMap, newTree) ==
  [t \in Tokens |->
     IF tokensMap[t].created /\
        OracleProjection(newTree, hasFileId, tokensMap[t].spec) # tokensMap[t].initOracle
     THEN [tokensMap[t] EXCEPT !.oracleDiverged = TRUE]
     ELSE tokensMap[t]]

ApplyWatchMutation(updated) ==
  /\ fs' = updated
  /\ tok' = UpdateOracleDivergence(tok, updated)

ModifyChildInPlace ==
  /\ mutations < MaxMutations
  /\ LET targetPath == IF fs[PathAppAx].kind = "FILE" THEN PathAppAx ELSE PathAppA
     IN  /\ fs[targetPath].kind = "FILE"
         /\ LET cur == fs[targetPath]
                grown == Append(cur.bytes, 5)
            IN  ApplyWatchMutation(
                  [fs EXCEPT ![targetPath] = FileNode(cur.id, Len(grown), cur.mtime + 10, grown)])
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<nextId, fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

SameMtimeReplaceWithDir ==
  /\ mutations < MaxMutations
  /\ fs[PathAppA].kind = "FILE"
  /\ ApplyWatchMutation(
       [fs EXCEPT ![PathApp]  = DirNode(fs[PathApp].id, fs[PathApp].mtime + 10),
                  ![PathAppA] = DirNode(nextId, fs[PathAppA].mtime)])
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

SameMtimeSameSizeFileSwap ==
  /\ mutations < MaxMutations
  /\ fs[PathAppA].kind = "FILE"
  /\ LET cur == fs[PathAppA]
     IN  ApplyWatchMutation(
           [fs EXCEPT ![PathApp]  = DirNode(fs[PathApp].id, fs[PathApp].mtime + 10),
                      ![PathAppA] = FileNode(nextId, cur.length, cur.mtime, [i \in 1..cur.length |-> 9])])
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

CreateOrDeleteTarget ==
  /\ mutations < MaxMutations
  /\ LET nextA == IF fs[PathAppA].kind = "ABSENT"
                  THEN FileNode(nextId, 2, 30 + nextId, <<7, 8>>)
                  ELSE AbsentNode
     IN  ApplyWatchMutation(
           [fs EXCEPT ![PathApp]   = DirNode(fs[PathApp].id, fs[PathApp].mtime + 10),
                      ![PathAppA]  = nextA,
                      ![PathAppAA] = AbsentNode,
                      ![PathAppAx] = AbsentNode])
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

MutateNonMatchingSiblingB ==
  /\ mutations < MaxMutations
  /\ LET nextB == IF fs[PathAppB].kind = "ABSENT"
                  THEN FileNode(nextId, 1, 50 + nextId, <<4>>)
                  ELSE AbsentNode
     IN  ApplyWatchMutation(
           [fs EXCEPT ![PathApp]  = DirNode(fs[PathApp].id, fs[PathApp].mtime + 10),
                      ![PathAppB] = nextB])
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<fault, providerDisposed, watchReq, hasFileId, activeCallbacks>>

Next ==
  \/ \E t \in Tokens, c \in Callbacks :
       RegisterCallback(t, c) \/ UnregisterCallback(t, c) \/ DeliverCallback(t, c)
  \/ \E t \in Tokens : Poll(t)
  \/ OnChangeCreateSuccessor
  \/ DisposeProvider
  \/ \E f \in {"none", "transport"} : SetFault(f)
  \/ ModifyChildInPlace
  \/ SameMtimeReplaceWithDir
  \/ SameMtimeSameSizeFileSwap
  \/ CreateOrDeleteTarget
  \/ MutateNonMatchingSiblingB

Spec == Init /\ [][Next]_vars

TokenContractSafety ==
  /\ ValidTree(fs)
  /\ \A t \in Tokens :
       /\ (~tok[t].created => tok[t] = UncreatedToken)
       /\ \A c \in Callbacks :
            /\ tok[t].cbCount[c] <= 1
            /\ (tok[t].cbCount[c] = 1 => (TokenActive(t) /\ tok[t].hasChanged))
            /\ (tok[t].cbState[c] = "invoked" => tok[t].cbCount[c] = 1)
       /\ (TokenIsNull(t) =>
             /\ ~tok[t].hasChanged
             /\ \A c \in Callbacks : tok[t].cbCount[c] = 0)
       /\ (~activeCallbacks => \A c \in Callbacks : tok[t].cbCount[c] = 0)
  /\ (tok[2].created => (tok[1].hasChanged /\ tok[1].cbCount["cb1"] = 1))

FalsePositiveIsolation ==
  \A t \in Tokens :
    (tok[t].created /\ ~tok[t].oracleDiverged /\ tok[t].baseline # UnobservedSignature) =>
      ~tok[t].hasChanged

WatchStepDiscipline ==
  \A t \in Tokens :
    tok[t].created =>
      /\ tok'[t].created
      /\ tok'[t].spec = tok[t].spec
      /\ tok'[t].baseline = tok[t].baseline
      /\ tok'[t].initOracle = tok[t].initOracle
      /\ (tok[t].oracleDiverged => tok'[t].oracleDiverged)
      /\ (tok[t].hasChanged => tok'[t].hasChanged)
      /\ ((~tok[t].hasChanged /\ tok'[t].hasChanged) =>
            (fault = "none" /\ ~providerDisposed /\ ~TokenIsNull(t)))
      /\ \A c \in Callbacks :
           /\ tok'[t].cbCount[c] >= tok[t].cbCount[c]
           /\ ((tok[t].cbState[c] # "invoked" /\ tok'[t].cbState[c] = "invoked") =>
                 (tok[t].hasChanged /\ tok'[t].hasChanged))
           /\ (tok[t].cbState[c] = "disposed" =>
                 (tok'[t].cbState[c] = "disposed" /\ tok'[t].cbCount[c] = tok[t].cbCount[c]))

WatchTransitionSafety == [][WatchStepDiscipline]_vars

DetectionProgress ==
  /\ \A t \in Tokens :
       LET cur == ObserveWatchSignature(fs, fault, "namenode-user", hasFileId, tok[t].spec)
       IN  /\ ((tok[t].created /\ ~TokenIsNull(t) /\ ~tok[t].hasChanged /\
                ~providerDisposed /\ fault = "none" /\
                (tok[t].baseline = UnobservedSignature \/ cur # tok[t].baseline)) =>
                  ENABLED (Poll(t) /\ tok'[t].hasChanged))
           /\ \A c \in Callbacks :
                (TokenActive(t) /\ tok[t].hasChanged /\ tok[t].cbState[c] = "registered") =>
                  ENABLED (DeliverCallback(t, c) /\ tok'[t].cbState[c] = "invoked")
  /\ ((~tok[2].created /\ tok[1].cbState["cb1"] = "invoked") =>
        ENABLED (OnChangeCreateSuccessor /\ tok'[2].created))

\* Deliberately false shortcuts / physical-limit probes:

MtimeOnlyDetectsSameMsReplace ==
  LET cur == ObserveWatchSignature(fs, "none", "namenode-user", hasFileId, tok[1].spec)
  IN  (tok[1].spec.mode = "exact" /\ fault = "none" /\
       tok[1].baseline # UnobservedSignature /\ cur # tok[1].baseline) =>
        (cur.state # tok[1].baseline.state \/ cur.mtime # tok[1].baseline.mtime)

ParentDirectoryMtimeDetectsChildModify ==
  LET cur == ObserveWatchSignature(fs, "none", "namenode-user", hasFileId, tok[1].spec)
  IN  (tok[1].spec.mode = "flat-dir" /\ tok[1].spec.target = PathApp /\ fault = "none" /\
       tok[1].baseline # UnobservedSignature /\ cur # tok[1].baseline) =>
        fs[PathApp].mtime # 20

PollingDetectsTransientAbaMutation ==
  LET cur == ObserveWatchSignature(fs, "none", "namenode-user", TRUE, tok[1].spec)
  IN  (tok[1].spec.mode = "exact" /\ hasFileId /\ fault = "none" /\
       tok[1].baseline # UnobservedSignature /\ tok[1].oracleDiverged) =>
        (tok[1].hasChanged \/ cur # tok[1].baseline)

FairWatchExecution ==
  /\ \A t \in Tokens : WF_vars(Poll(t))
  /\ \A t \in Tokens, c \in Callbacks : WF_vars(DeliverCallback(t, c))
  /\ WF_vars(OnChangeCreateSuccessor)

LiveSpec == Spec /\ FairWatchExecution

QuiescentEventualNotification ==
  /\ \A t \in Tokens :
       <>[] (tok[t].created /\ ~TokenIsNull(t) /\ ~providerDisposed /\ fault = "none" /\
             (tok[t].baseline = UnobservedSignature \/
              ObserveWatchSignature(fs, fault, "namenode-user", hasFileId, tok[t].spec) #
                tok[t].baseline))
       => <> tok[t].hasChanged
  /\ \A t \in Tokens, c \in Callbacks :
       []((TokenActive(t) /\ tok[t].hasChanged /\ tok[t].cbState[c] = "registered")
          => <> (tok[t].cbState[c] \in {"invoked", "disposed"}))
=============================================================================
