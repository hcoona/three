----------------------------- MODULE Evaluation -----------------------------
EXTENDS OMR, ReviewEvidence
VARIABLES mask, sealed, audit, retracted
bench == <<mask,sealed,audit,retracted>>
allVars == <<vars,bench>>

AttachInit == Init /\ mask \in Masks(scene) /\ sealed = Absent /\ audit = Absent /\ retracted = {}
FreezeFlags ==
  /\ job.first # Absent /\ sealed = Absent
  /\ sealed' = BlindReport(job.first,Draft(scene),DevRisk)
  /\ UNCHANGED <<vars,mask,audit,retracted>>
Reveal ==
  /\ sealed # Absent /\ audit = Absent
  /\ audit' = ScoreReport(scene,sealed.p0,sealed.draft,sealed.flags,mask)
  /\ UNCHANGED <<vars,mask,sealed,retracted>>
Retract ==
  /\ sealed # Absent
  /\ \E j \in sealed.flags \ retracted : retracted' = retracted \cup {j}
  /\ UNCHANGED <<vars,mask,sealed,audit>>
\* Every product action lifts without asking the observer. The observer never writes the product.
AttachNext == (Next /\ UNCHANGED bench) \/ FreezeFlags \/ Reveal \/ Retract
Attached == AttachInit /\ [][AttachNext]_allVars
ReviewsBound == IF sealed = Absent THEN retracted = {} ELSE retracted \subseteq sealed.flags
EvidenceHistory == [][
  /\ IF sealed # Absent THEN sealed' = sealed ELSE sealed' # Absent =>
       job.first # Absent /\ sealed' = BlindReport(job.first,Draft(scene),DevRisk)
  /\ IF audit # Absent THEN audit' = audit ELSE audit' # Absent =>
       sealed # Absent /\ audit' = ScoreReport(scene,job.first,sealed.draft,sealed.flags,mask)]_allVars

\* Deliberately false: first finish a repaired score with NO subscriber,
\* then let a late subscriber freeze flags and grade the original, incomplete reading.
NoLateAssessment ==
  []((job.phase = "editable" /\ job.kind = "clean" /\ job.again /\ job.answer # Absent /\ sealed = Absent /\
      job.first.mandatory # {} /\ Markers(Join(job.first.docs)) # {}) => [](audit = Absent))
=============================================================================
