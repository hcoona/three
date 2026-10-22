# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Derive tool and runtime versions from committed configuration

- **Work carrier:** the pull request proposing this entry and its linked
  implementation pull requests; [Issue #1247](https://github.com/hcoona/three/issues/1247)
  retains the triggering Python 3.15 investigation. The repository owner
  accepted an independent multi-reviewer audit after Renovate PR
  [#1166](https://github.com/hcoona/three/pull/1166) failed because Workflow
  Delivery hard-coded the Python 3.14 line, and requested a complete fix.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  the Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md) and
  [integration contracts](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  root `mise.toml`, `mise.lock`, `global.json`, `global.pkl`,
  `renovate.json` and the GitHub workflows.
- **Owner decisions:** root `mise.toml` and `mise.lock` are the only tool and
  runtime version authority; a project-local mise configuration does not
  select CI tools. The .NET SDK is the exception: `global.json` is its
  authority, with `rollForward` kept `disable`, and mise reads it as an
  idiomatic version file. Its upgrades edit `global.json`. The required check
  `Test (Python 3.14)` becomes `Test (Python)` because no Python matrix is
  planned; the owner authorizes the matching ruleset update.
- **Advancement and outcome:**
    - Make mise read the .NET SDK from `global.json`, stop generating the SDK
      selection from `mise.lock`, and update the Renovate .NET rules and their
      post-upgrade tasks accordingly.
    - Remove version literals that duplicate committed configuration from
      Workflow Delivery code, tests, fixtures and workflows: application tool
      checks accept the locked committed selection; tests and fixtures read the
      root `mise.lock` or `global.json`; workflows read versions once from the
      checked-out root `mise.lock` or `global.json` or install through
      `mise --locked`. A retained version bound must be a documented native
      adapter compatibility limit with its own named check and message.
    - Rename the required Python check and update its consumers and the main
      ruleset together.
    - Repair the other audited principle findings: preparation and native
      query failures report their stage, termination, exit and bounded stderr
      in caller-visible output with scenario tests; tool-check messages name
      the tool and failed condition; unit tests exercise tool selection against
      fixture configuration rather than copied literals; test selection covers
      configuration guards; a mirror-only test is removed; record the PNPM
      12.13.0 depth recheck under research-evidence review.
- **Ordering and gates:** land the .NET authority and shared helpers before
  their consumers. Each implementation PR follows the existing Workflow
  Delivery engineering, OCR, record-system and research-evidence review gates;
  independently classify material findings before repair. New feasibility or
  product tradeoffs return to the owner.
- **Acceptance:** a routine Renovate patch update of each affected tool
  changes only committed configuration and passes ordinary CI without code,
  test or workflow edits; no remaining literal duplicates a committed tool
  selection outside a documented adapter limit; the renamed required check
  protects `main`. Dispatch-only Release and smoke workflows are accepted on
  static validation (actionlint, workflow tests and their local readers)
  because their campaigns are closed. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation,
  workflow, Renovate and implementation changes, locked tool preparation,
  isolated local builds and credential-free tests, independent delegated
  review, ordinary PR/check operations and their normal CI, and the owner-
  authorized `main` ruleset required-check rename.
- **Exclusions:** no Python 3.15 migration (tracked by #1247) and no change to
  the Renovate Python hold; no release, proving or campaign dispatch,
  publication, credential, Environment, account, publisher or other access
  change; no change to the retained legacy `release-orchestrate.yml` stack
  beyond an owner decision recorded separately; no new runtime matrix.

### Investigate and dispose of retired-stack GitHub Releases

- **Work carrier:** the pull request proposing this entry and
  [Issue #1352](https://github.com/hcoona/three/issues/1352). The repository
  owner observed that `release/circular-list/v1.2.0-beta.2.g0d2d401` is the
  repository's Latest release and differs from every other release.
- **Accepted inputs:** the repository's GitHub Releases, tags and Actions run
  history; the retired-stack history in Git; the Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md)
  and `GithubReleaseDestination`.
- **Advancement and outcome:** identify the workflow run and cause of that
  release; inventory the other `release/*` releases and tags from the retired
  stack for the same defects; report findings and disposition options (leave,
  edit or delete) on Issue #1352 for the owner; apply the disposition the owner
  records there. Report whether the first Official ImageOcclusionEditor release
  becoming Latest is intended; any change to that behavior belongs to the
  Official tag and Release entry below.
- **Ordering and gates:** follow the record-system and research-evidence review
  gates for any record change. The disposition is an owner decision.
- **Acceptance:** findings with cited run, release and tag evidence are on
  Issue #1352, and the owner-recorded disposition is applied. Delete this entry
  when accepted.
- **Effects:** permit repository/GitHub inspection and, only after the owner
  records the disposition on Issue #1352, editing or deleting the named
  retired-stack GitHub Releases and their tags.
- **Exclusions:** no change to Workflow Delivery code, workflows or
  requirements; no change to Workflow Delivery-created releases or tags; no
  package unpublish or deletion; no release dispatch, publication, credential
  or access change.

### Official tags, GitHub Release pages and changelogs

- **Work carrier:** the pull request proposing this entry, its linked pull
  requests, [Issue #1353](https://github.com/hcoona/three/issues/1353) and
  [Issue #1351](https://github.com/hcoona/three/issues/1351). The repository
  owner requested a tag and GitHub Release page for each Official publication,
  with that version's changes shown.
- **Accepted inputs:** the Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md)
  (including WD-REL-001, WD-REL-008, WD-RET-003 and WD-RET-004),
  [HLD](../src/private/app/workflow-delivery/docs/high-level-design.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  `GithubReleaseDestination`, the release caller workflows, the existing
  product release-note and changelog files, and the
  [engineering principles](engineering/engineering-principles.md).
- **Advancement and outcome:**
    - Amend the requirements and designs so that Official publication of a unit
      creates a tag on the selected commit and a GitHub Release page whose body
      comes from that commit's changelog section for the released version;
      Buddy creates neither. Decide the changelog source and format, tag naming,
      completion and failure semantics, `make_latest` behavior, and the
      relationship to units whose destination is a GitHub Release.
    - Implement the amendment with scenario tests, including the workflow
      permission it needs within WD-REL-008 scoping.
    - Bring every publishable product's changelog to the decided format and
      current through its latest published version, and wire it into packages
      where the ecosystem supports release notes.
- **Ordering and gates:** the requirements and design amendment merges first,
  with each listed decision returned to the owner; implementation follows.
  Each PR follows the Workflow Delivery engineering, OCR, record-system and
  research-evidence review gates; independently classify material findings
  before repair.
- **Acceptance:** the accepted amendment, its implementation and scenario tests
  are merged; the Official release callers pass static validation (actionlint,
  workflow tests and their local readers); every publishable product has a
  current changelog in the decided format. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, requirements,
  design, documentation, workflow and implementation changes, isolated local
  builds and credential-free tests, independent delegated review, and ordinary
  PR/check operations and their normal CI.
- **Exclusions:** no release, proving or Official dispatch and no publication;
  no change to Environments, credentials, trusted publishers, rulesets or
  other access; no browser-store release-note change unless the owner extends
  this entry; no change to retired-stack releases or tags.

### Product READMEs on package pages

- **Work carrier:** the pull request proposing this entry, its linked pull
  requests and [Issue #1350](https://github.com/hcoona/three/issues/1350). The
  repository owner requested that each package show its own README rather than
  the repository README.
- **Accepted inputs:** each publishable product's package metadata (csproj,
  `package.json`, `pyproject.toml`, gemspec and browser-store listings), the
  published package pages on GitHub Packages and each Official destination,
  and the [engineering principles](engineering/engineering-principles.md).
- **Advancement and outcome:** record, per ecosystem and destination, which
  README each package and version page shows; fix any NuGet, npm, PyPI or
  RubyGems product whose package metadata does not ship or declare its own
  README; report on Issue #1350 any remaining case that is a platform
  rendering limit for the owner to accept. Browser-store and GitHub Release
  units are observed only.
- **Ordering and gates:** follow the existing engineering, OCR and
  record-system review gates. Platform-limit acceptance is an owner decision.
- **Acceptance:** every NuGet, npm, PyPI and RubyGems product's packed
  artifact ships and declares its own README, verified by locally packing or
  inspecting the artifact; the per-destination observations, including
  browser-store and GitHub Release units, and any owner-accepted platform limit
  are recorded on Issue #1350. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub and public package-page
  inspection, product packaging-metadata and README changes, isolated local
  builds, packs and credential-free tests, independent delegated review, and
  ordinary PR/check operations and their normal CI.
- **Exclusions:** no application workaround for platform rendering limits; no
  release dispatch or publication; no store listing edit; no credential or
  access change.

### Batch dispatch entry for per-unit release requests

- **Work carrier:** the pull request proposing this entry, its linked pull
  requests and [Issue #1349](https://github.com/hcoona/three/issues/1349). The
  repository owner chose the thin batch entry (proposal 1) after the wiring
  round needed 11 separate dispatches and approvals.
- **Accepted inputs:** the Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md)
  (WD-REL-001 unchanged), the release caller workflows, root `mise.toml`, and
  the [engineering principles](engineering/engineering-principles.md).
- **Advancement and outcome:** add a thin repository entry point, such as a
  `mise` task, that takes a list of caller, unit, destination and dry-run
  entries, dispatches one ordinary per-unit run for each, waits, and prints a
  per-run summary with run IDs and outcomes; document it with the release
  operation guidance.
- **Ordering and gates:** follow the existing Workflow Delivery engineering,
  OCR and record-system review gates; independently classify material findings
  before repair.
- **Acceptance:** the entry point and its scenario tests with a substituted
  dispatcher are merged and documented; one invocation dispatches dry-run
  Buddy requests for at most three units, and its summary matches the runs.
  Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation
  and implementation changes, credential-free tests, independent delegated
  review, ordinary PR/check operations and their normal CI, and the single
  bounded dry-run Buddy validation invocation above; dry-run requests skip
  the Environment-protected publication jobs.
- **Exclusions:** no change to WD-REL-001, per-run Environment approval or
  per-unit completion; no multi-unit request; no non-dry-run or Official
  dispatch; no credential, Environment or access change.
