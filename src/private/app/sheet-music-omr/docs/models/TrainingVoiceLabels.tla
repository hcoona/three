------------------------ MODULE TrainingVoiceLabels ------------------------
EXTENDS Integers, FiniteSets

\* Two bars, two source lines, two events per line per bar. An edge joins
\* successive events on a source line. Event identity does not include its
\* output voice number. Each subset of the six possible edges is a score.
Bars == 1..2
Lines == 1..2
Edges == (1..3) \X Lines
VARIABLES edges, up, together, hidden
Bar(i) == IF i <= 2 THEN 1 ELSE 2
Audible(i,v) == ~(hidden /\ i = 2 /\ v = 1)
Duration(i) == IF together[Bar(i)] THEN 2 ELSE 1
Onset(i,v) == Duration(i)*((i-1) % 2) +
    (IF together[Bar(i)] THEN 0 ELSE 2*(v-1))
vars == <<edges, up, together, hidden>>
Init == /\ up \in [Bars -> Lines]
        /\ together \in [Bars -> BOOLEAN]
        /\ hidden \in BOOLEAN
        /\ edges \in SUBSET {e \in Edges : e[1] # 2 \/
              (Onset(2,e[2]) + Duration(2) = 4 /\ Onset(3,e[2]) = 0)}
Spec == Init /\ [][UNCHANGED vars]_vars
Overlap(b) == \E i,j \in (2*b-1)..(2*b) :
    Audible(i,1) /\ Audible(j,2) /\ Onset(i,1) = Onset(j,2)
StemLine(b,v) == IF up[b] = v THEN 1 ELSE 2

\* Derive start/continue/end from the reference graph. The producer sees
\* per-event flags, not the oracle's edges. A hidden spacer still has flags.
Incoming(i,v) == <<i-1,v>> \in edges
Outgoing(i,v) == <<i,v>> \in edges
Flag(i,v) == IF Incoming(i,v)
             THEN IF Outgoing(i,v) THEN "continue" ELSE "end"
             ELSE IF Outgoing(i,v) THEN "start" ELSE "none"
Blocked(b,ignoreHidden) == \E v \in Lines :
    \/ (Flag(2*b-1,v) \in {"continue","end"} /\
        (~ignoreHidden \/ Audible(2*b-1,v)))
    \/ (Flag(2*b,v) \in {"start","continue"} /\
        (~ignoreHidden \/ Audible(2*b,v)))
AnyBeam(b) == \E i \in (2*b-1)..(2*b), v \in Lines : Flag(i,v) # "none"
Agree == /\ \A b \in Bars : Overlap(b)
         /\ up[1] = up[2]
Crosses == \E w \in Lines : <<2,w>> \in edges
Rename(b,mode) == Overlap(b) /\
    CASE mode = "blanket" -> ~AnyBeam(b)
      [] mode = "independent" -> TRUE
      [] mode \in {"run","guess"} -> ~Blocked(b,FALSE) \/ Agree
      [] mode = "notes-only" -> ~Blocked(b,TRUE) \/ Agree
      [] OTHER -> ~Blocked(b,FALSE)
Address(i,v,mode) ==
    IF mode = "guess" /\ Crosses /\ ~Agree /\ (\A b \in Bars : Overlap(b))
    THEN 3-v
    ELSE IF Rename(Bar(i),mode) THEN StemLine(Bar(i),v) ELSE v

\* Re-read the emitted flags on their new lines, comparing the original
\* event endpoints, not just the number of open and closed runs.
Graph(mode) == {e \in (1..3) \X Lines \X Lines :
    /\ Flag(e[1],e[2]) \in {"start","continue"}
    /\ Flag(e[1]+1,e[3]) \in {"continue","end"}
    /\ Address(e[1],e[2],mode) = Address(e[1]+1,e[3],mode)}
Reference == {<<e[1],e[2],e[2]>> : e \in edges}
Contract(mode) ==
    /\ Graph(mode) = Reference
    /\ \A b \in Bars, v \in Lines : ~Overlap(b) => Address(2*b,v,mode) = v
    /\ (Crosses /\ ~Agree) => (\A b \in Bars, v \in Lines : Address(2*b,v,mode) = v)
    /\ \A b \in Bars, v \in Lines :
        (Overlap(b) /\ (~Crosses \/ Agree))
        => Address(2*b,v,mode) = StemLine(b,v)
Safe == Contract("run")
Freeze == Contract("boundary")
Guess == Contract("guess")
Blanket == Contract("blanket")
Independent == Contract("independent")
NotesOnly == Contract("notes-only")
=============================================================================
