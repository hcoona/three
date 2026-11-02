# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Reconcile Workflow V1, V2 and V3 documentation

- **Work carrier:** [Issue #1406](https://github.com/hcoona/three/issues/1406)
  and its linked authorization and implementation pull requests. The repository
  owner requested this cleanup and the explicit closure condition below.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md), [documentation portal](README.md),
  and Workflow Delivery's
  [project portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [design and integration contracts](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md) and
  [historical readers](../src/private/app/workflow-delivery/docs/validation/historical-readers.md).
- **Advancement and outcome:** audit and reconcile Workflow V1, V2 and V3
  documentation against the current implementation and accepted authorities.
  Delete superseded records after migrating current consumers, links and any
  unique current rationale or evidence. Use immutable Git references for
  historical bodies rather than an archive or duplicate authority. Begin with
  closed-campaign validation records and the mixed smoke research records;
  preserve current native-integration findings, recheck obligations, provenance,
  evidence limits, stable requirement identifiers, legal notices and immutable
  human-source inputs.
- **Ordering and gates:** merge this authorization before substantive cleanup.
  Migrate consumers and navigation atomically with retirement. Retain independent
  record-system, research-evidence and applicable engineering/OCR reviews,
  independent classification of material findings, record checks and affected
  HK validation. Product, evidence-level or authority changes outside faithful
  documentation reconciliation return to the owner.
- **Acceptance and closure:** the current tree has one authority per retained
  concern, accurate implementation/status routing and no superseded body
  without a current consumer. Only an explicit further repository-owner request
  may close or delete this entry; completing or merging an individual cleanup
  PR does not authorize its closure. Retaining the entry does not enlarge this
  bounded documentation scope. Keep progress and results in the Issue and PRs.
- **Effects:** permit repository and read-only GitHub inspection, documentation
  and directly affected record-routing/catalog changes, local record and
  conformance checks, independent delegated review, and ordinary Issue/PR/check
  operations and their normal CI.
- **Exclusions:** no runtime, dependency, workflow-execution or product-behavior
  changes; no new empirical experiments or support claims; no release/proving
  dispatch, publication, authentication, Environment, account, publisher or
  access change. All closed-campaign failures, spent ceilings and operation
  limits remain unchanged.

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
  change; no restoration or modification of the retired legacy
  `release-orchestrate.yml` stack beyond an owner decision recorded separately;
  no new runtime matrix.

### Repair the root causes blocking the open Renovate pull requests

- **Work carrier:** the pull request proposing this entry and its linked
  implementation pull requests. The repository owner requested root-cause
  repairs after Renovate pull requests
  [#1449](https://github.com/hcoona/three/pull/1449),
  [#1475](https://github.com/hcoona/three/pull/1475),
  [#1474](https://github.com/hcoona/three/pull/1474) and
  [#1323](https://github.com/hcoona/three/pull/1323) failed CI, and selected
  the decisions below.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  Workflow Delivery's
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md) and
  [Python native packaging research](../src/private/app/workflow-delivery/docs/research/python-native-packaging.md),
  the `asciidoctor-latexmath`
  [specification](../src/public/lib/asciidoctor-latexmath/specs/001-asciidoctor-latexmath-asciidoctor/spec.md),
  root `mise.toml`, `mise.lock`, `pyproject.toml` and `uv.lock`,
  `renovate.json` and the GitHub workflows.
- **Diagnosis:**
    - The Workflow Delivery Python smoke fixture pins `hatchling==1.32.4` while
      the root `uv.lock` selects the backend used by its
      `--no-build-isolation` build. Lock maintenance and backend updates move
      them separately; the existing Renovate grouping rule cannot keep them
      equal.
    - The Node packaging runtime must appear in the `node-tests` compatibility
      matrix. Node 26 became Active LTS on 2026-10-28, Node 22 remains
      supported until 2027-04-30, and the public packages declare
      `engines.node >=22`.
    - On Ruby 4.0, `logger` is a bundled gem. `asciidoctor` 2.0.26 requires it
      without declaring it; Aruba 2 supplied it transitively through its
      dependencies. No spec uses Aruba, whose 3.0 release also removed the
      configured `working_directory` option. The caller already reports bounded
      stderr for failed native commands; in #1323, the RSpec load error was on
      stdout and stderr was empty, so CI showed only termination and exit.
- **Owner decisions:** `uv.lock` alone selects the Hatchling version for the
  smoke fixture. The Node compatibility matrix becomes `22.x`, `24.x` and
  `26.x`; `engines.node` stays `>=22`. `logger` is declared in the
  `asciidoctor-latexmath` development Gemfile, not its gemspec, until a
  supported `asciidoctor` release no longer requires the undeclared library.
  Aruba is removed rather than migrated.
- **Advancement and outcome:**
    - Replace the smoke fixture's exact backend requirement with an unbounded
      `hatchling` requirement and delete the Renovate rule that tried to keep
      the pin equal to the workspace tooling.
    - Add `26.x` to the `node-tests` matrix; describe the matrix update in the
      Renovate Node major rule; remove the pytest assertion that duplicates the
      Workflow Delivery locked-Node matrix contract test.
    - Declare `logger` in the `asciidoctor-latexmath` Gemfile; remove Aruba
      from its Gemfile, lockfile, spec helper and Renovate rule; update
      current documentation that names Aruba as the isolation mechanism.
    - Include bounded native stdout in caller-visible failed Ruby RSpec
      diagnostics alongside the existing stderr, without changing the retained
      native result or report; cover the observed load-error pattern with a
      scenario test.
- **Ordering and gates:** the repairs are independent. Workflow Delivery
  changes follow its engineering, OCR, record-system and research-evidence
  review gates; research-evidence review confirms that removing the fixture pin
  stays within the packaging research's version-change guard. Independently
  classify material findings before repair. New feasibility or product
  tradeoffs return to the owner.
- **Acceptance:** after rebasing, or after Renovate recreates them, #1449 and
  #1474 pass ordinary CI without further dependency-specific edits; #1475 and
  #1323 are closed as superseded by fixture-pin and Aruba removal, respectively;
  a later backend-only or lock-maintenance Hatchling update needs no fixture
  edit; an RSpec load failure with empty stderr exposes its bounded stdout
  diagnostic in caller-visible output. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation,
  workflow, Renovate, dependency-manifest, lockfile and implementation
  changes, locked tool preparation, isolated local builds and credential-free
  tests, independent delegated review, ordinary PR/check operations and their
  normal CI, rebasing or closing the four named Renovate pull requests.
- **Exclusions:** no Node 22 removal or `engines` change; no `asciidoctor` 3
  migration or gemspec dependency change; no change to the Renovate Python
  hold or Python 3.15 migration; no release, proving or campaign dispatch,
  publication, credential, Environment, account, publisher or other access
  change.
