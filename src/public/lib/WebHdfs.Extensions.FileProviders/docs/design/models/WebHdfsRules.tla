---------------------------- MODULE WebHdfsRules ----------------------------
EXTENDS Integers, Sequences, FiniteSets

\* Concrete HDFS namespace skeleton represented directly as segment sequences:
\* <<>> is "/", <<"app">> is "/app", grandchildren "A" and "x" exercise both
\* multi-page listing on mutable "/app/a" and Ordinal case-sensitive globbing,
\* and <<"outside">> tests root confinement.
PathRoot    == <<>>
PathApp     == <<"app">>
PathAppA    == <<"app", "a">>
PathAppB    == <<"app", "b">>
PathAppAA   == <<"app", "a", "A">>
PathAppAx   == <<"app", "a", "x">>
PathOutside == <<"outside">>
NoTarget    == <<"__none__">>

Paths == {PathRoot, PathApp, PathAppA, PathAppB, PathAppAA, PathAppAx, PathOutside}
Roots == {PathRoot, PathApp}

NodeKinds == {"ABSENT", "FILE", "DIRECTORY", "SYMLINK"}
ServerVariants == {"namenode-user", "namenode-super", "httpfs", "httpfs-webhdfs"}
RemoteFaults == {"none", "auth-security", "access-denied", "standby", "server-5xx", "transport"}
ListFaults == RemoteFaults \cup {"truncated-200"}

AbsentNode          == [kind |-> "ABSENT",    id |-> 0, length |-> 0, mtime |-> 0, bytes |-> <<>>]
DirNode(i, t)       == [kind |-> "DIRECTORY", id |-> i, length |-> 0, mtime |-> t, bytes |-> <<>>]
FileNode(i, l, t, b)== [kind |-> "FILE",      id |-> i, length |-> l, mtime |-> t, bytes |-> b]
SymNode(i, t)       == [kind |-> "SYMLINK",   id |-> i, length |-> 0, mtime |-> t, bytes |-> <<>>]

ParentOf(p)          == SubSeq(p, 1, Len(p) - 1)
ChildNameOf(p)       == IF p = PathRoot THEN "" ELSE p[Len(p)]
AncestorSeq(p)       == IF p = PathRoot THEN <<>> ELSE [i \in 1..Len(p) |-> SubSeq(p, 1, i - 1)]
Ancestors(p)         == {SubSeq(p, 1, i - 1) : i \in 1..Len(p)}
DirectChildren(d)    == {p \in Paths : p # PathRoot /\ ParentOf(p) = d}
IsUnderRoot(root, q) == q # NoTarget /\ Len(q) >= Len(root) /\ SubSeq(q, 1, Len(root)) = root
NodeAt(fs, q)        == IF q \in Paths THEN fs[q] ELSE AbsentNode

RECURSIVE JoinSlash(_)
JoinSlash(seq) == IF seq = <<>> THEN "" ELSE "/" \o Head(seq) \o JoinSlash(Tail(seq))
FormatPath(p)  == IF p = PathRoot THEN "/" ELSE JoinSlash(p)

\* HDFS namespace invariant: root is a directory, every existing node sits
\* directly under an existing directory, and reported file length never exceeds
\* readable bytes (strictly less when the last block is under construction).
ValidTree(fs) ==
  /\ DOMAIN fs = Paths
  /\ fs[PathRoot].kind = "DIRECTORY"
  /\ \A p \in Paths :
       /\ fs[p].kind \in NodeKinds
       /\ (p # PathRoot /\ fs[p].kind # "ABSENT") => fs[ParentOf(p)].kind = "DIRECTORY"
       /\ fs[p].kind \in {"ABSENT", "DIRECTORY", "SYMLINK"} =>
            (fs[p].length = 0 /\ fs[p].bytes = <<>>)
       /\ fs[p].kind = "FILE" =>
            (fs[p].length >= 0 /\ fs[p].length <= Len(fs[p].bytes))

\* Sticky segment-stack reducer: once traversal drops above the mounted root,
\* subsequent segments cannot re-enter the root by naming it.
RECURSIVE ReduceStep(_, _)
ReduceStep(state, remaining) ==
  IF remaining = <<>> THEN state
  ELSE IF ~state.ok THEN state
  ELSE LET seg == Head(remaining)
           rest == Tail(remaining)
       IN  IF seg \in {"", "."} THEN ReduceStep(state, rest)
           ELSE IF seg = ".." THEN
             IF state.stack = <<>>
             THEN [ok |-> FALSE, stack |-> <<>>]
             ELSE ReduceStep(
                    [ok |-> TRUE,
                     stack |-> SubSeq(state.stack, 1, Len(state.stack) - 1)],
                    rest)
           ELSE ReduceStep([ok |-> TRUE, stack |-> Append(state.stack, seg)], rest)

NormalizeSegments(segs) == ReduceStep([ok |-> TRUE, stack |-> <<>>], segs)

LegalLeafNames == {"", ".", "..", "app", "outside", "a", "b", "A", "x", "bad-syntax"}
FlatWildcardTokens == {"*", "*.*", "a*", "b*"}
RecursiveWildcardTokens == {"**", "**.txt"}
WildcardTokens == FlatWildcardTokens \cup RecursiveWildcardTokens
LiteralFilterTokens == LegalLeafNames \ {"", "bad-syntax"}
NullSpec == [mode |-> "null-token", target |-> NoTarget, pattern |-> ""]

\* Case-sensitive (StringComparison.Ordinal) glob matching for POSIX HDFS names,
\* including PatternBuilder's "*.*" -> "*" folding (matching extensionless names).
MatchesFlatWildcard(pattern, childName) ==
  CASE pattern \in {"*", "*.*"} -> TRUE
    [] pattern = "a*" -> childName \in {"app", "a"}
    [] pattern = "b*" -> childName = "b"
    [] OTHER -> FALSE

LeafNameOfSegments(segs) ==
  IF segs = <<>> THEN "" ELSE segs[Len(segs)]

HasTrailingSlash(segs) ==
  Len(segs) > 0 /\ segs[Len(segs)] = ""

\* Leading slashes are always interpreted relative to root, never HDFS "/".
\* Trailing slashes are preserved on the resolution record so GetFileInfo("a/")
\* rejects directory-shaped paths locally without letting WebHDFS strip "/".
ResolveSubpath(root, req) ==
  IF ~req.validSyntax THEN
    [status |-> "rejected", trailingSlash |-> FALSE, target |-> NoTarget, name |-> "bad-syntax"]
  ELSE LET norm == NormalizeSegments(req.segments)
           rawLeaf == LeafNameOfSegments(req.segments)
           tSlash == HasTrailingSlash(req.segments)
       IN  IF ~norm.ok THEN
             [status |-> "rejected", trailingSlash |-> tSlash, target |-> NoTarget, name |-> rawLeaf]
           ELSE IF norm.stack = <<>> THEN
             [status |-> "root", trailingSlash |-> tSlash, target |-> root, name |-> ""]
           ELSE
             [status |-> "child",
              trailingSlash |-> tSlash,
              target |-> root \o norm.stack,
              name |-> IF tSlash THEN "" ELSE norm.stack[Len(norm.stack)]]

\* PatternBuilder.cs only allows ".." at the very beginning of a wildcard pattern
\* (where it immediately navigates above Root), throwing ArgumentException on
\* mid-pattern ".." such as "b/../a*"; recursive "**" / "**.txt" RaggedPattern
\* tokens likewise degrade to NullChangeToken.
ClassifyFilter(root, pollingEnabled, disposed, req) ==
  IF ~pollingEnabled \/ disposed \/ ~req.validSyntax \/ req.trailingSlash \/
     req.leafToken = "" \/ req.leafToken \in RecursiveWildcardTokens \/
     (\E i \in 1..Len(req.dirSegments) : req.dirSegments[i] \in WildcardTokens) THEN
    NullSpec
  ELSE IF req.leafToken \in FlatWildcardTokens THEN
    IF \E i \in 1..Len(req.dirSegments) : req.dirSegments[i] = ".." THEN
      NullSpec
    ELSE LET dirNorm == NormalizeSegments(req.dirSegments)
         IN  IF ~dirNorm.ok THEN NullSpec
             ELSE [mode |-> "flat-dir", target |-> root \o dirNorm.stack, pattern |-> req.leafToken]
  ELSE IF req.leafToken \in LiteralFilterTokens THEN
    LET fullNorm == NormalizeSegments(Append(req.dirSegments, req.leafToken))
    IN  IF ~fullNorm.ok \/ fullNorm.stack = <<>> THEN NullSpec
        ELSE [mode |-> "exact", target |-> root \o fullNorm.stack, pattern |-> ""]
  ELSE
    NullSpec

\* Exact HTTP status mapping across NameNode ExceptionHandler (IOException -> 403,
\* SecurityException -> 403) and HttpFSExceptionProvider (IOException -> 500,
\* SecurityException -> 401).
IsHttpFsVariant(variant) ==
  variant \in {"httpfs", "httpfs-webhdfs"}

IoErrorWire(variant) ==
  IF IsHttpFsVariant(variant) THEN "http-500" ELSE "http-403"

SecurityErrorWire(variant) ==
  IF IsHttpFsVariant(variant) THEN "http-401" ELSE "http-403"

\* Multi-line RemoteException messages (such as IPC stack traces before/without
\* server truncation) are matched strictly by their first line, mirroring
\* HttpExceptionUtils.getOneLineMessage and FSPermissionChecker's format.
MakeMessage(firstLine, hasStackTrace) ==
  [firstLine |-> firstLine, hasStackTrace |-> hasStackTrace]

EmptyMessage == MakeMessage("", FALSE)

\* FileSystemLinkResolver.resolve (FileSystemLinkResolver.java:84-88) wraps
\* UnresolvedLinkException into IOException with this exact message when
\* HDFS-backed HttpFS runs with fs.client.resolve.remote.symlinks = false.
DisabledSymlinkMessage(target) ==
  "Path " \o FormatPath(target) \o
  " contains a symlink and symlink resolution is disabled (fs.client.resolve.remote.symlinks)."

SymlinkWireError(variant, stackSuffix, target) ==
  IF variant = "httpfs" THEN
    [wire |-> "http-500",
     exception |-> "IOException",
     message |-> MakeMessage(DisabledSymlinkMessage(target), FALSE)]
  ELSE
    [wire |-> IoErrorWire(variant),
     exception |-> "UnresolvedPathException",
     message |-> MakeMessage("Unresolved symlink", stackSuffix)]

FaultWireResponse(fault, variant) ==
  CASE fault = "auth-security" ->
         [wire |-> SecurityErrorWire(variant),
          exception |-> "SecurityException",
          message |-> MakeMessage("Authentication required", FALSE)]
    [] fault = "access-denied" ->
         [wire |-> IoErrorWire(variant),
          exception |-> "AccessControlException",
          message |-> MakeMessage("Permission denied", ~IsHttpFsVariant(variant))]
    [] fault = "standby" ->
         [wire |-> IoErrorWire(variant),
          exception |-> "StandbyException",
          message |-> MakeMessage("Operation category READ is not supported in state standby", FALSE)]
    [] fault = "server-5xx" ->
         [wire |-> "http-500",
          exception |-> "RuntimeException",
          message |-> MakeMessage("Internal server error", TRUE)]
    [] OTHER ->
         [wire |-> "transport-error",
          exception |-> "TransportError",
          message |-> MakeMessage("Connection reset", FALSE)]

\* Walk proper ancestors in FSDirectory.resolvePath order for STAT/LIST ops
\* ("httpfs" / "httpfs-webhdfs" model non-superuser callers).
\* FSDirectory.java:752-759 wraps !isCreate ParentNotDirectoryException into
\* AccessControlException("<ancestor> (is not a directory)"); FSDirStatAndListingOp
\* getFileInfo (lines 103-111) swallows that ACE to 404 only for HDFS superuser
\* getFileInfo lookups (STAT, superuser HDFS-backed HttpFS stat, and NameNode
\* OPEN's preliminary lookup), never for LISTSTATUS or normal-user calls.
RECURSIVE WalkAncestors(_, _, _, _, _, _)
WalkAncestors(fs, variant, stackSuffix, op, target, seq) ==
  IF seq = <<>> THEN [blocked |-> FALSE, wire |-> "", exception |-> "", message |-> EmptyMessage]
  ELSE LET ancestor == Head(seq)
           kind == NodeAt(fs, ancestor).kind
       IN  IF kind = "ABSENT" THEN
             [blocked |-> TRUE,
              wire |-> "http-404",
              exception |-> "FileNotFoundException",
              message |-> MakeMessage("File does not exist", FALSE)]
           ELSE IF kind = "SYMLINK" THEN
             LET symErr == SymlinkWireError(variant, stackSuffix, target)
             IN  [blocked |-> TRUE,
                  wire |-> symErr.wire,
                  exception |-> symErr.exception,
                  message |-> symErr.message]
           ELSE IF kind = "FILE" THEN
             IF variant = "namenode-super" /\ op = "STAT"
             THEN [blocked |-> TRUE,
                   wire |-> "http-404",
                   exception |-> "FileNotFoundException",
                   message |-> MakeMessage("File does not exist", FALSE)]
             ELSE [blocked |-> TRUE,
                   wire |-> IoErrorWire(variant),
                   exception |-> "AccessControlException",
                   message |-> MakeMessage(FormatPath(ancestor) \o " (is not a directory)", stackSuffix)]
           ELSE WalkAncestors(fs, variant, stackSuffix, op, target, Tail(seq))

StatusRecord(suffix, node) ==
  [pathSuffix |-> suffix,
   kind |-> node.kind,
   id |-> node.id,
   length |-> node.length,
   mtime |-> node.mtime]

EmptyStatus == [pathSuffix |-> "", kind |-> "ABSENT", id |-> 0, length |-> 0, mtime |-> 0]

\* WebHDFS op=GETFILESTATUS wire behavior across NameNode, HDFS-backed HttpFS
\* with fs.client.resolve.remote.symlinks = false ("httpfs"), and WebHDFS-proxied
\* HttpFS ("httpfs-webhdfs").
WebHdfsStatWire(fs, fault, variant, stackSuffix, target) ==
  IF fault # "none" THEN
    LET err == FaultWireResponse(fault, variant)
    IN  [wire |-> err.wire, exception |-> err.exception, message |-> err.message, entry |-> EmptyStatus]
  ELSE LET walk == WalkAncestors(fs, variant, stackSuffix, "STAT", target, AncestorSeq(target))
           node == NodeAt(fs, target)
       IN  IF walk.blocked THEN
             [wire |-> walk.wire,
              exception |-> walk.exception,
              message |-> walk.message,
              entry |-> EmptyStatus]
           ELSE IF node.kind = "ABSENT" THEN
             [wire |-> "http-404",
              exception |-> "FileNotFoundException",
              message |-> MakeMessage("File does not exist", FALSE),
              entry |-> EmptyStatus]
           ELSE IF node.kind = "SYMLINK" THEN
             LET symErr == SymlinkWireError(variant, stackSuffix, target)
             IN  [wire |-> symErr.wire,
                  exception |-> symErr.exception,
                  message |-> symErr.message,
                  entry |-> EmptyStatus]
           ELSE
             [wire |-> "http-200-stat", exception |-> "", message |-> EmptyMessage, entry |-> StatusRecord("", node)]

WebHdfsStat(fs, fault, variant, target) ==
  WebHdfsStatWire(fs, fault, variant, variant = "namenode-user", target)

\* WebHDFS op=LISTSTATUS wire behavior, including singleton empty-suffix status
\* on regular files, child type="SYMLINK" entries inside directories (JsonUtil
\* and FSOperations.toJsonInner), and mid-stream HTTP 200 truncation.
WebHdfsListWire(fs, fault, variant, stackSuffix, target) ==
  IF fault = "truncated-200" THEN
    [wire |-> "http-200-truncated",
     exception |-> "StreamTruncated",
     message |-> MakeMessage("Truncated JSON", FALSE),
     entries |-> {}]
  ELSE IF fault # "none" THEN
    LET err == FaultWireResponse(fault, variant)
    IN  [wire |-> err.wire, exception |-> err.exception, message |-> err.message, entries |-> {}]
  ELSE LET walk == WalkAncestors(fs, variant, stackSuffix, "LIST", target, AncestorSeq(target))
           node == NodeAt(fs, target)
       IN  IF walk.blocked THEN
             [wire |-> walk.wire,
              exception |-> walk.exception,
              message |-> walk.message,
              entries |-> {}]
           ELSE IF node.kind = "ABSENT" THEN
             [wire |-> "http-404",
              exception |-> "FileNotFoundException",
              message |-> MakeMessage("File does not exist", FALSE),
              entries |-> {}]
           ELSE IF node.kind = "SYMLINK" THEN
             LET symErr == SymlinkWireError(variant, stackSuffix, target)
             IN  [wire |-> symErr.wire,
                  exception |-> symErr.exception,
                  message |-> symErr.message,
                  entries |-> {}]
           ELSE IF node.kind = "FILE" THEN
             [wire |-> "http-200-list", exception |-> "", message |-> EmptyMessage, entries |-> {StatusRecord("", node)}]
           ELSE
             [wire |-> "http-200-list",
              exception |-> "",
              message |-> EmptyMessage,
              entries |-> {StatusRecord(ChildNameOf(c), fs[c]) :
                           c \in {x \in DirectChildren(target) : fs[x].kind # "ABSENT"}}]

WebHdfsList(fs, fault, variant, target) ==
  WebHdfsListWire(fs, fault, variant, variant = "namenode-user", target)

FirstLineOf(msg) == msg.firstLine

IsFileAncestorAceMessage(target, msg) ==
  \E ancestor \in Ancestors(target) :
    FirstLineOf(msg) = (FormatPath(ancestor) \o " (is not a directory)")

ClassifyWireError(target, wire, exception, message) ==
  IF wire = "http-404" /\ exception = "FileNotFoundException" THEN
    "not-found"
  ELSE IF wire \in {"http-403", "http-500"} /\ exception = "UnresolvedPathException" THEN
    "not-found"
  ELSE IF wire = "http-500" /\
          exception = "IOException" /\
          FirstLineOf(message) = DisabledSymlinkMessage(target) THEN
    "not-found"
  ELSE IF wire \in {"http-403", "http-500"} /\
          exception = "AccessControlException" /\
          IsFileAncestorAceMessage(target, message) THEN
    "not-found"
  ELSE
    "fault"

NotFoundFile(name) ==
  [exists |-> FALSE,
   isDir |-> FALSE,
   length |-> -1,
   mtime |-> -1,
   name |-> name,
   target |-> NoTarget,
   streamable |-> FALSE]

RegularFileProjection(name, target, status) ==
  [exists |-> TRUE,
   isDir |-> FALSE,
   length |-> status.length,
   mtime |-> status.mtime,
   name |-> name,
   target |-> target,
   streamable |-> TRUE]

DirectoryChildProjection(parentTarget, status) ==
  LET childTarget == Append(parentTarget, status.pathSuffix)
  IN  IF status.kind = "FILE" THEN
        RegularFileProjection(status.pathSuffix, childTarget, status)
      ELSE
        [exists |-> TRUE,
         isDir |-> TRUE,
         length |-> -1,
         mtime |-> status.mtime,
         name |-> status.pathSuffix,
         target |-> childTarget,
         streamable |-> FALSE]

NotFoundDirectory == [exists |-> FALSE, entries |-> {}]

ValidFileInfoShape(info) ==
  /\ info.name \in LegalLeafNames
  /\ ~info.exists =>
       (~info.isDir /\ info.length = -1 /\ info.mtime = -1 /\ ~info.streamable /\ info.target = NoTarget)
  /\ info.exists => info.name \notin {"", ".", ".."}
  /\ info.isDir => (info.exists /\ info.length = -1 /\ ~info.streamable /\ info.target # NoTarget)
  /\ (info.exists /\ ~info.isDir) => (info.length >= 0 /\ info.streamable /\ info.target # NoTarget)

ValidDirContentsShape(dir) ==
  /\ (~dir.exists => dir.entries = {})
  /\ \A c \in dir.entries : ValidFileInfoShape(c)
  /\ \A c1, c2 \in dir.entries : c1.name = c2.name => c1 = c2

ValidDirContentsFor(parentTarget, dir) ==
  /\ ValidDirContentsShape(dir)
  /\ \A c \in dir.entries :
       /\ c.target = Append(parentTarget, c.name)
       /\ ParentOf(c.target) = parentTarget
       /\ ChildNameOf(c.target) = c.name

EvalGetFileInfo(fs, root, disposed, fault, variant, req) ==
  IF disposed THEN
    [outcome |-> "disposed", reason |-> "disposed", info |-> NotFoundFile(""), targets |-> {}]
  ELSE LET res == ResolveSubpath(root, req)
       IN  IF res.status \in {"rejected", "root"} \/ res.trailingSlash THEN
             [outcome |-> "ok", reason |-> "", info |-> NotFoundFile(res.name), targets |-> {}]
           ELSE LET raw == WebHdfsStat(fs, fault, variant, res.target)
                IN  IF raw.wire = "http-200-stat" THEN
                      IF raw.entry.kind = "FILE" THEN
                        [outcome |-> "ok",
                         reason |-> "",
                         info |-> RegularFileProjection(res.name, res.target, raw.entry),
                         targets |-> {res.target}]
                      ELSE
                        [outcome |-> "ok",
                         reason |-> "",
                         info |-> NotFoundFile(res.name),
                         targets |-> {res.target}]
                    ELSE IF ClassifyWireError(res.target, raw.wire, raw.exception, raw.message) = "not-found" THEN
                      [outcome |-> "ok",
                       reason |-> "",
                       info |-> NotFoundFile(res.name),
                       targets |-> {res.target}]
                    ELSE
                      [outcome |-> "fault",
                       reason |-> raw.exception,
                       info |-> NotFoundFile(""),
                       targets |-> {res.target}]

EvalGetDirectoryContents(fs, root, disposed, fault, variant, req) ==
  IF disposed THEN
    [outcome |-> "disposed", reason |-> "disposed", dir |-> NotFoundDirectory, targets |-> {}]
  ELSE LET res == ResolveSubpath(root, req)
       IN  IF res.status = "rejected" THEN
             [outcome |-> "ok", reason |-> "", dir |-> NotFoundDirectory, targets |-> {}]
           ELSE LET raw == WebHdfsList(fs, fault, variant, res.target)
                IN  IF raw.wire = "http-200-list" THEN
                      IF \E e \in raw.entries : e.pathSuffix = "" THEN
                        [outcome |-> "ok",
                         reason |-> "",
                         dir |-> NotFoundDirectory,
                         targets |-> {res.target}]
                      ELSE
                        [outcome |-> "ok",
                         reason |-> "",
                         dir |-> [exists |-> TRUE,
                                  entries |-> {DirectoryChildProjection(res.target, e) :
                                               e \in {x \in raw.entries : x.kind \in {"FILE", "DIRECTORY"}}}],
                         targets |-> {res.target}]
                    ELSE IF ClassifyWireError(res.target, raw.wire, raw.exception, raw.message) = "not-found" THEN
                      [outcome |-> "ok",
                       reason |-> "",
                       dir |-> NotFoundDirectory,
                       targets |-> {res.target}]
                    ELSE
                      [outcome |-> "fault",
                       reason |-> raw.exception,
                       dir |-> NotFoundDirectory,
                       targets |-> {res.target}]

\* Homogeneous signature records for Watch baseline and poll comparison.
MakeSignature(stateTag, kind, id, len, mtime, children) ==
  [state |-> stateTag,
   kind |-> kind,
   id |-> id,
   length |-> len,
   mtime |-> mtime,
   children |-> children]

UnobservedSignature == MakeSignature("unobserved", "", 0, 0, 0, {})
UnknownSignature    == MakeSignature("unknown", "", 0, 0, 0, {})
AbsentSignature     == MakeSignature("absent", "", 0, 0, 0, {})

ObserveWatchSignature(fs, fault, variant, hasFileId, spec) ==
  IF spec.mode = "null-token" THEN AbsentSignature
  ELSE IF spec.mode = "exact" THEN
    LET raw == WebHdfsStat(fs, fault, variant, spec.target)
    IN  IF raw.wire = "http-200-stat" THEN
          IF raw.entry.kind \in {"FILE", "DIRECTORY"} THEN
            MakeSignature(
              "present-node",
              raw.entry.kind,
              IF hasFileId THEN raw.entry.id ELSE 0,
              raw.entry.length,
              raw.entry.mtime,
              {})
          ELSE AbsentSignature
        ELSE IF ClassifyWireError(spec.target, raw.wire, raw.exception, raw.message) = "not-found" THEN
          AbsentSignature
        ELSE UnknownSignature
  ELSE
    LET raw == WebHdfsList(fs, fault, variant, spec.target)
    IN  IF raw.wire = "http-200-list" THEN
          IF \E e \in raw.entries : e.pathSuffix = "" THEN
            MakeSignature("matched-set", "", 0, 0, 0, {})
          ELSE
            MakeSignature(
              "matched-set",
              "",
              0,
              0,
              0,
              {<<e.pathSuffix,
                 e.kind,
                 IF hasFileId THEN e.id ELSE 0,
                 e.length,
                 e.mtime>> :
               e \in {x \in raw.entries :
                      x.kind = "FILE" /\ MatchesFlatWildcard(spec.pattern, x.pathSuffix)}})
        ELSE IF ClassifyWireError(spec.target, raw.wire, raw.exception, raw.message) = "not-found" THEN
          MakeSignature("matched-set", "", 0, 0, 0, {})
        ELSE UnknownSignature
=============================================================================
