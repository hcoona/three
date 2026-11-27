---------------------------- MODULE ReleaseRules ----------------------------
\* "originals" stands for an admitted, channel-specific, served-byte route.
Destinations == {"originals", "nuget.org", "chrome", "amo", "edge"}
Stores == {"chrome", "amo", "edge"}
PublisherEvidence(d) == d \in {"amo", "edge"}
Proof(d) == CASE d = "nuget.org" -> "version-available"
              [] d \in Stores    -> "version-received"
              [] OTHER           -> "served-originals"
Reader(d) == IF PublisherEvidence(d) THEN "publisher" ELSE "verifier"
BeforeGate(d, mode) ==
    IF PublisherEvidence(d) \/ (d \in Stores /\ mode = "dry")
    THEN "unobserved" ELSE "read-only"

\* Observations are for the exact native coordinate. Only a positive result
\* supplies proof; an absence, conflict or failed read needs its own diagnosis.
PositiveProof(d, coordinateMatches, metadataMatches, allBytesMatch, status) ==
    IF coordinateMatches /\
       CASE d = "nuget.org" -> status = "available"
         [] d \in Stores    -> status \in {"received", "in-review", "published"}
         [] OTHER           -> metadataMatches /\ allBytesMatch
    THEN Proof(d) ELSE "no-proof"

\* The request includes the logical execution identity. This relation requires
\* the binding, not an application-issued certificate or a particular encoding.
Qualified(request, preparation, control, originals) ==
    /\ preparation.subject = request /\ control = request.revision
    /\ preparation.origin = "release" /\ preparation.complete /\ preparation.clean
    /\ DOMAIN preparation.originals = request.files
    /\ request.files # {} /\ originals = preparation.originals

\* Recovery relies on supported reproducibility of the whole set, including
\* files not yet at the destination. This is not a requirement to find an old
\* run or to materialize its artifacts for comparison.
FreshRecoveryOf(old, new, trigger, reproducesSet) ==
    /\ trigger = "manual-dispatch" /\ new.mode = "live" /\ old.execution # new.execution
    /\ [old EXCEPT !.execution = new.execution] = new /\ reproducesSet

Admitted(r) ==
    /\ r.destination \in Destinations /\ r.channel \in {"buddy", "official"} /\ r.mode \in {"dry", "live"}
    /\ (r.destination = "originals" \/ r.channel = "official" \/ (r.destination \in Stores /\ r.mode = "dry"))

Route(r, qualified, eligibleChannels, initial) ==
    IF ~qualified \/ ~Admitted(r) THEN "blocked"
    ELSE IF r.mode = "dry" THEN
        IF r.destination \in Stores /\ initial # "unobserved" THEN "blocked" ELSE "simulation"
    ELSE IF r.channel \notin eligibleChannels THEN "blocked"
    ELSE IF PublisherEvidence(r.destination) THEN
        IF initial = "unobserved" THEN "publisher" ELSE "blocked"
    ELSE CASE initial = Proof(r.destination) -> "verify-without-write"
           [] initial = "missing" -> "publisher"
           [] initial \in {"unknown", "unobserved"} -> "unresolved"
           [] OTHER -> "blocked"

PublisherAccess(r, route, bound, eligibleChannels, nativeGrant, actor) ==
    /\ route = "publisher" /\ r.mode = "live" /\ Admitted(r)
    /\ bound /\ r.channel \in eligibleChannels /\ nativeGrant /\ actor = "isolated-publisher"

\* "missing" denotes a supported creation/verified subset, not an unreadable
\* or unsupported state. The execution must also respect its operation ceiling.
CanInvoke(r, access, fresh, edgeBasis) ==
    /\ access
    /\ IF r.destination = "edge"
          THEN fresh = "unobserved" /\ edgeBasis \in {"first", "owner-checked"}
          ELSE fresh = "missing"

\* receipt is about this request's publisher; a known non-start is native
\* evidence, not something inferred from a missing result.
Conclusion(d, route, retained, receipt, reader, final) ==
    IF route = "blocked" THEN "unsatisfied"
    ELSE IF ~retained THEN "unknown"
    ELSE IF route = "simulation" THEN "simulation"
    ELSE IF d = "originals" /\ reader = Reader(d) /\ final = "conflict" THEN "unsatisfied"
    ELSE IF route = "unresolved" THEN "unknown"
    ELSE IF route = "publisher" /\ receipt # "present" THEN
        IF receipt = "known-nonstart" THEN "unsatisfied" ELSE "unknown"
    ELSE IF reader # Reader(d) THEN "unknown"
    ELSE CASE final = Proof(d) -> "satisfied"
           [] final \in {"conflict", "established-incomplete"} -> "unsatisfied"
           [] OTHER -> "unknown"

Attempted(route, receipt, commands) ==
    IF route # "publisher" THEN "no"
    ELSE CASE receipt = "known-nonstart" -> "no"
           [] receipt = "present" -> IF commands = <<>> THEN "no" ELSE "yes"
           [] OTHER -> "unknown"
=============================================================================
