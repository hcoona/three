------------------------- MODULE VerifierReadGateOracle -------------------------
\* The initial gem is missing. Enumerate sealed receipts from the port algebra,
\* not from the publisher or its optional diagnostic readback.
EXTENDS DeliveryPortAlgebra, TLC
VARIABLE receipt
P == SingleOpProfile({"official"}, Channels, "unpriv-always", "served-bytes", "gem", {"gem"})
Req == [mode |-> "live", channel |-> "official", clearance |-> "first"]
Target == [bound |-> TRUE, subject |-> Req, coordinate |-> "gem@1"]
Initial == BlankObs(P, "missing")
Route == SelectRoute(P, Req, TRUE, P.liveChannels, AggregateObsState(P, Initial))
Envelope == [subject |-> Req, coordinate |-> Target.coordinate, route |-> Route,
             initialObs |-> Initial, reviewed |-> ReviewedCeiling(P, Route, Initial)]
Start == receipt \in SealedPubReceiptsFromPool(P, Req, Target, Envelope, ValidObsMaps(P))
Idle == UNCHANGED receipt
OracleSpec == Start /\ [][Idle]_receipt
Export ==
    LET fresh == receipt.refreshed["gem"]
        outcome == IF receipt.commands = <<>> THEN "none" ELSE receipt.commands[1][2]
        allow == PrimaryPermitsAnchor(P, Req, Target, Envelope, receipt)
    IN PrintT("WD-GATE|" \o fresh \o "-" \o outcome \o "|" \o fresh \o "|" \o outcome
              \o "|" \o IF allow THEN "allow" ELSE "deny")
=============================================================================
