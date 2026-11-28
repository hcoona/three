----------------------------- MODULE CiJoinOracle -----------------------------
\* At the verify boundary every planned group is present once unless the
\* chosen fault changes it. This is the component specification's verdict,
\* not a second receipt validator.
EXTENDS Sequences, TLC
VARIABLE anomaly
CI == INSTANCE CiComponentComposition WITH ctx <- "verify", mode <- "selective",
    planAnomaly <- "none", directNodes <- {"former"}, focus <- <<"library", "native">>,
    fault <- anomaly
Start == anomaly \in CI!BoundaryFaults
Idle == UNCHANGED anomaly
OracleSpec == Start /\ [][Idle]_anomaly
Export == PrintT("WD-CI|" \o anomaly \o "|" \o IF CI!Verdict THEN "pass" ELSE "reject")
=============================================================================
