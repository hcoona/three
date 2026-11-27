-------------------- MODULE SnapshotAndStreamConsistency --------------------
EXTENDS WebHdfsRules

CONSTANT MaxMutations
ASSUME MaxMutations \in Nat

ReqA    == [validSyntax |-> TRUE, segments |-> <<"a">>]
ReqRoot == [validSyntax |-> TRUE, segments |-> <<>>]
Scenarios == {"list-dir", "stream-from-file", "stream-from-dir"}

InitialTrees ==
  { [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> FileNode(3, 2, 30, <<7, 8>>)
         [] p = PathAppB -> FileNode(4, 1, 32, <<4>>)
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> FileNode(3, 1, 30, <<7, 8>>)
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> FileNode(3, 0, 30, <<>>)
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] p = PathAppA -> DirNode(3, 30)
         [] p = PathAppAA -> FileNode(4, 1, 33, <<3>>)
         [] p = PathAppAx -> FileNode(5, 1, 35, <<6>>)
         [] OTHER -> AbsentNode],
    [p \in Paths |->
       CASE p = PathRoot -> DirNode(1, 10)
         [] p = PathApp  -> DirNode(2, 20)
         [] OTHER -> AbsentNode] }

IdleStream ==
  [phase |-> "idle",
   error |-> "",
   snapshotLength |-> -1,
   boundId |-> 0,
   targetLength |-> -1,
   cachedBytes |-> <<>>,
   delivered |-> <<>>,
   spliced |-> FALSE]

VARIABLES
  scenario,
  backendMode,
  dirReq,
  fs,
  nextId,
  mutations,
  fault,
  providerDisposed,
  fileSnap,
  dirSnap,
  stream,
  pathRewritten,
  openWindowBytes,
  seenFileBytes

vars ==
  <<scenario, backendMode, dirReq, fs, nextId, mutations, fault, providerDisposed,
    fileSnap, dirSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

BackendVariant == IF backendMode = "httpfs-direct" THEN "httpfs" ELSE "namenode-user"
DirTarget      == IF dirReq = ReqA THEN PathAppA ELSE PathApp
Page1ChildName == IF dirReq = ReqA THEN "A" ELSE "a"
Page2ChildName == IF dirReq = ReqA THEN "x" ELSE "b"

RecordRedirectWindow(versions, tree) ==
  IF stream.phase = "redirected-307" /\ tree[PathAppA].kind = "FILE"
  THEN versions \cup {tree[PathAppA].bytes}
  ELSE versions

RecordSeenFileVersion(versions, tree) ==
  IF tree[PathAppA].kind = "FILE"
  THEN versions \cup {tree[PathAppA].bytes}
  ELSE versions

Init ==
  /\ scenario \in Scenarios
  /\ backendMode \in {"namenode-redirect", "httpfs-direct"}
  /\ dirReq \in (IF scenario = "list-dir" THEN {ReqA, ReqRoot} ELSE {ReqRoot})
  /\ fs \in InitialTrees
  /\ nextId = 6
  /\ mutations = 0
  /\ fault = "none"
  /\ providerDisposed = FALSE
  /\ fileSnap = [captured |-> FALSE, outcome |-> "", info |-> NotFoundFile("")]
  /\ dirSnap = [phase |-> "idle", outcome |-> "", buffered |-> {}, dir |-> NotFoundDirectory]
  /\ stream = IdleStream
  /\ pathRewritten = FALSE
  /\ openWindowBytes = {}
  /\ seenFileBytes = RecordSeenFileVersion({}, fs)

CaptureFileInfo ==
  /\ scenario = "stream-from-file"
  /\ ~fileSnap.captured
  /\ LET res == EvalGetFileInfo(fs, PathApp, providerDisposed, fault, BackendVariant, ReqA)
     IN  fileSnap' = [captured |-> TRUE, outcome |-> res.outcome, info |-> res.info]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, dirSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

ProjectListEntries(parentPath, rawEntries) ==
  IF \E e \in rawEntries : e.pathSuffix = "" THEN
    NotFoundDirectory
  ELSE
    [exists |-> TRUE,
     entries |-> {DirectoryChildProjection(parentPath, e) :
                  e \in {x \in rawEntries : x.kind \in {"FILE", "DIRECTORY"}}}]

\* HdfsFileStatus.makeQualified(uri, parent) sets an empty localName (FILE status)
\* to parent Path, whose getName() is ChildNameOf(target); FSOperations.toJson
\* later clears pathSuffix to "" iff fs.getFileStatus(path).isFile() is TRUE.
HttpFsQualifyBuffered(target, rawEntries) ==
  { IF e.pathSuffix = ""
    THEN [e EXCEPT !.pathSuffix = ChildNameOf(target)]
    ELSE e : e \in rawEntries }

HttpFsFormatJson(bufferedEntries, isFile) ==
  { IF isFile THEN [e EXCEPT !.pathSuffix = ""] ELSE e : e \in bufferedEntries }

BeginCaptureDir ==
  /\ scenario \in {"list-dir", "stream-from-dir"}
  /\ dirSnap.phase = "idle"
  /\ IF providerDisposed THEN
       dirSnap' = [phase |-> "captured", outcome |-> "disposed", buffered |-> {}, dir |-> NotFoundDirectory]
     ELSE
       LET raw == WebHdfsList(fs, fault, BackendVariant, DirTarget)
       IN  IF raw.wire = "http-200-list" THEN
             IF Cardinality(raw.entries) <= 1 THEN
               IF backendMode = "httpfs-direct" THEN
                 dirSnap' = [phase |-> "httpfs-stat-check",
                             outcome |-> "",
                             buffered |-> HttpFsQualifyBuffered(DirTarget, raw.entries),
                             dir |-> NotFoundDirectory]
               ELSE
                 dirSnap' = [phase |-> "captured",
                             outcome |-> "ok",
                             buffered |-> {},
                             dir |-> ProjectListEntries(DirTarget, raw.entries)]
             ELSE
               dirSnap' = [phase |-> "page1",
                           outcome |-> "",
                           buffered |-> {e \in raw.entries : e.pathSuffix = Page1ChildName},
                           dir |-> NotFoundDirectory]
           ELSE IF ClassifyWireError(DirTarget, raw.wire, raw.exception, raw.message) = "not-found" THEN
             dirSnap' = [phase |-> "captured", outcome |-> "ok", buffered |-> {}, dir |-> NotFoundDirectory]
           ELSE
             dirSnap' = [phase |-> "captured", outcome |-> "fault", buffered |-> {}, dir |-> NotFoundDirectory]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

FinishCaptureDirPage2 ==
  /\ dirSnap.phase = "page1"
  /\ IF fault # "none" THEN
       dirSnap' = [phase |-> "captured", outcome |-> "fault", buffered |-> {}, dir |-> NotFoundDirectory]
     ELSE
       LET raw == WebHdfsList(fs, "none", BackendVariant, DirTarget)
       IN  IF raw.wire # "http-200-list" THEN
             IF backendMode = "httpfs-direct" /\
                ClassifyWireError(DirTarget, raw.wire, raw.exception, raw.message) = "not-found"
             THEN dirSnap' = [phase |-> "captured", outcome |-> "ok", buffered |-> {}, dir |-> NotFoundDirectory]
             ELSE dirSnap' = [phase |-> "captured", outcome |-> "fault", buffered |-> {}, dir |-> NotFoundDirectory]
           ELSE
             LET rawPage2 ==
                   IF \E e \in raw.entries : e.pathSuffix = ""
                   THEN raw.entries
                   ELSE {e \in raw.entries : e.pathSuffix = Page2ChildName}
                 combined == dirSnap.buffered \cup rawPage2
             IN  IF backendMode = "httpfs-direct" THEN
                   dirSnap' = [phase |-> "httpfs-stat-check",
                               outcome |-> "",
                               buffered |-> HttpFsQualifyBuffered(DirTarget, combined),
                               dir |-> NotFoundDirectory]
                 ELSE
                   dirSnap' = [phase |-> "captured",
                               outcome |-> "ok",
                               buffered |-> {},
                               dir |-> ProjectListEntries(DirTarget, combined)]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

FinishHttpFsListStatCheck ==
  /\ dirSnap.phase = "httpfs-stat-check"
  /\ LET stat == WebHdfsStat(fs, fault, BackendVariant, DirTarget)
     IN  IF stat.wire = "http-200-stat" THEN
           dirSnap' =
             [phase |-> "captured",
              outcome |-> "ok",
              buffered |-> {},
              dir |-> ProjectListEntries(DirTarget, HttpFsFormatJson(dirSnap.buffered, stat.entry.kind = "FILE"))]
         ELSE IF ClassifyWireError(DirTarget, stat.wire, stat.exception, stat.message) = "not-found" THEN
           dirSnap' = [phase |-> "captured", outcome |-> "ok", buffered |-> {}, dir |-> NotFoundDirectory]
         ELSE
           dirSnap' = [phase |-> "captured", outcome |-> "fault", buffered |-> {}, dir |-> NotFoundDirectory]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

ChildHandleForA ==
  IF \E c \in dirSnap.dir.entries : c.name = "a"
  THEN CHOOSE c \in dirSnap.dir.entries : c.name = "a"
  ELSE NotFoundFile("a")

StreamSourceReady ==
  IF scenario = "stream-from-file"
  THEN fileSnap.captured /\ fileSnap.outcome = "ok"
  ELSE scenario = "stream-from-dir" /\
       dirSnap.phase = "captured" /\ dirSnap.outcome = "ok" /\
       (\E c \in dirSnap.dir.entries : c.name = "a")

SelectedHandle ==
  IF scenario = "stream-from-file" THEN fileSnap.info ELSE ChildHandleForA

\* CreateReadStream entry: NameNode issues 307 for both FILE and 0-length DIRECTORY
\* (deferred to DataNode onOpen 404); HttpFS opens DFSInputStream directly.
BeginStream ==
  /\ stream.phase = "idle"
  /\ StreamSourceReady
  /\ LET h == SelectedHandle
     IN  IF ~h.exists THEN
           /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "file-not-found"]
           /\ UNCHANGED <<pathRewritten, openWindowBytes>>
         ELSE IF h.isDir THEN
           /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "invalid-operation-dir"]
           /\ UNCHANGED <<pathRewritten, openWindowBytes>>
         ELSE IF providerDisposed THEN
           /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "disposed"]
           /\ UNCHANGED <<pathRewritten, openWindowBytes>>
         ELSE IF fault # "none" THEN
           /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "io-fault"]
           /\ UNCHANGED <<pathRewritten, openWindowBytes>>
         ELSE
           LET node == fs[PathAppA]
           IN  IF backendMode = "namenode-redirect" THEN
                 IF node.kind \in {"ABSENT", "SYMLINK"} THEN
                   /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "file-not-found"]
                   /\ UNCHANGED <<pathRewritten, openWindowBytes>>
                 ELSE
                   /\ stream' = [IdleStream EXCEPT !.phase = "redirected-307", !.snapshotLength = h.length]
                   /\ pathRewritten' = FALSE
                   /\ openWindowBytes' = IF node.kind = "FILE" THEN {node.bytes} ELSE {}
               ELSE
                 IF node.kind \in {"ABSENT", "SYMLINK", "DIRECTORY"} THEN
                   /\ stream' = [IdleStream EXCEPT !.phase = "closed-error", !.error = "file-not-found"]
                   /\ UNCHANGED <<pathRewritten, openWindowBytes>>
                 ELSE
                   /\ stream' = [IdleStream EXCEPT !.phase = "streaming",
                                                   !.snapshotLength = h.length,
                                                   !.boundId = node.id,
                                                   !.targetLength = Len(node.bytes),
                                                   !.cachedBytes = node.bytes]
                   /\ pathRewritten' = FALSE
                   /\ openWindowBytes' = {node.bytes}
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, seenFileBytes>>

ConnectDataNode ==
  /\ backendMode = "namenode-redirect"
  /\ stream.phase = "redirected-307"
  /\ IF fault # "none" THEN
       /\ stream' = [stream EXCEPT !.phase = "closed-error", !.error = "io-fault"]
       /\ UNCHANGED <<pathRewritten, openWindowBytes>>
     ELSE
       LET node == fs[PathAppA]
       IN  IF node.kind \in {"ABSENT", "SYMLINK", "DIRECTORY"} THEN
             /\ stream' = [stream EXCEPT !.phase = "closed-error", !.error = "file-not-found"]
             /\ UNCHANGED <<pathRewritten, openWindowBytes>>
           ELSE
             /\ stream' = [stream EXCEPT !.phase = "streaming",
                                         !.boundId = node.id,
                                         !.targetLength = Len(node.bytes),
                                         !.cachedBytes = node.bytes]
             /\ pathRewritten' = FALSE
             /\ openWindowBytes' = openWindowBytes \cup {node.bytes}
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, seenFileBytes>>

IsPrefixOf(short, full) ==
  /\ Len(short) <= Len(full)
  /\ \A i \in 1..Len(short) : short[i] = full[i]

\* DFSInputStream primitives:
\* 1. ReadCachedByte: read next byte from cached LocatedBlocks view within targetLength.
ReadCachedByte ==
  /\ stream.phase = "streaming"
  /\ fault = "none"
  /\ Len(stream.delivered) < stream.targetLength
  /\ Len(stream.delivered) < Len(stream.cachedBytes)
  /\ LET idx == Len(stream.delivered) + 1
     IN  stream' = [stream EXCEPT !.delivered = Append(stream.delivered, stream.cachedBytes[idx])]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, openWindowBytes, seenFileBytes>>

\* 2. RefreshHttpFsLocatedBlocks: openInfo(true) (fetchAndCheckLocatedBlocks) on chunked
\*    HttpFS updates targetLength (either growth via append or block-boundary shrinkage via
\*    truncate, even when pos >= new length) when overlapping block IDs match.
RefreshHttpFsLocatedBlocks ==
  /\ backendMode = "httpfs-direct"
  /\ stream.phase = "streaming"
  /\ fault = "none"
  /\ ~stream.spliced
  /\ fs[PathAppA].kind = "FILE"
  /\ fs[PathAppA].id = stream.boundId
  /\ fs[PathAppA].bytes # stream.cachedBytes
  /\ (IsPrefixOf(stream.cachedBytes, fs[PathAppA].bytes) \/
      IsPrefixOf(fs[PathAppA].bytes, stream.cachedBytes))
  /\ stream' = [stream EXCEPT !.targetLength = Len(fs[PathAppA].bytes),
                              !.cachedBytes = fs[PathAppA].bytes]
  /\ openWindowBytes' = openWindowBytes \cup {fs[PathAppA].bytes}
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, seenFileBytes>>

\* 3. FetchUncachedBlockAtOffset: DFSInputStream.fetchBlockAt(offset) fetches an uncached
\*    block by pathname at offset >= 1 without revalidating block 0, keeping targetLength fixed.
FetchUncachedBlockAtOffset ==
  /\ stream.phase = "streaming"
  /\ fault = "none"
  /\ pathRewritten
  /\ Len(stream.delivered) >= 1
  /\ Len(stream.delivered) < stream.targetLength
  /\ fs[PathAppA].kind = "FILE"
  /\ Len(fs[PathAppA].bytes) > Len(stream.delivered)
  /\ LET idx == Len(stream.delivered) + 1
     IN  stream' = [stream EXCEPT
                      !.cachedBytes = fs[PathAppA].bytes,
                      !.delivered = Append(stream.delivered, fs[PathAppA].bytes[idx]),
                      !.spliced = TRUE]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, openWindowBytes, seenFileBytes>>

FinishStreamEof ==
  /\ stream.phase = "streaming"
  /\ fault = "none"
  /\ IF backendMode = "namenode-redirect"
     THEN Len(stream.delivered) = stream.targetLength
     ELSE Len(stream.delivered) >= stream.targetLength
  /\ stream' = [stream EXCEPT !.phase = "eof-ok"]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, openWindowBytes, seenFileBytes>>

FailStream ==
  /\ stream.phase = "streaming"
  /\ (fault # "none" \/ fs[PathAppA].kind # "FILE" \/ pathRewritten)
  /\ stream' = [stream EXCEPT !.phase = "closed-error", !.error = "io-fault"]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, openWindowBytes, seenFileBytes>>

CloseStream ==
  /\ stream.phase \in {"streaming", "eof-ok", "closed-error"}
  /\ stream' = [stream EXCEPT !.phase = "disposed-stream"]
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 providerDisposed, fileSnap, dirSnap, pathRewritten, openWindowBytes, seenFileBytes>>

DisposeProvider ==
  /\ ~providerDisposed
  /\ providerDisposed' = TRUE
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations, fault,
                 fileSnap, dirSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

SetFault(f) ==
  /\ f \in {"none", "transport"}
  /\ f # fault
  /\ fault' = f
  /\ UNCHANGED <<scenario, backendMode, dirReq, fs, nextId, mutations,
                 providerDisposed, fileSnap, dirSnap, stream, pathRewritten, openWindowBytes, seenFileBytes>>

ApplyTreeMutation(updated, rewritesStreamRange) ==
  /\ fs' = updated
  /\ openWindowBytes' = RecordRedirectWindow(openWindowBytes, updated)
  /\ seenFileBytes' = RecordSeenFileVersion(seenFileBytes, updated)
  /\ pathRewritten' = (pathRewritten \/ (stream.phase = "streaming" /\ rewritesStreamRange))

MutateTarget(newNode) ==
  /\ mutations < MaxMutations
  /\ ApplyTreeMutation(
       [fs EXCEPT ![PathAppA] = newNode, ![PathAppAA] = AbsentNode, ![PathAppAx] = AbsentNode],
       newNode.kind = "FILE")
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<scenario, backendMode, dirReq, fault, providerDisposed, fileSnap, dirSnap, stream>>

SwapAForB ==
  /\ mutations < MaxMutations
  /\ ApplyTreeMutation(
       [fs EXCEPT ![PathAppA] = AbsentNode,
                  ![PathAppAA] = AbsentNode,
                  ![PathAppAx] = AbsentNode,
                  ![PathAppB] = FileNode(nextId, 1, 40 + nextId, <<5>>)],
       FALSE)
  /\ nextId' = nextId + 1
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<scenario, backendMode, dirReq, fault, providerDisposed, fileSnap, dirSnap, stream>>

AppendInPlace ==
  /\ mutations < MaxMutations
  /\ fs[PathAppA].kind = "FILE"
  /\ LET cur == fs[PathAppA]
         grown == Append(cur.bytes, 9)
     IN  ApplyTreeMutation(
           [fs EXCEPT ![PathAppA] = FileNode(cur.id, Len(grown), cur.mtime + 10, grown)],
           FALSE)
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<scenario, backendMode, dirReq, nextId, fault, providerDisposed, fileSnap, dirSnap, stream>>

TruncateInPlace ==
  /\ mutations < MaxMutations
  /\ fs[PathAppA].kind = "FILE"
  /\ Len(fs[PathAppA].bytes) >= 2
  /\ LET cur == fs[PathAppA]
         shortened == SubSeq(cur.bytes, 1, 1)
     IN  ApplyTreeMutation(
           [fs EXCEPT ![PathAppA] = FileNode(cur.id, 1, cur.mtime + 10, shortened)],
           TRUE)
  /\ mutations' = mutations + 1
  /\ UNCHANGED <<scenario, backendMode, dirReq, nextId, fault, providerDisposed, fileSnap, dirSnap, stream>>

Next ==
  \/ CaptureFileInfo
  \/ BeginCaptureDir
  \/ FinishCaptureDirPage2
  \/ FinishHttpFsListStatCheck
  \/ BeginStream
  \/ ConnectDataNode
  \/ ReadCachedByte
  \/ RefreshHttpFsLocatedBlocks
  \/ FetchUncachedBlockAtOffset
  \/ FinishStreamEof
  \/ FailStream
  \/ CloseStream
  \/ DisposeProvider
  \/ \E f \in {"none", "transport"} : SetFault(f)
  \/ MutateTarget(AbsentNode)
  \/ MutateTarget(FileNode(nextId, 1, 40 + nextId, <<1>>))
  \/ MutateTarget(FileNode(nextId, 2, 40 + nextId, <<1, 2>>))
  \/ MutateTarget(DirNode(nextId, 40 + nextId))
  \/ MutateTarget(SymNode(nextId, 40 + nextId))
  \/ SwapAForB
  \/ AppendInPlace
  \/ TruncateInPlace

Spec == Init /\ [][Next]_vars

TreeAndSnapshotSafety ==
  /\ ValidTree(fs)
  /\ (fileSnap.captured => (ValidFileInfoShape(fileSnap.info) /\ ~fileSnap.info.isDir))
  /\ (dirSnap.phase = "captured" => ValidDirContentsFor(DirTarget, dirSnap.dir))
  /\ (dirSnap.phase = "captured" /\ dirSnap.outcome # "ok" => ~dirSnap.dir.exists)

StepDiscipline ==
  /\ scenario' = scenario
  /\ backendMode' = backendMode
  /\ dirReq' = dirReq
  /\ (fileSnap.captured => fileSnap' = fileSnap)
  /\ (dirSnap.phase = "captured" => dirSnap' = dirSnap)
  /\ (providerDisposed => providerDisposed')
  /\ ((backendMode = "namenode-redirect" /\
       stream.phase \in {"streaming", "eof-ok", "closed-error", "disposed-stream"}) =>
        openWindowBytes' = openWindowBytes)
  /\ (\A i \in 1..Len(stream.delivered) : stream'.delivered[i] = stream.delivered[i])
  /\ ((stream.phase = "idle" /\ stream'.phase \in {"redirected-307", "streaming"}) =>
        (~providerDisposed /\ fault = "none" /\ StreamSourceReady /\
         SelectedHandle.exists /\ ~SelectedHandle.isDir))

TransitionSafety == [][StepDiscipline]_vars

StreamIntegrityAndLifecycle ==
  /\ ((backendMode = "namenode-redirect" /\ stream.targetLength >= 0) =>
        Len(stream.delivered) <= stream.targetLength)
  /\ ((stream.phase \in {"streaming", "eof-ok"} /\ ~stream.spliced) =>
        \E v \in openWindowBytes : IsPrefixOf(stream.delivered, v))
  /\ (stream.phase = "eof-ok" =>
        /\ openWindowBytes # {}
        /\ (backendMode = "namenode-redirect" => Len(stream.delivered) = stream.targetLength)
        /\ (backendMode = "httpfs-direct" => Len(stream.delivered) >= stream.targetLength)
        /\ (~pathRewritten =>
              /\ stream.delivered \in openWindowBytes
              /\ stream.delivered \in seenFileBytes))
  /\ (stream.phase \in {"streaming", "eof-ok", "closed-error"} =>
        ENABLED (CloseStream /\ stream'.phase = "disposed-stream"))

StreamProgress ==
  /\ ((scenario = "stream-from-file" /\ ~fileSnap.captured /\ ~providerDisposed /\ fault = "none") =>
       ENABLED (CaptureFileInfo /\ fileSnap'.captured /\ fileSnap'.outcome = "ok"))
  /\ ((StreamSourceReady /\ SelectedHandle.exists /\ ~SelectedHandle.isDir /\
       stream.phase = "idle" /\ ~providerDisposed /\ fault = "none" /\
       fs[PathAppA].kind = "FILE") =>
         ENABLED (BeginStream /\ stream'.phase \in {"redirected-307", "streaming"}))
  /\ ((backendMode = "namenode-redirect" /\
       stream.phase = "redirected-307" /\ fault = "none" /\
       fs[PathAppA].kind = "FILE") =>
         ENABLED (ConnectDataNode /\ stream'.phase = "streaming"))
  /\ ((stream.phase = "streaming" /\ fault = "none" /\
       Len(stream.delivered) < stream.targetLength /\
       Len(stream.delivered) < Len(stream.cachedBytes)) =>
         ENABLED (ReadCachedByte /\ Len(stream'.delivered) = Len(stream.delivered) + 1))
  /\ ((stream.phase = "streaming" /\ fault = "none" /\
       ((backendMode = "namenode-redirect" /\ Len(stream.delivered) = stream.targetLength) \/
        (backendMode = "httpfs-direct" /\ Len(stream.delivered) >= stream.targetLength))) =>
         ENABLED (FinishStreamEof /\ stream'.phase = "eof-ok"))

\* Deliberately false shortcuts / physical-limit probes:

\* Shortcut 1: Assuming CreateReadStream() bytes always match the earlier IFileInfo.Length.
SnapshotLengthMatchesStreamBytes ==
  stream.phase = "eof-ok" => Len(stream.delivered) = stream.snapshotLength

\* Shortcut 2: Uncached two-step GetDirectoryContents("a") (STAT says Exists=TRUE,
\* later GetEnumerator runs LISTSTATUS("a")) never hits 404 or a FILE singleton.
UncachedTwoStepDirectoryCoherent ==
  (scenario = "list-dir" /\ dirReq = ReqA /\ dirSnap.phase = "captured" /\
   dirSnap.outcome = "ok" /\ dirSnap.dir.exists /\ fault = "none") =>
    LET lateList == WebHdfsList(fs, "none", BackendVariant, PathAppA)
    IN  /\ lateList.wire = "http-200-list"
        /\ ~\E e \in lateList.entries : e.pathSuffix = ""

\* Probe 3 (Physical WebHDFS boundary): Mid-stream pathname block refetches can
\* splice bytes across versions into a hybrid sequence matching no version in seenFileBytes.
UntornEvenUnderMidStreamReplacement ==
  stream.phase = "eof-ok" => stream.delivered \in seenFileBytes

\* Probe 4 (Physical HttpFS boundary): FSOperations.FSListStatus buffers fs.listStatus
\* pages before checking fs.getFileStatus(path).isFile(), fabricating child "a" if
\* "/app/a" was observed as FILE during buffering and DIRECTORY at the final check.
HttpFsListNeverFabricatesChild ==
  (scenario = "list-dir" /\ backendMode = "httpfs-direct" /\ dirReq = ReqA /\
   dirSnap.phase = "captured" /\ dirSnap.outcome = "ok") =>
    ~\E c \in dirSnap.dir.entries : c.name = "a"
=============================================================================
