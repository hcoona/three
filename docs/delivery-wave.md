# Current Delivery Wave

This record is the sole positive work-authorization authority for the migration
governed by the [bootstrap](governance/bootstrap.md). An entry is authorized only
as accepted on `main`. An Issue, Milestone, branch, pull request, comment, label,
session, or unmerged edit cannot add to or enlarge this record.

Adding or changing an entry through merge grants or changes its bounded authorization.
Deleting an entry through merge ends that authorization. Git and the proposing pull
request retain the reason and history; this record contains no progress or historical
status.

Preparing and reviewing an explicitly repository-owner-approved pull request whose sole
substantive purpose is to change this record is permitted without an existing entry. The
proposal does not authorize any work it would add before merge.

## Authorized Advancements

### Prepare the Reference-Aligned Two-Case Migration Candidate

- **Work carrier:** The candidate pull request. Use an Issue when its preparation
  and acceptance require coordination across multiple pull requests or contributors.
- **Prerequisite:** The bootstrap, [migration design](governance/migration-design.md),
  and this entry are accepted on `main`. The existing repository authorities remain
  effective. If a prerequisite or relied-on authority changes materially, pause
  affected work and refresh its scope, validation, and review.
- **Accepted inputs:** The source commit pinned by the bootstrap; Three's accepted
  migration design, instructions, records, code, manifests, controls, and related
  Git and pull-request evidence. Public-source retrieval may verify existing
  citations but cannot enlarge product scope or supply runtime evidence.
- **Accepted migration dispositions:** Keep one repository control plane and
  separate project document roots. Preserve the source principles and current
  product/domain semantics. Prepare one final atomic replacement through bounded
  reviews; candidate records remain unmerged proposals until that replacement.
- **Authorized advancement:** Prepare an unmerged candidate adapting the source
  governance and record policies, family/control catalog and checker contracts,
  human/agent routing, and applicable review procedures for the monorepo. Prepare
  exact record transformations for `nbgv-python` with its sample references and
  for Workflow Delivery v3 with the `hcoona-release-smoke-npm` cross-project case.
  Identify current consumers, preserve evidence and identifiers, and validate the
  necessary path, namespace, generated-interface, and HK adaptations. Record
  unresolved content or ownership decisions for the repository owner.
- **Execution mode:** An existing Codex session may act as orchestrator for this
  advancement, assigning bounded subwork to persisted `codex exec --json`
  sessions in separate Git worktrees. Independent subwork may run concurrently;
  dependent work and conflicting changes to shared authorities remain ordered.
  The orchestrator owns coordination and integration, while independent reviewers
  and the repository owner retain their review and acceptance responsibilities.
  Apply the design's [recovery requirements](governance/migration-design.md#recoverable-coordination-requirements):
  associate sessions with the work carrier, inspect runtime and Git state before
  resuming or replacing a worker, and validate outputs before integration. A
  worker may act only within this entry and its assigned boundary. Stop affected
  work when authority, worker ownership, or a prerequisite is unresolved; a retry
  or replacement cannot enlarge scope or repeat an unverified external effect.
- **Bounded outcome:** One reviewable candidate commit for the repository control
  surfaces and two selected cases, with relevant check and independent review
  evidence and a concrete list of remaining preparation boundaries. The candidate
  is not a completed repository-wide migration and must not merge as the final
  replacement under this entry.
- **Acceptance condition:** The owner accepts the bounded preparation outcome
  through a merged Wave transition whose pull request links the reviewed candidate
  commit, applicable checks, independent record and domain reviews, material-finding
  triage, and owner dispositions. That transition ends this grant and does not
  accept the candidate's policies as effective. Further preparation or final
  replacement requires its own accepted grant.
- **Excluded:** Migrating other project record sets; merging the candidate or
  activating replacement policies, routing, or new controls on `main`; changing
  source principles, product behavior, project contracts, support claims, or build
  and release runtime behavior; changing protected v3 Governance bytes or path;
  deleting existing human-source material or Git history; building or deploying
  a standalone coordination service or orchestration framework; and package
  publication, authentication experiments, deployment,
  dispatch, or credential/access/Environment changes. Project evidence may be
  relocated in the candidate only with its provenance and current consumers intact.
- **External effects:** Public-source retrieval and normal Git, Issue,
  pull-request, review, repository-record, local worktree, delegated Codex
  execution, and local candidate-check operations within this entry.
  Existing checks and bounded record-control validation may inspect the candidate;
  new product/runtime experiments require separate accepted authorization and
  protocol. No release or product external-system mutation is permitted.

The [source and license](governance/bootstrap.md#source-and-adaptations) are
recorded in the bootstrap.
