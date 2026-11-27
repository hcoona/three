---------------------------- MODULE PostPipeline ----------------------------
EXTENDS BridgeAlgebra

CONSTANTS Surface, DisableNunjucks, EncodeBraces, RestrictHostEnv, AllowAnonPost, AllowTagInput
ASSUME /\ Surface \in {"renderer-only", "hexo-hooks"}
       /\ DisableNunjucks \in BOOLEAN /\ EncodeBraces \in BOOLEAN
       /\ RestrictHostEnv \in BOOLEAN /\ AllowAnonPost \in BOOLEAN /\ AllowTagInput \in BOOLEAN

Entries  == {"programmatic", "hexo-render", "hexo-render-sync", "hexo-post"}
Zones    == {"prose", "included-prose", "pre-plain", "code-plain", "code-lexed", "raw-script", "stem"}
BaseToks == {"plain", "single-brace", "double-brace", "hash-brace", "swig-literal", "md-fence"}
FoldCtxs == {"none", "prose-comment", "top-pass", "preamble", "sect1", "composite"}

VARIABLES entry, hasExtHint, hlEnabled, fmNunjucks, fmExcerpt, outerHasSwig, zone, tok, foldCtx, fnBeforeFold, dupMore
vars == <<entry, hasExtHint, hlEnabled, fmNunjucks, fmExcerpt, outerHasSwig, zone, tok, foldCtx, fnBeforeFold, dupMore>>

IsSwig(t)      == t \in {"double-brace", "hash-brace", "swig-literal", "hexo-tag"}
HasBrace(t)    == IsSwig(t) \/ t = "single-brace"
HooksActive    == Surface = "hexo-hooks" /\ hasExtHint
LatchedDisable == CASE fmNunjucks = "true" -> TRUE [] fmNunjucks = "false" -> FALSE [] OTHER -> DisableNunjucks
SwigStashRuns  == entry = "hexo-post" /\ ~LatchedDisable /\ (outerHasSwig \/ (zone # "included-prose" /\ IsSwig(tok)))

FoldStack ==
  CASE foldCtx = "preamble"  -> <<Open("div", <<"doc", "preamble">>, "post"), Open("div", NoId, "body")>>
    [] foldCtx = "sect1"     -> <<Open("div", NoId, "sect1"), Open("div", NoId, "body")>>
    [] foldCtx = "composite" -> <<Open("table", NoId, "t"), Open("tr", NoId, "r"), Open("td", NoId, "c")>>
    [] OTHER                 -> <<>>

\* `<!-- more -->` comment obeys the shared `RoundTripSentinel` law when `PostRenderEscape` stashes it:
FoldComment     == IF SwigStashRuns /\ foldCtx # "none"
                   THEN RoundTripSentinel("hexo-sentinel", "more", foldCtx = "prose-comment", FALSE)
                   ELSE [state |-> "intact", val |-> "more", entityEncoded |-> FALSE]
Sliced          == entry = "hexo-post" /\ ~fmExcerpt /\ foldCtx \notin {"none", "prose-comment"}
UnsupportedPost == ~LatchedDisable \/ (Sliced /\ (foldCtx \notin {"top-pass", "preamble"} \/ fnBeforeFold \/ dupMore))
Outcome ==
  IF entry = "hexo-render-sync" THEN "unrendered-sync"
  ELSE IF HooksActive /\ entry = "hexo-post" /\ UnsupportedPost THEN "rejected"
  ELSE "rendered"

\* True non-commuting order: pre_filter -> escape (brace-free sentinel) ->
\* convert + EncodeBraces (no-op on stashed sentinels!) -> onRenderEnd restore + Nunjucks
RenderedItem ==
  LET base == [state |-> "intact", val |-> tok, entityEncoded |-> FALSE]
      afterFence ==
        IF entry = "hexo-post" /\ hlEnabled /\ ~HooksActive /\ zone = "pre-plain" /\ tok = "md-fence"
        THEN [state |-> "injected-hexo-figure", val |-> tok, entityEncoded |-> FALSE] ELSE base
      stashed == SwigStashRuns /\ zone # "included-prose" /\ IsSwig(tok)
      afterRestore ==
        IF stashed THEN
          IF tok = "hash-brace" THEN [state |-> "deleted", val |-> tok, entityEncoded |-> FALSE]
          ELSE RoundTripSentinel("hexo-sentinel", tok, zone = "prose", zone = "code-lexed")
        ELSE [afterFence EXCEPT !.entityEncoded = (@ \/ (EncodeBraces /\ HasBrace(afterFence.val)))]
  IN IF SwigStashRuns /\ afterRestore.state = "intact"
        /\ ~afterRestore.entityEncoded /\ zone \notin {"code-plain", "code-lexed"} /\ IsSwig(afterRestore.val)
     THEN IF afterRestore.val = "hexo-tag"
          THEN [state |-> "expanded-tag", val |-> "hexo-tag", entityEncoded |-> FALSE]
          ELSE [state |-> "nunjucks-corrupted", val |-> afterRestore.val, entityEncoded |-> FALSE]
     ELSE afterRestore

ExcerptDom ==
  IF ~Sliced THEN <<>>
  ELSE IF HooksActive /\ foldCtx = "preamble" THEN FoldStack \o ClosesFor(FoldStack) ELSE FoldStack
MoreDom ==
  IF ~Sliced THEN FoldStack \o ClosesFor(FoldStack)
  ELSE IF HooksActive /\ foldCtx = "preamble" THEN StripIds(FoldStack) \o ClosesFor(FoldStack) ELSE ClosesFor(FoldStack)
ContentAnchors ==
  IF ~Sliced THEN <<>>
  ELSE <<Open("span", <<"doc", "more">>, "post")>> \o (IF dupMore THEN <<Open("a", <<"doc", "more">>, "post")>> ELSE <<>>)

Init ==
  /\ entry \in Entries
  /\ hasExtHint \in (IF entry # "hexo-post" THEN {FALSE} ELSE IF AllowAnonPost THEN BOOLEAN ELSE {TRUE})
  /\ hlEnabled \in (IF entry = "hexo-post" /\ ~RestrictHostEnv THEN BOOLEAN ELSE {FALSE})
  /\ fmNunjucks \in (IF entry = "hexo-post" /\ ~RestrictHostEnv THEN {"unset", "true", "false"} ELSE {"unset"})
  /\ fmExcerpt \in (IF entry = "hexo-post" THEN BOOLEAN ELSE {FALSE})
  /\ outerHasSwig \in (IF entry = "hexo-post" THEN BOOLEAN ELSE {FALSE})
  /\ zone \in Zones
  /\ tok \in (IF AllowTagInput /\ zone = "prose" THEN BaseToks \cup {"hexo-tag"} ELSE BaseToks)
  /\ foldCtx \in (IF entry # "hexo-post" THEN {"none"}
                  ELSE IF RestrictHostEnv THEN {"none", "prose-comment", "top-pass"} ELSE FoldCtxs)
  /\ fnBeforeFold \in (IF Sliced /\ ~RestrictHostEnv THEN BOOLEAN ELSE {FALSE})
  /\ dupMore \in (IF Sliced /\ ~RestrictHostEnv THEN BOOLEAN ELSE {FALSE})

Spec         == Init /\ [][UNCHANGED vars]_vars
PrePlainSpec == (Init /\ entry = "hexo-post" /\ zone = "pre-plain") /\ [][UNCHANGED vars]_vars
-----------------------------------------------------------------------------
LiteralAndStemFidelity ==
  (entry # "hexo-render-sync" /\ Outcome # "rejected" /\ tok # "hexo-tag") =>
    /\ Outcome = "rendered"
    /\ (zone \notin {"raw-script", "stem"} => ObservesLiteral("normal", RenderedItem, tok))
    /\ (zone = "stem" => ObservesLiteral("server-filter", RenderedItem, tok))
RawScriptFidelity ==
  (entry # "hexo-render-sync" /\ Outcome # "rejected" /\ zone = "raw-script" /\ tok # "hexo-tag") =>
    ObservesLiteral("rawtext", RenderedItem, tok)
ExcerptDyckAndIdLaw ==
  (entry = "hexo-post" /\ Outcome = "rendered") =>
    /\ FoldComment.state # "leaked-sentinel" /\ WellFormedDyck(ExcerptDom) /\ WellFormedDyck(MoreDom)
    /\ UniqueIds(ExcerptDom \o MoreDom) /\ UniqueIds(ContentAnchors) /\ ~(Sliced /\ fnBeforeFold)
ExecutionModeLaw ==
  /\ (entry = "hexo-render-sync" <=> Outcome = "unrendered-sync")
  /\ (Outcome = "rejected" => entry = "hexo-post" /\ HooksActive /\ UnsupportedPost)
HexoTagExpansionSupported ==
  (entry = "hexo-post" /\ zone = "prose" /\ tok = "hexo-tag") => Outcome = "rendered" /\ RenderedItem.state = "expanded-tag"
SyncRenderSupported == Outcome # "unrendered-sync"
NeverRendersPost    == ~(entry = "hexo-post" /\ Outcome = "rendered")
=============================================================================
