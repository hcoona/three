------------------------ MODULE PublicationWindowOracle ------------------------
\* Drive the independent specification with the same bounded port responses as
\* the compiled Release caller. Never take its choice of commands as an input.
EXTENDS PublicationWindow, TLC

Cases == {
    <<"both",    "mm", "mm", "ack",  "keep", "ee">>,
    <<"subset",  "mm", "em", "ack",  "keep", "ee">>,
    <<"swap",    "em", "me", "ack",  "keep", "me">>,
    <<"settled", "mm", "ee", "ack",  "keep", "ee">>,
    <<"unread",  "mm", "mu", "ack",  "keep", "mu">>,
    <<"timeout", "mm", "mm", "timeout", "keep", "mm">>,
    <<"lag",     "mm", "mm", "ack",  "keep", "em">>,
    <<"lost",    "mm", "mm", "ack",  "drop", "ee">>,
    <<"clash",   "mm", "mm", "ack",  "drop", "cu">>,
    <<"no-gate", "ee", "ee", "ack",  "keep", "ee">>
}
VARIABLE example
windowVars == <<vars, example>>
Cell(c) == CASE c = "m" -> "missing" [] c = "e" -> "exact"
             [] c = "c" -> "conflict" [] c = "u" -> "unknown"
ReadPair(word) == [op \in Ops |-> LET i == IF op = "wheel" THEN 1 ELSE 2
                                  IN Cell(SubSeq(word, i, i))]
Start == /\ example \in Cases /\ Init /\ before = ReadPair(example[2])
Step ==
    /\ UNCHANGED example
    /\ \/ Grant(ReadPair(example[3]))
       \/ /\ phase = "write"
          /\ Invoke(IF example[4] = "timeout" /\ FirstRemaining = "wheel"
                    THEN "ambiguous" ELSE "acknowledged")
       \/ /\ phase = "verify" /\ receipt = "present" /\ example[5] = "drop"
          /\ LoseResult
       \/ /\ (example[5] # "drop" \/ receipt = "lost")
          /\ Verify(ReadPair(example[6]))
       \/ (phase = "done" /\ UNCHANGED vars)
OracleSpec == Start /\ [][Step]_windowVars /\ WF_windowVars(Step)
Terminates == <>(phase = "done")

RECURSIVE WriteList(_)
WriteList(log) == IF log = <<>> THEN ""
                  ELSE log[1][1] \o (IF Len(log) = 1 THEN "" ELSE "," \o WriteList(Tail(log)))
Export == phase # "done" \/
    PrintT("WD-WINDOW|" \o example[1] \o "|" \o example[2] \o "|" \o example[3] \o "|"
           \o example[4] \o "|" \o example[5] \o "|" \o example[6] \o "|"
           \o WriteList(commands) \o "|" \o WriteList(Returned) \o "|"
           \o Report.satisfaction \o "|" \o Report.attempted)
=============================================================================
