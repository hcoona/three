---------------------- MODULE PathAndProjectionDecisions ----------------------
EXTENDS WebHdfsRules

\* Representative HDFS trees spanning empty/non-empty dirs, zero/non-zero and
\* under-construction files, symlinks, file-as-ancestor, and outside-root data.
BaseTree ==
  [p \in Paths |->
    CASE p = PathRoot -> DirNode(1, 10)
      [] p = PathApp -> DirNode(2, 20)
      [] p = PathAppA -> FileNode(3, 2, 30, <<7, 8>>)
      [] p = PathAppB -> DirNode(4, 40)
      [] p \in {PathAppAA, PathAppAx} -> AbsentNode
      [] OTHER -> FileNode(9, 1, 90, <<9>>)]

TreeEmptyFileAndSubdir ==
  [BaseTree EXCEPT ![PathAppA] = DirNode(3, 30),
                   ![PathAppB] = FileNode(4, 0, 40, <<>>),
                   ![PathAppAA] = FileNode(5, 1, 45, <<3>>),
                   ![PathAppAx] = FileNode(6, 0, 50, <<6>>)]

TreeWithSymlinks ==
  [BaseTree EXCEPT ![PathAppA] = SymNode(3, 30),
                   ![PathAppB] = SymNode(4, 40),
                   ![PathOutside] = DirNode(9, 90)]

TreeMissingMountRoot ==
  [BaseTree EXCEPT ![PathApp] = AbsentNode,
                   ![PathAppA] = AbsentNode,
                   ![PathAppB] = AbsentNode]

TreeMountRootIsFile ==
  [TreeMissingMountRoot EXCEPT ![PathApp] = FileNode(2, 1, 20, <<5>>)]

TreeCatalog ==
  {BaseTree, TreeEmptyFileAndSubdir, TreeWithSymlinks, TreeMissingMountRoot, TreeMountRootIsFile}

CallerRequests ==
  { [validSyntax |-> FALSE, segments |-> <<"a">>],
    [validSyntax |-> TRUE,  segments |-> <<>>],
    [validSyntax |-> TRUE,  segments |-> <<"">>],
    [validSyntax |-> TRUE,  segments |-> <<"a">>],
    [validSyntax |-> TRUE,  segments |-> <<"", "a">>],
    [validSyntax |-> TRUE,  segments |-> <<"a", "">>],
    [validSyntax |-> TRUE,  segments |-> <<"b", "..", ".", "a">>],
    [validSyntax |-> TRUE,  segments |-> <<"b">>],
    [validSyntax |-> TRUE,  segments |-> <<"b", "">>],
    [validSyntax |-> TRUE,  segments |-> <<"a", "A">>],
    [validSyntax |-> TRUE,  segments |-> <<"a", "x">>],
    [validSyntax |-> TRUE,  segments |-> <<"outside">>],
    [validSyntax |-> TRUE,  segments |-> <<"", "outside">>],
    [validSyntax |-> TRUE,  segments |-> <<"..", "outside">>],
    [validSyntax |-> TRUE,  segments |-> <<"a", "..", "..", "app", "a">>],
    [validSyntax |-> TRUE,  segments |-> <<"app", "a">>] }

WatchRequests ==
  { [validSyntax |-> FALSE, dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> ""],
    [validSyntax |-> TRUE,  dirSegments |-> <<"a">>,           trailingSlash |-> TRUE,  leafToken |-> ""],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> TRUE,  leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "**"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "**.txt"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"*">>,           trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"b*">>,          trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"b", "..">>,     trailingSlash |-> FALSE, leafToken |-> "a*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"..", "outside">>, trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"..", "outside">>, trailingSlash |-> FALSE, leafToken |-> "*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"b", "..">>,     trailingSlash |-> FALSE, leafToken |-> "a"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"a">>,           trailingSlash |-> FALSE, leafToken |-> "x"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "*.*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<>>,              trailingSlash |-> FALSE, leafToken |-> "a*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"a">>,           trailingSlash |-> FALSE, leafToken |-> "*"],
    [validSyntax |-> TRUE,  dirSegments |-> <<"a">>,           trailingSlash |-> FALSE, leafToken |-> "a*"] }

VARIABLES fs, root, variant, fault, disposed
vars == <<fs, root, variant, fault, disposed>>

Init ==
  /\ fs \in TreeCatalog
  /\ root \in Roots
  /\ variant \in ServerVariants
  /\ fault \in ListFaults
  /\ disposed \in BOOLEAN

Spec == Init /\ [][UNCHANGED vars]_vars

FileFaultOf(f) == IF f = "truncated-200" THEN "none" ELSE f

WellFormedCatalog == ValidTree(fs)

\* Confinement: no operation touches a target outside root's subtree, and changing
\* "/outside" when root = "/app" cannot alter any result, error, or watch signature.
RootConfinementLaw ==
  /\ \A req \in CallerRequests :
       LET fi == EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, req)
           dc == EvalGetDirectoryContents(fs, root, disposed, fault, variant, req)
           res == ResolveSubpath(root, req)
       IN  /\ \A q \in fi.targets : IsUnderRoot(root, q)
           /\ \A q \in dc.targets : IsUnderRoot(root, q)
           /\ (res.status = "rejected" =>
                /\ fi.targets = {} /\ dc.targets = {}
                /\ (~disposed =>
                     /\ fi.outcome = "ok" /\ ~fi.info.exists
                     /\ dc.outcome = "ok" /\ ~dc.dir.exists /\ dc.dir.entries = {}))
  /\ \A wreq \in WatchRequests, pollOn \in BOOLEAN :
       LET spec == ClassifyFilter(root, pollOn, disposed, wreq)
       IN  spec.mode # "null-token" => IsUnderRoot(root, spec.target)
  /\ (root = PathApp =>
       LET altFs == [fs EXCEPT ![PathOutside] =
                      IF fs[PathOutside].kind = "FILE" THEN AbsentNode
                      ELSE FileNode(99, 3, 999, <<1, 2, 3>>)]
       IN  /\ ValidTree(altFs)
           /\ \A req \in CallerRequests :
                /\ EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, req) =
                   EvalGetFileInfo(altFs, root, disposed, FileFaultOf(fault), variant, req)
                /\ EvalGetDirectoryContents(fs, root, disposed, fault, variant, req) =
                   EvalGetDirectoryContents(altFs, root, disposed, fault, variant, req)
           /\ \A wreq \in WatchRequests, pollOn \in BOOLEAN, hasId \in BOOLEAN :
                LET spec == ClassifyFilter(root, pollOn, disposed, wreq)
                IN  ObserveWatchSignature(fs, fault, variant, hasId, spec) =
                    ObserveWatchSignature(altFs, fault, variant, hasId, spec))

\* Leading slashes and benign dot segments normalize identically relative to root;
\* trailing slashes are rejected locally on GetFileInfo ("a/" never matches FILE "a")
\* while permitted on GetDirectoryContents; PatternBuilder rejects mid-pattern ".."
\* ("b/../a*") and folds "*.*" to "*" under Ordinal case-sensitive matching.
PathEquivalenceAndFilterLaw ==
  LET reqRel == [validSyntax |-> TRUE, segments |-> <<"a">>]
      reqSlash == [validSyntax |-> TRUE, segments |-> <<"", "a">>]
      reqTrail == [validSyntax |-> TRUE, segments |-> <<"a", "">>]
      reqDot == [validSyntax |-> TRUE, segments |-> <<"b", "..", ".", "a">>]
      reqReenter == [validSyntax |-> TRUE, segments |-> <<"a", "..", "..", "app", "a">>]
      wTrailing == [validSyntax |-> TRUE, dirSegments |-> <<"a">>, trailingSlash |-> TRUE, leafToken |-> ""]
      wWildDir == [validSyntax |-> TRUE, dirSegments |-> <<"b*">>, trailingSlash |-> FALSE, leafToken |-> "a"]
      wMidDotWild == [validSyntax |-> TRUE, dirSegments |-> <<"b", "..">>, trailingSlash |-> FALSE, leafToken |-> "a*"]
      wRaggedRec == [validSyntax |-> TRUE, dirSegments |-> <<>>, trailingSlash |-> FALSE, leafToken |-> "**.txt"]
      wExactNested == [validSyntax |-> TRUE, dirSegments |-> <<"a">>, trailingSlash |-> FALSE, leafToken |-> "x"]
      wFlatNested == [validSyntax |-> TRUE, dirSegments |-> <<"a">>, trailingSlash |-> FALSE, leafToken |-> "a*"]
      wStarDotStar == [validSyntax |-> TRUE, dirSegments |-> <<>>, trailingSlash |-> FALSE, leafToken |-> "*.*"]
      wStar == [validSyntax |-> TRUE, dirSegments |-> <<>>, trailingSlash |-> FALSE, leafToken |-> "*"]
  IN  /\ EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, reqRel) =
         EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, reqSlash)
      /\ EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, reqRel) =
         EvalGetFileInfo(fs, root, disposed, FileFaultOf(fault), variant, reqDot)
      /\ (~disposed =>
            /\ EvalGetFileInfo(fs, root, FALSE, FileFaultOf(fault), variant, reqTrail).outcome = "ok"
            /\ ~EvalGetFileInfo(fs, root, FALSE, FileFaultOf(fault), variant, reqTrail).info.exists
            /\ EvalGetFileInfo(fs, root, FALSE, FileFaultOf(fault), variant, reqTrail).targets = {})
      /\ EvalGetDirectoryContents(fs, root, disposed, fault, variant, reqRel) =
         EvalGetDirectoryContents(fs, root, disposed, fault, variant, reqSlash)
      /\ EvalGetDirectoryContents(fs, root, disposed, fault, variant, reqRel) =
         EvalGetDirectoryContents(fs, root, disposed, fault, variant, reqTrail)
      /\ ResolveSubpath(root, reqReenter).status = "rejected"
      /\ ResolveSubpath(root, reqReenter).name = "a"
      /\ ClassifyFilter(root, TRUE, FALSE, wTrailing).mode = "null-token"
      /\ ClassifyFilter(root, TRUE, FALSE, wWildDir).mode = "null-token"
      /\ ClassifyFilter(root, TRUE, FALSE, wMidDotWild).mode = "null-token"
      /\ ClassifyFilter(root, TRUE, FALSE, wRaggedRec).mode = "null-token"
      /\ ObserveWatchSignature(fs, fault, variant, TRUE, ClassifyFilter(root, TRUE, disposed, wStarDotStar)) =
         ObserveWatchSignature(fs, fault, variant, TRUE, ClassifyFilter(root, TRUE, disposed, wStar))
      /\ \A wreq \in WatchRequests :
           /\ ClassifyFilter(root, FALSE, disposed, wreq).mode = "null-token"
           /\ ClassifyFilter(root, TRUE, TRUE, wreq).mode = "null-token"
      /\ (root = PathApp =>
            /\ ClassifyFilter(root, TRUE, FALSE, wExactNested) =
                 [mode |-> "exact", target |-> PathAppAx, pattern |-> ""]
            /\ ClassifyFilter(root, TRUE, FALSE, wFlatNested) =
                 [mode |-> "flat-dir", target |-> PathAppA, pattern |-> "a*"]
            /\ (fault = "none" =>
                  ObserveWatchSignature(fs, "none", variant, TRUE,
                    ClassifyFilter(root, TRUE, FALSE, wFlatNested)).children = {}))

\* GetFileInfo projects Exists=TRUE iff request has no trailing slash and target
\* is a regular FILE without non-dir ancestors; GetDirectoryContents projects
\* Exists=TRUE iff target is a DIRECTORY, and every enumerated child binds to
\* its exact parent+child path.
TypeSegregationAndMetadataLaw ==
  \A req \in CallerRequests :
    LET fFault == FileFaultOf(fault)
        fi == EvalGetFileInfo(fs, root, disposed, fFault, variant, req)
        dc == EvalGetDirectoryContents(fs, root, disposed, fault, variant, req)
        res == ResolveSubpath(root, req)
        cleanAncestors ==
          res.status # "rejected" /\
          \A a \in Ancestors(res.target) : NodeAt(fs, a).kind = "DIRECTORY"
    IN  /\ ValidFileInfoShape(fi.info)
        /\ ~fi.info.isDir
        /\ IF res.status = "rejected" \/ disposed
           THEN ValidDirContentsShape(dc.dir)
           ELSE ValidDirContentsFor(res.target, dc.dir)
        /\ ((~disposed /\ fFault = "none") =>
              /\ fi.outcome = "ok"
              /\ (fi.info.exists <=>
                    (res.status = "child" /\ ~res.trailingSlash /\
                     cleanAncestors /\ NodeAt(fs, res.target).kind = "FILE")))
        /\ ((~disposed /\ fault = "none") =>
              /\ dc.outcome = "ok"
              /\ (dc.dir.exists <=>
                    (res.status \in {"root", "child"} /\ cleanAncestors /\
                     NodeAt(fs, res.target).kind = "DIRECTORY")))

\* NameNode user, NameNode superuser, HDFS-backed HttpFS (with remote symlink
\* resolution disabled), and WebHDFS-proxied HttpFS return different HTTP codes
\* and exception wrappers on symlinks (403 UnresolvedPathException vs 500
\* IOException vs 500 UnresolvedPathException) and file-ancestors (403 vs 404 vs
\* 500), yet the adapter projection is invariant across all four.
ServerVariantUniformityLaw ==
  (~disposed /\ fault = "none") =>
    /\ \A req \in CallerRequests, other \in ServerVariants :
         /\ EvalGetFileInfo(fs, root, FALSE, "none", variant, req) =
            EvalGetFileInfo(fs, root, FALSE, "none", other, req)
         /\ EvalGetDirectoryContents(fs, root, FALSE, "none", variant, req) =
            EvalGetDirectoryContents(fs, root, FALSE, "none", other, req)
    /\ \A wreq \in WatchRequests, other \in ServerVariants, hasId \in BOOLEAN :
         LET spec == ClassifyFilter(root, TRUE, FALSE, wreq)
         IN  ObserveWatchSignature(fs, "none", variant, hasId, spec) =
             ObserveWatchSignature(fs, "none", other, hasId, spec)
    /\ \A hasStack \in BOOLEAN :
         /\ ClassifyWireError(
              PathAppAx,
              IoErrorWire(variant),
              "AccessControlException",
              MakeMessage("/app/a (is not a directory)", hasStack)) = "not-found"
         /\ ClassifyWireError(
              PathAppAx,
              IoErrorWire(variant),
              "AccessControlException",
              MakeMessage("Permission denied: /app/a (is not a directory)", hasStack)) = "fault"
         /\ ClassifyWireError(
              PathAppAx,
              IoErrorWire(variant),
              "AccessControlException",
              MakeMessage("/outside (is not a directory)", hasStack)) = "fault"
         /\ ClassifyWireError(
              PathAppA,
              "http-500",
              "IOException",
              MakeMessage(DisabledSymlinkMessage(PathAppA), hasStack)) = "not-found"
         /\ ClassifyWireError(
              PathAppA,
              "http-500",
              "IOException",
              MakeMessage("Disk read error", hasStack)) = "fault"

\* Under strict_epistemic, genuine auth/permission/standby/5xx/transport/truncation
\* faults on remote lookups surface as "fault", never masquerading as NotFound;
\* Provider.Dispose() preempts all GetFileInfo and GetDirectoryContents calls.
EpistemicFaultLaw ==
  /\ (disposed =>
        \A req \in CallerRequests :
          /\ EvalGetFileInfo(fs, root, TRUE, FileFaultOf(fault), variant, req).outcome = "disposed"
          /\ EvalGetDirectoryContents(fs, root, TRUE, fault, variant, req).outcome = "disposed")
  /\ (~disposed =>
        \A req \in CallerRequests :
          LET res == ResolveSubpath(root, req)
              fFault == FileFaultOf(fault)
              fi == EvalGetFileInfo(fs, root, FALSE, fFault, variant, req)
              dc == EvalGetDirectoryContents(fs, root, FALSE, fault, variant, req)
          IN  /\ ((res.status = "child" /\ ~res.trailingSlash) =>
                    (fi.outcome = "fault" <=> fFault # "none"))
              /\ ((res.status \in {"rejected", "root"} \/ res.trailingSlash) =>
                    fi.outcome = "ok")
              /\ (res.status \in {"root", "child"} =>
                    (dc.outcome = "fault" <=> fault # "none")))

\* Deliberately flawed shortcuts refuted by dedicated counterexample configs:

\* Shortcut 1: Classify single-RPC errors using only structured fields (wire, exception)
\* without matching "<ancestor> (is not a directory)" on AccessControlException—proving
\* NO choice of aceOutcome ("ok" or "fault") satisfies both file-ancestor NotFound and
\* permission-denied Fault on NameNode user / HttpFS (which also subsumes status-code-only).
StructuredExceptionFieldsAloneSuffice ==
  (~disposed /\ fault = "none") =>
    \E aceOutcome \in {"ok", "fault"} :
      \A req \in CallerRequests, simFault \in {"none", "access-denied"} :
        LET res == ResolveSubpath(root, req)
        IN  (res.status = "child" /\ ~res.trailingSlash) =>
              LET raw == WebHdfsStat(fs, simFault, variant, res.target)
                  policyOutcome ==
                    IF raw.wire = "http-200-stat" \/ raw.wire = "http-404" \/
                       raw.exception \in {"UnresolvedPathException", "IOException"}
                    THEN "ok"
                    ELSE IF raw.exception = "AccessControlException"
                    THEN aceOutcome
                    ELSE "fault"
              IN  policyOutcome =
                  EvalGetFileInfo(fs, root, FALSE, simFault, variant, req).outcome

\* Shortcut 2: Treat any HTTP 200 LISTSTATUS response as IDirectoryContents.Exists=TRUE
\* without filtering out WebHDFS's singleton pathSuffix="" response on regular files.
Http200ListMeansDirectoryExists ==
  (~disposed /\ fault = "none") =>
    \A req \in CallerRequests :
      LET res == ResolveSubpath(root, req)
      IN  res.status \in {"root", "child"} =>
            LET raw == WebHdfsList(fs, "none", variant, res.target)
            IN  (raw.wire = "http-200-list") =>
                  EvalGetDirectoryContents(fs, root, FALSE, "none", variant, req).dir.exists
=============================================================================
