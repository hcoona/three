# Negative Fixture

## Proposed Change

A pull request:

- adds `docs/status.md` with a second copy of the current Delivery Wave authorization;
- adds an hk rule requiring every architecture directory to contain at least two files,
  without a governing policy or failure mode.

## Expected Findings

1. A blocking GOV-002 finding: `docs/status.md` duplicates the current authorization
   authority of `docs/delivery-wave.md`; remove it or make it a non-normative link.
2. A blocking GOV-005 finding: the directory-size rule creates policy in a control and
   contradicts the record system's granularity rule; remove the rule.
