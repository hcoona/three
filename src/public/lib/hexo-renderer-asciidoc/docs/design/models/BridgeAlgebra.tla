---------------------------- MODULE BridgeAlgebra ----------------------------
EXTENDS Naturals, Sequences

\* 1. Non-Fixed-Point Sentinel Algebra (`<!--flag\uFFFCk-->` & `\u0096k\u0097`)
SentinelSurvives(kind, arrowsMutated, digitsSpanned) ==
  ~digitsSpanned /\ (kind = "hexo-sentinel" => ~arrowsMutated)

RoundTripSentinel(kind, payload, arrowsMutated, digitsSpanned) ==
  IF SentinelSurvives(kind, arrowsMutated, digitsSpanned)
  THEN [state |-> "intact", val |-> payload, entityEncoded |-> FALSE]
  ELSE [state |-> "leaked-sentinel", val |-> kind, entityEncoded |-> FALSE]

\* 2. Dyck Tag-Stack & Fragment-ID Algebra over HTML5 Event Streams
NoId               == <<"none", "none">>
Open(tag, id, cls) == [op |-> "open", tag |-> tag, id |-> id, cls |-> cls]
Close(tag)         == [op |-> "close", tag |-> tag]
Atom(val)          == [op |-> "atom", val |-> val]

Reverse(s)       == [i \in 1..Len(s) |-> s[Len(s) + 1 - i]]
ClosesFor(opens) == [i \in 1..Len(opens) |-> Close(opens[Len(opens) + 1 - i].tag)]
StripIds(opens)  == [i \in 1..Len(opens) |-> [opens[i] EXCEPT !.id = NoId]]

RECURSIVE DyckScan(_, _)
DyckScan(seq, st) ==
  IF ~st.ok \/ Len(seq) = 0 THEN st
  ELSE LET e == Head(seq) IN
    CASE e.op = "open" ->
           DyckScan(Tail(seq), [ok |-> TRUE, stack |-> Append(st.stack, [tag |-> e.tag, cls |-> e.cls])])
      [] e.op = "close" ->
           IF Len(st.stack) > 0 /\ st.stack[Len(st.stack)].tag = e.tag
           THEN DyckScan(Tail(seq), [ok |-> TRUE, stack |-> SubSeq(st.stack, 1, Len(st.stack) - 1)])
           ELSE [ok |-> FALSE, stack |-> st.stack]
      [] OTHER -> DyckScan(Tail(seq), st)

DyckEval(seq)       == DyckScan(seq, [ok |-> TRUE, stack |-> <<>>])
WellFormedDyck(seq) == LET r == DyckEval(seq) IN r.ok /\ r.stack = <<>>

UniqueIds(seq) ==
  \A i, j \in 1..Len(seq) :
    (i # j /\ seq[i].op = "open" /\ seq[j].op = "open" /\ seq[i].id # NoId) => seq[i].id # seq[j].id

ResolveId(seq, target) ==
  LET hits == {i \in 1..Len(seq) : seq[i].op = "open" /\ seq[i].id = target}
  IN IF hits = {} THEN "missing" ELSE seq[CHOOSE i \in hits : \A j \in hits : i <= j].cls

BalanceLinesFifo(l1, l2) ==
  LET st == (DyckEval(l1)).stack
  IN [line1 |-> l1 \o [i \in 1..Len(st) |-> Close(st[Len(st) + 1 - i].tag)],
      line2 |-> [i \in 1..Len(st) |-> Open(st[i].tag, NoId, st[i].cls)] \o l2]

\* `hexo-util/dist/highlight.js:100-112` (`closeTags`) reopens `unshift` stack unreversed:
HljsCloseTags(l1, l2) ==
  LET rev == Reverse((DyckEval(l1)).stack)
  IN [line1 |-> l1 \o [i \in 1..Len(rev) |-> Close(rev[i].tag)],
      line2 |-> [i \in 1..Len(rev) |-> Open(rev[i].tag, NoId, rev[i].cls)] \o l2]

\* 3. HTML5 Normal vs Rawtext (`<script>`, `<style>`) Entity Decoding
ObservesLiteral(ctx, item, expected) ==
  item.state = "intact" /\ item.val = expected /\ (ctx \in {"rawtext", "server-filter"} => ~item.entityEncoded)
=============================================================================
