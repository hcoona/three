# Governance Migration Bootstrap

## Purpose and Authority

This record governs the migration of Three's repository records and development
controls. The repository owner produces and maintains it; contributors, agents,
and reviewers consume it before migration work. Its failure mode is migration
without recoverable authority, scope, or acceptance evidence.

The target branch's accepted copy, normally on `main`, governs migration work.
The repository owner's instruction permits preparing and reviewing this initial
bootstrap proposal. The proposed Wave does not authorize its work before merge.
Report the completed proposal and every source adaptation to the repository owner
for confirmation before acceptance.

This bootstrap governs migration procedure; the [Delivery Wave](../delivery-wave.md)
governs current positive migration-work authorization. It does not authorize
unrelated project work or replace product behavior, contracts, evidence, or runtime
release authorization. Existing record and control changes require an authorized
advancement and atomic migration of their affected consumers.

## Source and Adaptations

The source is `hcoona/microsoft-authentication-cli` at commit
`1a02498589769c38bc16eefc2efc5f9eca6e6994`:

- [Governance System][source-governance]: trust base, GOV-001 through GOV-011,
  and rule classification.
- [Repository Record System][source-records]: record admission, authorization,
  concurrency, work carriers, and lifecycle.
- [Record-System Review][source-review]: independent review and finding triage.
- [Delivery Wave][source-wave]: the separate current-authorization record.

GOV-001 through GOV-011 retain the source wording; the only substitution is
`main-v2` to `main` in GOV-008. The local adaptations are the migration-only
scope, separate project document roots, and session-to-work-carrier coordination.
Any proposed principle change requires an explicit repository-owner decision;
it must not be introduced as a monorepo or tooling adaptation.

The source [copyright and license notice](./reference-license.txt) is retained.

## Trust Base

The governance system relies on:

- the repository owner for product scope, risk acceptance, and release decisions;
- Git for repository history and deleted records;
- pull requests and reviews for proposed changes and their rationale;
- branch and workflow controls when they are explicitly enabled;
- applicable license, security, and platform obligations.

No additional governance layer is required to validate this trust base.

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

## Work Authorization and Concurrency

The target branch's accepted `docs/delivery-wave.md` is the sole positive
work-authorization authority. Each entry identifies accepted inputs, one bounded
advancement and outcome, material exclusions, and any permitted external effects.

A work item is executable only when:

- an accepted Delivery Wave entry grants that work and remains present;
- the work stays inside the entry's advancement, outcome, exclusions, and effects;
- its requirement, architecture, evidence, or other declared prerequisites are already
  accepted;
- any policy-required repository-owner risk disposition is recorded in the entry; and
- it does not depend on an unaccepted result.

Relied-on prerequisites and shared canonical authorities must remain current through
acceptance. If the target branch supersedes or materially changes one, dependent work
must pause until its scope and gate evidence are reevaluated and its validation and
review are refreshed against the current authority.

An Issue, Milestone, branch, pull request, comment, label, or unmerged Wave edit cannot
grant or enlarge work authorization. Adding or changing a Wave entry grants or changes
authorization only when merged into `main`. Deleting an entry through merge ends its
authorization. Git and the proposing pull request retain the reason and history; the
current Wave does not retain progress or historical status.

Preparing and reviewing an explicitly repository-owner-approved pull request whose sole
substantive purpose is to add, change, delete, or replace Delivery Wave entries is a
permitted control-plane operation and does not require an existing entry. It cannot
perform newly proposed substantive work. New or enlarged authorization begins only after
the Wave change merges.

Use an Issue when work spans multiple pull requests or contributors, needs dependency or
progress coordination, or benefits from separate proposal discussion. A bounded change
may use its pull request as the work carrier when the accepted Wave entry already
contains sufficient scope. Work carriers do not become authorization authorities.

Independent entries may proceed concurrently. Work remains ordered when it has an
unresolved dependency, modifies the same canonical authority under conflicting
assumptions, or requires an earlier owner decision. A material change to a shared
prerequisite pauses only the entries that rely on it.

There is no global delivery phase. Each Slice follows its own accepted dependencies.
Implementation requires accepted Slice requirements, applicable architecture and
contracts, a validation basis, explicit unsupported cases, and any additional
preimplementation condition owned by an applicable domain record. Implementation of one
Slice may overlap requirements or architecture work for another Slice whose own
advancement is authorized.

One or more pull requests may contribute to a bounded outcome. When that outcome is
accepted, or the repository owner decides not to continue it, an accepted Wave change
deletes the entry. That deletion ends only the current grant; later defects, changed
requirements, or further advancement require a new or amended entry.

A Delivery Wave has no fixed duration. A replacement Wave selects a new finite set.
Unfinished work continues only when the new accepted file contains an entry authorizing
its next bounded outcome. Omission ends the old grant; it is not automatic carry-over.

## Project Records

Repository-wide policies have one repository-wide authority. Each project keeps
its own product, user-story, requirement, architecture, research, and validation
records in its own document root. Shared libraries and engineering tools may be
projects. A package or build manifest does not by itself define that boundary.
A cross-project change references the affected authorities instead of copying
project knowledge into a shared progress document.

## Record Admission

Before adding a record or control, identify:

1. the concern it owns;
2. who or what produces it;
3. who maintains it when the concern changes;
4. the human, Agent, or automation consumer and its use point;
5. the failure that occurs if the record is absent;
6. why an existing carrier cannot serve the same purpose.

If these questions do not have concrete answers, do not add the record.

## Review and Owner Disposition

The accepted target-branch versions of the applicable instructions, policies,
controls, and review procedures govern an amendment. Proposed versions are review
subjects before merge; they may add validation but cannot waive an accepted
obligation. The initial bootstrap is reviewed against the repository owner's
instruction, existing applicable repository instructions, and the pinned sources.

Before accepting a bootstrap, Wave, or migration change:

1. Verify authorization, accepted prerequisites, scope, and external effects.
2. Check each retained concern for one authority, actual consumers, appropriate
   granularity, and the smallest sufficient mechanism.
3. Run applicable existing hk checks and record their results against the reviewed
   change. Mechanical checks do not establish contextual correctness.
4. Obtain record-system review independent of the change author and implementation
   agent, using the pinned [review procedure][source-review] and the applicable
   local authorities. Source-repository paths and catalogs are reference context;
   do not claim they are installed Three controls.
5. Record material findings with their governing rule, severity, confidence,
   location, evidence, and smallest required action. Apply GOV-011 before a finding
   drives remediation or dismissal.
6. Obtain repository-owner disposition for unresolved findings, owner decisions,
   and acceptance of changed governance or work authorization.

The applicable pull request carries the validation, review, independent triage,
and required owner-disposition evidence. A local proposal retains that evidence
for inclusion in its pull request. No gate is passed merely because a session
reports success. Every merged Wave change is a governance-review fallback and
also evaluates any current mutable-source recheck registry.

## Coordination and Evidence

Use the accepted bootstrap and Wave to determine authority; the Issue or direct
pull request to recover scope, discussion, dependencies, and progress; and Git,
checks, and reviews to recover actual changes and acceptance evidence.

Associate an executing or reviewing session with its work carrier and branch or
worktree. Sessions and any local lookup index are execution aids, not authorities
or a second progress ledger. Do not put session IDs, retries, check results, or
historical status in the Wave, or commit changes solely to track those events.

Before continuing or replacing a session, inspect its activity, the actual
worktree and target-branch state, and the work carrier's current evidence and
unresolved findings. A stopped session does not establish completion or prove
that no changes occurred. Preserve existing work and refresh affected evidence
when its accepted prerequisite changes. A replacement session must recover the
next bounded action from these carriers rather than infer new authorization.

## Replacement

Preparation may proceed in bounded changes while preserving a valid accepted
repository state. The repository owner accepts the final replacement only when
retained concerns have one authority, project records remain separately located,
current consumers and links have migrated, and required checks and independent
reviews have recorded results.

Retire this bootstrap in the same accepted change that installs its replacement
and migrates its consumers. Preserve the inherited principles unless the owner
explicitly accepts a stated amendment. Git and the proposing pull requests retain
history; no parallel control authority or migration progress archive is required.

[source-governance]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/governance/governance-system.md
[source-records]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/governance/record-system.md
[source-review]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/.github/skills/record-system-review/SKILL.md
[source-wave]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/delivery-wave.md
