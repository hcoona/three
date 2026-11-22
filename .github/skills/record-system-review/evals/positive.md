# Positive Fixture

## Case 1: Record Relocation With Consumer Migration

### Proposed Change

A pull request moves a security architecture record into `src/public/lib/example/docs/security/`, updates its
only current links, updates the current record-family path, deletes the replaced path, and
passes all blocking hk checks. The concern, producer, maintainer, consumers, and review
point are unchanged.

### Expected Review

```text
No material findings.
```

## Case 2: Routing Summary, Ordered Batch, and Routine Non-Triggers

### Proposed Change

1. An authorized implementation pull request modifies only in-place `.cs` source, test,
   non-control product workflow steps, and a CLI flag description in an existing project
   `README.md` (preserving record ownership, portal routing, requirement identifiers, and
   consumers, without altering `repository`-namespace records, governance catalogs,
   generated interfaces, `apm.lock.yaml`, or control-governed workflow/hook surfaces, or
   adding/renaming/deleting tracked paths). It runs `records:check` for the `README.md`
   edit and retains full HK, checker, test, and `GOV-011` triage details in the pull
   request's `## Validation and Review` section.
2. A companion Issue progress comment—reviewed together with the pull-request body update
   as a declared two-step publication batch—summarizes the outcome and links to the pull
   request's `## Validation and Review` ledger without repeating secondary HK
   path-exclusion caveats, making no broader claim.

### Expected Review

```text
No material findings.
```

Routine source, test, non-control workflow, and ownership-preserving in-place project
documentation updates do not require independent `record-system-review`, the concise Issue
summary obeys `GOV-002` single-ledger routing, and the ordered batch is evaluated as one
logical update rather than rejected before step 2 executes.
