# Repository Record System

## Purpose

This record defines how durable repository and project information is assigned to
repository files, Git, GitHub, Agent Skills, and automated controls under
[`governance-system.md`](governance-system.md).

## Authority by Concern

Authority is scoped by concern rather than globally ranked; navigation portals
and instructions route to canonical authorities without restating their rules.

| Concern                                                              | Canonical carrier                                    |
| -------------------------------------------------------------------- | ---------------------------------------------------- |
| Repository identity, layout, and imported-project provenance         | Root `README.md` and `docs/engineering/`             |
| Public navigation to repository and project entry points             | `README.md`, `docs/README.md`, and project `README`s |
| AI-agent behavior and routing                                        | Root and project-local `AGENTS.md`                   |
| Human contribution workflow                                          | Root `CONTRIBUTING.md`                               |
| Current positive work authorization                                  | Root `docs/delivery-wave.md`                         |
| Repository governance and record policy                              | `docs/governance/`                                   |
| Product purpose, behavior, architecture, decisions, research, gates  | Owning `<project-root>/README.md` and `docs/`        |
| Upstream provenance and license notices                              | Project provenance and license notices               |
| Multi-PR coordination, discussion, and progress                      | GitHub Issues and Milestones                         |
| Proposed repository changes and review evidence                      | Pull requests                                        |
| Current accepted repository state                                    | `main`                                               |
| Published state                                                      | Git tags and GitHub Releases                         |
| Authorship, history, and deleted records                             | Git history                                          |
| Retired-project summaries, rationale, and last-intact Git tree links | `docs/retired-projects.md`                           |

## Project Namespaces

Generic governance, work authorization, local iteration freedom, and review
triggers are uniform across the repository. Each project, shared library,
engineering tool, or standalone skill owns its domain requirements, architecture,
research, security, and validation criteria under its own directory; project
records must not impose workflow gates, `main`-merge preconditions, or run-count
quotas on harmless local development and debugging. Test container roots and
generated deployment roots (`tests/`, `.agents/`, `.github/agents/`) do not form
separate namespaces.

[`record-families.yaml`](record-families.yaml) (validated against
[`record-families.schema.json`](../../schemas/governance/record-families.schema.json))
maps canonical paths to their owning namespaces (`repository` or project
directories) via concise wildcard globs. Standard projects under
`src/{public,private}/{app,lib}/<project>/` and `src/sample/<project>/` are
covered automatically without catalog edits; add an explicit binding only when a
project places canonical records outside standard locations. Cross-project
references use repository-relative paths and anchors. Small projects may remain
README-only.

## Record Admission

Admit a distinct record or control only when concrete answers exist for: (1) the
owned concern, (2) producer and maintainer, (3) consumer and use point, (4) the
failure caused by absence, and (5) why an existing README or record is
insufficient. Admit separate user-story records only when user-journey motivation
is not already clear from the README or requirements. Split records only when
lifecycles or consumers diverge, not merely for file length.

Use Markdown for narrative policy, requirements, design, and synthesis; YAML for
small human-maintained state; JSON with JSON Schema for strict machine contracts;
and JSONL/CSV or standard tool formats (CycloneDX, SPDX, JUnit, SARIF) for
structured observations. Do not maintain duplicate prose copies of structured
records or add status/author/revision-history headers to ordinary files.

## Work Authorization and Concurrency

### Wave-Free Ordinary Pull Requests

The following execute directly through ordinary pull requests without a
`docs/delivery-wave.md` entry, provided they introduce no new external, paid,
credentialed, or privacy effects:

- Contract-preserving defect and test fixes, CI/build troubleshooting and
  diagnostics, lint/formatting, and documentation-drift repairs;
- Routine Renovate or dependency compatibility updates;
- Contract-preserving internal code refactoring, internal CI/build/tooling
  script consolidation, dead-code or superseded-internal-route retirement, and
  repository governance or documentation cleanup reviewed and merged by the
  repository owner;
- Committed release or prerelease version and changelog/release-note transitions
  (`version.json`, static `pyproject.toml`, `version.rb`) for already-admitted
  public product release units (`src/public/**` in
  [Official channel scope](../../src/private/app/workflow-delivery/docs/integration/common.md#official-channel-scope),
  excluding internal `hcoona-release-smoke-*` fixtures) that do not alter public
  APIs, target frameworks, or Workflow Delivery contracts.

Routine controlled release operations for those admitted public product release
units also require no Wave entry: credential-free `dry-run` dispatches and
eligible live Buddy or Official dispatches (gated by destination GitHub
Environment approval) of `.github/workflows/workflow-delivery-v3-*-release.yml`
initiated by the repository owner—or executed by an agent only on an explicit
owner instruction for that specific dispatch—and owner maintenance of existing
publisher/storefront registrations. Agents must never autonomously dispatch
release workflows, approve Environments, or run publication commands.

### Positive Wave Authorization

Target-branch `docs/delivery-wave.md` is the sole positive authorization
authority for:

1. New products/projects or feature and capability advancements;
2. Breaking public-contract, target-framework, Official release-scope, or
   cross-project architectural boundary changes;
3. Non-routine external, paid, credentialed, privacy-sensitive, or irreversible
   operations—including admitting new release units, out-of-band publication,
   live smoke/proving operations, ruleset/permission changes, non-public or
   credentialed external services, persistent host mutations outside
   repository/toolchain/build/cache/temp state, and paid or empirical campaigns
   (even inside a test or CI step).

Within authorized work, routine local editing, builds, lint, tests, scratch
harnesses outside the repository, read-only inspection, independent review, and
ordinary Issue/PR/CI validation (including failed-job reruns) are permitted with
no attempt quota. Implementation and owning records may evolve concurrently in
one branch or across a stacked PR chain so long as each merge into `main` stays
self-consistent and respects any ordered external-risk/protocol gate. If `main`
materially changes a relied-on shared authority, pause only dependent work until
refreshed.

### Thin Wave Entry Contract and Lifecycle

An Issue, branch, PR, or unmerged Wave edit cannot authorize itself. Preparing a
repository-owner-approved PR whose sole substantive purpose is adding, changing,
or retiring Wave entries is a permitted control-plane operation.

Each Wave entry must remain a concise authorization contract (~15–25 lines)
containing only:

- **Work carrier:** Issue (for multi-PR coordination) or PR link;
- **Accepted inputs and scope/outcome:** bounded objective and authority inputs;
- **Permitted external/paid effects:** numeric ceilings only when applicable
  (detailed procedures belong in project protocols, not the Wave);
- **Exclusions and closure:** explicit prohibitions and closure condition.

Do not place root-cause `Diagnosis`, class/package inventories, or step-by-step
implementation plans in `docs/delivery-wave.md`. Delete an entry atomically in
its final delivery PR (or immediately once its carrier Issue closes); Git and PR
history retain rationale and progress. Evaluate mutable external-interface
rechecks in the consuming project protocol only when a change relies on that
interface or runtime behavior contradicts the recorded contract.

## Lifecycle and Deletion

Merge into `main` defines current accepted state within the declared scope and
evidence level; tags and GitHub Releases define published state; Git retains
deleted records. Delete superseded records once current consumers, links, and
unique active rationale/evidence are migrated. [`docs/retired-projects.md`](../retired-projects.md)
indexes retired projects and immutable links to their last intact Git trees.

## Workflows

Organize requirements by capability, architecture by overview and scoped views,
and empirical experiments by reproducible protocol. Routine credential-free local
and governed CI validation of declared contracts is not an empirical experiment:
it requires no experiment protocol, disposable host, or attempt quota, retains
relevant CI run/attempt identities when reporting gates, and supports claims only
about tested source conformance.

Requirement heading identifiers (`REQ`, `FR`, `NFR`, `AC`, including project
prefixes) are permanent within their owning namespace once merged into `main`.
Keep each established identifier defined once in its namespace—either as an
active requirement or as `<ID>: Retired - current authority: <ID or [target](path#anchor)>`
(with cross-file links validated by `lychee`)—and qualify cross-project
references with Markdown links to `path#anchor`.

## Enforcement

| Control                    | Class & enforcement          | Implementation & execution point                                                                                                   |
| -------------------------- | ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Source & link conformance  | Mechanical (blocking)        | `hk.pkl` (`typos`, `editorconfig-check`, formatters, `lychee` offline Markdown link/anchor check) in local hooks and CI            |
| Record & ID validation     | Mechanical (blocking in HK)  | `eng/scripts/check_repository_records.py` (`mise run records:check`) checking `record-families.yaml` coverage and `REQ/FR/NFR/AC`  |
| Public release inventory   | Mechanical (blocking)        | `eng/scripts/release_triage.py --check-inventory` in `hk.pkl` (`docs/engineering/public-release-triage.md`)                        |
| Engineering guidance       | Procedural review (advisory) | `.github/skills/engineering-guidance/SKILL.md` for design, security, and test-strategy decisions                                   |
| Record-system review       | Procedural (review-required) | `.github/skills/record-system-review/SKILL.md` by an independent reviewer for governance, Wave, routing, or contract-authority PRs |
| Research-evidence review   | Procedural (review-required) | `.github/skills/research-evidence-review/SKILL.md` by an independent reviewer for research, empirical claims, and paid/risk ops    |
| Finding disposition & gate | Hybrid / owner disposition   | [`GOV-011`](governance-system.md#gov-011-separate-findings-from-disposition) delta/third-party triage and owner merge acceptance   |

Contextual reviews inspect the proposed repository diff and relied-on evidence,
not PR/Issue prose formatting or write order. Manually maintained authorities use
direct repository files rather than symbolic links; generated `.agents/skills/`
and `.github/agents/` outputs are owned by `apm.yml` / `apm.lock.yaml`.

## Record-System Gate

A record-system change is complete when each retained concern has one canonical
authority, `record-families.yaml` and mechanical checks (`hk`, link check, and
`records:check`) pass, audience interfaces and controls agree with governing
policies, replaced records and broken links are removed, and required independent
reviews and `GOV-011` dispositions are recorded in the governing PR.

## Source

Adapted from [`hcoona/microsoft-authentication-cli`][source] (`1a024985` and
`f2e36bbc`), with Three's wildcard monorepo namespaces, thin Wave contracts,
release/internal-refactoring exemptions, and external-effect boundaries
(`hcoona/microsoft-authentication-cli#523`). The source
[copyright and MIT notice](reference-license.txt) is retained.

[source]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/docs/governance/record-system.md
