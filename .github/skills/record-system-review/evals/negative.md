# Negative Fixture

## Proposed Change

A pull request:

- adds `docs/status.md` with a second copy of the current Delivery Wave authorization;
- adds an hk rule requiring every architecture directory to contain at least two files,
  without a governing policy or failure mode;
- adds a rule in `src/private/app/example/docs/agent-handoff.md` forbidding local
  credential-free test runs on unmerged feature branches or capping local debugging at
  three runs before opening a pull request.

## Expected Findings

1. A blocking GOV-002 finding: `docs/status.md` duplicates the current authorization
   authority of `docs/delivery-wave.md`; remove it or make it a non-normative link.
2. A blocking GOV-005 finding: the directory-size rule creates policy in a control and
   contradicts the record system's granularity rule; remove the rule.
3. A blocking `record-system.md#project-namespaces` / GOV-002 finding: development
   workflow and local iteration freedom are repository-wide; a project handoff record
   may define formal acceptance-evidence criteria or external-risk bounds, not `main`-merge
   preconditions or run-count quotas on local harmless debugging.
