# Quality, Build, and Validation Requirements

## V2-REQ-050: Retired - current authority: docs/governance/project.md#public-record-boundary

Public-source evidence policy is governed by the project record. Evidence required for a
support claim is governed by the validation strategy.

## V2-REQ-051: Retired - current authority: docs/validation/strategy.md#real-environment-tests

Real-platform support evidence is a validation obligation rather than product behavior.

## V2-REQ-052: Retired - current authority: docs/validation/strategy.md#dependency-upgrade-matrix

Dependency-upgrade isolation is an engineering and validation practice rather than
product behavior.

## V2-REQ-053: Public Build Chain

V2 implementation and release builds must restore, build, test, and package from publicly
retrievable dependencies and fork-owned infrastructure. They must not require Microsoft
private feeds, private service connections, or upstream signing systems.

## V2-REQ-054: Retired - current authority: docs/research/experiment-safety.md#experiment-authorization

Experiment authorization and isolation are governed by the experiment-safety policy.
The validation strategy governs the evidence needed for support claims.

## V2-REQ-055: Native AOT Publishing

Production .NET 10 executables must use Native AOT for each accepted runtime identifier
unless the corresponding design accepts a target-specific exception. Assess every
in-scope executable. An eligible target has a supported Native AOT toolchain and a
compatible implementation and dependency path that preserves all applicable product
requirements. An existing incompatible implementation choice does not by itself justify
an exception: investigate supported alternatives and remediable blockers first.

An exception must identify the exact target, toolchain and dependency versions, the
blocking capability or justified engineering/maintenance tradeoff, public evidence,
alternatives considered, selected non-AOT publishing mode, and an observable condition for
reassessment. A still-unresolved compatibility question must remain explicit and cannot
be represented as a validated target or publishing mode. Do not weaken authentication,
security, interaction, cancellation, process-lifetime, or protocol requirements to obtain
Native AOT output. The public build chain in `V2-REQ-053` also applies to native compiler
tools and deployment dependencies.

Owned libraries consumed by these executables must use AOT-compatible paths where
feasible and participate in the applicable AOT/trimming analysis. Do not apply executable
publishing settings indiscriminately to libraries, tests, developer tools, or historical
probes. Resolve compatibility warnings where possible; a remaining suppression requires
a specific justification and validation basis, never blanket suppression to declare
compatibility.

Native AOT means native compilation at publish time without a managed JIT at runtime.
ReadyToRun, trimming, single-file packaging, or self-contained deployment alone does not
satisfy this requirement. Account for required native libraries and other deployed
assets; Native AOT does not require a physically single-file distribution. Startup,
memory, and deployment benefits must be evaluated through the
[publishing validation basis](../../validation/strategy.md#native-aot-publishing), not
assumed from the publishing setting. This requirement does not add a supported platform,
select a concrete UI or dependency replacement, or authorize implementation or experiments.
