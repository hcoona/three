-------------------------- MODULE ScorePublication --------------------------
EXTENDS ScoreMeaning

VARIABLES first, edition, releases
observations == <<first,edition,releases>>
Manuscripts(n) == [text : Seq(WrittenEvent), gap : BOOLEAN, observation : {n}]
Pending(e) == e # Absent /\ (e.gap \/ Markers(e.text) # {})

Init == first = Absent /\ edition = Absent /\ releases = <<>>
TakeReading ==
  /\ first = Absent /\ first' # Absent
  /\ edition' \in Manuscripts(1)
  /\ edition'.text = first'.text /\ edition'.gap = (first'.mandatory # {})
  /\ UNCHANGED releases
ObserveAgain ==
  /\ edition # Absent /\ edition.gap /\ releases = <<>>
  /\ edition' \in Manuscripts(edition.observation+1)
  /\ UNCHANGED <<first,releases>>
Answer ==
  /\ edition # Absent /\ ~edition.gap
  /\ \E i \in Markers(edition.text), m \in Glyphs \cup {No} :
       edition' = [edition EXCEPT !.text = ReplaceMark(@,i,m)]
  /\ UNCHANGED <<first,releases>>
Amend ==
  /\ releases # <<>> /\ ~Pending(edition)
  /\ \E i \in DOMAIN edition.text : edition' = [edition EXCEPT !.text[i].finger = ~@]
  /\ UNCHANGED <<first,releases>>
Publish ==
  /\ edition # Absent /\ ~Pending(edition) /\ edition.text # <<>>
  /\ releases' = Append(releases,ScoreGraph(edition.text))
  /\ UNCHANGED <<first,edition>>
Next == TakeReading \/ ObserveAgain \/ Answer \/ Amend \/ Publish
Spec == Init /\ [][Next]_observations
=============================================================================
