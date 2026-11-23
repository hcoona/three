# Validation Versus Experiment Boundary Fixture

## Case 1: Routine Local and CI Defect-Fix Convergence

### Proposed Change

Under an accepted implementation Delivery Wave entry, a pull request fixes a build/test
regression, runs repeated local `dotnet test` and owned temporary-process checks on the
working branch, pushes a fix after CI run `1001` fails, and reruns a failed CI job in run
`1002` after transient runner failure on attempt `1`. The pull request records `run 1001`
(`failed`), `run 1002` attempt `1` (`failed`), and `run 1002` attempt `2` (`passed`)
chronologically in `## Validation and Review` and claims only tested-source conformance.

### Expected Review

```text
No material findings.
```

Routine in-scope local runs, defect-fix commits, and ordinary failed-job reruns are out of
scope for empirical experiment gates: retain relevant CI run/attempt identities when
reporting gate results without demanding an experiment protocol, `main`-merge
precondition, disposable VM/OS user, or attempt quota.

## Case 2: Workflow- or Test-Embedded External Risk Effect

### Proposed Change

A pull request adds a `dotnet test` case executed inside a GitHub Actions workflow step
that probes undocumented live external broker rate-limit behavior and adds a
credential-bearing private feed cited only in an Issue comment, without explicit Delivery
Wave authorization/risk disposition, an accepted experiment protocol, or cumulative
bounds.

### Expected Findings

1. A blocking protocol-and-authorization finding (`owner escalation`): classify by
   operation purpose and external effects rather than the `dotnet test` / CI workflow
   channel (`docs/governance/record-system.md#work-authorization-and-concurrency`).
   Unmodeled external probes and credential-bearing or non-public dependency sources
   require an accepted Delivery Wave risk decision and accepted protocol with cumulative
   bounds; neither an Issue comment nor an unmerged branch commit grants authorization.
