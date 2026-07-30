# Workflow Delivery v3 Requirements

## Status

Architecture version: **v3**.

Review state: **Confirmed on 2026-07-30**.

This page is the normative product and system requirements baseline for the
clean v3 implementation line. It defines what Workflow Delivery must achieve,
not the internal structure used to achieve it.

The [High-Level Design](./high-level-design.md) describes the architectural
realization. Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

Requirement identifiers are stable traceability anchors. Later design and
acceptance artifacts may refine a requirement, but they must not silently
weaken or reinterpret it.

## Mission

Workflow Delivery must provide evidence-driven software delivery governance for
a polyglot monorepo.

- CI Qualification determines whether an immutable change candidate satisfies
  every applicable quality obligation.
- Release Delivery independently rebuilds and qualifies an immutable Release
  Unit, obtains explicit authorization, and performs traceable external
  publication.

When requirements conflict, use this priority order:

1. security and correctness;
2. traceability and explainability;
3. evolvability and recoverability; and
4. latency and operating cost.

## Design Assumptions

The design may rely on documented guarantees owned by lower platform layers,
including immutable Git commit identity, protected-environment review, OIDC
claims, and destination API contracts. Workflow Delivery must validate bindings
that it creates, but it must not reimplement a lower layer merely to prove the
lower layer's own contract.

If a required guarantee is unavailable at the layer that must own it, the
affected capability is unsupported or blocked. Application logic must not
simulate a weaker substitute and present it as equivalent assurance.

Security controls must address realistic threats and balance risk reduction
against implementation and maintenance cost.

## System Requirements

### System Scope and Separation

- **WD-SYS-001:** Workflow Delivery must provide CI Qualification and Release
  Delivery as separate business capabilities with independent runtime Plans,
  Evidence, artifacts, state, and verdicts.
- **WD-SYS-002:** The two capabilities must share normalized repository facts,
  Build Definitions, and mechanism-level adapters where semantics are
  genuinely common.
- **WD-SYS-003:** Shared mechanisms must not own CI scope policy, release
  channel policy, approval policy, or final business decisions.
- **WD-SYS-004:** Delivery authority must be controlled by an external
  governance boundary. A business workflow may request authority but must not
  grant final authority to itself.
- **WD-SYS-005:** Every authoritative operation must bind an immutable target
  identity and the complete scope of the decision or side effect.
- **WD-SYS-006:** Unknown, unclassified, incomplete, or conflicting required
  scope must block a successful decision.

### CI Qualification

- **WD-CI-001:** CI must identify the exact candidate tree under evaluation,
  including the applicable base, head, tested merge, merge-group, or push
  revision identity.
- **WD-CI-002:** CI must map changed paths through discovered Project Nodes,
  dependency relationships, and global inputs to the affected Release Unit
  closure before execution.
- **WD-CI-003:** CI must close the complete Qualification Target before
  execution. Executors must not add, remove, substitute, or downgrade planned
  obligations.
- **WD-CI-004:** CI must build every publishable artifact variant of each
  affected Release Unit.
- **WD-CI-005:** CI must execute every applicable required quality obligation
  and distinguish required outcomes from advisory outcomes.
- **WD-CI-006:** CI success requires admitted, satisfied Evidence for every
  required obligation. Missing, skipped, canceled, timed-out, unknown, and
  conflicting outcomes must not become success.
- **WD-CI-007:** CI must produce an immutable, explainable Final Decision and
  project the latest authoritative result through the required GitHub check.
- **WD-CI-008:** CI must not authorize Release or perform publication side
  effects.

### Release Delivery

- **WD-REL-001:** Release must begin from an explicit Release Intent for one
  Release Unit, immutable target commit, and release channel.
- **WD-REL-002:** Release must verify target and channel eligibility through
  Delivery Governance before live publication can be authorized.
- **WD-REL-003:** Release must independently derive its complete Release Unit
  closure, build its outputs, and execute all Release quality obligations.
- **WD-REL-004:** Release must not consume CI Plans, Evidence, artifacts,
  checks, or verdicts as Release qualification inputs.
- **WD-REL-005:** CI and Release must use the same Build Definition for the same
  artifact variant, while materializing separate builds for their distinct
  immutable targets and purposes.
- **WD-REL-006:** Release outputs must be bit-for-bit reproducible for
  identical target, Build Definition, toolchain, and declared inputs. The
  system is not required to certify reproducibility by performing duplicate
  builds.
- **WD-REL-007:** Publication authorization must bind the exact artifact bytes,
  provenance, destination observations, intended actions, qualification
  decision, and required destination capabilities.
- **WD-REL-008:** Release must obtain short-lived, destination-specific
  Publication Capabilities only after qualification and observation establish
  the exact authorized action.
- **WD-REL-009:** Release must record a Receipt for every completed destination
  side effect and an explainable final outcome for the Release Attempt.

### Buddy and Official Channels

- **WD-CHN-001:** Buddy must produce distributable, non-authoritative previews
  through identities, destinations, and capabilities isolated from Official.
- **WD-CHN-002:** Official must publish the canonical version for a target
  commit that Delivery Governance recognizes as authoritative.
- **WD-CHN-003:** Buddy artifacts, Evidence, Decisions, and Receipts must not be
  promoted or reclassified as Official.
- **WD-CHN-004:** Non-authoritative branches may exercise Official dry-run
  behavior but must not obtain live Official publication capability.

### Governed Control Code

- **WD-AUTH-001:** CI decision code must come from the tested candidate
  revision. Release decision code must come from the exact protected target
  revision being released.
- **WD-AUTH-002:** Changes to the Decision Kernel, workflow control code,
  authoritative record contracts, or minimum policy must require
  Governance-configured owner review before merge or live Release eligibility.
- **WD-AUTH-003:** Merging a reviewed control-code change makes that code
  eligible only as part of the resulting new candidate or Release target
  revision. No independent runtime promotion protocol is required.
- **WD-AUTH-004:** CI execution must not receive publication capability, and
  Release execution must not receive live capability until the target revision
  satisfies protected-ref and environment policy.
- **WD-AUTH-005:** Delivery Governance must control protected target
  eligibility, control-code review, protected environment review, OIDC and
  destination trust, capability grant and revocation, and Break-Glass
  Remediation approval.

### Trust and Capability Isolation

- **WD-SEC-001:** Runtime execution of target-controlled code and possession of
  publication authority must never coexist in one trust boundary.
- **WD-SEC-002:** Authoritative planning, Evidence Admission, and final
  decision logic must not execute target-controlled code or hold publication
  credentials.
- **WD-SEC-003:** Publication execution must consume only verified immutable
  artifacts and a fully materialized, authorized publication description. It
  must not execute target-controlled code.
- **WD-SEC-004:** Publication Capability must be scoped to the destination,
  identity, and action being authorized.
- **WD-SEC-005:** Failure to obtain the required OIDC identity or Publication
  Capability must block the affected side effect. No long-lived credential
  fallback is permitted.

### Evidence, Decisions, and Explanation

- **WD-EVD-001:** Evidence Admission must verify exact ownership, target,
  obligation, artifact, attempt, and integrity bindings without rerunning the
  quality command.
- **WD-EVD-002:** Final Decisions must be append-only. GitHub checks and human
  summaries are projections, not the authoritative audit record.
- **WD-EVD-003:** CI explanation must connect changed paths, Project Nodes,
  dependency relationships, Release Units, variants, obligations, Evidence,
  outcomes, and the verdict.
- **WD-EVD-004:** Release explanation must connect target, version, channel,
  artifacts, destinations, observations, actions, Receipts, authority,
  authorization, outcome, and allowed operator actions.
- **WD-EVD-005:** Authoritative Plans, Evidence, Decisions, artifact identities,
  and Receipts must persist before a later stage relies on them. Optional
  telemetry may fail without changing the business verdict.

### Observation, Replay, and Recovery

- **WD-OPS-001:** Every Release Attempt must observe all destinations before
  requesting publication capability.
- **WD-OPS-002:** Absent destination state may publish, exact satisfied state
  must skip the side effect, and partial, unknown, conflicting, or unprovable
  state must fail closed.
- **WD-OPS-003:** Release retry must use whole-release replay. GitHub
  `Re-run failed jobs` is not a supported recovery protocol.
- **WD-OPS-004:** Every replay must rerun planning, build, qualification,
  authorization checks, observation, and reporting for the complete Release
  Attempt.
- **WD-OPS-005:** A control-code fix creates a new candidate or Release target
  revision. Ordinary replay of an older target must continue using that
  target's original control code.
- **WD-OPS-006:** Publication must use append-only Saga semantics. A successful
  destination must not be automatically rolled back solely because another
  destination fails.
- **WD-OPS-007:** Reconciliation must be exceptional handling for destination
  state that cannot safely proceed through normal observation and replay.
- **WD-OPS-008:** Break-Glass Remediation must be separately approved, use
  expected-state checks and scoped capability, and record append-only
  before-and-after state without rewriting the original Release history.

### Concurrency

- **WD-CON-001:** CI may cancel runs superseded by a newer candidate identity.
- **WD-CON-002:** Release must serialize operations that share an Official
  canonical identity or Buddy preview identity.
- **WD-CON-003:** An in-progress Release execution must not be automatically
  canceled.
- **WD-CON-004:** Different release versions may execute concurrently unless a
  destination declares a wider mutable-resource lock.
- **WD-CON-005:** Remediation must acquire the original Release and destination
  locks.
- **WD-CON-006:** Duplicate pending Release requests must be rejected or
  coalesced rather than accumulated as an unbounded workflow queue.

### Retention and Platform State

- **WD-RET-001:** Caches must be treated as non-authoritative performance
  mechanisms.
- **WD-RET-002:** Workflow Delivery must not assume that GitHub Actions
  artifacts or logs outlive the configured platform retention window.
- **WD-RET-003:** Longer-lived release identity and provenance may rely on Git
  tags, destination records, GitHub Releases when selected, and GitHub Artifact
  Attestations.
- **WD-RET-004:** The initial scope must not require a permanent external
  Release ledger or a GitHub Release audit anchor for every Release Unit.
- **WD-RET-005:** If required state can no longer be established after
  operational records expire, the affected operation must fail closed.

## Quality Attributes

- **WD-NFR-001 - Security and correctness:** Security and correctness dominate
  availability and latency when the qualities conflict.
- **WD-NFR-002 - Explainability:** Operators and reviewers must be able to
  understand why scope, obligations, authorization, actions, and verdicts were
  selected.
- **WD-NFR-003 - Evolvability:** Adding an ecosystem or destination should
  normally require an adapter and policy mapping, not changes to cross-system
  authority semantics.
- **WD-NFR-004 - Recoverability:** Retry and remediation must preserve identity,
  authority, and append-only history across partial external side effects.
- **WD-NFR-005 - CI latency:** Ordinary pull-request CI has a P95 12-minute
  Final Decision objective. Broad authority, policy, toolchain, and
  multi-Release-Unit changes are measured separately.
- **WD-NFR-006 - Performance safety:** Performance work must not weaken
  obligation coverage, artifact variant coverage, Evidence Admission, or
  authorization.

## Non-Goals

Workflow Delivery v3 does not:

- replace ecosystem build and package-management tools;
- become a general workflow engine;
- provide distributed transactions across destinations;
- promote pull-request artifacts into Release;
- consume CI results as Release Evidence;
- certify reproducible builds through duplicate building;
- provide a permanent external Release ledger in the initial scope; or
- use ordinary Release force flags to rewrite published history.

## Requirements Stage Exit

The requirements stage is complete when:

1. every requirement is accepted, rejected, or explicitly marked for later
   scope;
2. the HLD maps each accepted requirement group to an owning architectural
   element;
3. assumptions owned by GitHub, identity providers, and destinations are
   documented at the appropriate contract boundary;
4. unresolved product policy is not hidden inside implementation detail; and
5. no later design document weakens these requirements without an explicit
   requirements change.
