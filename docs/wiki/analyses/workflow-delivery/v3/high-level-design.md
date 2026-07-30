# Workflow Delivery v3 High-Level Design

## Status

Architecture version: **v3**.

Review state: **Confirmed architecture reorganized as HLD**.

This page is the normative high-level design for the clean v3 implementation
line. It realizes the confirmed
[Workflow Delivery v3 Requirements](./requirements.md) and intentionally does
not inherit the v1 or v2 control-plane architecture.

Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

## Architectural Shape

```text
                         Delivery Governance
             policy authority / review / environments / OIDC
                                  |
                         Trusted Decision Kernel
              minimum obligations / evidence admission / decisions
                                  |
               +------------------+------------------+
               |                                     |
        CI Qualification                       Release Delivery
    change-oriented qualification       release-unit-oriented delivery
               |                                     |
               +------------------+------------------+
                                  |
                           Shared Foundation
       repository model / build / quality / destination adapters
```

CI Qualification and Release Delivery are peer bounded contexts. Delivery
Governance is an external authority boundary, not a third business system. The
Shared Foundation provides mechanisms and normalized facts, not business
policy.

The Trusted Decision Kernel is the small cross-system authority core. CI and
Release retain independent aggregate roots, Plans, Evidence, Decisions, and
state machines.

## System Boundaries

### CI Qualification

CI Qualification:

- evaluates the GitHub candidate tree;
- maps changed paths through Project Nodes, dependency relationships, and
  global inputs to affected Release Units;
- closes the complete Qualification Target;
- builds every publishable variant of each affected Release Unit;
- executes required and advisory quality obligations;
- admits Evidence and emits an explainable Final Decision; and
- publishes the latest authoritative Decision through the required GitHub
  check.

It has no publication side effects and does not authorize Release.

### Release Delivery

Release Delivery:

- accepts a Release Intent for one Release Unit and immutable target commit;
- verifies target eligibility through Delivery Governance;
- independently plans, builds, and qualifies the complete Release Unit closure;
- creates a Publication Snapshot bound to exact artifacts and destination state;
- obtains destination-specific Publication Capabilities;
- performs publication through Side-Effect Zone adapters;
- records per-destination Receipts and the final Release outcome; and
- handles retry through whole-release replay.

It does not consume CI Plans, Evidence, artifacts, status checks, or verdicts.

### Buddy and Official

Buddy and Official are Release policy channels over the same Release machinery.

- Buddy produces distributable but non-authoritative previews through isolated
  identities, destinations, and Capabilities.
- Official publishes the canonical version for an authoritative target commit.
  Authorization binds the immutable Publication Snapshot.
- Buddy artifacts, Evidence, and Receipts are never promoted to Official.
- Non-authoritative branches may exercise Official dry-run behavior but cannot
  obtain live Official publication capability.

### Delivery Governance

Delivery Governance controls:

- protected branch and target eligibility;
- policy and control-code review;
- protected environments and required reviewers;
- OIDC and registry trust policy;
- Publication Capability grant and revocation; and
- Break-Glass Remediation approval.

A business workflow may request authority but cannot grant authority to itself.

## Repository Facts and Core Domain Model

### Project Node

A Project Node is a normalized technical fact discovered from an ecosystem
manifest or workspace. Examples include a .NET project, Python project, or
JavaScript package.

Project Nodes and their dependency relationships describe how the ecosystem
builds software. They are not independently authored business objects and do
not define ownership, qualification policy, versioning, or publication
identity.

Repository Model Providers also identify global inputs and path relationships
that affect Project Nodes, such as `Directory.Build.props`, workspace
configuration, or an explicitly declared cross-ecosystem generated-source
dependency.

### Release Unit

A Release Unit is an independently versioned and delivered product unit
containing one or more artifact variants and the Build Definitions needed to
produce them.

Release Units are explicit business objects. Their Build Definitions select
entry points, while ecosystem build systems retain responsibility for internal
project dependency closure and output composition.

### Qualification Target

A Qualification Target is the immutable object covered by a quality decision.

- CI derives it from a candidate change, affected Project Node closure, affected
  Release Units, and applicable repository obligations.
- Release derives it from the complete Project Node and declared-input closure
  needed by the Release Unit Build Definitions, all selected variants, and
  explicit compatibility obligations.

The target closes before execution. Unknown or unclassified scope is blocking.

### Aggregate Ownership

CI Qualification owns the CI Qualification Plan, CI Evidence admission state,
and CI Final Decision.

Release Delivery owns Release Intent, Release Attempt, Release Plan lineage,
Qualification Decision, destination observations, publication actions,
Receipts, and Release outcome.

Shared Foundation values may appear in either aggregate, but no aggregate
imports the other system's runtime state.

### Abstraction Discipline

A named domain abstraction must have concrete independent behavior, identity,
lifecycle, or policy responsibility. The architecture does not add a layer
solely to group existing objects or make the model appear more symmetrical.

Project Nodes remain technical repository facts because the current CI and
Release scenarios require no independent domain object between them and Release
Units.

## Shared Foundation

The Shared Foundation exposes four stable extension families:

1. Repository Model Providers normalize ecosystem manifests, workspaces,
   global configuration, Project Nodes, dependency relationships, path impact,
   and build capabilities.
2. Build Adapters execute shared Build Definitions and map declared artifacts
   to produced outputs.
3. Quality Adapters execute quality definitions and emit standard Evidence.
4. Destination Adapters implement observation, publication, Receipt,
   mutability, digest, Capability, and remediation semantics.

Adding an ecosystem or destination normally adds an adapter and policy mapping
rather than modifying the Trusted Decision Kernel.

Adapters provide facts and mechanical execution. They do not decide business
scope, downgrade obligations, authorize publication, or reinterpret verdicts.

## Authority and Trust

### Trusted Decision Kernel

The kernel owns only cross-system authority semantics:

- minimum obligation enforcement;
- Evidence Admission;
- final decision rules;
- authorization prerequisite validation; and
- strict authoritative-record validation.

Repository discovery, ecosystem execution, batching, destination API
integration, and presentation remain outside the kernel.

### Governed Same-Revision Control

CI uses the Decision Kernel from the tested candidate revision. Release uses
the Decision Kernel from the exact protected target revision being released.
The kernel has no independently selected authority revision or runtime
promotion protocol.

GitHub Governance supplies authority through control-code ownership, required
review, protected refs, workflow permissions, protected environments, and OIDC
trust. A change to the kernel, workflow control code, authoritative record
shape, or minimum policy becomes eligible only as part of the reviewed revision
that contains it.

A control-code fix therefore creates a new candidate or Release target. An
ordinary replay of an older target continues to use that target's original
kernel. Exceptional state left by an older target is handled through
reconciliation or separately authorized remediation.

### Runtime Zones

The architecture has three runtime trust zones:

1. **Decision Zone:** Runs authoritative planning, Evidence Admission, and final
   decision logic. It executes no target code and holds no publication
   credentials.
2. **Build and Qualification Zone:** Executes candidate or release-target code.
   It holds no publication credentials and cannot approve itself.
3. **Side-Effect Zone:** Receives a short-lived destination Capability and
   consumes only verified immutable artifacts plus a fully materialized
   Publication Snapshot. It does not execute target code.

Target-code execution and publication authority never coexist in one runtime
boundary.

## CI Qualification Design

### Flow

```text
GitHub event
  -> candidate identity (base/head/tested merge, merge-group, or push SHA)
  -> repository model facts
  -> closed CI Qualification Plan
  -> parallel build and quality obligations
  -> kernel-owned Evidence envelopes
  -> Evidence Admission
  -> immutable Final Decision
  -> required-check and human-summary projections
```

### Responsibility Split

The Planner owns semantic scope. It resolves the candidate identity, affected
Project Node and Release Unit closure, artifact variants, and required and
advisory obligations.

Executors resolve only mechanical details required to perform an immutable
Plan. They may not add, remove, substitute, or downgrade planned scope.

Evidence producers execute in the Build and Qualification Zone. Evidence
Admission and Final Decision execute in the Decision Zone.

Success requires a ready Plan and `satisfied` Evidence for every required
obligation. Missing, skipped, canceled, timed-out, unknown, and conflicting
states cannot become success.

## Release Delivery Design

### Flow

```text
Release Intent
  -> target eligibility and identity
  -> Qualification Snapshot
  -> complete Release build and qualification
  -> actual artifact identity and provenance
  -> Remote-State Observation
  -> Publication Snapshot
  -> Governance approval and just-in-time Capabilities
  -> destination side effects
  -> Receipts and Release outcome
```

### Release Plan Lineage

Each Release Attempt has one logical Plan lineage with two immutable snapshots.

- The Qualification Snapshot freezes what must be built and qualified.
- The Publication Snapshot references the Qualification Snapshot and adds the
  exact artifact bytes, provenance, remote observations, actions, Decision, and
  Capability requirements.

The Publication Snapshot cannot alter fields frozen by the Qualification
Snapshot. Governance approves only the Publication Snapshot digest.

This structure preserves one Release Attempt identity while preventing
post-qualification mutation from changing what was qualified.

### Build Alignment

CI and Release use the same Build Definitions and Build Adapters.

CI builds every publishable variant of an affected Release Unit. Release
rebuilds every selected variant for its final target commit and reruns all
Release quality obligations.

Pull-request artifacts and CI Evidence are never used by Release.

Release builds are required by business contract to be bit-for-bit
reproducible for identical target, Build Definition, toolchain, and declared
inputs. The delivery system records and compares observed digests where needed
for identity safety but does not certify reproducibility through duplicate
builds.

### Remote-State Observation

Every Release Attempt observes all destinations before requesting publication
Capability.

- Absent state may publish.
- Exact satisfied state skips the side effect.
- Partial, unknown, conflicting, or unprovable state fails closed.

Reconciliation is exceptional handling for state that cannot safely proceed.

### Retry

Retry uses whole-release replay.

- GitHub `Re-run all jobs` is the standard transient retry.
- GitHub `Re-run failed jobs` is not a supported Release recovery protocol.
- Every replay reruns planning, build, qualification, authorization checks,
  observation, and reporting.
- Already satisfied remote destinations do not repeat side effects.
- A control-code fix produces a new target revision; it is not injected into an
  ordinary replay of the old target.

### Partial Publication and Remediation

Publication follows an append-only Saga. Successful destinations are not
automatically rolled back when another destination fails.

Ordinary replay may resume only absent or exactly satisfied state.
Break-Glass Remediation is a separately approved operation with expected-state
checks, scoped Capability, and append-only before-and-after records. It never
rewrites the original Release history.

## Concurrency Design

- CI cancels superseded candidate runs.
- Release serializes by Official canonical identity or Buddy preview identity.
- In-progress Release executions are never auto-canceled.
- Different versions may run concurrently unless a Destination Adapter declares
  a wider mutable-resource lock.
- Remediation shares the original Release and destination locks.
- Duplicate pending requests are rejected or coalesced rather than treated as
  an unbounded GitHub concurrency queue.

Concurrency keys are projections of domain identity. GitHub workflow
concurrency is an execution mechanism, not the source of Release identity.

## Evidence, Decisions, and Explanation

Evidence Admission verifies exact ownership and integrity without rerunning the
quality command.

Final Decisions are append-only. GitHub checks and summaries are projections of
the latest authoritative Decision, not the audit record itself.

CI explanations connect paths, Project Nodes, dependency relationships, Release
Units, obligations, variants, Evidence, outcomes, and verdicts.

Release explanations connect target, version, channel, artifacts,
destinations, observations, actions, Receipts, authority, authorization, and
allowed operator actions.

The same structured model drives machine-readable records and human
projections. Diagnostics explain a verdict but never determine it.

## Platform-Aware Record Retention

Caches are non-authoritative performance mechanisms.

GitHub Actions artifacts and logs are operational records subject to the
platform retention window. This public repository supports at most 90 days,
and the current Release workflows use 30 days.

Longer-lived release identity and provenance rely on Git tags, registry
records, GitHub Releases when selected, and GitHub Artifact Attestations with
public Sigstore transparency-log publication.

The first architecture does not add a Durable Release Ledger or require every
Release Unit to create a GitHub Release audit anchor. State that cannot be
proved after operational records expire fails closed.

## Requirement Coverage

| Requirement Group | Owning Design Elements                                                               |
| ----------------- | ------------------------------------------------------------------------------------ |
| `WD-SYS-*`        | Peer bounded contexts, aggregate ownership, Shared Foundation, Delivery Governance   |
| `WD-CI-*`         | CI Qualification flow, Planner, executors, Evidence Admission, Final Decision        |
| `WD-REL-*`        | Release Attempt, Plan lineage, independent build and qualification, Side-Effect Zone |
| `WD-CHN-*`        | Buddy and Official channel policy over Release Delivery                              |
| `WD-AUTH-*`       | Same-revision Decision Kernel, protected review, Delivery Governance                 |
| `WD-SEC-*`        | Decision, Build and Qualification, and Side-Effect runtime zones                     |
| `WD-EVD-*`        | Evidence Admission, append-only Decisions, structured explanation projections        |
| `WD-OPS-*`        | Remote-State Observation, whole-release replay, Saga, reconciliation, remediation    |
| `WD-CON-*`        | Domain-derived concurrency and destination locks                                     |
| `WD-RET-*`        | Platform-aware records, durable destination identities, fail-closed expiration       |
| `WD-NFR-*`        | Kernel minimization, adapter extension model, explanation contract, CI objective     |

## Middle-Layer Design Decomposition

The next design stage should produce separate MLDs for:

1. **Repository Model and Release Units:** Project Node discovery, dependency
   and path-impact facts, Release Unit authoring, variants, and Build
   Definitions.
2. **Trusted Decision Kernel and Governance Integration:** same-revision
   control, admission, decision, review, and authorization contracts.
3. **CI Qualification:** candidate identity, affected-scope planning,
   execution, Evidence, Decision, and GitHub projection contracts.
4. **Release Delivery:** Release Intent, Plan lineage, build and qualification,
   observation, capability, publication, Receipt, replay, and remediation
   contracts.
5. **Shared Foundation:** provider and adapter interfaces, normalized facts,
   artifact identity, provenance, and execution envelopes.

CI and Release remain together in this HLD because their separation, shared
foundation, and governance relationship are top-level architectural decisions.
They separate into bounded-context documents at the MLD stage.

## Deferred Lower-Layer Questions

The HLD intentionally leaves these questions to later design:

- exact strict record schemas and transport;
- adapter package decomposition;
- exact GitHub workflow and job topology;
- artifact and Evidence physical naming;
- batching and matrix partitioning;
- destination-specific observation and digest APIs;
- user-interface rendering and operator commands; and
- migration from the current implementation into the selected vertical slice.
