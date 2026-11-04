# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Migrate Agensh to the single-machine C# LiteAgensh application

- **Work carrier:** [Issue #1526](https://github.com/hcoona/three/issues/1526)
  and its linked pull requests. The repository owner requested establishment
  and project-local records, then explicitly authorized the entire migration
  and rewrite, including design, implementation, integration and acceptance.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md) and
  [documentation portal](README.md). The public upstream baseline is
  `microsoft/Agensh@2dae4e14a653da48549fd9aa093f834841475d6c`.
  Preparation findings in #1526 are research inputs, not runtime evidence;
  project records relying on them receive research-evidence review.
- **Owner decisions:** LiteAgensh belongs at `src/private/app/lite-agensh/`
  and is written in C#. One local coordinator uses SQLite for collaboration
  state, local Git branches/worktrees for integration and existing native
  agent processes for reasoning, editing and tool execution. Use an official
  Copilot native interface selected in accepted project requirements and
  contracts, including the official .NET SDK or documented ACP. This remains
  one native backend, not a requirement to implement both adapters or add
  another model/tool reasoning loop. Map collaboration MCP capabilities
  semantically with names describing their actual local operations, not
  Gitea/Mattermost compatibility names. Do not add hosted collaboration
  services, Docker or process/network sandboxing. The owner selects a trusted
  single-user environment: worktrees separate edits, not host permissions or
  credentials, and local review gates do not provide server-enforced isolation.
- **Advancement and outcome:** establish and complete this local product port,
  including project records, public-source consolidation, requirements,
  architecture and contracts, C# implementation, SQLite persistence, native/MCP
  integration, Git integration, lifecycle/recovery, tests, monorepo integration,
  operator documentation and acceptance. Preserve applicable upstream
  cooperation, board, event-delivery, review and lifecycle policies; identify
  platform replacements and intentional semantic differences rather than
  inventing a new scheduling framework. Keep the upstream responsibility and
  tool mapping complete, with source identity and required attribution.
- **Ordering and gates:** merge this entry before substantive project work.
  Accept each implementation slice's requirements, applicable architecture
  and contracts, validation basis and unsupported cases before implementing
  that slice. Accept an exact protocol under research-evidence review before
  any experiment or live native-backend observation; it must bound its subject,
  environment, observations, finite execution and cumulative effects, stop
  conditions and cleanup. Live provider use additionally requires owner-approved
  run/spend bounds in that protocol; this entry is not unrestricted paid usage.
  Recheck mutable native interfaces before relying on them. Each pull request
  retains applicable independent record-system, research-evidence and
  engineering reviews, independent classification of material findings, record
  checks, affected HK validation and project checks. Order shared root/CI
  changes with other entries. New product or risk tradeoffs return to the owner.
- **Acceptance and closure:** the accepted project requirements and validation
  basis are met for the local Copilot-backed product, including cooperation,
  state persistence/recovery, candidate-bound validation, local integration,
  cancellation and retained local delivery. Operator documentation and the
  full mapping identify actual supported behavior and evidence limits; public
  desk evidence or scripted workers alone cannot establish live integration.
  Record progress and evidence in #1526 and its pull requests. Delete this
  entry when that outcome is accepted or the owner stops the migration.
- **Effects:** permit repository and read-only GitHub inspection, public-source
  retrieval, project documentation and routing/catalog changes, C# source and
  tests, necessary workspace/CPM/lockfile/check/CI integration, locked dependency
  restoration, isolated local builds and credential-free tests, local owned
  subprocesses/worktrees/SQLite state and native acceptance runs within the
  accepted protocols and existing owner-approved backend configuration.
  Permit Issue/PR/check operations and normal CI, independent delegated review
  and ordinary merges of reviewed changes. Preserve unrelated work and target
  repository instructions; cleanup is explicit and limited to owned state.
- **Exclusions:** no other agent backend in this outcome, hosted collaboration
  platform, arbitrary multi-repository/team service, remote product delivery,
  automatic push/PR publication by LiteAgensh, model gateway or credential
  distribution, equivalent aggregate token-request cutoff without verified
  support, cross-backend runtime patches, sandbox/security-isolation claims,
  portable sealed-backup equivalence, history-hiding/private-evaluation or
  ProgramBench reproduction. No unrelated project behavior or tool-version
  change, release/proving dispatch, package/product publication, authentication,
  account, Environment or access change. These exclusions do not prevent
  ordinary repository delivery of LiteAgensh's reviewed source and records.

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

### Design and plan sheet-music recognition

- **Work carrier:** [Issue #686](https://github.com/hcoona/three/issues/686),
  its sub-issues and their linked pull requests. After the desk study recorded
  there, the repository owner requested this design and planning grant.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md) and
  [documentation portal](README.md). Findings in #686 and its sub-issues are
  unreviewed research inputs, not accepted evidence; a repository record that
  relies on one rechecks it under research-evidence review.
- **Owner decisions:** the project root is `src/private/app/sheet-music-omr/`;
  its product implementation language and packaging remain undecided. Public
  records, Issues and pull requests exclude private paths, piece identities
  and private score content, using anonymized batch numbers and aggregate
  counts instead. Content that the owner's development agents read is
  processed by their model services; the owner accepts this.
- **Advancement and outcome:** refine the overall design and plan for
  recognizing photographed or scanned printed piano scores into editable
  notation, including model fine-tuning options. Maintain the workstream
  decomposition, dependencies, open owner decisions and desk-research
  findings in the Issues. Admit project records only when the record policy's
  admission questions have concrete answers. The outcome is a current plan
  from which the owner can authorize each further workstream as its own
  bounded entry.
- **Ordering and gates:** apply record-system review to new or changed records,
  research-evidence review to research records and evidence-backed claims, and
  the shared engineering guidance to architecture, dependency-boundary and
  test/evidence strategy decisions, with independent classification of
  material findings. Product value, scope and risk decisions, including those
  listed on #686, return to the owner.
- **Acceptance and closure:** only an explicit further repository-owner request
  closes or deletes this entry. Retaining it does not enlarge this design and
  planning scope.
- **Effects:** permit repository and read-only GitHub inspection, read-only
  retrieval of public web sources, read-only local inspection of the owner's
  private transcription repository and local desk-study archive, documentation
  and directly affected record-routing/catalog changes, Issue and sub-issue
  creation and editing, independent delegated review, and ordinary
  pull-request/check operations and their normal CI.
- **Exclusions:** no implementation, experiment, runtime observation, tool or
  package installation, dataset or model download, training, inference or paid
  compute; work on a workstream beyond its design and planning requires its
  own entry. No other upload of score material to external recognition,
  model-hosting or storage services; no change to the private transcription
  repository; no publication, credential or access change.

### Define the sheet-music text representation (W1)

- **Work carrier:** [Issue #1488](https://github.com/hcoona/three/issues/1488),
  a sub-issue of #686, and its linked pull requests. The repository owner
  requested authorization of all W1 work.
- **Accepted inputs:** those of the sheet-music design and planning entry and
  the project records accepted under either entry. Findings in #1488 are
  unreviewed research inputs.
- **Owner decisions:**
    - W1 code consists of experiments under `src/private/app/sheet-music-omr/`
      in any language the repository currently supports. It does not select
      the product's language, packaging or release.
    - Converters are written anew in this repository; the private v1 converter
      source is not imported.
    - The owner's development agents may read the private transcription
      repository, including its drafts, review records and accepted gold, and
      the owner's local desk-study archive, locally and read-only. Their model
      services process what they read; the owner accepts this. That content,
      anything derived from it and piece identities are never committed,
      otherwise uploaded or quoted; public carriers report only aggregate
      results and anonymized batch numbers.
    - Acceptance uses two measures: a hard criterion of zero in-scope content
      differences on the accepted private gold pieces, and a reported
      layout-fidelity score, where higher is better, that does not gate
      acceptance.
    - Tokenizer comparisons may use the tokenizer files of any candidate base
      model; no base model is selected.
    - The project integrates into the existing monorepo workspaces, checks and
      CI rather than a separate toolchain.
    - The owner approved the local installations and downloads listed under
      Effects. Numeric limits there are accident fuses, not bounds on normal
      development.
- **Advancement and outcome:**
    - Accept W1 requirements covering the required notation categories, the
      as-printed and canonical invariants, system-level carry-in and
      carry-out state, both acceptance measures, explicit unsupported cases
      and the validation basis. The validation basis freezes, from the
      recorded review findings and under research-evidence review, the set of
      toolchain-class findings that the text and converters must eliminate.
    - Accept the text-format specification as the project's contract, with a
      normative grammar and, subject to record admission, the recognition
      sidecar interface. Record the ABC comparison evidence available inside
      W1: an xml2abc/abc2xml round trip and tokenizer counts.
    - Implement experimental forward (text to MusicXML) and reverse (MusicXML
      to text, for MuseScore, PDMX and OpenScore exports) converters, a grammar
      validator, a round-trip harness using MuseScore import/re-export and
      musicdiff scoring, the system-assembly test and the replay of the frozen
      toolchain-class findings.
- **Ordering and gates:** merge this entry before any W1 work. Accept the
  requirements and specification before converter implementation relies on
  them. Accept each experiment or runtime-observation protocol, including the
  ABC round trip, tokenizer counts and MuseScore import checks, under
  research-evidence review before it executes. If the ABC round trip loses
  under about 1% of in-scope elements, the format choice remains open pending
  model evidence outside W1; return to the owner before implementing
  converters. Integration changes to shared root or CI configuration are
  ordered with other entries changing the same files. Each pull request
  retains the applicable record-system, research-evidence and engineering
  reviews, with independent classification of material findings. New
  feasibility, product or scope tradeoffs return to the owner.
- **Acceptance:** the accepted W1 requirements and validation basis are met,
  including the hard criterion and a zero-difference system-assembly test on
  every accepted private gold piece, at least 99% of in-scope elements
  identical on the public corpus files and their MuseScore re-exports, and
  every finding in the frozen toolchain-class set reproduced correctly from
  the text without score-file edits. The desk study counted 34 such findings,
  an unreviewed estimate; if the frozen set materially differs, return to the
  owner. The layout-fidelity score and ABC comparison result are recorded.
  Delete this entry when accepted or when the owner stops W1.
- **Effects:** permit the design and planning entry's inspection and
  documentation effects; local read-only use of the private inputs above;
  local installation of musicdiff 6.0.0, xml2abc/abc2xml and, when missing,
  the MuseScore 4.7.6 command line; download of public scores from OpenScore
  Lieder (CC0) and PDMX, including PDMX's whole-dataset archives, using only
  license-compatible files, and of candidate tokenizer files without model
  weights, all kept outside the repository, stopping to return to the owner
  before cumulative downloads exceed 20 GB; experimental source and tests;
  root workspace membership, lockfile, central package version, check
  configuration and CI test selection and preparation changes needed to
  integrate the project into the existing monorepo tooling; isolated local
  runs and credential-free tests; and ordinary pull-request/check operations
  and their normal CI.
- **Exclusions:** no model weights, including rokot-omr-2b, and no GPU
  inference, training or paid compute; no other workstream's implementation,
  synthetic training data or format A/B training; no committed fixture other
  than self-authored, public-domain or CC0 material with its provenance; no
  other upload of score material to external recognition, model-hosting or
  storage services; no change to the private transcription repository or its
  workflow; no change to existing tool or runtime versions, other projects'
  behavior or required checks, and no new CI workflow; no publication,
  release, credential or access change.

### Align .NET tests that launch external tools with the engineering principles

- **Work carrier:** [Issue #1501](https://github.com/hcoona/three/issues/1501),
  [Issue #1503](https://github.com/hcoona/three/issues/1503) and their linked
  pull requests. The AzureAuth Python doctor harness timed out repeatedly in
  `Build & Test (.NET 10)`. The repository owner then reviewed the external
  tools that job's tests launch and requested the two advancements below.
  Running tests that need a locked external runtime belongs to the entry
  "Add an external-tool dimension to the Workflow Delivery .NET group".
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  root `mise.toml`, `mise.lock`, `.python-version`, `global.json`,
  `eng/scripts/locked_tool_versions.py`, the GitHub workflows, and the
  accepted records of AzureAuth CredProvider, Workflow Delivery and
  vscode-copilot-telegram-hook. The tool inventory and stall analysis in
  #1501 are unreviewed inputs.
- **Diagnosis:**
    - `Build & Test (.NET 10)` provisions only the .NET SDK. Its tests resolve
      `python`, `npm` and `node` from the runner image, which can differ from
      the locked versions; the timed-out harness ran the image's Python
      3.12.10 while `.python-version` selects 3.14.3.
    - Workflow Delivery already runs its locked-toolchain native contracts
      outside that job through `testconfig.json` category exclusions;
      AzureAuth CredProvider and vscode-copilot-telegram-hook have no
      equivalent split.
    - Some Workflow Delivery `NativeProcess` and `NativeCommand` tests launch
      `pwsh` only as an arbitrary child process; some of them re-prove
      documented .NET `Process` behavior.
    - The AzureAuth Python and npm integration tests skip rather than fail
      when their tool is missing, so CI can pass without running them.
- **Owner decisions:**
    - Order the work as advancement 1, then 2.
    - Advancement 2 does not wait for the external-tool dimension entry. It
      applies fail-closed behavior in whichever job runs each test at the
      time; a later move into a tool-provisioning group keeps that behavior.
    - Tests whose subject is a workflow `shell: bash` or `shell: pwsh` step,
      such as `BashWorkflowCallerTests` and `NuGetWorkflowCallerTests`, keep
      the runner image's shells and git, which GitHub Actions itself uses,
      and remain in their current job. Advancement 1 still reviews them.
- **Advancement and outcome:**
    1. Inventory every .NET test that launches an external executable other
       than the test's own build output, and assess each against the testing
       principles. Remove tests that only re-prove documented platform
       contracts. Replace external tools used merely as an arbitrary child
       process with an in-repository test helper process. Repair other
       principle findings in those tests, such as missing launch deadlines.
       Record the inventory and dispositions in #1501.
    2. Under #1503, decide which real-tool tests CI must run. In CI, those
       tests fail rather than skip when their tool is missing. CI results
       name skipped tests and their reasons.
- **Ordering and gates:** merge this entry before any of the work. Each pull
  request follows the owning project's engineering and OCR review gates. It
  also follows record-system review for record changes, and
  research-evidence review for evidence claims. Independently classify
  material findings before repair. Any new feasibility or product tradeoff
  returns to the owner.
- **Acceptance:**
    - Each remaining external-executable test has a recorded disposition.
    - Tests that CI must run fail when their tool is missing.
    - Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection,
  documentation, test, test-configuration and CI workflow changes,
  isolated local builds and credential-free tests, independent delegated
  review, ordinary Issue/PR/check operations and their normal CI, and
  ordinary reruns of failed CI jobs.
- **Exclusions:**
    - No product-behavior, runtime or dependency version changes.
    - No change to existing mise tool installation in workflows (#1505).
    - No toolcache prewarming.
    - No required-check or ruleset change.
    - No release, proving or campaign dispatch, publication, credential,
      Environment, account, publisher or other access change.

### Gate merges only through the Validate aggregate

- **Work carrier:** [Issue #1511](https://github.com/hcoona/three/issues/1511)
  and its linked pull requests. The repository owner reviewed how
  `.github/workflows/ci.yml` gates merges and approved a single gate.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  the [HK execution boundary](engineering/hk-execution.md), the GitHub
  workflows and `tests/eng/test_general_ci_workflow.py`, the `main` ruleset,
  and Workflow Delivery's
  [CI design](../src/private/app/workflow-delivery/docs/design/ci.md) and
  [repository and selection design](../src/private/app/workflow-delivery/docs/design/repository-and-selection.md).
- **Diagnosis:**
    - The `main` ruleset requires `Validate`, `Build & Test (.NET 10)`,
      `Test (Python)`, `Test & Build (Node.js 22.x)` and
      `Test & Build (Node.js 24.x)`. `Validate` aggregates `conformance`,
      `scholarly-tests`, `nuget-native-preparation`,
      `github-release-native-preparation`, `native-preparation-tests`,
      `ruby-group` and `node-tests`.
    - No rule separates the two mechanisms. `mise-lock` and
      `azureauth-foundation-artifacts` gate nothing, although the latter is
      the only job that runs the AzureAuth host tests on Linux and macOS.
    - A matrix job named in the ruleset breaks when its matrix changes.
- **Owner decisions:**
    - `Validate` is the only merge gate. Every other job in `ci.yml`,
      including `mise-lock` and `azureauth-foundation-artifacts`, is a direct
      or transitive dependency of `Validate`; no job is exempt.
    - A repository check fails when any other `ci.yml` job is not a direct or
      transitive dependency of `Validate`.
    - The owner authorizes changing the `main` ruleset so that `Validate` is
      its only required status check.
- **Advancement and outcome:**
    1. Make every job a direct or transitive dependency of `Validate`, make
       each job report success when it does not apply, and add the enforcing
       check. Update the records and tests that describe the gated set.
    2. After that merges and `Validate` passes on `main`, change the `main`
       ruleset to require only `Validate`, and update the tests and records
       that name the former required checks.
- **Ordering and gates:** merge this entry before any of the work, and
  advancement 1 before advancement 2, so `main` is never less protected.
  Each pull request follows the Workflow Delivery engineering and OCR review
  gates and record-system review for record changes, with independent
  classification of material findings. A gated job that fails on `main`
  for a reason outside this entry returns to the owner.
- **Acceptance:**
    - Every other `ci.yml` job is a direct or transitive dependency of
      `Validate`, and the enforcing check passes.
    - The `main` ruleset requires only `Validate`.
    - Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection,
  documentation, test and CI workflow changes, credential-free local
  tests, independent delegated review, ordinary Issue/PR/check operations
  and their normal CI, and the owner-authorized `main` ruleset
  required-check change.
- **Exclusions:**
    - No other ruleset, branch-protection, permission or access change.
    - No change to which checks a job runs, its tools or its applicability.
    - No change to existing mise tool installation in workflows (#1505).
    - No release, proving or campaign dispatch, publication, credential,
      Environment, account or publisher change.

### Add an external-tool dimension to the Workflow Delivery .NET group

- **Work carrier:** [Issue #1509](https://github.com/hcoona/three/issues/1509)
  and its linked pull requests. Issue #1501 holds the triggering timeout and
  the external-tool inventory. The repository owner chose this managed
  design over hand-written retained jobs.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  root `mise.toml`, `mise.lock`, `.python-version` and `global.json`, the
  GitHub workflows, and Workflow Delivery's
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [CI design](../src/private/app/workflow-delivery/docs/design/ci.md) and
  [.NET integration](../src/private/app/workflow-delivery/docs/integration/dotnet.md),
  and the accepted records of AzureAuth CredProvider and
  vscode-copilot-telegram-hook. The stall analysis in #1501 is an
  unreviewed input.
- **Diagnosis:**
    - The .NET group plans every check on `windows-latest`, and
      `Build & Test (.NET 10)` provisions only the .NET SDK. Tests that need
      Python, Node.js or npm resolve them from the runner image, which can
      differ from the locked versions. The timed-out harness ran the image's
      Python 3.12.10 while `.python-version` selects 3.14.3.
    - The Python group already plans a runtime dimension read from the
      candidate's committed `.python-version`.
- **Owner decisions:**
    - A whole .NET test project declares the external tools it requires in
      its project file. The plan carries that tool dimension, with versions
      read from the candidate's committed configuration.
    - The managed caller executes the plan in groups by tool set. Each group
      provisions its tools through official setup actions reading committed
      versions, without mise installation (`mise install`,
      `mise exec --locked`, `jdx/mise-action` installing tools, or an
      equivalent). Results join to cover the whole plan exactly.
    - Each execution group is gated through `Validate`.
    - Runner selection, filtered subsets of one project and selection
      inputs outside the project graph belong to
      [Issue #1512](https://github.com/hcoona/three/issues/1512), which this
      entry does not authorize.
- **Advancement and outcome:** amend the Workflow Delivery design and .NET
  integration records, implement the tool dimension in planning, execution
  and result collection, and declare the required tools in the AzureAuth
  CredProvider and vscode-copilot-telegram-hook test projects whose tests
  launch Python, Node.js or npm.
- **Ordering and gates:** merge this entry before any of the work. Accept the
  design record changes before code relies on them. Start execution grouping
  only after the `main` ruleset no longer names `Build & Test (.NET 10)`
  under advancement 2 of the `Validate` entry. Each pull request
  follows the Workflow Delivery engineering and OCR review gates,
  record-system review for record changes, and research-evidence review for
  evidence claims such as the #1501 cause analysis, with independent
  classification of material findings. New feasibility or product tradeoffs
  return to the owner.
- **Acceptance:**
    - The .NET execution group without declared tools launches no Python,
      Node.js or npm.
    - Each declaring test project runs against its locked tool versions.
    - #1501 records whether the timeout recurs once the doctor test runs
      against the locked Python. Toolcache prewarming is considered only if
      it does, and only by a further owner decision.
    - Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, Workflow
  Delivery source, documentation, test, test-project and CI workflow changes,
  isolated local builds and credential-free tests, independent delegated
  review, ordinary Issue/PR/check operations and their normal CI, and
  ordinary reruns of failed CI jobs.
- **Exclusions:**
    - No runner dimension, filtered subsets or extra selection inputs
      (#1512).
    - No AzureAuth CredProvider or vscode-copilot-telegram-hook product
      behavior change, and no runtime or dependency version change.
    - No change to existing mise tool installation in workflows (#1505).
    - No toolcache prewarming.
    - No ruleset change beyond the `Validate` entry.
    - No release, proving or campaign dispatch, publication, credential,
      Environment, account, publisher or other access change.

### Derive the Node.js CI matrix from declared engines

- **Work carrier:** [Issue #1508](https://github.com/hcoona/three/issues/1508)
  and its linked pull requests. The repository owner chose committed
  release-schedule data over an online query.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  the root `package.json` and the `package.json` files of packages the Node
  group selects, root `mise.toml`,
  `renovate.json`, the GitHub workflows, and Workflow Delivery's
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [CI design](../src/private/app/workflow-delivery/docs/design/ci.md) and
  [Node integration](../src/private/app/workflow-delivery/docs/integration/node.md)
  records.
- **Diagnosis:**
    - The `node-tests` matrix in `ci.yml` is the literal
      `[22.x, 24.x, 26.x]`, unconnected to any declared `engines.node`
      range, so it drifts silently when either changes.
    - The declared ranges differ. The root, `hexo-renderer-asciidoc` and
      `steam-account-history-to-csv` declare `>=22`; the
      `im-acp-gateway/poc/*` packages declare `>=22.6.0`; the
      `hcoona-release-smoke-npm` test fixture declares `>=24.0.0`.
- **Owner decisions:**
    - The matrix is every LTS major from the lowest version satisfying the
      declared `engines.node` up to the newest release, plus the newest
      Node.js major.
    - The release schedule comes from data committed to the repository and
      updated through reviewable pull requests, preferably by Renovate.
      Planning never queries the network, so one commit always plans the
      same matrix.
    - The Workflow Delivery Node group plans the matrix; the workflow does
      not hard-code it. Each matrix job is gated through `Validate`.
- **Advancement and outcome:** amend the Node integration and CI design
  records, commit the schedule data and its update mechanism, implement the
  planning, and replace the literal matrix.
- **Ordering and gates:** merge this entry before any of the work. Replace
  the matrix only after the `main` ruleset no longer names Node.js matrix
  jobs under the `Validate` entry. Each pull request follows the Workflow
  Delivery engineering and OCR review gates and record-system review for
  record changes, with independent classification of material findings. A
  new scheduled workflow with write access, differing `engines` ranges whose
  combination is a product tradeoff, or another new feasibility or product
  tradeoff returns to the owner.
- **Acceptance:**
    - No Node.js version list remains hard-coded in `ci.yml`.
    - Changing `engines.node` or the committed schedule data changes the
      planned matrix without a workflow edit.
    - A schedule update arrives as an ordinary reviewable pull request.
    - Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, Workflow
  Delivery source, documentation, test, Renovate configuration, committed
  schedule data and CI workflow changes, credential-free local tests,
  independent delegated review, ordinary Issue/PR/check operations and their
  normal CI.
- **Exclusions:**
    - No change to declared `engines` ranges or the locked Node.js version.
    - No change to existing mise tool installation in workflows (#1505).
    - No ruleset change beyond the `Validate` entry.
    - No release, proving or campaign dispatch, publication, credential,
      Environment, account, publisher or other access change.
