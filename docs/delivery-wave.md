# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Derive HK configuration responsibility from native imports

- **Work carrier:** the pull request proposing this entry and one linked
  implementation pull request. The repository owner requested a root-cause fix
  after Renovate PR [#1066](https://github.com/hcoona/three/pull/1066), which
  changes only the HK version pin in `src/private/lib/hk/Config.pkl` and its
  mise tool entries, failed CI scope selection with
  `Unresolved endpoint responsibility: src/private/lib/hk/Config.pkl`.
- **Accepted inputs:** the Workflow Delivery
  [endpoint responsibility contract](../src/private/app/workflow-delivery/docs/integration/node.md#endpoint-responsibility-and-selection),
  the [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  the [HK execution guide](engineering/hk-execution.md), root `hk.pkl` and the
  shared HK component under `src/private/lib/hk/`.
- **Advancement and outcome:** CI scope selection gives source-conformance
  responsibility to the root HK configuration and every repository-local Pkl
  module in its import graph, read at each endpoint from native
  `pkl analyze imports` output under the locked mise `pkl`, instead of a
  hard-coded `hk.pkl` path. Remote package imports remain outside repository
  responsibility. A failed or malformed native read fails planning. Update the
  scope job's locked tool preparation, the owning integration contract and
  tests in the same change. The outcome is accepted when an HK component change
  such as #1066 selects conformance without an unresolved-responsibility failure.
- **Ordering and gates:** follow the existing Workflow Delivery engineering,
  OCR and record-system review gates. Independently classify material findings
  before repair. Do not infer HK responsibility from directory names or a
  repository-local Pkl parser.
- **Effects:** permit source and repository/GitHub inspection, documentation,
  workflow and implementation changes, locked tool preparation, isolated local
  builds and credential-free tests, independent delegated review, ordinary
  PR/check operations and their normal CI.
- **Exclusions:** no change to HK step behavior, profiles or the conformance
  job's checks; no general Pkl ownership for non-HK modules; no responsibility
  for other unresolved paths; no release, dispatch, publication, credential or
  access change. Delete this entry when the outcome is accepted.

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

### Give repository tooling configuration CI scope responsibility

- **Work carrier:** the pull request proposing this entry and one linked
  implementation pull request. PR
  [#1253](https://github.com/hcoona/three/pull/1253) (global.json as the .NET
  SDK authority) failed CI scope selection with
  `Unresolved endpoint responsibility: .github/renovate/global.cjs`; the
  repository owner approved a bounded fix through a Wave entry.
- **Accepted inputs:** the Workflow Delivery
  [endpoint responsibility contract](../src/private/app/workflow-delivery/docs/integration/node.md#endpoint-responsibility-and-selection),
  the [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  `renovate.json`, `.github/renovate/`, `.github/workflows/renovate.yml`,
  `.github/workflows/ci.yml` and root `hk.pkl`.
- **Advancement and outcome:** CI scope selection gives a present changed
  coordinate finite, native responsibility in two cases, evaluated against each
  endpoint's own committed bytes (deleted coordinates keep confirmed absence):
    - **Renovate configuration:** `renovate.json`, `.github/renovate/**` and
      `.github/workflows/renovate.yml` receive owner `renovate` with target
      `.github/workflows/renovate.yml` and rule `native-renovate-configuration`.
    - **Literal tooling reference:** a coordinate whose repository-relative path
      occurs as a complete path token (quoted or bounded by non-path characters,
      never as a substring of a longer name) in the endpoint's root `hk.pkl` or
      `renovate.json` receives owner `conformance` with target
      `.github/workflows/ci.yml#conformance` and rule
      `literal-tooling-reference`. This is a reference, not proof that a step
      reads the file; it includes paths HK excludes. Only root `hk.pkl` and
      `renovate.json` are scanned; imported HK modules keep the existing
      `native-hk-configuration` route.

    These reasons select no retained job. Do not parse shell commands or depend
    on HK plan output: locked HK 2.6.0 `check --plan --json` reports step status,
    reasons and file counts but no per-step files. Update the owning integration
    contract and tests in the same change, including tests that substrings such
    as `.editorconfig` in `Steps.editorconfig_check` and `PklProject` in
    `PklProject.deps.json` do not match. The outcome is accepted when #1253
    passes scope selection.

- **Ordering and gates:** follow the existing Workflow Delivery engineering,
  OCR and record-system review gates. Independently classify material findings
  before repair. Do not add a blanket fallback owner or infer responsibility
  from directory names.
- **Effects:** permit source and repository/GitHub inspection, documentation
  and implementation changes, locked tool preparation, isolated local builds
  and credential-free tests, independent delegated review, ordinary PR/check
  operations and their normal CI.
- **Exclusions:** no change to Renovate or HK behavior; no retained-job
  selection from these reasons; no responsibility for other unresolved path
  classes; no release, dispatch, publication,
  credential or access change. Delete this entry when the outcome is accepted.

### Retire the legacy v1 release workflow stack

- **Work carrier:** the pull request proposing this entry and one linked
  implementation pull request. The repository owner asked to retire the
  retained v1 Official release stack if it is no longer usable; an independent
  read-only investigation found it unusable as a maintained route: the current chain has never run, its Ruby path cannot succeed, its declared reviewer gates are absent, and the Python and Node paths are unvalidated.
- **Accepted inputs:** the
  [retained legacy workflow boundary](https://github.com/hcoona/three/blob/0f83aa3649ff84c5e305a4f824b1da6c8e7eaba6/src/private/app/workflow-delivery/docs/research/legacy-workflow-boundary.md),
  the Workflow Delivery
  [integration contracts](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  the [record-family catalog](governance/record-families.yaml) and the
  investigation evidence in the proposing pull request.
- **Owner decision:** retire the stack. It is the v1 Official route
  (`official.yml` → `release-orchestrate.yml` → `release-resolve.yml`, the
  `release-build-*.yml`, `release-prepare-release-notes.yml` and
  `release-create-github-release.yml` reusable workflows) plus the v2-era
  `release-official.yml` stub. Their products currently have no in-repository
  replacement release route; that gap is accepted until a separately authorized
  Workflow Delivery adoption.
- **Advancement and outcome:** delete those workflows,
  `.github/workflows/REFACTOR_PLAN.md`, the scripts and tests used only by them,
  and the CI scope and Python selection associations that exist only for them;
  retire the legacy boundary record and its catalog family and route, adding its commit-pinned link to the [retired records](../src/private/app/workflow-delivery/docs/validation/historical-readers.md#retired-records) index; update
  the product READMEs, NBGV path filters and release-pack validators that point
  to the stack; and move any still-needed guard against reintroducing the retired
  Buddy routes into an existing test. Shared helpers with another current
  consumer stay. Before or with the merge, the owner disables the `Official Release` (`official.yml`) and `Release Official` (`release-official.yml`) workflow identities, so neither a pushed `release/*/v*` tag nor a dispatch at an old ref can run a retired workflow.
  Because removing the legacy stack also removes the only scope owner every
  workflow file had, CI scope selection gives direct children of
  `.github/workflows/` ending in `.yml` or `.yaml` a finite owner: owner
  `conformance`, target `.github/workflows/ci.yml#conformance` and rule
  `native-hk-workflow-lint`, applied only when that endpoint commits root
  `hk.pkl` and `ci.yml`; HK's actionlint step also checks these files; its glob covers every workflow YAML under any `.github/workflows/` directory, and this owner is a fixed copy for the root directory only. A drift test evaluates the endpoint's `hk.pkl` with the locked Pkl and fails unless its `check` hook keeps an `actionlint` step in a profile `ci.yml` runs whose glob still matches these files.
  The rule selects no retained job. Bind
  `.github/workflows/workflow-delivery-v3-*.yml` to its real Python reader,
  `pytest:tests/eng/test_python_runtime.py`. Record the rule in the owning
  endpoint responsibility contract and cover a deleted workflow, a workflow with
  no Python reader and a Python-read workflow in tests.
  Retirement also deletes shell scripts whose only basis owner is the retired legacy route, or that have none.
  CI scope selection therefore gives every committed `*.sh` or `*.bash` file
  that the shared shellcheck and shfmt exclusions do not match (`**/.github/**`, `**/.specify/**`, `**/generated/**` and the root-only `.agents/**`) a finite owner:
  owner `conformance`, target `.github/workflows/ci.yml#conformance` and
  rule `native-hk-shell-lint`, applied only when that endpoint commits root
  `hk.pkl` and `ci.yml`. HK's `shellcheck` and `shfmt` steps also check
  these files; this owner is a fixed copy of their shared glob and exclusions,
  and a drift test evaluates the endpoint's `hk.pkl` with the locked Pkl and
  fails unless both steps stay in a profile `ci.yml` runs, their shared glob still matches `*.sh` and `*.bash`, and their exclusions that can match a shell script equal this copied set. The rule selects no retained job and does not replace a script's
  existing owner. Record it in the same contract and cover a deleted script, a
  current script and an excluded path in tests.
  Retirement also deletes standalone Python scripts that no Python workspace
  member owns and no Python test reads, which the Python group cannot resolve
  as inputs. CI scope selection therefore replaces, and never adds, the
  `native-python-caller-responsibility` reason of a committed `*.py` file with
  a finite owner, owner `conformance`, target
  `.github/workflows/ci.yml#conformance` and rule `native-hk-python-lint`,
  when all of these hold at the endpoint that commits the file:
    - line 1 is `# /// script` and a closing `# ///` line follows (PEP 723
      inline script metadata, the uv-native standalone-script marker);
    - the file is not named `conftest.py` and is not under `tests/`;
    - the row would emit that Python reason only because the path ends in
      `.py`: it is not in the retained-job or Python input lists, not under
      `tests/eng`, not an AzureAuth input and no record task matches it;
    - it is not under the directory of any committed non-root `pyproject.toml`;
    - the shared `ruff` and `ruff_format` exclusions that can match a Python
      file (`**/.github/**`, `**/.specify/**`, `**/generated/**`, the root-only
      `.agents/**` and `src/private/lib/scan-restoration/skills/**`) do not
      match it;
    - the endpoint commits root `hk.pkl` and `ci.yml`.

    HK selects these files for its `ruff` and `ruff_format` steps; the owner is a
    fixed copy of their glob and of those exclusions, and a drift test evaluates
    the endpoint's `hk.pkl` with the locked Pkl and fails unless both steps stay
    in a profile `ci.yml` runs, their glob still matches `*.py`, and those
    exclusions equal this copied set. Other owners, such as record, Scholarly or
    gem owners, are kept. The rule selects no retained job and adds no Python
    execution. Record it in the same contract and cover a deleted standalone
    script, a current standalone script, a record-task script, a `conftest.py`,
    a `tests/eng` helper and an excluded path in tests.

- **Ordering and gates:** follow the record-system, engineering and Workflow
  Delivery review gates; independently classify material findings before
  repair. Preserve evidence consumers through commit-pinned links.
- **Acceptance:** no current file, test, record, path filter or workflow treats the retired stack as a current or executable route (commit-pinned historical links, the reintroduction guard and evidence text that names the stack only as history are allowed); ordinary CI passes; both workflow identities are disabled. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation,
  workflow, test and implementation changes, isolated local tests, independent
  delegated review, ordinary PR/check operations and their normal CI, and the
  owner-authorized disabling of the retired workflow identities.
- **Exclusions:** no deletion of tags, GitHub Releases, Environments, registry
  trusted-publisher entries or repository variables (they are listed for the
  owner's separate disposition); no new release route; no release, dispatch,
  publication or credential change.
