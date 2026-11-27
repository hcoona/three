-------------------------- MODULE ReleaseDecisions --------------------------
EXTENDS ReleaseRules

VARIABLES destination, channel, mode
vars == <<destination, channel, mode>>
Address == [registry |-> IF destination # "originals" THEN destination
                         ELSE IF channel = "buddy" THEN "preview-feed" ELSE "official-feed",
            package |-> "unit-a", version |-> "1"]
NativeFiles == CASE destination = "nuget.org" -> {"nupkg"}
                 [] destination = "chrome" -> {"chrome-zip"}
                 [] destination = "amo" -> {"firefox-zip", "firefox-sources"}
                 [] destination = "edge" -> {"edge-zip"}
                 [] OTHER -> {"wheel", "sdist"}
Request == [execution |-> "new", unit |-> "unit-a", revision |-> "commit-a",
            inputs |-> "frozen-inputs", coordinate |-> Address,
            files |-> NativeFiles,
            channel |-> channel, destination |-> destination, mode |-> mode]
Originals == [f \in NativeFiles |-> <<f, "qualified-bytes">>]
OneFile == IF "sdist" \in NativeFiles THEN "sdist" ELSE CHOOSE f \in NativeFiles : TRUE
Partial == [f \in NativeFiles \ {OneFile} |-> Originals[f]]
Extra == [f \in NativeFiles \cup {"unexpected"} |-> <<f, "qualified-bytes">>]
Preparation == [subject |-> Request, origin |-> "release", complete |-> TRUE,
                clean |-> TRUE, originals |-> Originals]
OtherSubject ==
    { [Request EXCEPT !.execution = "earlier"], [Request EXCEPT !.unit = "unit-b"],
      [Request EXCEPT !.revision = "commit-b"],
      [Request EXCEPT !.inputs = "changed-inputs"], [Request EXCEPT !.coordinate.version = "2"],
      [Request EXCEPT !.coordinate.registry = "other-feed"],
      [Request EXCEPT !.files = NativeFiles \cup {"unexpected"}],
      [Request EXCEPT !.channel = IF channel = "buddy" THEN "official" ELSE "buddy"],
      [Request EXCEPT !.mode = IF mode = "dry" THEN "live" ELSE "dry"],
      [Request EXCEPT !.destination = "other-destination"] }
Qualifies(r, p, originals) == r.files = NativeFiles /\ Qualified(r, p, r.revision, originals)
Bound(p) == Qualifies(Request, p, Originals)
Initial == IF BeforeGate(destination, mode) = "unobserved" THEN "unobserved" ELSE "missing"
SelectedRoute == Route(Request, Bound(Preparation), {channel}, Initial)
SimulationReport == [observation |-> Initial, operations |-> "hypothetical",
                     attempted |-> Attempted(SelectedRoute, "not-required", <<>>),
                     result |-> Conclusion(destination, SelectedRoute, TRUE, "not-required", "verifier", "no-proof")]
Access(bound, refs, grant, actor) ==
    PublisherAccess(Request, SelectedRoute, bound, refs, grant, actor)
MayWrite(bound, refs, grant, state, basis) ==
    CanInvoke(Request, Access(bound, refs, grant, "isolated-publisher"), state, basis)
Fresh == IF destination = "edge" THEN "unobserved" ELSE "missing"

Init == /\ destination \in Destinations /\ channel \in {"buddy", "official"}
        /\ mode \in {"dry", "live"}
Spec == Init /\ [][UNCHANGED vars]_vars

BindingLaw ==
    /\ Bound(Preparation)
    /\ \A s \in OtherSubject : ~Bound([Preparation EXCEPT !.subject = s])
    /\ \A p \in {[Preparation EXCEPT !.origin = "CI"],
                 [Preparation EXCEPT !.clean = FALSE],
                 [Preparation EXCEPT !.complete = FALSE]} : ~Bound(p)
    /\ ~Qualified(Request, Preparation, "different-control", Originals)
    /\ ~Qualified(Request, Preparation, Request.revision,
                  [Originals EXCEPT ![OneFile] = <<OneFile, "other-bytes">>])
    /\ ~Qualified(Request, [Preparation EXCEPT !.originals = Partial], Request.revision, Partial)
    /\ ~Qualifies(Request, [Preparation EXCEPT !.originals = Extra], Extra)
    /\ LET expanded == [Request EXCEPT !.files = DOMAIN Extra]
       IN ~Qualifies(expanded, [Preparation EXCEPT !.subject = expanded, !.originals = Extra], Extra)
    /\ Route(Request, FALSE, {channel}, Initial) = "blocked"
    /\ Route([Request EXCEPT !.destination = "not-admitted"], TRUE, {channel}, Initial) = "blocked"

RecoveryBinding ==
    LET old == [Request EXCEPT !.execution = "earlier"]
        rebuilt == [Originals EXCEPT ![OneFile] = <<OneFile, "different-bytes">>]
    IN  /\ (FreshRecoveryOf(old, Request, "manual-dispatch", TRUE) <=> mode = "live")
        /\ ~FreshRecoveryOf(old, Request, "manual-dispatch", rebuilt = Originals)
        /\ ~FreshRecoveryOf(Request, Request, "manual-dispatch", TRUE)
        /\ ~FreshRecoveryOf(old, Request, "job-rerun", TRUE)
        /\ ~Bound([Preparation EXCEPT !.subject = old])
        /\ \A s \in OtherSubject \ {old} : ~FreshRecoveryOf(old, s, "manual-dispatch", TRUE)

JourneyLaw ==
    /\ (SelectedRoute = "simulation" <=>
         (mode = "dry" /\ (destination # "nuget.org" \/ channel = "official")))
    /\ (MayWrite(TRUE, {channel}, TRUE, Fresh, "first") <=>
         (mode = "live" /\ (channel = "official" \/ destination = "originals")))
    /\ (mode = "live" => Route(Request, TRUE, {}, Initial) = "blocked")
    /\ (SelectedRoute = "simulation" => Route(Request, TRUE, {}, Initial) = "simulation")
    /\ ~Access(TRUE, {channel}, TRUE, "build-or-CI")
    /\ ~Access(TRUE, {channel}, TRUE, "read-only-verifier")
    /\ (mode = "dry" => ~Access(TRUE, {channel}, TRUE, "isolated-publisher"))
    /\ ~MayWrite(TRUE, {}, TRUE, Fresh, "first")
    /\ ~MayWrite(TRUE, {channel}, FALSE, Fresh, "first")
    /\ ~MayWrite(FALSE, {channel}, TRUE, Fresh, "first")
    /\ ~MayWrite(TRUE, {channel}, TRUE, "unknown", "first")
    /\ ~MayWrite(TRUE, {channel}, TRUE, Proof(destination), "first")

ReadBoundary ==
    /\ (BeforeGate(destination, mode) = "unobserved" <=>
         (destination \in {"amo", "edge"} \/ (mode = "dry" /\ destination = "chrome")))
    /\ ((destination \in Stores /\ mode = "dry") =>
           /\ SimulationReport = [observation |-> "unobserved", operations |-> "hypothetical",
                                   attempted |-> "no", result |-> "simulation"]
           /\ \A supplied \in {"missing", Proof(destination)} :
                  Route(Request, TRUE, {}, supplied) = "blocked")
    /\ (mode = "live" /\ channel = "official" =>
        /\ (Route(Request, TRUE, {channel}, Proof(destination)) = "verify-without-write"
            <=> destination \notin {"amo", "edge"})
        /\ SelectedRoute = "publisher"
        /\ (MayWrite(TRUE, {channel}, TRUE, "unobserved", "owner-checked")
            <=> destination = "edge")
        /\ ~MayWrite(TRUE, {channel}, TRUE, "unobserved", "unresolved")
        /\ \A s \in {"conflict", "unsupported-subset"} :
               /\ Route(Request, TRUE, {channel}, s) = "blocked"
               /\ ~MayWrite(TRUE, {channel}, TRUE, s, "first")
        /\ Route(Request, TRUE, {channel}, "unknown") \in {"blocked", "unresolved"}
        /\ (destination = "amo" => ~MayWrite(TRUE, {channel}, TRUE, "unobserved", "first")))

EvidenceLaw ==
    \A coord, metadata, bytes, retained \in BOOLEAN, actor \in {"publisher", "verifier"},
       status \in {"available", "uploaded", "received", "in-review", "published", "unknown"} :
      LET proof == PositiveProof(destination, coord, metadata, bytes, status)
          result(route, receipt) == Conclusion(destination, route, retained, receipt, actor, proof)
          authoritative == ((actor = "publisher") <=> destination \in {"amo", "edge"})
          sufficient == CASE destination = "originals" -> metadata /\ bytes
                          [] destination = "nuget.org" -> status = "available"
                          [] OTHER -> status \in {"received", "in-review", "published"}
          exactRoute == Route(Request, TRUE, {channel}, Proof(destination))
      IN  /\ (result(SelectedRoute, "present") = "satisfied" <=>
               (SelectedRoute = "publisher" /\ retained /\ coord /\ authoritative /\ sufficient))
          /\ result(SelectedRoute, "lost") # "satisfied"
          /\ (result(exactRoute, "not-required") = "satisfied" <=>
               (exactRoute = "verify-without-write" /\ retained /\ coord /\ authoritative /\ sufficient))
          /\ (SelectedRoute = "simulation" /\ retained => result(SelectedRoute, "present") = "simulation")
=============================================================================
