# Governance System

## Purpose

This record defines how repository and project rules, policies, controls, and review procedures are
created, changed, deferred, enforced, and retired.

It does not define product behavior. Product requirements, architecture, security,
research, and release records retain authority within their own scopes.

## Trust Base

The governance system relies on:

- the repository owner for product scope, risk acceptance, and release decisions;
- Git for repository history and deleted records;
- pull requests and reviews for proposed changes and their rationale;
- branch and workflow controls when they are explicitly enabled;
- applicable license, security, and platform obligations.

No additional governance layer is required to validate this trust base.

## Governance Layers

Repository governance is divided into:

1. **Governance principles:** rules for maintaining other rules.
2. **Domain policies:** project, record, security, experiment, upstream, and release
   policies.
3. **Structured standards:** catalogs and schemas consumed by tools or agents.
4. **Audience interfaces:** contributor guidance and AI-agent instructions.
5. **Controls:** human review, Agent Skills, hk checks, workflows, and repository rules.
6. **Evidence:** pull-request reviews, check results, experiment observations, tags, and
   releases.

A lower layer may implement or summarize a higher layer but must not silently redefine
it.

## Governance Principles

### GOV-001: Consumer-Backed Governance

A rule, record, control, or workflow mechanism must have an identifiable producer,
maintainer, consumer, and failure mode. Information that lacks a decision-relevant
consumer must not be retained solely because it may become useful.

### GOV-002: One Canonical Authority

A concern must have one manually maintained canonical authority. Summaries, indexes,
audience-specific interfaces, and generated views may route to that authority but must not
become independent sources.

### GOV-003: Minimum Sufficient Mechanism

Use the least costly mechanism that reliably prevents the identified failure. Prefer, in
order, ordinary guidance, human review, repeatable Agent review, advisory mechanical
checks, blocking checks, and platform enforcement.

The order is not a maturity ladder. A contextual judgment remains a review concern even
when it is important.

### GOV-004: Preserve Human Judgment

Do not convert product value, architecture quality, evidence sufficiency, risk acceptance,
scope, or release judgment into a mechanical rule merely because the decision matters.

Use hk for deterministic invariants, Agent Skills for repeatable contextual review, and
the repository owner for value, risk, scope, and release decisions.

### GOV-005: Controls Implement Policy

A control must identify the policy or invariant it enforces. A tool, workflow, template,
or Agent Skill must not create an undeclared policy through its implementation.

If a control and its governing policy disagree, the control is defective until the
repository owner accepts a policy change.

### GOV-006: Atomic Governance Changes

A governance change should update its directly affected policies, structured standards,
audience interfaces, controls, and current records in the same accepted change.

Temporary dual authorities are not an acceptable migration strategy.

### GOV-007: Trigger-Bound Deferral

A scheduled mechanism must identify an observable activation trigger, an evaluator, the
action to take when triggered, and a named fallback review event.

A conditional mechanism must identify an observable trigger and fallback review. A
speculative idea without a decision-relevant trigger is discarded rather than placed in
an indefinite backlog.

### GOV-008: Merge Defines Current Repository State

Content merged into `main` is the current accepted repository state within the scope
and evidence level the content declares.

Acceptance does not change epistemic meaning. A merged hypothesis remains a hypothesis;
a merged research result does not become a product commitment; and an unreleased
requirement does not become a support promise.

### GOV-009: Git Retains History

Replaced records are deleted by default after current consumers and links are migrated.
Git retains their history.

A retired record remains in the current tree only when a current consumer requires it,
such as a simultaneously supported contract version or evidence referenced by a current
conclusion.

### GOV-010: Gates Require Evidence

A claimed gate is effective only when its execution and result have a defined carrier.
Examples include a GitHub review, a check result, a Delivery Wave change, a release
manifest, or a tag.

### GOV-011: Separate Findings From Disposition

A reviewer must not solely adjudicate its own material findings. A reviewer independent
of the originating review, change author, and implementation agent must classify each
material finding as a true positive, false positive, or unresolved and record the
evidence for that classification in the pull request or other governing review carrier.

The repository owner decides the disposition of unresolved findings and any finding that
requires a value, risk, scope, governance, or release decision.

## Rule Classification

Rules fall into four enforcement classes:

- **Mechanical:** deterministic and suitable for hk or another tool.
- **Procedural review:** stable review method with contextual judgment, suitable for an
  Agent Skill.
- **Owner decision:** product value, risk, scope, governance authority, or release.
- **Hybrid:** mechanical prerequisites followed by contextual review and, when required,
  an owner decision.

New custom mechanical checks begin as advisory unless their behavior and remediation are
already deterministic and well exercised.

## Changing Rules

A normal policy or control change uses a reviewable pull request and the review procedure
applicable to the affected scope.

The accepted target-branch versions of `AGENTS.md`, this policy, the record-system
policy, the control catalog, and the applicable review Skill govern review of a proposed
governance amendment. Proposed versions are review subjects before merge. They may impose
additional validation on the proposal, but they cannot waive or weaken an accepted
obligation during their own review. If an accepted authority needed for review is
unavailable or ambiguous, stop and request repository-owner disposition.

A change to this governance system must also:

- identify a concrete failure or changed project need;
- name the affected policies, controls, interfaces, and records;
- define any required migration;
- preserve a valid repository state at merge;
- receive explicit repository-owner disposition.

A high-cost change to authority, trust, or acceptance semantics may use an architecture
decision record. A separate governance-decision record family is not required.

The current governance rules apply while a proposed amendment is under review. Merge
makes the amended rules current; Git preserves the previous version.

## Review Triggers

Review this governance system when:

- the same rule repeatedly needs an exception;
- a check is frequently bypassed or produces material false positives;
- Agent and human reviews repeatedly disagree about the same procedure;
- a policy has no current consumer or enforcement point;
- a control begins to expand policy;
- a new contributor cannot recover the work model from the repository;
- a merged Delivery Wave change exposes missing or duplicated authority;
- maintaining the governance system costs more than the failures it prevents.

Every merge that changes `docs/delivery-wave.md` is a fallback opportunity to identify
such conditions. A review that finds no required change does not create a separate
report.

## Conflict Resolution

When two records claim authority for the same concern, or an interface or control
conflicts with its governing policy, work that depends on the conflict must stop.

The repository owner resolves the authority boundary through a reviewable change. Agents
and automation must not choose a winner by inference.

## Source

Adapted from [the pinned Governance System][source] at
`1a02498589769c38bc16eefc2efc5f9eca6e6994`. GOV-001 through GOV-011 preserve
its wording; only `main-v2` becomes `main` in GOV-008. Repository scope and
project namespaces are the local adaptations. The source
[copyright and license notice](reference-license.txt) is retained.

[source]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/governance/governance-system.md
