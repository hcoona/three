# Repository Record System

## Purpose

This record defines how durable project information is assigned to repository files,
structured records, Git, GitHub, Agent Skills, and automated controls.

The system follows the governance principles in
[`governance-system.md`](governance-system.md).

## Authority by Concern

Authority is scoped rather than globally ranked.

| Concern | Canonical carrier |
| --- | --- |
| Public project identity and release posture | `docs/governance/project.md` |
| Public navigation to canonical records | Root `README.md` |
| AI-agent behavior | Root `AGENTS.md` |
| Human contribution workflow | Root `CONTRIBUTING.md` |
| Current positive work authorization | `docs/delivery-wave.md` |
| Product purpose and directional boundary | Product vision |
| User context and motivation for required behavior | Product user stories |
| Required product behavior | Product requirements |
| Current system structure and invariants | Architecture records |
| Durable design choices and rationale | Accepted decision records |
| Public source and empirical evidence | Research records |
| Security assumptions and trust boundaries | Security records |
| Validation needed for a claim | Validation records |
| Upstream relationship and imports | Root `UPSTREAM.md` |
| Issue-backed work scope, ownership, discussion, dependencies, and progress | GitHub Issues and Milestones |
| Direct single-PR work scope and proposed repository state | Pull requests |
| Current accepted repository state | `main-v2` |
| Published state | Git tags and GitHub Releases |
| Authorship, changes, and deleted records | Git history |

An index or overview may identify the authoritative record but must not restate its
normative content.

## Record Admission

Before adding a record or control, identify:

1. the concern it owns;
2. who or what produces it;
3. who maintains it when the concern changes;
4. the human, Agent, or automation consumer and its use point;
5. the failure that occurs if the record is absent;
6. why an existing carrier cannot serve the same purpose.

If these questions do not have concrete answers, do not add the record.

## Granularity

Split information when parts have independent producers, consumers, acceptance,
versioning, security boundaries, or retirement conditions.

Keep information together when it must be reviewed as one decision, shares the same
lifecycle, and would require duplicated context if separated.

File length is evidence that a review may be useful, not a mechanical split rule.

A directory may establish a stable authority namespace even when it initially contains
one record, provided the domain is already distinct and is expected to gain another
consumer or record within the next one or two iterations.

## Formats

Choose the canonical format by the primary operation:

| Primary operation | Format |
| --- | --- |
| Explanation, rationale, policy, architecture, or synthesis | Markdown |
| Small human-maintained structured state | YAML |
| Strict machine contract or interchange | JSON with JSON Schema |
| Repeated homogeneous observations | JSONL |
| Flat analytical data | CSV when nested structure is unnecessary |
| Dependencies and supply-chain data | An established format such as CycloneDX or SPDX |
| Test and scanner output | The tool's standard format, such as JUnit or SARIF |
| Executable threat-model input | The modeling tool's native format, with native open/analysis validation recorded in the pull request |

Do not maintain a Markdown copy of a structured record. A narrative report may summarize
structured evidence when it adds a distinct conclusion, limitation, or decision impact.

Structured records require a real consumer, a schema or equivalent typed contract, and a
defined validation point.

Generated dependency lock files retain their generator's content. The repository
normalizes their line endings to LF; no other byte is rewritten. Repository text checks
may inspect them but must not rewrite them; a changed lock is reviewed as a regenerated
dependency selection.

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
authorization only when merged into `main-v2`. Deleting an entry through merge ends its
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

Every merged change to `docs/delivery-wave.md` evaluates the current mutable-source
recheck registry, even when no research file changes.

## Lifecycle Through Git and GitHub

- An Issue expresses a problem, question, proposed work item, or bounded work carrier.
- A pull-request branch expresses a repository proposal and may be the direct work
  carrier when this policy permits.
- A branch may propose a changed `docs/delivery-wave.md`, but work authorization
  continues to come from the target branch's accepted copy until merge.
- Merge into `main-v2` accepts the changed records as current within their declared
  scopes.
- A tag and GitHub Release identify published state.
- Git history retains replaced and deleted records.

Ordinary records do not need `accepted`, `effective`, `author`, `last_updated`, branch, or
revision-history metadata.

Rejected proposals remain in closed Issues or pull requests. They do not need a rejected
record in `main-v2`.

## Deletion and Replacement

Delete a replaced record after:

- all current consumers have moved;
- internal links have been updated;
- unique current rationale or evidence has moved to its new authority;
- applicable checks pass.

Do not create a general archive or supersession graph. Retain multiple versions only when
current consumers require them.

## Current, Scheduled, Conditional, and Discarded Mechanisms

- **Current:** has an active producer and consumer.
- **Scheduled:** has an observable event trigger, evaluator, activation action, and named
  fallback review.
- **Conditional:** may be needed if an observable condition occurs.
- **Discarded:** lacks sufficient value, consumer, or trigger.

Current and scheduled record families and controls may be represented in structured
catalogs when those catalogs are consumed by hk or Agent routing.
Conditional mechanisms remain in this policy or another relevant policy until activated.

## Workflows

Long-term information is organized by stable concern. Current positive work authorization
is represented by `docs/delivery-wave.md`; chronology and progress are represented by
Issues, Milestones, pull requests, commits, and releases.

Issues and Milestones may show proposed, active, blocked, and completed work without
granting or changing authorization.

Requirements are organized by capability rather than by delivery sequence or Wave.
Architecture uses a global overview and scoped views. Designs are created only for
coherent implementation boundaries that need independent review. Research is organized
by question and baseline; experiments are organized by reproducible protocol.

Requirement identifiers are permanent once merged into the accepted target branch. In
`V2-REQ-NNN`, `NNN` is three digits; a requirement inserted after an established
identifier may instead append one uppercase letter to it, as in `V2-REQ-011A`. An
active requirement heading uses `V2-REQ-NNN: Nonempty title`. If a requirement is
retired, replaced, or moved, retain its identifier exactly once as a nonnormative
`V2-REQ-NNN: Retired - current authority: <requirement ID or repository path#anchor>`
marker. Never assign an established identifier to a different requirement. Mechanical
checks enforce the two representations and target existence; review determines whether
the requirement's meaning was preserved.

### Decision Records

An accepted decision record in `docs/decisions/` is required for a choice that constrains
a future Slice, the product boundary, or a public contract. Another durable choice with
high reversal cost or non-obvious rationale may also use one. A repository-owner choice
confined to one Slice stays in that Slice's design and links to the carrier where the
owner made it. Earlier owner choices are not converted retroactively.

### Decision Record Numbers

Each accepted decision record in `docs/decisions/` has a unique four-digit number, and
its file name begins with that number followed by a hyphen. Never reuse a number for a
different decision, even after the earlier record is deleted. Mechanical checks enforce
uniqueness among current records; review determines whether a number was reused.

## Enforcement

Use:

- hk for deterministic syntax, schema, identifier, path, reference, and secret-shape
  checks;
- Agent Skills for repeatable contextual review;
- human review for product value, risk, scope, and release decisions.

The repository checker discovers record candidates independently from the mutable family
catalog. Its bounded record roots are the canonical root interfaces and the `docs/`,
`schemas/`, and `contracts/` namespaces. Adding another record-bearing root
requires updating the checker and catalog in the same change. A catalog edit therefore
cannot remove a retained record from family, schema, or portal validation merely by
deleting its family entry. Governed records must use direct repository paths; symbolic
links are rejected rather than treated as authority routing. The documentation portal
routes every retained record directly.

Every current hk control maps each declared hk execution point to the concrete step names
that implement it. The repository checker evaluates hk's actual `pre-commit` and `check`
plans and verifies those steps are reachable at the declared execution point.

The full CI hk set must include every check in the local fast subset, except a local-only
step that checks the local staged snapshot, which a CI checkout does not have. The
control that a local-only step implements declares it in `controls.yaml`, and the
repository checker verifies the subset relation against both plans. Local hooks may omit
slower checks but must not implement different semantics.

Agent Skills reference this policy and the policies for their domains. They do not become
independent authorities.

## Record-System Gate

A record-system change is complete only when:

- each retained concern has one canonical authority;
- active structured records pass their schemas;
- current and scheduled mechanisms identify their producer, maintainer, consumer, failure
  mode, and review or execution point;
- scheduled mechanisms identify their trigger, evaluator, activation action, and
  named fallback review;
- audience interfaces and controls agree with their governing policies;
- replaced records and broken references have been removed;
- mechanical checks and required contextual reviews have recorded results.

The repository owner determines whether the gate has passed from those results.
