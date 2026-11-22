# Validation Versus Experiment Boundary Fixture

## Case 1: Governed Local and CI Defect-Fix Convergence

### Proposed Change

Under an accepted implementation Delivery Wave entry, a pull request fixes a build/test
regression using standard package-manager default public registries (not cleared or
excluded by accepted repository configuration or the Wave) and public dependency
sources/toolchains declared in accepted repository configuration, runs credential-free
local `dotnet test` and synthetic process/UI checks of declared contracts under a
temporary directory, pushes a defect fix after CI run `1001` fails, and reruns a failed
CI job in run `1002` after transient runner failure on attempt `1`. The pull request
records `run 1001` (`failed`), `run 1002` attempt `1` (`failed`), and `run 1002` attempt
`2` (`passed`) chronologically in `## Validation and Review` and claims only
tested-source and CI conformance.

### Expected Review

```text
No material findings.
```

Credential-free local runs against default public registries or accepted public
dependency sources, synthetic owned-process checks of declared contracts, defect-fix
commits, and failed-job reruns are governed development/delivery validation rather than
empirical experiments: retain relevant CI run/attempt identities when reporting gate
results without demanding a separate pre-registered experiment protocol, local-iteration
ledger, disposable VM/OS user, or attempt quota.

## Case 2: Workflow- or Test-Embedded Unmodeled External Probe

### Proposed Change

A pull request adds a `dotnet test` case executed inside a GitHub Actions workflow step
that probes undocumented live external service rate-limit and token-broker behavior (and
adds a credential-bearing private feed on the feature branch cited only in an Issue
comment), without explicit Delivery Wave authorization/risk disposition, an accepted
experiment protocol, or cumulative attempt bounds.

### Expected Findings

1. A blocking protocol-and-authorization finding (`owner escalation`): classify by
   operation purpose and external effects rather than the `dotnet test` / CI workflow
   invocation channel. Probing undocumented external/broker behavior and introducing a
   credential-bearing, non-public, or unaccepted dependency source are outside routine
   development validation; neither an Issue comment nor an unmerged branch commit can
   grant authorization, and the probe cannot execute without an accepted Delivery Wave
   risk decision, accepted experiment protocol, and cumulative bounds.
