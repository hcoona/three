# Negative Fixture

## Proposed Change

A research note states that WAM works in every WSL environment because one interactive
login succeeded. It records the tester's corporate user principal name and includes no
host version, account-state/effects boundary, expected observations, stop conditions,
or cleanup/retention.

## Expected Findings

1. A blocking claim-boundary finding: one runtime observation cannot establish universal
   WSL support; narrow the statement to the recorded environment and evidence type.
2. A blocking safety finding: remove the private account identifier and replace the
   evidence with a sanitized observation.
3. A blocking reproducibility finding: require an accepted Delivery Wave entry and
   protocol with the environment, state/effects, finite bounds, stop, and cleanup/retention information
   before relying on the result.

## Attempt Accounting and Operator Evidence

A protocol amendment moves the probe to another owner-designated machine, resets its
three-attempt limit despite two prior failed starts, and accepts an unrecorded manual
login as proof that first-use OS-account reuse works. The prior state is unknown.

Expected findings: preserve the two consumed attempts across the machine switch; require
the manual observation to follow the accepted protocol and carry sanitized provenance;
and reject the clean-first-use conclusion. Existing-machine authorization does not waive
these obligations.
