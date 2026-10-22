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

### Find and fix the AzureAuth Python harness stall

- **Work carrier:** the pull request proposing this entry, its linked
  implementation pull requests and
  [Issue #1395](https://github.com/hcoona/three/issues/1395). The repository
  owner rejected raising the 10 s timeout of the AzureAuth platform test that
  runs `python -c <harness>` through `SystemProcessRunner` after it failed
  `Build & Test (.NET 10)` on Windows for Renovate PR
  [#1316](https://github.com/hcoona/three/pull/1316), and required a root cause.
  Over 220 Windows runs the harness took median 0.72 s, p99 5.5 s and at most
  6.7 s when passing; runs 39092109952 and 39259837708 timed out.
- **Accepted inputs:** the AzureAuth credprovider product and its tests under
  `src/private/app/azureauth-credprovider/` and
  `tests/private/app/azureauth-credprovider/`, including
  `PythonPhase11ProductDoctorTests.cs`, `SystemProcessRunner.cs` and the
  `PythonProbeTimeout` budget in `PythonPhase11VerticalSliceService.cs`; the
  stall diagnostics from PR [#1322](https://github.com/hcoona/three/pull/1322);
  `.github/workflows/ci.yml`.
- **Advancement and outcome:** determine the stall's root cause from the
  captured diagnostics. If no recurrence yields evidence, use a bounded
  reproduction on a Windows runner through ordinary PR CI: one draft
  reproduction PR, at most 10 CI runs and at most 50 harness launches per run,
  stopping at the first captured stall; close the PR and delete its branch when
  done. Fix the cause with
  scenario tests. Evaluate the 5 s production doctor probe budget against
  measured interpreter startup and fix it with its tests if it is wrong.
- **Ordering and gates:** follow the existing engineering, OCR and
  record-system review gates. Independently classify material findings before
  repair. New feasibility or product tradeoffs return to the owner.
- **Acceptance:** the root cause is identified with cited CI evidence and fixed,
  and the probe budget is either confirmed against measured startup or
  corrected; or the owner records a decision on Issue #1395 after bounded
  reproduction yields no evidence. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation
  and implementation changes, isolated local builds and credential-free tests,
  independent delegated review, ordinary PR/check operations and their normal
  CI, including bounded reproduction runs.
- **Exclusions:** no raising of the test timeout as the fix and no retry,
  skip or other masking of the failure; no change outside the AzureAuth
  credprovider product and its tests unless the root cause is in shared test
  or process infrastructure, which the implementation PR must name; no
  release, dispatch, publication, credential or access change.
