-------------------------- MODULE IngestionAndScope --------------------------
EXTENDS BridgeAlgebra

CONSTANTS Surface, ValidYamlOnly, YamlTaxonomyOnly, MatchingExtOnly, IsolatedPartialsOnly, IncludeBasePolicy, FootnoteScope, AllowUntrusted
ASSUME /\ Surface \in {"renderer-only", "hexo-hooks", "additive-public-processor"}
       /\ ValidYamlOnly \in BOOLEAN /\ YamlTaxonomyOnly \in BOOLEAN
       /\ MatchingExtOnly \in BOOLEAN /\ IsolatedPartialsOnly \in BOOLEAN /\ AllowUntrusted \in BOOLEAN
       /\ IncludeBasePolicy \in {"cwd-default", "naive-dirname", "abs-or-hexo-root-else-secure"}
       /\ FootnoteScope \in {"unscoped", "ids-without-hrefs", "targets-only", "rename-box-too"}

YfmClasses    == IF ValidYamlOnly THEN {"valid-mapping"} ELSE {"valid-mapping", "corrupts-body", "throws-error"}
PartialStates == IF IsolatedPartialsOnly THEN {"isolated"} ELSE {"isolated", "exposed-post", "exposed-asset"}

VARIABLES
  extMatchesCfg, yfmClass, partialState, assetFolder, hasAsset,
  fmHasTitle, adocHasTitle, adocShowtitle, adocOnlyTaxonomy,
  dataPath, cwd, incKind, rawPassthrough, trust, pageView, shareHeading, hasFootnotes,
  postProcessed, assetProcessed, assetRegistered

fixedVars == <<extMatchesCfg, yfmClass, partialState, assetFolder, hasAsset,
               fmHasTitle, adocHasTitle, adocShowtitle, adocOnlyTaxonomy,
               dataPath, cwd, incKind, rawPassthrough, trust, pageView, shareHeading, hasFootnotes>>
vars == <<fixedVars, postProcessed, assetProcessed, assetRegistered>>

BuiltInAdmits == ~assetFolder \/ extMatchesCfg
Admitted      == BuiltInAdmits \/ Surface = "additive-public-processor"
PostSaved     == postProcessed /\ Admitted /\ yfmClass # "throws-error"
PostIntact    == postProcessed /\ Admitted /\ yfmClass = "valid-mapping"

Init ==
  /\ extMatchesCfg \in (IF MatchingExtOnly THEN {TRUE} ELSE BOOLEAN)
  /\ yfmClass \in YfmClasses /\ partialState \in PartialStates
  /\ assetFolder \in BOOLEAN /\ hasAsset \in (IF assetFolder THEN BOOLEAN ELSE {FALSE})
  /\ fmHasTitle \in BOOLEAN /\ adocHasTitle \in BOOLEAN /\ adocShowtitle \in BOOLEAN
  /\ adocOnlyTaxonomy \in (IF YamlTaxonomyOnly THEN {FALSE} ELSE BOOLEAN)
  /\ dataPath \in {"abs-file", "rel-file-hexo", "rel-file-unbound", "in-memory"}
  /\ cwd \in {"site-root", "parent-secret"}
  /\ incKind \in {"none", "sibling", "symlink-escape"} /\ rawPassthrough \in BOOLEAN
  /\ trust \in (IF AllowUntrusted THEN {"trusted", "untrusted"} ELSE {"trusted"})
  /\ pageView \in {"single-post", "index-excerpts", "index-full"}
  /\ shareHeading \in BOOLEAN /\ hasFootnotes \in BOOLEAN
  /\ postProcessed = FALSE /\ assetProcessed = ~hasAsset /\ assetRegistered = FALSE

\* Genuine Hexo `Box` concurrency: `scanAssetDir` runs only when `BuiltInAdmits` holds;
\* built-in `processAsset` (`post.js:239`) drops assets processed before `Post.save`.
ProcessPost ==
  /\ ~postProcessed /\ postProcessed' = TRUE
  /\ assetRegistered' = (assetRegistered \/ (hasAsset /\ BuiltInAdmits /\ yfmClass # "throws-error"))
  /\ UNCHANGED <<fixedVars, assetProcessed>>
ProcessAsset ==
  /\ ~assetProcessed /\ assetProcessed' = TRUE
  /\ assetRegistered' = (assetRegistered \/ PostSaved)
  /\ UNCHANGED <<fixedVars, postProcessed>>
Done == postProcessed /\ assetProcessed
Next == ProcessPost \/ ProcessAsset \/ (Done /\ UNCHANGED vars)
Spec == Init /\ [][Next]_vars

TitleCoherent ==
  LET hasHooks   == Surface # "renderer-only"
      finalTitle == fmHasTitle \/ (adocHasTitle /\ hasHooks)
      bodyH1     == adocHasTitle /\ adocShowtitle /\ ~hasHooks
  IN (fmHasTitle \/ adocHasTitle => finalTitle) /\ (finalTitle => ~bodyH1)
HasFilePath == dataPath # "in-memory"
ResolvedInc(c) ==
  IF incKind = "none" THEN <<"none">>
  ELSE CASE IncludeBasePolicy = "cwd-default" ->
              <<IF incKind = "sibling" THEN "read" ELSE "symlink-escape", c>>
         [] IncludeBasePolicy = "naive-dirname" ->
              \* Relative `RendererData.path` (`dirname("nested/doc.adoc") = "nested"`) still resolves against `c`!
              IF dataPath = "in-memory" THEN <<"secure-disabled">>
              ELSE <<IF incKind = "sibling" THEN "read" ELSE "symlink-escape",
                     IF dataPath = "abs-file" THEN "abs-docdir" ELSE c>>
         [] IncludeBasePolicy = "abs-or-hexo-root-else-secure" ->
              IF dataPath \in {"in-memory", "rel-file-unbound"} THEN <<"secure-disabled">>
              ELSE <<IF incKind = "sibling" THEN "read" ELSE "symlink-escape",
                     IF dataPath = "abs-file" THEN "abs-docdir" ELSE "hexo-root-docdir">>
FnIdPrefix(p)   == IF FootnoteScope # "unscoped" /\ HasFilePath THEN p ELSE "global"
FnHrefPrefix(p) == IF FootnoteScope \in {"targets-only", "rename-box-too"} /\ HasFilePath THEN p ELSE "global"
FnBoxId(p)      == IF FootnoteScope = "rename-box-too" /\ HasFilePath THEN <<p, "footnotes">> ELSE <<"canonical", "footnotes">>
PostDom(p, full) ==
  (IF shareHeading /\ ~full THEN <<Open("div", <<"canonical", "preamble">>, p)>> ELSE <<>>)
  \o (IF shareHeading /\ full THEN <<Open("h2", <<"canonical", "intro">>, p)>> ELSE <<>>)
  \o (IF full /\ hasFootnotes
      THEN <<Open("a", <<FnIdPrefix(p), "fnref-1">>, p),
             Open("div-fnbox", FnBoxId(p), p),
             Open("div", <<FnIdPrefix(p), "fndef-1">>, p)>>
      ELSE <<>>)
PageDom ==
  CASE pageView = "single-post"    -> PostDom("p1", TRUE)
    [] pageView = "index-excerpts" -> PostDom("p1", FALSE) \o PostDom("p2", FALSE)
    [] pageView = "index-full"     -> PostDom("p1", TRUE) \o PostDom("p2", TRUE)
FnResolves(p) ==
  ResolveId(PageDom, <<FnHrefPrefix(p), "fndef-1">>) = p /\ ResolveId(PageDom, <<FnHrefPrefix(p), "fnref-1">>) = p
-----------------------------------------------------------------------------
IngestionAndAssetLaw ==
  Done => PostIntact /\ partialState = "isolated" /\ ~adocOnlyTaxonomy /\ TitleCoherent
          /\ (hasAsset => (Surface # "renderer-only" /\ assetRegistered))
FilesystemIsolationLaw ==
  /\ ResolvedInc("site-root") = ResolvedInc("parent-secret")
  /\ (dataPath \in {"in-memory", "rel-file-unbound"} => ResolvedInc(cwd)[1] \in {"none", "secure-disabled"})
  /\ ResolvedInc("parent-secret") # <<"read", "parent-secret">>
UntrustedInputSafety == trust = "untrusted" => (ResolvedInc(cwd)[1] # "symlink-escape" /\ ~rawPassthrough)
DomFootnoteAndAnchorLaw ==
  HasFilePath =>
    /\ (\A i \in 1..Len(PageDom) : PageDom[i].tag = "div-fnbox" => PageDom[i].id = <<"canonical", "footnotes">>)
    /\ ((pageView = "single-post" /\ shareHeading) => ResolveId(PageDom, <<"canonical", "intro">>) = "p1")
    /\ ((pageView \in {"single-post", "index-full"} /\ hasFootnotes) =>
          FnResolves("p1") /\ (pageView = "index-full" => FnResolves("p2")))
StrictMultiPostDomIdUniqueness == HasFilePath => UniqueIds(PageDom)
MultiPostSectionXrefIntegrity ==
  (HasFilePath /\ pageView = "index-full" /\ shareHeading) => ResolveId(PageDom, <<"canonical", "intro">>) = "p2"
=============================================================================
