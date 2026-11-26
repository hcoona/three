# Repository Record System

## Purpose

This record defines how durable project information is assigned to repository files,
structured records, Git, GitHub, Agent Skills, and automated controls.

The system follows the governance principles in
[`governance-system.md`](governance-system.md).

## Authority by Concern

Authority is scoped rather than globally ranked.

| Concern                                                                            | Canonical carrier                                             |
| ---------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Repository identity, layout, and imported-project provenance                       | Root `README.md` and repository engineering records           |
| Public navigation to canonical records                                             | Root and project `README.md` files                            |
| AI-agent behavior and routing                                                      | Root `AGENTS.md` and applicable local instructions            |
| Human contribution workflow                                                        | Root `CONTRIBUTING.md`                                        |
| Current positive work authorization                                                | Root `docs/delivery-wave.md`                                  |
| Repository governance and record policy                                            | `docs/governance/`                                            |
| Product purpose and directional boundary                                           | Project-local product vision, or README when sufficient       |
| User context and motivation for required behavior                                  | Project-local user stories, when admitted for a real consumer |
| Required product behavior                                                          | Project-local requirements                                    |
| Current system structure and invariants                                            | Project-local architecture records                            |
| Durable design choices and rationale                                               | Decision records in the owning namespace                      |
| Public source and empirical evidence                                               | Research records in the owning namespace                      |
| Security assumptions and trust boundaries                                          | Security records in the owning namespace                      |
| Validation needed for a claim                                                      | Validation records in the owning namespace                    |
| Upstream relationship and imports                                                  | Existing project provenance and license notices               |
| Issue-backed work scope, ownership, discussion, dependencies, and progress         | GitHub Issues and Milestones                                  |
| Direct single-PR work scope and proposed repository state                          | Pull requests                                                 |
| Current accepted repository state                                                  | `main`                                                        |
| Published state                                                                    | Git tags and GitHub Releases                                  |
| Authorship, changes, and deleted records                                           | Git history                                                   |
| Retired-project summaries, rationale, alternatives, and last-intact Git tree links | Root `docs/retired-projects.md`                               |

An index or overview may identify the authoritative record but must not restate its
normative content.

## Project Namespaces

There is one repository authority for generic governance and work authorization.
A project owns its product, architecture, decisions, research, security, and
validation records under its own `<project-root>/docs/` when those records are
needed. Development workflow, Delivery Wave semantics, local iteration freedom,
and review triggers are uniform across the repository; project records own
domain behavior, external/paid or privacy risk boundaries, and criteria for
accepting formal or release evidence, and must not impose workflow gates,
`main`-merge preconditions, or run-count limits on local harmless development
and debugging. Shared libraries, engineering tools, and standalone skills can
own component namespaces; a package, language, directory prefix, test assembly,
or build manifest alone does not define that boundary, and test roots, generated
deployment roots, or sub-concern folders do not form separate namespaces. The
owner performs the source's owner and maintainer roles.

The [family catalog](record-families.yaml) routes records and project namespaces;
it is not a second workspace or product database. Create concern directories
only for admitted records. A README-only small project remains valid. A
cross-project change links to each affected authority, using its repository
path and anchor; it does not copy requirements into repository policy.

The family catalog and [documentation portal](../README.md) route retained
concerns to their owning project. A companion component shares its product's
records unless independent consumers justify a separate authority. Existing
package, contract, evidence and skill-source paths remain when their actual
consumers require those interfaces.

The catalog separates shared record responsibilities (`families`) from concrete
project/path ownership (`bindings`). A family owns its producer, maintainer,
consumers, admission trigger, failure mode and review point. A binding names its
family, owning namespace, path, representation and lifecycle; an optional purpose
explains a location-specific role. Keep genuinely different responsibilities in
different families. Adding another location with the same responsibilities does
not require copying the family's review or admission rules. Binding IDs preserve
the former version-2 family-instance IDs; they are not product requirement IDs.

Path coverage does not admit a new concern or establish its semantic authority.
Project portals and the applicable review procedures retain concern-specific
routing even when several record types share a path binding.

## Record Admission

Before adding a record or control, identify:

1. the concern it owns;
2. who or what produces it;
3. who maintains it when the concern changes;
4. the human, Agent, or automation consumer and its use point;
5. the failure that occurs if the record is absent;
6. why an existing carrier cannot serve the same purpose.

If these questions do not have concrete answers, do not add the record.

## User Stories

User stories own user context and motivation for requirements work. They do not
define normative behavior, realization choices, validation procedures, current
support, or work authorization. Those concerns remain with the owning project's
requirements, architecture, validation and support records, and the Delivery Wave.

The repository owner produces and maintains stories with consumer input.
Requirement authors and reviewers use them when deriving behavior from user
goals and scenario context. Validation and release reviewers follow their routes
to the scenario and evidence authorities relevant to the proposed release.

Admit a distinct story record when an accepted Wave requires user goals or a
primary journey whose context the existing capability requirements or README do
not preserve. Evaluate that need during the relevant requirements or journey
review; the Wave review selecting that work is the fallback review. Keep an
admitted record in the owning project's documentation, normally
`<project-root>/docs/product/user-stories.md`, and reconcile its project portal
and family coverage. A README remains sufficient when it serves the actual
consumer; this rule does not require stories for every project.

An existing story section in a project specification may remain with its actual
consumers. Keep motivation distinct from normative requirements in that carrier;
preserve its paths and anchors rather than creating a duplicate story record.

Describe the actor, context, desired goal and motivation. Context variants of
the same goal need not become separate stories. Link the applicable capability
requirements and validation authorities rather than copying acceptance criteria.
A primary journey can explain priority, but its release-blocking evidence gate
belongs in the project's validation authority. Stories do not independently
select implementation mechanisms, platforms or additional release commitments.

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

| Primary operation                                          | Format                                             |
| ---------------------------------------------------------- | -------------------------------------------------- |
| Explanation, rationale, policy, architecture, or synthesis | Markdown                                           |
| Small human-maintained structured state                    | YAML                                               |
| Strict machine contract or interchange                     | JSON with JSON Schema                              |
| Repeated homogeneous observations                          | JSONL                                              |
| Flat analytical data                                       | CSV when nested structure is unnecessary           |
| Dependencies and supply-chain data                         | An established format such as CycloneDX or SPDX    |
| Test and scanner output                                    | The tool's standard format, such as JUnit or SARIF |

Do not maintain a Markdown copy of a structured record. A narrative report may summarize
structured evidence when it adds a distinct conclusion, limitation, or decision impact.

Structured records require a real consumer, a schema or equivalent typed contract, and a
defined validation point.

## Work Authorization and Concurrency

Contract-preserving, non-feature defect and test fixes, CI
troubleshooting/diagnostics, lint/formatting, doc-drift repairs, and routine
Renovate or dependency compatibility adjustments without new external, paid,
credentialed, or privacy effects execute directly through ordinary pull requests
without a Delivery Wave entry.

The target branch's accepted `docs/delivery-wave.md` is the sole positive
work-authorization authority for feature or capability advancements,
architectural or public-contract changes, and external or risk-bearing
operations. Each entry identifies accepted inputs, one bounded advancement and
outcome, material exclusions, and any explicitly permitted external or
risk-bearing effects.

Within authorized work, routine local editing, restore/build/lint/test/pilot
debugging, out-of-repository scratch harnesses, read-only
repository/GitHub/public-source inspection, independent delegated review, and
ordinary Issue/PR/CI validation (including failed-job reruns) are permitted by
default with no attempt quota and need not be repeated in each entry. External
or irreversible side effects—package or release publication, ruleset or
permission changes, non-public or credential-bearing dependency sources or
external services, ambient or external account/secret access or mutation,
persistent host mutations outside repository/toolchain/build/cache/temp state,
destructive fault injection, and paid or non-recoverable campaign
dispatches—remain unauthorized without explicit Delivery Wave authorization/risk
disposition (and accepted protocols with cumulative bounds where empirical or
paid), even inside a test command or CI workflow step.

A work item is executable when:

- either it qualifies as contract-preserving routine maintenance above without
  new external, paid, credentialed, or privacy effects, or an accepted Delivery
  Wave entry grants that work and remains present;
- the work stays inside the applicable scope, exclusions, and permitted external
  effects; and
- any required repository-owner risk disposition and external/paid protocol are
  already accepted before executing external or paid effects.

Relied-on shared canonical authorities must remain current through acceptance.
If the target branch supersedes or materially changes one, dependent work must
pause until its scope and gate evidence are reevaluated and its validation and
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

There is no global delivery phase, and owning records need not merge into `main`
before dependent implementation begins. Requirements, architecture, contracts,
validation criteria, and implementation may evolve concurrently in one branch or
across a stacked pull-request chain, provided each merge into `main` preserves a
valid, self-consistent repository state between implementation and its owning
records (and satisfies any ordered ruleset or external-risk gate). Independent
entries and Slices may proceed concurrently; work remains ordered when it
modifies the same canonical authority under conflicting assumptions, requires an
earlier owner decision, or executes external/paid effects gated on an accepted
protocol. A material change to a shared authority pauses only the work that
relies on it.

One or more pull requests may contribute to a bounded Wave outcome. Close the
entry atomically in its final implementation pull request (or through a
Wave-only pull request on owner stop or explicit closure). That deletion ends
only the current grant; further feature, contract, or external-risk advancement
requires a new or amended entry.

A Delivery Wave has no fixed duration. A replacement Wave selects a new finite set.
Unfinished Wave-scoped work continues only when the new accepted file contains an
entry authorizing its next bounded outcome. Omission ends the old grant; it is
not automatic carry-over.

Evaluate mutable-source rechecks on demand when a change relies on or updates
the governed external interface or when observed runtime behavior contradicts
the recorded contract. Rechecks stay with the concern that consumes them (for
example, [agent execution guidance](../engineering/agent-execution.md) owns the
Codex interface check). Admit a structured registry only with an actual
consumer, typed contract, and review point.

## Lifecycle Through Git and GitHub

- An Issue expresses a problem, question, proposed work item, or bounded work carrier.
- A pull-request branch expresses a repository proposal and may be the direct work
  carrier when this policy permits.
- A branch may propose a changed `docs/delivery-wave.md`, but work authorization
  continues to come from the target branch's accepted copy until merge.
- Merge into `main` accepts the changed records as current within their declared
  scopes.
- A tag and GitHub Release identify published state.
- Git history retains replaced and deleted records.

Ordinary records do not need `accepted`, `effective`, `author`, `last_updated`, branch, or
revision-history metadata.

Rejected proposals remain in closed Issues or pull requests. They do not need a rejected
record in `main`.

## Deletion and Replacement

Delete a replaced record after:

- all current consumers have moved;
- internal links have been updated;
- unique current rationale or evidence has moved to its new authority;
- applicable checks pass.

Do not create a general archive or supersession graph. Retain multiple versions only when
current consumers require them.

A single repository-level retired-projects index (`docs/retired-projects.md`) may record
retired project summaries, retirement rationale, recommended alternatives, and immutable
links to their last intact Git trees without retaining deleted project files in the working tree.

## Current, Scheduled, Conditional, and Discarded Mechanisms

- **Current:** has an active producer and consumer.
- **Scheduled:** has an observable event trigger, evaluator, activation action, and named
  fallback review.
- **Conditional:** may be needed if an observable condition occurs; identify that
  trigger and a named fallback review.
- **Discarded:** lacks sufficient value, consumer, or trigger.

Current and scheduled record families and controls may be represented in structured
catalogs when those catalogs are consumed by hk or Agent routing.
Conditional mechanisms remain in this policy or another relevant policy until activated.
An unmerged catalog change is a proposal, regardless of its declared state.
Catalog completeness and mechanism activation are separate decisions; neither
follows from schema validity.

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

Routine in-scope local and governed CI validation of declared contracts is not
an empirical experiment: it supports claims only about the tested source and
environment, retains chronological CI run/attempt identities when reporting gate
results, and requires no experiment protocol, disposable host, or attempt quota.
Operations with external, credentialed, destructive, or paid effects—or
unmodeled external/platform probes—follow the external-effect and experiment
rules in [Work Authorization and Concurrency](#work-authorization-and-concurrency)
regardless of invocation channel.

Requirement identifiers are permanent once merged into the accepted target branch.
Preserve each project's existing identifier syntax, meaning, and retirement
references. An established identifier remains defined exactly once in its owning
namespace as an active nonempty requirement or a nonnormative retirement marker
naming its current authority. Do not reuse an identifier for another requirement
or impose the source product's `V2-REQ` prefix on other projects.

Local requirement identifiers and decision numbers may repeat in different
project namespaces. Cross-project references include the owning repository path
and anchor. A move preserves the owning namespace and identifier; it does not
require a duplicate marker in a deleted carrier. Existing retirement forms stay
valid. When a new retirement form is needed, use
`<existing ID>: Retired - current authority: <ID or repository path#anchor>`.
Mechanical validation checks representation, namespace uniqueness, preservation
against the accepted base, and target existence; review determines whether the
meaning was preserved.

## Enforcement

Use:

- hk for deterministic syntax, schema, identifier, path, reference, and secret-shape
  checks;
- Agent Skills for repeatable contextual review;
- human review for product value, risk, scope, and release decisions.

The [checker contract](checker-contract.md) defines bounded validation inputs,
commands and review interfaces. The repository's record checker supplies
repeatable mechanical validation and an explicit inventory/report for reviewers.
Run it against an explicit accepted base and candidate snapshot when changing
maintained records, portals, generated interfaces, `apm.lock.yaml`, or catalogs,
moving or deleting paths they reference, or adding or renaming
[discovered record candidates](checker-contract.md#independent-discovery-and-classification),
and record the command, snapshots, and diagnostic summary in the governing pull
request or Issue review carrier. Reviewers resolve unprepared ownership and
inspect semantic admission, requirement meaning, evidence and domain gates; a
clean mechanical report does not decide those concerns. The checker is advisory
and does not install an automatic CI or blocking hook gate. Blocking activation
requires demonstrated snapshot semantics, deterministic remediation and an
explicitly accepted control change.

Contextual reviews evaluate the proposed repository diff and the validity and
provenance of the evidence it relies on, not the prose completeness, formatting
typos, or API write order of pull-request or Issue discussion. Record required
check results, independent reviews, and any disputed or unresolved
[GOV-011](governance-system.md#gov-011-separate-findings-from-disposition)
classifications or owner dispositions once in the governing review carrier
(`record-system-gate` remains on pull-request merge); agreed findings not
requiring an owner decision are verified on the remediation delta by the
originating independent reviewer, and nonmaterial mechanical defects use
mechanical verification.

Candidate discovery must be independent of the mutable family catalog and
include root interfaces, repository and project document roots, hidden legacy
record roots, and generated interfaces. Removing a family must not hide a
record from coverage, schema, identifier, or portal validation. Manually
maintained authorities use direct repository paths; symbolic links cannot
become canonical authority routing. Generated deployments remain classified
consumers of their source.

Every current hk control maps each declared execution point to concrete reachable
steps. Validate actual `pre-commit` and `check` plans, their profile and file
selection, and CI invocation. Three's stashing and index behavior must be
validated before claiming a check covers the staged or reviewed snapshot.

The full CI hk set must include every check in the local fast subset. Local hooks may omit
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

## Source

Adapted from [the pinned Repository Record System][source] at
`1a02498589769c38bc16eefc2efc5f9eca6e6994`; the source
[copyright and license notice](reference-license.txt) is retained. Adaptations
are repository/project namespaces, `main`, preservation of local identifiers,
Three's existing tool and provenance carriers, narrow mutable-source routing,
and explicit validation and control activation boundaries.

The User Story role, admission and requirement/validation routing additionally
derive from the [upstream story record][story-source] and
[record-family entry][story-family-source] at
`f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64`. Three adds repository-wide workflow
precedence over project records, merge-time consistency between implementation
and owning records, external-effect blacklisting (`G9` context in
[`hcoona/microsoft-authentication-cli#523`][mac-523]), and diff-focused review
boundaries without importing authentication-product policy.

[source]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/governance/record-system.md
[story-source]: https://github.com/hcoona/microsoft-authentication-cli/blob/f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64/docs/product/user-stories.md
[story-family-source]: https://github.com/hcoona/microsoft-authentication-cli/blob/f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64/docs/governance/record-families.yaml
[mac-523]: https://github.com/hcoona/microsoft-authentication-cli/issues/523
