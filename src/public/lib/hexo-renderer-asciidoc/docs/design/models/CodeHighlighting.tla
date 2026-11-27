-------------------------- MODULE CodeHighlighting --------------------------
EXTENDS BridgeAlgebra

CONSTANTS FollowHexoConfig, StripIndentFalse, EscapeNohighlight, PrismLineNumberFalse, BalancePrismSpans, FailClosedInlineSubs
ASSUME /\ FollowHexoConfig \in BOOLEAN /\ StripIndentFalse \in BOOLEAN /\ EscapeNohighlight \in BOOLEAN
       /\ PrismLineNumberFalse \in BOOLEAN /\ BalancePrismSpans \in BOOLEAN /\ FailClosedInlineSubs \in BOOLEAN

Engines    == {"highlight.js", "prismjs-preprocess", "prismjs-client", "disabled"}
Constructs == {"plain", "multiline-comment", "nested-two-spans", "asterisk-ident"}
InlineSubs == {"none", "quotes", "macros-pass"}

VARIABLES siteEngine, isSourceBlock, nohighlight, lang, hasIndent, hasEntity, construct, calloutLine, inlineSub, wrapLines
vars == <<siteEngine, isSourceBlock, nohighlight, lang, hasIndent, hasEntity, construct, calloutLine, inlineSub, wrapLines>>

SelectedEngine  == IF FollowHexoConfig THEN siteEngine ELSE "highlight.js"
ShouldColorize  == SelectedEngine # "disabled" /\ isSourceBlock /\ ~nohighlight
WouldGrammarLex == ShouldColorize /\ SelectedEngine \in {"highlight.js", "prismjs-preprocess"} /\ lang # "plaintext"
Rejected        == FailClosedInlineSubs /\ WouldGrammarLex /\ inlineSub # "none"
Colorized       == ShouldColorize /\ ~Rejected
GrammarLexed    == Colorized /\ SelectedEngine \in {"highlight.js", "prismjs-preprocess"} /\ lang # "plaintext"

PassSlotResult == RoundTripSentinel("adoc-pass-slot", "strong", FALSE, GrammarLexed)
IndentPrefix   == IF hasIndent /\ (~Colorized \/ StripIndentFalse) THEN <<Atom("indent")>> ELSE <<>>
AnglePrefix    ==
  IF ~hasEntity THEN <<>>
  ELSE IF isSourceBlock /\ nohighlight /\ SelectedEngine # "disabled" /\ ~EscapeNohighlight
       THEN <<Atom("unescaped-angle")>> ELSE <<Atom("escaped-angle")>>
Line1Prefix    ==
  IndentPrefix \o AnglePrefix \o
  (IF inlineSub # "macros-pass" THEN <<>>
   ELSE IF PassSlotResult.state = "intact" THEN <<Open("strong", NoId, "q"), Close("strong")>>
   ELSE <<Atom("leaked-c1-slot")>>)

RawLines ==
  IF ~GrammarLexed THEN
    IF construct = "asterisk-ident"
    THEN [line1 |-> Line1Prefix \o <<Atom("*"), Atom("x"), Atom("*")>>, line2 |-> <<Atom("y")>>]
    ELSE [line1 |-> Line1Prefix \o <<Atom("a")>>, line2 |-> <<Atom("b")>>]
  ELSE CASE construct = "plain" -> [line1 |-> Line1Prefix \o <<Atom("a")>>, line2 |-> <<Atom("b")>>]
         [] construct = "multiline-comment" ->
              [line1 |-> Line1Prefix \o <<Open("span", NoId, "comment"), Atom("a")>>, line2 |-> <<Atom("b"), Close("span")>>]
         [] construct = "nested-two-spans" ->
              [line1 |-> Line1Prefix \o <<Open("span", NoId, "string"), Open("span", NoId, "subst"), Atom("a")>>,
               line2 |-> <<Atom("b"), Close("span"), Close("span")>>]
         [] construct = "asterisk-ident" ->
              [line1 |-> Line1Prefix \o <<Open("span", NoId, "op"), Atom("*"), Close("span"),
                                          Atom("x"), Open("span", NoId, "op"), Atom("*"), Close("span")>>,
               line2 |-> <<Atom("y")>>]

\* `hexo-util.highlight` calls `HljsCloseTags` internally; only `prismHighlight` is adapter-balanced:
BalancedLines ==
  IF ~GrammarLexed THEN RawLines
  ELSE IF SelectedEngine = "highlight.js" THEN HljsCloseTags(RawLines.line1, RawLines.line2)
  ELSE IF BalancePrismSpans THEN BalanceLinesFifo(RawLines.line1, RawLines.line2)
  ELSE RawLines

WithPrismLineNums ==
  IF Colorized /\ SelectedEngine = "prismjs-preprocess" /\ ~PrismLineNumberFalse
  THEN [BalancedLines EXCEPT !.line2 = Append(@, Atom("line-numbers-rows"))] ELSE BalancedLines
WithCallouts ==
  CASE calloutLine = 1 -> [WithPrismLineNums EXCEPT !.line1 = Append(@, Atom("conum"))]
    [] calloutLine = 2 -> [WithPrismLineNums EXCEPT !.line2 = Append(@, Atom("conum"))]
    [] OTHER           -> WithPrismLineNums

RECURSIVE SubQuotes(_, _)
SubQuotes(seq, open) ==
  IF Len(seq) = 0 THEN <<>>
  ELSE IF Head(seq) = Atom("*")
       THEN <<IF ~open THEN Open("strong", NoId, "q") ELSE Close("strong")>> \o SubQuotes(Tail(seq), ~open)
       ELSE <<Head(seq)>> \o SubQuotes(Tail(seq), open)

FinalL1 == IF ~Rejected /\ inlineSub = "quotes" THEN SubQuotes(WithCallouts.line1, FALSE) ELSE WithCallouts.line1
FinalL2 == WithCallouts.line2
DomStream ==
  IF Rejected THEN <<>>
  ELSE IF Colorized /\ wrapLines
       THEN <<Open("pre", NoId, "none"), Open("line", NoId, "l")>> \o FinalL1 \o <<Close("line"), Open("line", NoId, "l")>> \o FinalL2 \o <<Close("line"), Close("pre")>>
       ELSE <<Open("pre", NoId, "none"), Open("code", NoId, "none")>> \o FinalL1 \o FinalL2 \o <<Close("code"), Close("pre")>>

HasAtom(s, v) == \E i \in 1..Len(s) : s[i] = Atom(v)
CalloutValid ==
  calloutLine = 0 \/ Rejected \/
    LET target == IF calloutLine = 1 THEN WithCallouts.line1 ELSE WithCallouts.line2
        prefix == IF calloutLine = 1 THEN SubSeq(target, 1, Len(target) - 1)
                  ELSE WithCallouts.line1 \o SubSeq(target, 1, Len(target) - 1)
    IN /\ target[Len(target)] = Atom("conum") /\ WellFormedDyck(prefix)
       /\ \A i \in 1..(Len(target) - 1) : target[i] # Atom("line-numbers-rows")

ExpectedL2Classes ==
  IF ~GrammarLexed THEN <<>>
  ELSE CASE construct = "multiline-comment" -> <<"comment">>
         [] construct = "nested-two-spans"  -> <<"string", "subst">>
         [] OTHER                           -> <<>>
RECURSIVE ClassesAtText(_, _)
ClassesAtText(seq, st) ==
  IF Len(seq) = 0 \/ Head(seq).op = "atom" THEN [i \in 1..Len(st) |-> st[i].cls]
  ELSE IF Head(seq).op = "open"
       THEN ClassesAtText(Tail(seq), Append(st, [tag |-> Head(seq).tag, cls |-> Head(seq).cls]))
       ELSE ClassesAtText(Tail(seq), SubSeq(st, 1, Len(st) - 1))

Init ==
  /\ siteEngine \in Engines /\ isSourceBlock \in BOOLEAN /\ nohighlight \in BOOLEAN
  /\ lang \in {"sql", "plaintext"} /\ hasIndent \in BOOLEAN /\ hasEntity \in BOOLEAN
  /\ construct \in Constructs /\ calloutLine \in {0, 1, 2} /\ inlineSub \in InlineSubs /\ wrapLines \in BOOLEAN
Spec == Init /\ [][UNCHANGED vars]_vars
-----------------------------------------------------------------------------
HighlightingLaw ==
  ~Rejected =>
    /\ (ShouldColorize => SelectedEngine = siteEngine)
    /\ (hasIndent => HasAtom(DomStream, "indent"))
    /\ (hasEntity => (HasAtom(DomStream, "escaped-angle") /\ ~HasAtom(DomStream, "unescaped-angle")))
    /\ WellFormedDyck(DomStream) /\ ~HasAtom(DomStream, "leaked-c1-slot") /\ (isSourceBlock => CalloutValid)

StrictNestedTokenClassOrder ==
  (~Rejected /\ isSourceBlock) => ClassesAtText(FinalL2, (DyckEval(FinalL1)).stack) = ExpectedL2Classes

UnconditionalInlineSubsOnHighlighted ==
  (isSourceBlock /\ inlineSub # "none") => ~Rejected /\ WellFormedDyck(DomStream) /\ ~HasAtom(DomStream, "leaked-c1-slot")
=============================================================================
