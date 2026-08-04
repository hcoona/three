# Workflow Delivery v3 Architecture Glossary

## Status

Architecture version: **v3**.

This is the normative glossary for the clean v3 implementation line. It records
terms and principles confirmed during architecture review before comparison
with v1 or v2 implementation details.

Confirmed entries should remain stable. Open terms are explicitly identified and
must not be treated as settled architecture.

## System Framing

The delivery architecture contains two peer business systems:

- **CI Qualification**
- **Release Delivery**

They share mechanism-level capabilities but retain separate policies, plans,
evidence, and decisions.

An external governance boundary controls authority across both systems.

## System Boundaries

### CI Qualification

The business system that determines which quality obligations apply to a change,
executes or delegates those obligations, and produces an explainable qualification
decision.

CI Qualification is change-centric. It does not own release authorization or
publication side effects.

### Release Delivery

The business system that accepts an explicit release intent for an immutable
revision, rebuilds the release outputs, performs its own required quality checks,
obtains authorization, publishes outputs, and records the resulting external state.

Release Delivery does not reuse artifacts produced by pull request builds and does
not consume CI check results as release evidence.

### Shared Foundation

The mechanism-level foundation shared by CI Qualification and Release Delivery.

It may define stable identity, revision, digest, fact, artifact, provenance,
contract, and execution-capability primitives. It does not own CI scope policy,
release-channel policy, approval policy, or final business decisions.

### Delivery Governance

The authority boundary that determines who may define policy, approve protected
actions, grant execution capabilities, and accept decisions.

Delivery Governance is not a third business system. It is a higher-authority
boundary implemented through facilities such as repository rules, protected
environments, identity federation, credential scopes, review policy, and audit
configuration.

CI Qualification and Release Delivery may request decisions or capabilities, but
they must not grant final authority to themselves.

### Decision Zone

The trusted runtime zone that executes authoritative policy, planning, evidence
admission, and final decision logic.

It does not execute code from a pull request or release target and does not
directly hold publication credentials.

### Build and Qualification Zone

The unprivileged runtime zone that may execute code from a pull request or
release target.

It performs build, test, lint, package, and other quality work. It produces
immutable artifacts and evidence but does not hold publication credentials or
grant authority to itself.

### Side-Effect Zone

The narrowly privileged runtime zone that receives a scoped publication
capability only after qualification and governance approval.

It consumes verified immutable artifacts and a fully materialized publish
request. It must not check out or execute code from the release target. Its
output is a publication receipt for reconciliation.

### Buddy

A Release Delivery policy channel for distributable, non-authoritative preview
releases.

Buddy may target a governance-authorized non-authoritative revision and may
produce externally installable outputs. It uses the same Release planning,
build, qualification, evidence, and side-effect mechanisms as Official, but it
publishes only to isolated preview destinations, namespaces, identities, or
prerelease channels.

Buddy does not freeze the Official version identity, occupy an Official
canonical destination, or create an authoritative production release record.
Buddy artifacts and evidence are not promoted to Official.

Buddy is not a separate release implementation and is not merely another name
for dry-run.

### Official

A Release Delivery policy channel for authoritative production publication.

A live Official release must:

- target a revision reachable from a Governance-configured authoritative branch;
- use the Release Planner and Finalizer contained in that target revision;
- rebuild every publishable artifact variant declared by the Release Unit;
- complete its own Release Qualification Target;
- freeze the Release Plan, artifact digests, destinations, and plan digest before
  authorization; and
- bind authorization to that immutable plan digest.

After authorization, the revision, version, artifacts, and destinations must not
change. Official creates the canonical version identity and authoritative release
record.

A non-authoritative branch may exercise Official dry-run behavior but cannot
receive live Official publication capability.

Official is not a separate release implementation.

## Core Domain Terms

### Project Node

A normalized technical fact discovered from an ecosystem manifest or workspace
at an immutable repository revision.

Examples include a .NET project, Python project, or JavaScript package. A
Project Node records ecosystem identity, source location, dependency
relationships, build capabilities, and relevant manifest facts.

A Project Node is not an authored ownership, qualification, versioning, or
publication boundary. The ecosystem build system remains authoritative for its
internal dependency and output semantics.

### Source-Tree Conformance

The repository-local assertion that an immutable checkout satisfies its
formatting, linting, static source, lock, generated-file, configuration, and
path-triggered scenario rules.

The repository-root HK gate owns this assertion as one opaque composite Quality
Definition. CI binds the candidate and definition identity but does not inspect
HK profiles, steps, file applicability, batching, or internal planning.

### Affected-System Qualification

The model-driven assertion that systems affected by a candidate remain correct
across Project Node dependency closure, provider-native quality targets,
execution dimensions, supporting tests, and affected Release Unit variants.

The CI Planner owns this scope. HK does not replace it.

### Quality Capability

An ecosystem-resolvable quality operation available to a Project Node or
provider-native aggregate target, such as build, type checking, analysis, or
test execution.

Providers discover standard capabilities from native manifests and metadata.
Project custom policy supplies only definitions that native ecosystem facts
cannot express.

### Quality Preset

An ecosystem-qualified, semantically versioned quality contract selected by a
project.

A preset states which capabilities are required, required when present, or
advisory. It does not copy native commands or imply cross-ecosystem equivalence.
Adding or strengthening required semantics creates a new preset identity that
projects adopt explicitly.

### Effective Quality Policy

The preset or custom policy that applies to one Project Node after
directory-scoped authoring is resolved.

For one ecosystem, the effective selection is the nearest tracked ancestor
descriptor entry matching that ecosystem. Directory scope is an authoring
mechanism, not a domain object. The compiled Plan records the resolved policy
rather than directory inheritance.

### Quality Definition

The executable semantic contract for one kind of quality proof.

A Quality Definition identifies its Provider or Quality Adapter, supported
target kind, inputs, coverage dimensions, runner constraints, prerequisites,
raw result interpretation, and Evidence contract.

### Release Unit

A logical product unit that is versioned, qualified, authorized, and delivered as
one release concern.

A Release Unit may produce multiple artifacts or platform variants. It selects
Build Definitions and entry points without reproducing the internal dependency
closure already owned by the ecosystem build system. Repository project
boundaries do not automatically define Release Unit boundaries.

### Qualification Target

The exact object covered by one quality decision. A Qualification Target must
bind to an immutable repository revision and identify the relevant Project
Nodes, Release Units, variants, inputs, and obligations.

In CI Qualification, the target is derived from changed paths, affected Project
Nodes and dependency relationships, effective project quality policy,
provider-native quality targets and dimensions, affected Release Units, global
inputs, and repository obligations.

In Release Delivery, the target is derived from the selected Release Unit and the
complete quality scope required by release policy.

A Release Qualification Target contains:

- the complete Project Node and declared-input closure required by the selected
  Build Definitions;
- every artifact variant selected for publication;
- every quality check declared by the Release Unit, without changed-path
  pruning; and
- explicit compatibility obligations or contract tests for designated
  consumers.

It does not include every reverse-dependent Project Node or every repository
check by default. Release Policy may require a broader consumer closure or a
repository-wide qualification profile for a specific class of Release Unit.

A Qualification Target must be closed before execution. Planning may explicitly
fail to close the target, but it must not emit a partial target that leaves
unknown Project Nodes, Release Units, variants, obligations, or destinations
for an executor to interpret.

For CI, unclassified changed paths, unresolved workspace relationships, and
changes to control-plane policy or global build configuration produce explicit
blocking obligations or diagnostics.

For Release, missing descriptors, incomplete dependency closure, undefined
variant builds, unresolved compatibility obligations, and invalid destinations
block the Release before execution.

### CI Candidate Identity

The immutable source identity evaluated by CI for a specific GitHub event.

For a pull request, CI evaluates the current GitHub-generated merge commit and
records the base, head, and tested merge commit SHAs. A change to the base or
head invalidates the previous decision.

For a merge queue, CI evaluates the merge-group commit SHA. For a push, CI
evaluates the pushed commit SHA.

Branch names, pull request numbers, workflow run IDs, and check-run IDs are
indexes rather than source identities.

### Release Identity

The stable identity of one channel-specific business Release. It contains:

- the Release Unit identity;
- the channel;
- the target commit SHA whose source is built and qualified; and
- the Official canonical version or Buddy preview identity.

Plan digests, artifact digests, workflow attempts, and Authorization Records are
Attempt bindings rather than Release Identity fields. Tags, branches, and
workflow run IDs are indexes rather than Release identities.

## Build and Quality Terms

### Pull Request Build

A build used to provide early qualification feedback for an unmerged change.

Its artifacts are not admissible as Release artifacts.

### Release Build

A fresh build performed by Release Delivery for the final immutable release
revision.

Release Build outputs are subject to Release-owned quality, provenance,
authorization, and publication decisions.

### Quality Check Definition

A reusable definition of how to perform a quality check. CI Qualification and
Release Delivery may share the same check definitions and execution capabilities
without sharing plans, evidence, or decisions.

### Build Definition

The authoritative, reusable definition of how one artifact variant is built.

It freezes the build entry point, toolchain constraints, parameter model,
variant dimensions, expected output structure, and provenance requirements.
CI Qualification and Release Delivery use the same Build Definition but
materialize separate Build Requests for different revisions and purposes.

When a change affects a Release Unit, CI builds every publishable artifact
variant defined for that Release Unit. Release later rebuilds the complete
publishable variant set for the final release revision.

### Build Request

A system-owned, immutable invocation of a Build Definition for a specific
revision, purpose, and artifact variant.

CI and Release Build Requests are separate. They may differ in revision,
version identity, and authorization context, but they must preserve the shared
binary-production definition.

### Reproducible Release Build

A Release Unit business contract requiring the same target commit, Build
Definition, toolchain, and declared inputs to produce bit-for-bit identical
release artifacts.

The delivery system does not certify reproducibility by performing duplicate
builds. It still records artifact content digests and refuses to continue when
an observed replay or remote-state digest conflicts with the Release identity.
That integrity check is not a general reproducible-build certification.

### Semantic Plan Finality

The rule that an executor may resolve mechanical execution details but may not
change the business meaning of an accepted Plan.

An executor may restore locked dependencies, enumerate tests within a selected
test target, locate declared outputs, inspect remote state for idempotency, and
adapt paths to the assigned runner.

An opaque composite definition may internally select implementation steps when
those steps are not domain obligations. The root HK gate uses this rule for
source-tree conformance.

It may not add, remove, substitute, or downgrade Project Nodes, Release Units,
variants, obligations, versions, artifacts, destinations, or authorization
requirements. A runtime discovery that conflicts with the Plan or Build
Definition causes failure rather than replanning.

### Planner

The bounded-context-owned decision service that converts immutable inputs,
repository facts, and applicable policy into a closed Plan before execution.

CI and Release have separate Planners because they select different scope,
obligations, identities, and side effects.

### Finalizer

The bounded-context-owned decision service that admits execution records and
produces the immutable Decision or outcome after execution.

CI and Release have separate Finalizers. Shared Foundation may provide strict
record validation, canonicalization, digest, and Evidence-binding functions,
but it does not select the verdict.

The CI Finalizer considers required obligations only. Advisory obligations use
a separate non-authoritative Reporter.

### Advisory Reporter

The CI-owned projection component that validates and summarizes advisory
Evidence without producing an authoritative qualification verdict.

It may finish after the required Final Decision. Its failure is visible but does
not change the required check.

### Official Version Identity

The canonical version identity assigned by Official to one Release Unit and one
authoritative revision.

The original mapping and its Release Record are immutable. Replaying the same
Release Plan is a recovery operation: an existing destination state with the
expected digest is accepted, while a conflicting digest requires reconciliation.

### Buddy Preview Identity

A channel-isolated identity for one Buddy Release Intent and source revision.

It must not occupy or freeze the Official canonical identity. Replaying the same
Buddy intent retains the same preview identity; a different source revision
requires a different preview identity.

The physical version string or namespace encoding is ecosystem-specific.

### Qualification Evidence

Evidence produced by executing quality obligations against a specific
Qualification Target.

CI evidence and Release evidence have separate ownership. Release Delivery reruns
its required quality checks rather than adopting CI results as release evidence.

CI required and advisory obligations both produce Plan-bound Evidence. Only
required Evidence enters the authoritative CI Final Decision.

### Evidence Admission

The lightweight process that proves an execution result belongs to the exact
Plan obligation being decided.

Admission verifies strict record shape, candidate or target commit, Plan digest,
obligation identity, definition and request digests, producer job, attempt,
runner family, and relevant artifact content digests.

Admission does not rerun the command, reinterpret test results, or duplicate the
executor's quality logic. High-risk side-effect boundaries may additionally
recompute artifact digests and verify provenance before publication.

Target code may produce raw results, but a CI- or Release-owned Evidence writer
creates the final Evidence envelope from the execution result and GitHub job
context.

### Plan Readiness

The structural state that determines whether a closed Qualification Target can
be executed toward a successful decision.

A Plan is either `ready` or `blocked`. Unclassified paths, unresolved
dependencies, undefined variants, missing definitions, and conflicting policy
make the Plan `blocked`.

Diagnostics explain readiness but do not determine it.

### Obligation Disposition

The policy-assigned effect of one quality obligation:

- `required`: successful admitted Evidence is necessary for success; or
- `advisory`: the result must be reported but does not block the final decision.

Checks that do not apply are excluded explicitly during planning rather than
being silently skipped during execution.

`required-when-present` is a preset capability-resolution mode rather than a
terminal obligation disposition. When the capability exists, the resulting
obligation is required. Its absence is valid.

### Obligation Outcome

The terminal qualification state of one obligation:

- `satisfied`;
- `failed`;
- `incomplete`, including skipped, cancelled, timed out, missing, or lost
  execution; or
- `conflicted`, including inconsistent Evidence or artifact identity.

A successful qualification decision requires a ready Plan and every required
obligation to be satisfied.

### Final Decision

An immutable record produced after required aggregation completes. It binds the
candidate or target identity, Plan digest, required Evidence Set digest,
required obligation outcomes, verdict, and completion time.

Required workflow reruns produce a new Final Decision rather than modifying an
existing record. Advisory Evidence is reported separately and does not create a
new authoritative Decision.

A GitHub required-check context may project the latest authoritative Final
Decision for the current candidate. That mutable user-interface projection does
not replace the append-only Decision history.

Release authorization binds a specific Final Decision and Plan digest. An
in-progress side-effect execution cannot switch automatically to a later
Decision.

### Runtime-Decoupled Delivery Systems

CI Qualification and Release Delivery do not depend on each other's runtime
plans, evidence, artifacts, status checks, or decisions.

They align through shared domain identities, quality definitions, build
specifications, ecosystem capabilities, and provenance primitives. Each system
materializes and executes its own plan against its own Qualification Target.

Release target eligibility is a Delivery Governance concern rather than a
runtime dependency on CI status.

### Release Intent

The request to deliver exactly one Release Unit through exactly one channel.

Manual initiation uses the GitHub-selected workflow ref as the target. An
independent target SHA or ref input is forbidden. The workflow pins the resolved
`github.sha`, and every Plan, artifact, Evidence object, action, and record binds
that same revision.

### Release Execution

The channel-specific business Release created from one Release Intent. It
contains append-only whole-release Attempts while preserving one immutable
Release Identity.

Its state is one of `in-progress`, `replayable`,
`reconciliation-required`, or `completed`. Platform retention limits how long
its operational records remain replayable.

### Release Simulation

A dry-run execution that performs planning, build, qualification, observation,
and publication simulation without obtaining live publication Capability or
entering the Buddy or Official Release lineage.

A Buddy simulation uses an immutable non-live simulation projection identity
as the Buddy version-projection input. That identity cannot reserve or collide
with a live Buddy Intent or preview identity.

### Release Plan Lineage

The single logical Plan history for one Release Attempt. It contains two
immutable sealed snapshots rather than one mutable document or two unrelated
Plans.

### Qualification Snapshot

The first sealed snapshot in a Release Plan Lineage. It freezes the Release
Unit, target commit, channel, version, Project Node and declared-input closure,
build dependencies, the complete Release Unit artifact variant set, Build
Definitions, selected Release quality policy, required quality obligations,
logical publication projections, and Publication Capability requirements.

It authorizes only unprivileged build and qualification work.

### Publication Snapshot

The second sealed snapshot in a Release Plan Lineage. It references the
Qualification Snapshot digest, preserves every frozen semantic field, and adds
actual artifact identities, content digests, provenance, destination
observations, publication actions and dependency edges, capability groups,
Qualification Decision, and exact Capability requirements.

An Authorization Record binds Governance approval to the Publication Snapshot
digest. A finalizer verifies that no Qualification Snapshot field changed.

GitHub transports the snapshots as separate attempt-specific artifacts even
though they share one logical Release Plan lineage.

### Release Attempt

One coherent plan, build, qualification, authorization, publication, and
reporting pass within a Release Execution.

An Attempt is identified by its Release Identity and GitHub workflow run
attempt. Its Qualification Snapshot, Publication Snapshot, artifact digests,
and Authorization Record are immutable bindings created within that Attempt.

An Attempt does not combine successful jobs, artifacts, approvals, or evidence
from multiple GitHub run attempts as if they formed one atomic pass.

An Attempt Outcome records qualification, observation, authorization, action,
Receipt, and reporting results without replacing the Release Execution state.

### Whole-Release Replay

The supported retry model for a failed Release Attempt.

Every replay reruns planning, the complete Release build, Release
qualification, observation, authorization, and reporting. The planner observes
every projection again: exact satisfied state skips its side effect, absent
state may publish, and unknown, projection-internal partial, or conflicting
state requires reconciliation.

GitHub `Re-run failed jobs` is not a supported Release recovery protocol because
it produces a mixed-attempt job graph. A normal transient retry uses `Re-run all
jobs`. A workflow or control-code fix creates a new target revision; ordinary
replay of an older target continues to use that target's original code.

Each live side-effect job obtains a new attempt-scoped Publication Capability.

### Remote-State Observation

The mandatory read-only pre-authorization planning step in every Release
Attempt, including the first attempt.

Each logical publication projection is classified atomically against the
desired Release identity:

- absent state may produce a publish action;
- exact satisfied state produces no side effect;
- partial, unknown, conflicting, or unprovable state fails closed and requires
  reconciliation.

Cancellation does not create a separate reconciliation workflow. A later
whole-release replay performs the same normal Remote-State Observation before
any new write.

The initial architecture assumes Delivery Governance is the only normal writer.
It does not require a second observation after authorization and accepts
out-of-band mutation between observation and publication as a residual risk.

### Publication Projection

A channel-selected logical remote product that must become exact as one unit,
such as a registry package version or a GitHub Release with its required assets.

The Release Unit selects projections, the Destination Adapter defines their
mechanics and action expansion, and Delivery Governance grants the necessary
Capability. A projection may expand into multiple ordered actions, but
projection-internal partial state is not ordinary replayable state.

### Capability Group

An execution group whose actions share one destination-specific authorization
and credential boundary.

Independent groups may run in parallel after channel approval. Actions within a
group run in order and stop after the first failure. Every action retains its
own identity and Receipt.

### Authorization Record

The append-only record that binds one channel-level approval to one exact
Publication Snapshot digest.

It authorizes capability-group execution but does not itself contain or replace
destination credentials. Each capability group independently obtains its
short-lived Capability through its configured Governance boundary.

### Release Reconciliation

The read-only exceptional process used when Remote-State Observation cannot
classify a projection as safely absent or exactly satisfied.

Reconciliation resolves partial, unknown, conflicting, or unprovable state.
Existing successful publication is not automatically rolled back.

### Release Execution Serialization

The best-effort GitHub execution boundary for one externally visible Release
identity.

Official concurrency groups use the Release Unit and canonical version. Buddy
groups use the Release Unit and preview identity. Break-Glass Remediation uses
the matching Release and affected destination serialization groups.

Serialization does not grant authorization or replace Remote-State Observation.
Different versions may run concurrently unless a Destination Adapter declares a
concrete broader mutable-resource serialization group.

Release concurrency never cancels an in-progress execution. `queue: single`
retains only the newest pending duplicate request.

GitHub concurrency is not a distributed lock, durable queue, or protection
against external writers. Correctness still depends on observation,
Destination Adapter behavior, and the governed single-writer assumption.

## Quality Attribute Terms

### Non-Authoritative Cache

A cache is a performance mechanism rather than a correctness dependency.

Package managers, setup actions, and build tools may use cache entries whenever
they become available during an execution. The delivery system does not require
an explicit cache-disabled mode.

Continuous cache unavailability must not change the Plan, skip an obligation,
alter Evidence semantics, or prevent execution when authoritative dependency
sources remain available. Cache writes may fail without changing the result.

Release artifacts and durable release records are not caches.

### Just-in-Time Publication Capability

A Publication Capability is requested by the Side-Effect Zone when the planned
action actually requires it.

The system does not add a separate OIDC or credential-availability probe.
Failure to obtain the required OIDC token or other destination Capability blocks
that publication action and prevents a completed Release result.

Build and qualification may complete without publication authority. The system
must not fall back to a long-lived token, personal access token, alternate
environment, or alternate workflow identity.

### Authoritative Delivery Record

A small structured object required to establish delivery correctness, including
a frozen Plan, admitted Evidence, artifact identity and provenance, Final
Decision, Publication Receipt, or Remediation Record.

Authoritative Delivery Records must be persisted before a later stage relies on
them. If a publication succeeds but its Receipt cannot be persisted, the
Attempt stops before additional destination side effects and a later replay
observes remote state.

Performance metrics, optional diagnostic logs, dashboards, and notifications
are telemetry rather than Authoritative Delivery Records. Their failure may
reduce observability without changing the decision.

### Platform-Native Record Retention

The retention model that uses each existing platform for the facts it can
actually preserve.

GitHub Actions Plans, Evidence, Receipts, reports, and build artifacts are
operational records available only within the configured Actions retention
window. In this public repository, GitHub supports at most 90 days and the
current Release workflows use 30 days.

Longer-lived release identity and provenance rely on Git tags, registry
package/version records, GitHub Release objects when selected, and GitHub
Artifact Attestations with public Sigstore transparency-log publication.

After Actions records expire, a replay may use only facts still provable from
those platforms. Unprovable exact state fails closed.

The first architecture does not add an external Durable Release Ledger or
require every Release Unit to create a GitHub Release audit anchor. A future
compliance requirement may introduce such a capability explicitly.

### Ordinary-Change CI Latency SLO

The product objective that the required CI Final Decision for an ordinary pull
request completes within 12 minutes at the 95th percentile.

Measurement begins when GitHub creates the workflow run and includes runner
queue time. Superseded candidates are excluded.

Broad changes to workflow authority, policy, global toolchains, or more than one
Release Unit are measured separately. Exceeding the SLO is performance debt
rather than a correctness failure.

Ordinary candidates remain in the cohort when quality fails or planning blocks.
Only supersession and explicit broad-change classification exclude them.

The SLO may drive parallelism, batching, early failure presentation, and cache
optimization. It must not reduce required obligations, publishable variant
coverage, or Evidence Admission.

Release active compute, runner queueing, and human approval wait are measured
separately and do not share the CI 12-minute objective.

### Repository Model Provider

An adapter that converts ecosystem manifests, workspace configuration, global
configuration, project relationships, and build capabilities into normalized
Project Node, dependency, path-impact, and global-input facts.

It does not infer Release Unit identity or own CI or Release policy.

### Build Adapter

An adapter that executes a Build Definition through one ecosystem toolchain and
maps declared artifacts to produced outputs.

CI and Release share the adapter. The adapter does not decide whether a build is
required.

### Quality Adapter

An adapter that executes one quality definition and emits standard Evidence.

It does not decide whether the resulting obligation is required or advisory.

### Destination Adapter

An adapter that implements observation, publication, Receipt, mutability, digest
visibility, Publication Capability, and remediation semantics for one
destination family.

It does not decide whether Buddy or Official may use the destination.

### Independent Aggregate Roots

The rule that CI Qualification and Release Delivery own separate Plans,
Evidence Sets, Decisions, and state machines while consuming shared normalized
foundation interfaces.

New ecosystems and destinations normally add providers or adapters and policy
mapping without modifying CI or Release decision semantics. Shared Foundation
remains a mechanism library rather than a universal business Planner or
Finalizer.

### Decision Explanation

The structured reason chain emitted as part of a CI or Release Final Decision.

CI explanations connect changed paths to Project Nodes, dependency
relationships, Release Units, selected obligations, variants, Evidence,
outcomes, verdict, and corrective actions.

Release explanations connect the Release Unit, target commit, version, channel,
artifacts, destination observations, planned actions, Receipts, outcomes,
authority, authorization, and allowed operator actions.

GitHub Job Summary is the human projection and the structured Decision or report
is the machine projection. Both are generated from the same model.

### Break-Glass Remediation

A separately authorized operational process that corrects an external release
projection without rewriting release history.

Break-Glass Remediation is not a `force` option on a normal Buddy or Official
Release Intent. It requires:

- an immutable reference to the original Release Record;
- an expected current remote state;
- an explicit remediation action and destination;
- a reason and incident or work-item reference;
- a dry-run or equivalent precondition check;
- a short-lived, narrowly scoped remediation capability; and
- an append-only Remediation Record containing before and after state.

Destination capability remains authoritative. An immutable registry may support
yank, deprecate, or a new version but not physical replacement. A mutable
destination may support replacement or retargeting when policy permits it.

Official remediation requires stronger governance than normal Official
publication. Buddy may use a lower approval tier, but it follows the same
append-only audit model.

### Publication Capability

A short-lived, externally granted credential or platform permission used to
perform a planned side effect.

Delivery Governance scopes the capability to the narrowest channel,
destination, repository, workflow, Environment, identity, and validity boundary
that the platform supports. The architecture does not assume that GitHub OIDC
or a destination token cryptographically carries the Publication Snapshot
digest, artifact digests, or exact action set.

The trusted side-effect executor validates the Authorization Record,
Publication Snapshot, artifact digests, action IDs, and Attempt before using the
capability. Receipts record both the authorized semantic action and the actual
platform identity used.

Qualification may request a Capability but cannot approve or create one.
Delivery Governance grants it through platform controls such as protected
environments, job permissions, OIDC trust, and registry trusted-publishing
policy.

Capabilities are destination-specific to the extent supported by the platform.
Buddy cannot reach Official destinations, dry-run receives no live Capability,
and Break-Glass Remediation uses a separate remediation Capability.

A Plan, artifact, attempt, or approval change invalidates the executor's
authorization to use a previously obtained capability even when the external
credential format cannot encode every such binding.

## Confirmed Architecture Principles

1. CI Qualification and Release Delivery are peer systems over a shared
   mechanism-level foundation.
2. Delivery Governance remains independent from both business systems and from the
   Shared Foundation.
3. Shared concepts must not collapse CI and Release into one universal plan or
   evidence model.
4. Pull request artifacts must not be reused or promoted by Release Delivery.
5. Release Delivery independently reruns every obligation in its selected
   channel-specific quality policy, and every such obligation is required.
6. CI and Release may share quality definitions, build specifications, ecosystem
   adapters, and execution capabilities.
7. Release Qualification covers the complete Project Node and declared-input
   closure required by the Release Unit Build Definitions, plus explicit
   compatibility obligations.
8. CI uses Planner and Finalizer code from the tested candidate revision. Live
   Release uses code from the exact protected target revision. Dry-run Release
   simulation uses code from its exact selected simulation revision without
   approval or live publication Capability.
9. Planning, finalization, workflow, record-shape, and minimum-policy changes
   require Governance-configured owner review.
10. Control-code changes create a new candidate or Release target; normal replay
    never injects newer control code into an older target.
11. A runtime that executes target code must not possess publication capability,
    and a runtime with publication capability must not execute target code.
12. CI Qualification and Release Delivery have no runtime evidence, artifact, or
    verdict dependency on each other.
13. CI builds all publishable variants of every affected Release Unit by using
    the same Build Definitions used by Release Delivery. Artifact variants
    belong to the Release Unit rather than to a channel.
14. Buddy is a distributable preview channel with destinations, identities, and
    capabilities isolated from Official.
15. Official publication requires an authoritative target revision and an
    Authorization Record bound to an immutable Publication Snapshot digest.
16. Original Buddy and Official Release Records remain immutable.
17. Forced correction is modeled as a separately authorized Break-Glass
    Remediation with append-only before-and-after evidence.
18. CI decisions bind the actual GitHub candidate tree and, for pull requests,
    its base and head commits.
19. Release actions bind the target commit, Release Unit, frozen plan, artifact
    digests, and plan-specific authorization.
20. CI and Release Qualification Targets are fully closed before execution;
    unresolved Project Nodes, Release Units, inputs, or obligations are blocking
    rather than implicitly excluded.
21. Executors may perform mechanical discovery but cannot change the semantic
    content of an accepted Plan.
22. Decision aggregation verifies Evidence ownership and integrity without
    repeating the executor's quality check.
23. Success requires a ready Plan and satisfied Evidence for every required
    obligation; skipped, missing, unknown, cancelled, timed out, and conflicting
    states cannot become success.
24. Diagnostics explain structural state but never determine the verdict.
25. Final Decisions are append-only; required reruns create new Decisions while
    GitHub checks project the latest authoritative result. Advisory Evidence
    remains outside the authoritative Decision.
26. Publication credentials are externally granted through short-lived,
    narrowly scoped platform Capabilities. Trusted side-effect executors enforce
    the exact immutable Plan, artifact, Attempt, and action bindings.
27. Release retry uses whole-release replay rather than GitHub failed-job
    resumption.
28. Release builds are required to be bit-for-bit reproducible as a Release Unit
    business contract; the delivery system does not certify reproducibility by
    duplicate building.
29. Partial publication is handled as an append-only Saga with
    projection-atomic reconciliation rather than automatic rollback.
30. Every Release Attempt performs read-only projection observation before
    authorization; cancellation adds no separate recovery workflow.
31. CI may cancel superseded candidate runs. Release uses best-effort GitHub
    execution serialization by Release identity and never cancels in-progress
    publication.
32. Cache availability may affect performance but never correctness, scope,
    Evidence, or verdict.
33. Publication Capabilities are requested just in time; their unavailability
    blocks publication without triggering a credential fallback.
34. Authoritative Plans, Evidence, Decisions, artifact identities, and Receipts
    must persist; optional telemetry may fail without changing correctness.
35. Record retention follows actual platform guarantees; Actions artifacts are
    operational rather than permanent release records.
36. Ordinary pull-request CI has a P95 12-minute Final Decision SLO without
    weakening qualification semantics.
37. Repository project and build facts, quality execution, and destination
    behavior extend through stable adapters while CI and Release keep separate
    aggregate roots.
38. CI and Release Decisions include a structured, machine-readable explanation
    that also drives the human GitHub summary.
39. Each Release Attempt has one logical Plan lineage containing immutable
    Qualification and Publication snapshots; in-place Plan backfill is forbidden.
40. Architecture review begins from the ideal system direction and boundaries
    before considering the current implementation.
41. CI separates source-tree conformance from affected-system qualification.
    The repository-root HK gate owns the former as one opaque composite
    obligation.
42. The CI Planner does not inspect HK profiles, steps, file applicability, or
    internal planning and does not depend on HK plan serialization.
43. Projects select ecosystem-specific, semantically versioned quality presets
    or custom policy. Required semantic strengthening requires project opt-in.
44. Effective project quality policy uses nearest-ancestor, ecosystem-matching
    authoring without creating a directory domain object or cross-ecosystem
    preset semantics.
45. CI impact includes the full typed reverse Project Node closure and all
    publishable variants of every affected Release Unit.
46. Quality obligation identity is Quality Definition, concrete target, and
    concrete dimensions. Execution batching and mechanical reuse do not change
    that identity.
47. Required and advisory obligations execute in separate lanes. The CI
    Finalizer decides required obligations, while an Advisory Reporter presents
    non-authoritative results.
48. Incremental CI requires an authoritative comparison range. Schedule and
    explicit manual full validation use complete repository qualification; an
    invalid range does not silently fall back to full validation.
49. A blocked CI Plan executes no authoritative partial obligations.
50. A domain abstraction is introduced only when concrete scenarios demonstrate
    independent behavior, identity, lifecycle, or policy responsibility.
51. Manual Release dispatch uses the selected Git ref as the exact target;
    independent target SHA or ref inputs are forbidden.
52. One Release Execution contains append-only whole-release Attempts. Dry-run
    uses a separate simulation identity and never enters live Release lineage.
53. Buddy and Official each select a complete Release quality policy. Neither
    implicitly inherits CI project policy or the other channel's policy.
54. The Release Unit selects logical publication projections, Destination
    Adapters define mechanics, and Delivery Governance grants Capability.
55. Projection-internal partial state requires read-only reconciliation and, if
    mutation is necessary, separately authorized Break-Glass Remediation.
56. One channel-level approval produces an Authorization Record for the exact
    Publication Snapshot. Destination-specific capability groups independently
    acquire their short-lived Capabilities.
57. Independent capability groups may execute in parallel. Actions within a
    group remain ordered, fail-stop, and individually receipted.
58. Release correctness assumes Delivery Governance is the only normal writer
    and treats out-of-band mutation after observation as an accepted residual
    risk rather than introducing a distributed lock or mandatory second read.
59. The Finalizer aggregates immutable result bundles and does not query remote
    destinations again.
60. Artifact final bytes are frozen before publication; the initial scope
    excludes signing or notarization that changes artifact bytes.
61. External provenance projections execute only after the Authorization Record
    exists, even when they are prerequisites for other publication groups.
62. Every live Release Attempt requires channel approval of its Publication
    Snapshot, including an exact-satisfied no-op Attempt. A no-op Attempt
    requires no destination Capability.
63. Each action Receipt is persisted before a later mutation begins in the same
    capability group. A group result bundle references those Receipts rather
    than being their sole durable container.
64. Break-Glass Remediation revalidates the complete expected remote state after
    approval and immediately before mutation. Normal Release does not add the
    same second observation under the governed single-writer assumption.
65. Buddy dry-run uses a non-live simulation projection identity for version and
    destination projection and never reserves a live Buddy Intent or preview
    identity.

## Open Decisions

No unresolved top-level terminology decisions remain.
