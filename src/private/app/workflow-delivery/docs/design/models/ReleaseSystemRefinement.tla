---------------------- MODULE ReleaseSystemRefinement ----------------------
\* Formal refinement bridge proving that the concrete-noun-free component models
\* (DeliveryPortAlgebra + ReleaseComponentProtocol) refine #1948's ReleaseRules
\* across all five named destination profiles AND temporally refine
\* PublicationWindow (PW!Spec) across both multi-step release surfaces.
EXTENDS ReleaseComponentProtocol

CONSTANT PwSurface
ASSUME PwSurface \in {"python", "github-release"}

RR == INSTANCE ReleaseRules

\* Map #1948's five concrete destination names to points in BoundedValidProfiles.
ProfileOfNamed(d) ==
    CASE d = "nuget.org" -> VersionOnlyFeed
      [] d = "chrome"    -> LiveReadStoreSubset
      [] d = "amo"       -> GatedReadBundledStore
      [] d = "edge"      -> BlindReceiptStore
      [] OTHER           -> ByteOrderedPair

\* Startup proof: compare DeliveryPortAlgebra's decision, access, recovery,
\* proof, conclusion, and attempt operators against ReleaseRules across all five
\* concrete destinations. Note the one intentional pre-gate refinement: when
\* d = "edge" and clearance = "unresolved", SelectRoute halts BEFORE the gate as
\* "unresolved" (whereas ReleaseRules!Route lacked a clearance input and halted
\* inside CanInvoke), yielding the same terminal ("unknown", attempted = "no").
ReleaseRulesRefinedByPortAlgebra ==
    \A d \in RR!Destinations, ch \in Channels, m \in Modes, clr \in Clearances :
      LET p   == ProfileOfNamed(d)
          req == [destination |-> d, channel |-> ch, mode |-> m,
                  clearance   |-> clr,
                  execution   |-> "e1", revision |-> "c1",
                  files       |-> TargetArtifactsOf(p)]
          req2 == [req EXCEPT !.execution = "e2"]
      IN  /\ IsValidRightProfile(p, UnitArtifacts)
          /\ (AdmittedRequest(p, req) <=> RR!Admitted(req))
          /\ (ProofToken(p.proofMode) = RR!Proof(d))
          /\ (CompletionReader(p) = RR!Reader(d))
          /\ (BeforeGateMode(p, m) = RR!BeforeGate(d, m))
          /\ \A trig \in {"manual-dispatch", "job-rerun"}, rep \in BOOLEAN :
               (clr = req.clearance) =>
                 (ValidRecoveryDispatch(req, req2, trig, rep) <=>
                    RR!FreshRecoveryOf(req, req2, trig, rep))
          /\ \A qual \in BOOLEAN, elig \in SUBSET Channels,
                init \in {ProofToken(p.proofMode), "missing", "unknown",
                          "unobserved", "conflict", "unsupported-subset"} :
               IF d = "edge" /\ m = "live" /\ qual /\ AdmittedRequest(p, req) /\
                  ch \in elig /\ init = "unobserved" /\ clr = "unresolved"
               THEN /\ SelectRoute(p, req, qual, elig, init) = "unresolved"
                    /\ EvaluateConclusion(p, "unresolved", TRUE, "not-required",
                                          "publisher", "no-proof") = "unknown"
                    /\ EvaluateAttempted("unresolved", "not-required", <<>>) = "no"
               ELSE SelectRoute(p, req, qual, elig, init) = RR!Route(req, qual, elig, init)
          /\ \A rt \in {"blocked", "simulation", "verify-without-write", "publisher", "unresolved"},
                boundOk, grant \in BOOLEAN, elig \in SUBSET Channels :
               HasPublisherAccess(p, req, rt, boundOk, elig, grant,
                                  TRUE, "isolated-pub-primary") <=>
                 RR!PublisherAccess(req, rt, boundOk, elig, grant, "isolated-publisher")
          /\ \A access \in BOOLEAN,
                fresh \in {"missing", "unobserved", "conflict", "unknown",
                           "unsupported-subset", ProofToken(p.proofMode)} :
               MayMutatePostGate(p, access, fresh, clr) = RR!CanInvoke(req, access, fresh, clr)
          /\ \A coord, meta, bytes, ret \in BOOLEAN,
                actor \in {"publisher", "verifier"},
                st \in {"available", "uploaded", "received", "in-review", "published", "unknown"},
                rt \in {"blocked", "simulation", "verify-without-write", "publisher", "unresolved"},
                rec \in {"not-required", "present", "lost", "known-nonstart"},
                cmds \in {<<>>, <<<<"op1", "acknowledged">>>>},
                fin \in {ProofToken(p.proofMode), "no-proof", "conflict",
                         "established-incomplete", "unknown"} :
               /\ EvaluatePositiveProof(p.proofMode, coord, meta, bytes, st) =
                    RR!PositiveProof(d, coord, meta, bytes, st)
               /\ EvaluateConclusion(p, rt, ret, rec, actor, fin) =
                    RR!Conclusion(d, rt, ret, rec, actor, fin)
               /\ EvaluateAttempted(rt, rec, cmds) =
                    RR!Attempted(rt, rec, cmds)

ASSUME ReleaseRulesRefinedByPortAlgebra

\* ---------------------------------------------------------------------------
\* Temporal refinement mapping to PublicationWindow (#1948) via sequence index
\* ---------------------------------------------------------------------------
PwOrder ==
    IF PwSurface = "python" THEN <<"wheel", "sdist">>
    ELSE <<"create-release", "installer", "portable">>

PwOps == {PwOrder[i] : i \in 1..Len(PwOrder)}

AbstractOrder == profile.ops

IndexOfAbstractOp(op) ==
    CHOOSE i \in 1..Len(AbstractOrder) : AbstractOrder[i] = op

ToPwOp(op) == PwOrder[IndexOfAbstractOp(op)]

ToPwRead(obs) ==
    IF IsUnobservedMap(profile, obs) THEN [w \in PwOps |-> "unknown"]
    ELSE [w \in PwOps |->
            LET i == CHOOSE k \in 1..Len(PwOrder) : PwOrder[k] = w
                v == obs[AbstractOrder[i]]
            IN  IF v = "established" THEN "exact" ELSE v]

ToPwPending == {ToPwOp(op) : op \in pending}

ToPwCommands(cmds) ==
    [k \in 1..Len(cmds) |-> <<ToPwOp(cmds[k][1]), cmds[k][2]>>]

ToPwReport(rep) ==
    [execution         |-> rep.execution,
     initial           |-> ToPwRead(rep.initial),
     observed          |-> ToPwRead(rep.observed),
     publisherRead     |-> ToPwRead(rep.publisherRead),
     publisherRecord   |-> rep.publisherRecord,
     satisfaction      |-> rep.satisfaction,
     attempted         |-> rep.attempted,
     outcomesAvailable |-> rep.outcomesAvailable,
     outcomes          |-> ToPwCommands(rep.outcomes),
     uncertainCalls    |-> {ToPwOp(op) : op \in rep.uncertainCalls}]

PwPhase ==
    CASE ctx = "done" -> "done"
      [] ctx = "unpriv" ->
           IF RouteEnv.route = "publisher" THEN "gate" ELSE "verify"
      [] ctx = "isolated-pub-primary" /\ pubSubphase = "pub-write" -> "write"
      [] OTHER -> "verify"

PwFinalRead ==
    IF ctx = "done" THEN ToPwRead(verifyObs)
    ELSE [w \in PwOps |-> "unknown"]

PwPrevious == [k \in 1..Len(history) |-> ToPwReport(history[k])]

PW == INSTANCE PublicationWindow WITH
    Surface   <- PwSurface,
    before    <- ToPwRead(preObs),
    refreshed <- ToPwRead(pubRefreshed),
    phase     <- PwPhase,
    pending   <- ToPwPending,
    commands  <- ToPwCommands(pubCommands),
    receipt   <- pubReceiptStatus,
    finalRead <- PwFinalRead,
    previous  <- PwPrevious

PWSpecRefinement == PW!Spec

RefinesPublicationWindow ==
    /\ PW!WriteDiscipline
    /\ PW!EvidenceDiscipline
    /\ PW!HistoryDiscipline
    /\ (ctx = "done" => ToPwReport(CurrentReport) = PW!Report)
=============================================================================
