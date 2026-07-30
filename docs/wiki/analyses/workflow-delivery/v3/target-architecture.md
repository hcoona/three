# Workflow Delivery v3 Target Architecture

## Status

Architecture version: **v3**.

This page records the confirmed top-level architecture for the clean v3
implementation line. It intentionally does not inherit v1 or v2 control-plane
architecture.

The normative terminology is maintained in the
[Workflow Delivery v3 Architecture Glossary](./architecture-glossary.md).

## Mission

Provide an evidence-driven software delivery governance platform for a
polyglot monorepo.

- CI Qualification determines whether an immutable change candidate satisfies
  all applicable quality obligations.
- Release Delivery rebuilds and independently qualifies an immutable Release
  Unit, obtains explicit authorization, and performs traceable external
  publication.
- The two systems share mechanisms and definitions but not runtime Plans,
  Evidence, artifacts, or verdicts.

The priority order is:

1. security and correctness;
2. traceability and explainability;
3. evolvability and recoverability; and
4. latency and operating cost.

## Architectural Shape

```text
                         Delivery Governance
             policy authority / review / environments / OIDC
                                  |
                         Trusted Decision Kernel
          authority epoch / minimum obligations / evidence admission
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
Governance is an external authority boundary, not a third business system.
The Shared Foundation provides mechanisms and normalized facts, not business
policy.

## System Boundaries

### CI Qualification

CI Qualification:

- evaluates the GitHub candidate tree;
- maps changed paths to Components and affected Release Units;
- closes the complete Qualification Target;
- builds every publishable variant of each affected Release Unit;
- executes required and advisory quality obligations;
- admits Evidence and emits an explainable Final Decision; and
- publishes the latest authoritative Decision through the required GitHub check.

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

- Authority Epoch activation;
- protected branch and target eligibility;
- policy and control-code review;
- protected environments and required reviewers;
- OIDC and registry trust policy;
- Publication Capability grant and revocation; and
- Break-Glass Remediation approval.

A business workflow may request authority but cannot grant authority to itself.

## Core Domain Model

### Component

A repository code or infrastructure unit with ownership, dependency
relationships, and quality capabilities.

### Release Unit

An independently versioned and delivered product unit containing one or more
Components and one or more artifact variants.

### Qualification Target

The immutable object covered by a quality decision.

- CI derives it from a candidate change and affected Component closure.
- Release derives it from the complete Release Unit closure, all selected
  variants, and explicit compatibility obligations.

The target must close before execution. Unknown or unclassified scope is
blocking.

## Shared Foundation

The Shared Foundation exposes four stable extension families:

1. Repository Model Providers normalize ecosystem manifests, workspaces,
   global configuration, Components, dependencies, and Release Units.
2. Build Adapters execute shared Build Definitions and map declared artifacts
   to produced outputs.
3. Quality Adapters execute quality definitions and emit standard Evidence.
4. Destination Adapters implement observation, publication, Receipt,
   mutability, digest, Capability, and remediation semantics.

CI and Release retain separate aggregate roots and state machines. Adding an
ecosystem or destination normally adds an adapter and policy mapping rather
than modifying the Trusted Decision Kernel.

## Authority and Trust

### Authority Evolution

The current Authority Epoch evaluates a Candidate Authority before merge.
Candidate code may run complete conformance, differential, replay, failure, and
Official dry-run tests but cannot issue final Decisions or receive Official
publication capability.

Authority promotion is atomic on merge by default. Post-merge shadowing is
reserved for governance or platform behavior that cannot be exercised before
merge.

### Runtime Zones

The architecture has three runtime trust zones:

1. Decision Zone: runs authoritative planning, Evidence Admission, and final
   decision logic; it executes no target code and holds no publication
   credentials.
2. Build and Qualification Zone: executes candidate or release-target code;
   it holds no publication credentials and cannot approve itself.
3. Side-Effect Zone: receives a short-lived destination Capability and consumes
   only verified immutable artifacts plus a fully materialized Publication
   Snapshot; it does not execute target code.

Target-code execution and publication authority must never coexist in one
runtime boundary.

## CI Flow

```text
GitHub event
  -> candidate identity (base/head/tested merge, merge-group, or push SHA)
  -> repository model facts
  -> closed CI Qualification Plan
  -> parallel build and quality obligations
  -> authority-owned Evidence envelopes
  -> Evidence Admission
  -> immutable Final Decision
  -> required-check and human-summary projections
```

The Planner owns semantic scope. Executors may resolve mechanical details but
cannot add, remove, substitute, or downgrade obligations.

Success requires a ready Plan and `satisfied` Evidence for every required
obligation. Missing, skipped, cancelled, timed-out, unknown, and conflicting
states cannot become success.

## Release Flow

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

- absent state may publish;
- exact satisfied state skips the side effect; and
- partial, unknown, conflicting, or unprovable state fails closed.

Reconciliation is exceptional handling for a state that cannot safely proceed.

### Retry

Retry uses whole-release replay.

- GitHub `Re-run all jobs` is the standard transient retry.
- GitHub `Re-run failed jobs` is not a supported Release recovery protocol.
- Every replay reruns planning, build, qualification, authorization checks, and
  reporting.
- Already satisfied remote destinations do not repeat side effects.
- A control-code fix requires a fresh dispatch for the same target commit under
  the new Authority Epoch.

### Partial Publication and Remediation

Publication follows an append-only Saga. Successful destinations are not
automatically rolled back when another destination fails.

Ordinary replay may resume only absent or exactly satisfied state.
Break-Glass Remediation is a separately approved operation with expected-state
checks, scoped Capability, and append-only before-and-after records. It never
rewrites the original Release history.

## Concurrency

- CI cancels superseded candidate runs.
- Release serializes by Official canonical identity or Buddy preview identity.
- In-progress Release executions are never auto-cancelled.
- Different versions may run concurrently unless a Destination Adapter declares
  a wider mutable-resource lock.
- Remediation shares the original Release and destination locks.
- Duplicate pending requests are rejected or coalesced rather than treated as
  an unbounded GitHub concurrency queue.

## Evidence, Decisions, and Explainability

Evidence Admission verifies exact ownership and integrity without rerunning the
quality command.

Final Decisions are append-only. GitHub checks and summaries are projections of
the latest authoritative Decision, not the audit record itself.

CI explanations connect paths, Components, Release Units, obligations,
variants, Evidence, outcomes, and verdicts.

Release explanations connect target, version, channel, artifacts,
destinations, observations, actions, Receipts, authority, authorization, and
allowed operator actions.

## Platform-Aware Record Retention

Caches are non-authoritative performance mechanisms.

GitHub Actions artifacts and logs are operational records subject to the
platform retention window. This public repository supports at most 90 days and
the current Release workflows use 30 days.

Longer-lived release identity and provenance rely on Git tags, registry
records, GitHub Releases when selected, and GitHub Artifact Attestations with
public Sigstore transparency-log publication.

The first architecture does not add a Durable Release Ledger or require every
Release Unit to create a GitHub Release audit anchor. State that cannot be
proved after operational records expire fails closed.

## Quality Attributes

- Security and correctness dominate availability and latency.
- Publication Capability is requested just in time; OIDC failure blocks the
  affected side effect and has no credential fallback.
- Optional telemetry may fail, but authoritative Plans, Evidence, Decisions,
  artifact identities, and Receipts must persist before later stages rely on
  them.
- Ordinary pull-request CI has a P95 12-minute Final Decision objective.
  Broad authority, policy, toolchain, and multi-Release-Unit changes are
  measured separately.
- Performance work must not weaken obligation coverage, variant coverage,
  Evidence Admission, or authorization.

## Non-Goals

The architecture does not:

- replace ecosystem build and package-management tools;
- become a general workflow engine;
- provide distributed transactions across registries;
- promote pull-request artifacts into Release;
- consume CI results as Release Evidence;
- certify reproducible builds by duplicate building;
- provide a permanent external Release ledger in the first scope; or
- use normal Release force flags to rewrite published history.

## Lower-Layer Questions

The target shape leaves these implementation questions to later design:

- exact JSON schemas and contract versioning;
- adapter package decomposition;
- exact GitHub workflow and job topology;
- artifact and Evidence physical naming;
- batching and matrix partitioning;
- destination-specific observation and digest APIs;
- user-interface rendering and operator commands; and
- migration from the current design and implementation.
