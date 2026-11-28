-------------------------------- MODULE OMR --------------------------------
EXTENDS ScoreObservations
CONSTANT Cases
ASSUME Cases \subseteq Scenes
VARIABLES scene, job
vars == <<scene,job>>

ExternalXML == [notes |-> [i \in 1..Beats |->
                    [display |-> IF i = 1 THEN "b" ELSE No, alter |-> "b", tag |-> No, finger |-> FALSE]],
                spans |-> {}, foreign |-> FALSE]
ForeignText == Report(<<Doc(Initial,[anchor |-> Unanchored,body |-> <<Plain>>],B(1,No,"lyrics",FALSE))>>,1)
Init ==
  /\ scene \in Cases
  /\ job = [phase |-> CASE scene = "native" -> "exchange" [] scene = "foreignText" -> "reading" [] OTHER -> "new",
            first |-> Absent, reading |-> IF scene = "foreignText" THEN ForeignText ELSE Absent,
            again |-> FALSE, answer |-> Absent, native |-> IF scene = "native" THEN ExternalXML ELSE Absent,
            kind |-> "clean", result |-> Absent]

Observe ==
  /\ job.phase = "new"
  /\ \E p \in Readings(Cues(scene)) : job' = [job EXCEPT !.phase = "reading", !.first = p, !.reading = p]
  /\ UNCHANGED scene
ObserveAgain ==
  /\ job.phase = "reading" /\ job.reading.mandatory # {} /\ ~job.again
  /\ \E cues \in SecondCues(scene) : \E p \in Readings(cues) :
       job' = [job EXCEPT !.reading = p, !.again = TRUE]
  /\ UNCHANGED scene
Text == Join(job.reading.docs)
Resolve(m) ==
  /\ job.phase = "reading" /\ job.reading.mandatory = {} /\ Markers(Text) # {}
  /\ job' = [job EXCEPT !.answer = [source |-> Text, mark |-> m],
             !.reading.docs = Rebind(Corrected(@,m),Initial)]
  /\ UNCHANGED scene
Hold ==
  /\ job.phase = "reading" /\ Needs(job.reading) # NoNeeds
  /\ job' = [job EXCEPT !.phase = "held"] /\ UNCHANGED scene
CanExport == job.phase = "reading" /\ Complete(job.reading)
RejectText ==
  /\ job.phase = "reading" /\ Needs(job.reading) = NoNeeds /\ ~Admits(job.reading.docs,TRUE)
  /\ job' = [job EXCEPT !.phase = "unsupportedText"] /\ UNCHANGED scene
Export ==
  /\ CanExport
  /\ job' = [job EXCEPT !.phase = "exported", !.native = Forward(Text)]
  /\ UNCHANGED scene
NativeInput(x,k) ==
  CASE k = "clean" -> x
    [] k = "edit" -> [x EXCEPT !.notes[1].finger = ~@]
    [] k = "foreign" -> [x EXCEPT !.foreign = TRUE]
    [] OTHER -> [x EXCEPT !.notes[2].alter = IF @ = "#" THEN "b" ELSE "#"]
OfferNative ==
  /\ job.phase \in {"exchange","exported"}
  /\ \E k \in {"clean","edit","foreign","inconsistent"} : job' = [job EXCEPT !.phase = "import", !.kind = k]
  /\ UNCHANGED scene
Reverse ==
  /\ job.phase = "import"
  /\ LET x == NativeInput(job.native,job.kind)
     IN job' = IF Importable(x) THEN [job EXCEPT !.phase = "editable", !.result = DecodeXML(x)]
               ELSE [job EXCEPT !.phase = "unsupported"]
  /\ UNCHANGED scene
Completed == job.phase \in {"editable","held","unsupported","unsupportedText"}
ProductStep == Observe \/ ObserveAgain \/ (\E m \in Glyphs \cup {No} : Resolve(m))
               \/ Hold \/ RejectText \/ Export \/ OfferNative \/ Reverse
Next == ProductStep \/ (Completed /\ UNCHANGED vars)
Spec == Init /\ [][Next]_vars
Convergence == <>[][FALSE]_vars
FrozenFirst == [][job.first # Absent => job.first' = job.first]_vars

\* Input receipt and delivered graphs come from opposite sides of the editor.
FirstReading == IF job.first = Absent THEN Absent
                ELSE [text |-> Join(job.first.docs), mandatory |-> job.first.mandatory]
WorkingEdition ==
  IF job.first = Absent THEN Absent
  ELSE LET t == IF job.answer = Absent THEN Text ELSE LET a == job.answer IN
                  ReplaceMark(a.source,CHOOSE i \in Markers(a.source) : TRUE,a.mark)
       IN [text |-> IF job.kind = "edit" THEN [t EXCEPT ![1].finger = ~@] ELSE t,
           observation |-> IF job.again THEN 2 ELSE 1, gap |-> job.reading.mandatory # {}]
Issued ==
  LET out == IF job.phase \in {"exported","import","editable","unsupported"}
             THEN <<IF job.native = Absent THEN Absent ELSE NativeGraph(job.native)>> ELSE <<>>
  IN IF job.phase = "editable" THEN Append(out,IF job.result = Absent THEN Absent ELSE ScoreGraph(job.result)) ELSE out
Publication == INSTANCE ScorePublication WITH first <- FirstReading, edition <- WorkingEdition, releases <- Issued
PublicationRefinement == Publication!Spec
\* Export is enabled exactly at CanExport and disables it. Rebinding is now part of accepting the answer.
AnsweredProgress == ([]<>(~CanExport)) => ((job.answer # Absent) ~> (Issued # <<>>))
Interchange ==
  /\ (job.phase = "editable" => LET x == NativeInput(job.native,job.kind) IN
         NativeGraph(Forward(job.result)) = NativeGraph(x) /\ ~x.foreign /\ Markers(job.result) = {})
  /\ (job.phase \in {"editable","unsupported"} => ((job.phase = "editable") <=> (job.kind \in {"clean","edit"})))
  /\ (job.phase = "unsupported" => job.result = Absent)
  /\ (scene = "foreignText" => job.native = Absent /\ job.result = Absent)
=============================================================================
