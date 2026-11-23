# Current Delivery Wave

This record is the sole positive work-authorization authority for feature or
capability advancements, architectural or public-contract changes, and external
or risk-bearing operations under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency);
contract-preserving routine maintenance without new external, paid,
credentialed, or privacy effects executes directly through ordinary pull
requests without a Wave entry. Only `main`'s accepted version grants Wave
authorization. Within an authorized workstream, routine local editing, builds,
lint, tests, scratch harnesses, read-only inspection, independent review, and
ordinary Issue/PR/CI validation (including failed-job reruns) are permitted by
default with no attempt quota, and implementation may evolve concurrently with
its owning records so long as each merge into `main` stays self-consistent.
Entries declare bounded scope and outcome, exclusions, and any explicitly
permitted external or risk-bearing effects. Merge changes authorization;
deletion ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Refine LiteAgensh migration parity and normal user operation

- **Work carrier:** [Issue #1542](https://github.com/hcoona/three/issues/1542)
  and its linked authorization, design and implementation pull requests.
  The repository owner requested project-wide adjustment and refinement,
  including migration reconciliation, normal user experience and
  engineering-principled simplification, not only one configuration change.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md), and
  LiteAgensh's [project portal](../src/private/app/lite-agensh/README.md),
  [requirements](../src/private/app/lite-agensh/docs/requirements.md),
  [architecture](../src/private/app/lite-agensh/docs/architecture.md),
  [implementation contracts](../src/private/app/lite-agensh/docs/implementation-contracts.md),
  [migration map](../src/private/app/lite-agensh/docs/upstream-migration.md)
  and [validation basis](../src/private/app/lite-agensh/docs/validation.md).
  Preserve the accepted implementation and bounded evidence in
  [PR #1530](https://github.com/hcoona/three/pull/1530); this is a new
  development grant, not revival of its closed native acceptance campaign.
- **Advancement and outcome:** make the existing C# single-machine product
  coherent and usable for normal human operation while preserving Agensh's
  equal-peer collaboration model. Reconcile concrete migration gaps, improve
  configuration, startup, progress inspection, reporting and lifecycle
  interaction, and simplify implementation where a current scenario
  demonstrates the benefit. The first selected change is editable YAML
  configuration with a separate Markdown goal and standard VS Code YAML
  Schema guidance, retaining existing JSON configuration compatibility.
  Follow-on changes may cover the whole LiteAgensh project—including directly
  required dependency, schema/editor and record-routing integration—rather than
  only that first input format; each remains inside this product outcome and
  its applicable accepted decisions.
- **Owner decisions and boundaries:** workers share a goal and cooperation
  template and select work through tasks, advisory claims and peer messages;
  do not replace that model with permanently assigned worker responsibilities
  or a central intelligent scheduler. Normal user operation is not a request
  to rerun the former development acceptance campaign. Questions about
  calibration, paging, reports and root-task lifecycle require concrete
  decisions before their behavior changes, not speculative rewrites.
- **Ordering and gates:** merge this authorization before substantive work.
  Keep implementation and its owning records self-consistent at each merge into
  `main`, and keep dependent consumers and navigation consistent. Retain
  applicable independent engineering, record-system and research-evidence
  review, `GOV-011` finding disposition, applicable explicit-base record
  checks, focused managed validation and ordinary CI. Recheck a relied-on
  mutable interface before dependent changes; uncertainty pauses only that
  dependent work. Do not infer native support from controlled tests or add a
  new support claim without its required evidence.
- **Acceptance and closure:** the owner accepts coherent migration semantics,
  human-usable operation and proportionate implementation within the existing
  product boundary. Only an explicit further repository-owner request closes
  or deletes this entry; accepting an individual change does not terminate
  the broader refinement workstream. Retention does not grant another
  product, provider or trust boundary. Keep plans, findings and progress in
  the Issue and PRs rather than the Wave.
- **Exclusions:** no new paid native acceptance executions, account or quota
  inspection, provider/backend substitution, publication, release/proving
  dispatch, credential, authentication, subscription or access change.
  Do not reset or delete retained native ledgers, unknown reservations,
  session/evidence state or owner work. New native observations require
  separately accepted exact effects and protocol boundaries; this entry
  does not revive or silently enlarge the former fixture-only protocol.
  No unrelated project implementation or new hosted collaboration stack.

### Run and repair the LiteAgensh 32-peer user audit

- **Work carrier:** [Issue #1552](https://github.com/hcoona/three/issues/1552)
  and its linked authorization, protocol, implementation and outcome PRs.
  The repository owner requests repeated actual execution and repair until
  the real user scenario works, rather than stopping at managed tests.
- **Accepted inputs:** the repository governance and record policies,
  [engineering principles](engineering/engineering-principles.md), and
  LiteAgensh's [portal](../src/private/app/lite-agensh/README.md),
  [requirements](../src/private/app/lite-agensh/docs/requirements.md),
  [architecture](../src/private/app/lite-agensh/docs/architecture.md),
  [contracts](../src/private/app/lite-agensh/docs/implementation-contracts.md),
  [migration map](../src/private/app/lite-agensh/docs/upstream-migration.md)
  and [validation basis](../src/private/app/lite-agensh/docs/validation.md).
- **Advancement and outcome:** accept a project-local user-audit protocol,
  execute the actual read-only migration review with 32 equal peers and one
  shared Chinese goal in a foreground psmux coordinator, diagnose retained
  failures, and repeat after bounded, reviewed repairs until the user journey
  works. First stabilize the runtime journey with short, small-peer Luna
  executions, expanding on Luna only after the smaller scenario succeeds.
  Require observed startup, peer cooperation, automatic latest-team/file report
  convergence after quiescence, and complete process/usage settlement before
  advancing to Terra; do not use Terra to debug an unstable runtime journey.
  Lightweight success is a prerequisite, not completion of the 32-peer audit.
  Keep runtime usability distinct from the audit's semantic conclusion
  about migration completeness; a test pass or merely launched coordinator is
  not completion. Source repairs use the existing refinement entry and its
  accepted product decisions, not a new product or recovery architecture.
- **Owner risk decision and effects:** the owner requests paid use of the
  existing Windows Copilot SDK/CLI, existing authentication and trusted-host
  policy for this actual repository audit, explicitly selecting Luna for
  lightweight stabilization and the supported GPT-6.1 Terra model only after
  that prerequisite. Resolve and verify each selected model through its
  supported native identifier without fallback or provider substitution.
  Workers may process
  the declared repository through that model service; worktrees are not a
  host-permission sandbox. Permit owned worktree/state/process creation,
  worker-bound local MCP cooperation, private audit output, read-only public
  upstream inspection, and required non-model preflight and paid calibration.
  Permit ordinary dependency preparation for already-declared MCP tools and
  permitted managed checks under the
  [user-audit protocol](../src/private/app/lite-agensh/docs/user-audit-protocol.md#dependency-preparation).
  Downloads, restoration and generated dependency directories or normal tool
  caches are environment preparation, not permission to change source,
  dependency declarations, declared versions or lockfiles. The owner permits
  the existing installed Copilot CLI without a version restriction; a CLI
  version change alone requires no further owner approval. Record the observed
  version and verify the required supported interfaces before dependent native
  work. Accept the corresponding owning-record and implementation changes on
  `main` before dependent native use. Retain the existing SDK selection,
  disabled runtime acquisition and automatic updates, and no CLI installation,
  backend substitution, authentication or access change.
  Workers must not edit those protected inputs or workflows, commit/push,
  mutate GitHub, invoke nested agents/models or inspect unrelated host,
  credential, account or historical-session data. The executor may inspect
  only this campaign's exact owned diagnostics to diagnose failures, plus
  aggregate host physical/commit/pagefile counters and memory counters bound
  to its owned process identities. Do not inspect unrelated processes or
  change host/pagefile settings.
- **Finite execution boundary:** retain no privileged planner and ceilings of
  32 workers, a 1,800-second per-run deadline, 20,000 AI Credits in total per
  initialized attempt and 100,000 AI Credits cumulatively across all subsequent
  trials. Luna stabilization uses fewer workers, shorter deadlines and smaller
  run allowances declared in the separately accepted operational protocol;
  the final Terra audit retains the 32-peer configuration. Model or scale
  changes require fresh initialized inputs and matching preflight/calibration
  receipts, not changes to an already initialized attempt.
  Include every worker, calibration, failed attempt, repetition, fresh session
  and paid diagnostic in those allowances; they are not per-worker budgets.
  The owner explicitly authorizes one new forward-only campaign ledger now,
  despite the old ledger's retained uncertain usage. First verify old owned
  processes are quiescent and preserve the old ledger, unknown reservations
  and evidence unchanged. Their usage remains unknown, not zero or reconciled.
  Record the one-time transition privately; after it, every subsequent trial
  uses that same new ledger and its 100,000-credit cumulative allowance.
  Do not continue spending from old ledgers, create parallel budgets or reset
  again. This exception does not change ordinary unknown-usage admission:
  uncertainty in the new ledger blocks further paid work until resolved by
  the accepted reconciliation path. There is no separate run-count quota.
  The owner accepts the distinct
  [verified pre-dispatch cancellation](../src/private/app/lite-agensh/docs/implementation-contracts.md#verified-pre-dispatch-cancellation)
  evidence level under the existing trusted same-user boundary, not
  provider-complete zero usage. Permit conditional application to the retained
  actual08 worker28 case declared in the
  [user-audit protocol](../src/private/app/lite-agensh/docs/user-audit-protocol.md#verified-pre-dispatch-accounting),
  plus qualifying fresh sessions in this continuing campaign. Accept the owning
  records and implementation on `main` before any disposition. The public
  accounting-only path must recheck live evidence and ownership; no snapshot
  substitution, manual edit, blanket historical release or second reset.
  Native limits remain soft, with possible in-flight overshoot accepted and
  recorded; tokens or elapsed time do not prove exact spend.
- **Accounting evidence decision:** the owner accepts the
  [SDK-native recovery boundary](../src/private/app/lite-agensh/docs/implementation-contracts.md#sdk-native-recovery-boundary)
  for qualifying dispatched uncertainty in this continuing campaign.
  Original routine native termination and a newly checked supported complete
  cumulative reading may establish accounting without successful original
  SDK-send delivery or individual final-return certificates for historical
  cancelled requests. This is native-reported accounting, not provider-final
  invoices or zero usage. Accept the owning decision and its implementation on
  `main` before an exact non-dispatch invocation; preserve original failures,
  unobserved-provider limits, all other uncertainty and the sole ledger.
- **Ordering and gates:** this Wave must merge before newly authorized work.
  Accept the exact project-local operational protocol on `main` before any paid
  native observation. It must cover the subject/build identities,
  environment and interface rechecks, repository and owned-state scope,
  actions, expected observations, finite bounds, stops, cleanup and evidence
  limits; the old fixture-only protocol is not a substitute. Retain applicable
  independent record-system, research-evidence and engineering review,
  `GOV-011` finding disposition, applicable exact-base record checks, relevant
  managed validation and ordinary CI. Failed attempts retain their outcomes and
  all ledger/session/evidence state before any safe repetition.
- **Acceptance and closure:** require actual native dispatch and peer
  coordination, substantive source-backed audit outputs with independent
  finding classification, a Chinese final summary in team state and the
  owned private output, and complete process/usage settlement. A run deadline,
  mechanical finalization or tests alone do not satisfy the journey. Keep
  missing coverage, unresolved findings and unsupported behavior explicit.
  Record commands, identities, outcomes and sanitized limitations in #1552
  and its PRs; keep private paths and raw transcripts local. Close this grant
  by an accepted Wave deletion after owner acceptance or explicit stop.
- **Exclusions:** no reopening the former development acceptance campaign,
  general 32-worker/backend support claim, provider substitution, account or
  quota inspection, authentication/subscription/access change, release,
  publication or proving dispatch. No deletion or rewriting of any old ledger,
  unknown reservation, session evidence or owner work, and no ledger reset
  beyond the single transition explicitly granted above. New implementation,
  dependency or recovery-policy decisions retain their existing prerequisites;
  repeated paid execution does not authorize bypassing them.

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
  documentation (including directly affected record-routing/catalog bindings)
  against the current implementation and accepted authorities.
  Delete superseded records after migrating current consumers, links and any
  unique current rationale or evidence. Use immutable Git references for
  historical bodies rather than an archive or duplicate authority. Begin with
  closed-campaign validation records and the mixed smoke research records;
  preserve current native-integration findings, recheck obligations, provenance,
  evidence limits, stable requirement identifiers, legal notices and immutable
  human-source inputs.
- **Ordering and gates:** merge this authorization before substantive cleanup.
  Migrate consumers and navigation atomically with retirement. Retain
  applicable independent record-system, research-evidence and engineering/OCR
  reviews, `GOV-011` finding disposition, record checks and affected HK
  validation. Product, evidence-level or authority changes outside faithful
  documentation reconciliation return to the owner.
- **Acceptance and closure:** the current tree has one authority per retained
  concern, accurate implementation/status routing and no superseded body
  without a current consumer. Only an explicit further repository-owner request
  may close or delete this entry; completing or merging an individual cleanup
  PR does not authorize its closure. Retaining the entry does not enlarge this
  bounded documentation scope. Keep progress and results in the Issue and PRs.
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
  their consumers. Each implementation PR follows the applicable Workflow
  Delivery engineering, OCR, record-system and research-evidence review gates
  and `GOV-011` finding disposition. New feasibility or product tradeoffs
  return to the owner.
- **Acceptance:** a routine Renovate patch update of each affected tool
  changes only committed configuration and passes ordinary CI without code,
  test or workflow edits; no remaining literal duplicates a committed tool
  selection outside a documented adapter limit; the renamed required check
  protects `main`. Dispatch-only Release and smoke workflows are accepted on
  static validation (actionlint, workflow tests and their local readers)
  because their campaigns are closed. Delete this entry when accepted.
- **External effects:** permit the owner-authorized `main` ruleset
  required-check rename.
- **Exclusions:** no Python 3.15 migration (tracked by #1247) and no change to
  the Renovate Python hold; no release, proving or campaign dispatch,
  publication, credential, Environment, account, publisher or other access
  change; no restoration or modification of the retired legacy
  `release-orchestrate.yml` stack beyond an owner decision recorded separately;
  no new runtime matrix.

### Repair Workflow Delivery dependency cohorts and SDK host compatibility

- **Work carrier:** [Issue #1679](https://github.com/hcoona/three/issues/1679)
  and its linked authorization and repair PRs, including the NuGet client
  update in [#1658](https://github.com/hcoona/three/pull/1658) and declared
  SDK update in [#1652](https://github.com/hcoona/three/pull/1652).
- **Accepted inputs:** the shared
  [engineering principles](engineering/engineering-principles.md), root
  `Directory.Packages.props`, `global.json` and `renovate.json`, and Workflow
  Delivery's [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [.NET integration contract](../src/private/app/workflow-delivery/docs/integration/dotnet.md),
  [NuGet publisher source basis](../src/private/app/workflow-delivery/docs/research/nuget-native-publisher.md)
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
- **Advancement and outcome:** reconcile the seven directly referenced
  official NuGet clients as a coherent version-8 cohort, including affected
  CPM declarations, locks, Renovate grouping and owning selection/source
  records. Repair the declared SDK 11 compilation and mixed-endpoint task
  journey without coupling the controller's target runtime to whichever
  SDK supplies native MSBuild. Keep tool selection derived from committed
  configuration, not a historical SDK allowlist.
- **Owner decisions and boundaries:** component owners select the complete
  engineering-principled implementation, rather than the smallest diff.
  Preserve current product target frameworks, endpoint-selected native
  MSBuild responsibilities, publisher behavior and existing transport,
  recovery and trust boundaries. A required change to those boundaries or
  a material product/feasibility tradeoff returns to the repository owner;
  it is not implicitly authorized as dependency maintenance.
- **Ordering and gates:** merge this authorization before the newly proposed
  substantive work. Keep changed project decisions, version-bound source
  findings, and implementation self-consistent at each merge into `main`.
  Retain applicable independent engineering/OCR, record-system and
  research-evidence review, `GOV-011` finding disposition, applicable
  explicit-base record checks and HK, and ordinary qualification. An earlier
  client/source qualification does not qualify a new dependency cohort.
- **Acceptance:** the declared updates pass the existing applicable ordinary
  checks on their actual source: resolved package/output identities,
  NBGV/SDK binding, locked native restore and clean consumers, controlled
  version-sensitive publisher guards, and the base/candidate endpoint
  journey. Preserve historical failures and original publication evidence
  without promoting them into new support or destination claims. Delete
  this entry when the bounded repair is accepted.
- **Exclusions:** no campaign replay, new proving generation, release/proving
  dispatch, publication, credential, authentication, permission or access
  change; no repository-wide target-framework migration or new runtime
  matrix. Existing closed-campaign slots remain spent. Ordinary qualification
  does not revive native experimentation or live-operation grants.

### Diagnose and repair Windows NuGet workflow caller liveness

- **Work carrier:** the authorization PR proposing this entry and its linked
  diagnosis and repair PRs. [PR #1673](https://github.com/hcoona/three/pull/1673)
  retains the triggering ordinary Windows CI failures and independent
  evidence-gap classification. The repository owner explicitly requested this
  narrow shared-adapter diagnostic and repair advancement.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md), and
  Workflow Delivery's [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [execution boundaries](../src/private/app/workflow-delivery/docs/design/execution-boundaries.md),
  [.NET integration](../src/private/app/workflow-delivery/docs/integration/dotnet.md)
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
  Retain the existing native-command contract, caller scenarios, failed source
  epochs and evidence limits; an evidence gap is not an established source or
  platform defect.
- **Advancement and outcome:** add the minimum necessary timeout-observation
  diagnostics at the shared `NativeProcess` boundary before cleanup, then use
  ordinary Windows validation to localize and repair the four existing
  `NuGetWorkflowCallerTests` failures (including directly required
  shared-adapter, caller/test and owning record changes): the Absent state row,
  the Exact and Conflict verification rows, and unhandled-observation failure.
  Bind original source/run/attempt and retained results before selecting a
  causal repair.
  Start with watchdog/launch/cancellation-observation timing and root/exit-wait/
  stdout/stderr completion states in the existing diagnostic result boundary,
  not a child-process tracking service or independent full tracing channel.
  A shared cancellation or reported await does not prove which boundary
  stalled; a phase observation is not by itself a root-cause attribution.
- **Owner decisions and boundaries:** preserve the real runner-image `pwsh`,
  the existing four first steps, 30-second deadline, method-level scheduling,
  assertions, serialized result contract, native execution and bounded cleanup
  semantics, target frameworks and dependency selections. Component owners
  select the proportionate causal repair under the engineering principles.
  A required change to those contracts, trust/recovery boundaries or material
  scope/feasibility tradeoff returns to the repository owner.
- **Ordering and gates:** merge this entry before substantive work. Keep any
  changed governing decision and its implementation self-consistent at each
  merge into `main`. Retain applicable independent engineering/OCR,
  record-system and research-evidence review, `GOV-011` finding disposition,
  applicable explicit-base record checks and HK, focused managed validation and
  ordinary CI on the actual source. Keep development-only probes in
  outside-repository temporary projects; design durable diagnostics and tests
  from the accepted contracts rather than copying a development fixture.
- **Acceptance and closure:** the four existing Windows caller scenarios and
  their applicable ordinary checks pass with the preserved contracts. Keep
  original failures, unproven causes and unrelated LiteAgensh uncertainty
  distinct; successful qualification does not retroactively establish a cause
  or broader platform support. Delete this entry when the bounded repair is
  accepted or the owner stops it. Other active grants remain unchanged.
- **Exclusions:** no timeout increase, retry or blanket serialization, new
  tracing service, unrelated process/handle/account inspection, shared-host
  configuration change, provider substitution or dependency/runtime upgrade.
  No live registry/model/service operation, paid native execution, publication,
  release/proving or campaign dispatch, credential, authentication or access
  change. Closed campaign limits and other grants are not reopened or enlarged.

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
  findings in the Issues. Admit project records (including directly affected
  record-routing/catalog bindings) only when the record policy's admission
  questions have concrete answers. The outcome is a current plan from which the
  owner can authorize each further workstream as its own bounded entry.
- **Ordering and gates:** apply applicable record-system review, applicable
  research-evidence review, and the shared engineering guidance to architecture,
  dependency-boundary and test/evidence strategy decisions, with `GOV-011`
  finding disposition. Product value, scope and risk decisions, including those
  listed on #686, return to the owner.
- **Acceptance and closure:** only an explicit further repository-owner request
  closes or deletes this entry. Retaining it does not enlarge this design and
  planning scope.
- **External effects:** permit read-only local inspection of the owner's
  private transcription repository and local desk-study archive, and Issue and
  sub-issue creation and editing.
- **Exclusions:** no implementation, experiment, runtime observation, tool or
  package installation, dataset or model download, training, inference or paid
  compute; work on a workstream beyond its design and planning requires its
  own entry. No other upload of score material to external recognition,
  model-hosting or storage services; no change to the private transcription
  repository; no publication, credential or access change.

### Build OMR synthetic data engine and system-detection benchmark (W4 and W5)

- **Work carrier:** [Issue #1491](https://github.com/hcoona/three/issues/1491)
  (W4, with sub-issues [Issue #1493](https://github.com/hcoona/three/issues/1493),
  [Issue #1494](https://github.com/hcoona/three/issues/1494),
  [Issue #1495](https://github.com/hcoona/three/issues/1495) and
  [Issue #1496](https://github.com/hcoona/three/issues/1496)) and
  [Issue #1497](https://github.com/hcoona/three/issues/1497) (W5), sub-issues
  of #686, and their linked pull requests. Following owner dispositions on
  [#1491](https://github.com/hcoona/three/issues/1491#issuecomment-7113656959)
  and [#1497](https://github.com/hcoona/three/issues/1497#issuecomment-7113657600),
  the repository owner requested authorization of the W4 synthetic engine and
  Stage S3 proof slice together with W5's system-detection benchmark and
  candidate system-assembly validator.
- **Accepted inputs:** those of the sheet-music design and planning entry and
  the accepted project records under `src/private/app/sheet-music-omr/`
  ([portal](../src/private/app/sheet-music-omr/README.md),
  [requirements](../src/private/app/sheet-music-omr/docs/requirements.md),
  [Text Score 2 format](../src/private/app/sheet-music-omr/docs/text-format.md),
  [comparison protocol](../src/private/app/sheet-music-omr/docs/comparison-protocol.md),
  [evaluation and real-data alignment protocol](../src/private/app/sheet-music-omr/docs/evaluation-protocol.md)
  and [handoff](../src/private/app/sheet-music-omr/docs/agent-handoff.md)).
  Findings in #686, #1491, #1493 through #1496 and #1497 are unreviewed
  research inputs; any repository record that relies on one rechecks it under
  research-evidence review.
- **Owner decisions:**
    - W4 and W5 implementation lives under `src/private/app/sheet-music-omr/`
      in Python integrated with workspace checks and CI. External rendering,
      rasterization and experimental detector tools install into isolated
      out-of-worktree environments (`venv` or `uvx`) recorded per run so
      repository-locked toolchains remain unchanged unless a reviewed
      dependency change is accepted. This does not select product language,
      packaging or release.
    - Authorize the end-to-end W4a–W4d synthetic engine and a local CPU Stage
      S3 proof slice (`>= 50` pieces per engine for the round-trip label gate,
      `>= 100` pages per engine for timing/memory/storage measurement, YOLO
      overlay checks and synthetic-versus-real crop statistics), deferring
      `20k`–`50k`-page bulk pilot generation and W6 GPU model training (#1498)
      to a later Wave.
    - Partition every ingested source and synthetic sample by explicit
      `rights_variant` (`permissive`, `nc_sa` or `private_traindev`) in
      physically separated directories and manifests so downstream training can
      select `permissive`-only or combined buckets by flag. Drive the Stage S3
      proof primarily from staged `permissive` corpora (`OpenScore Lieder`
      `CC0-1.0` and the `PDMX` `no_license_conflict` two-staff piano subset),
      while permitting isolated optional acquisition of `nc_sa` corpora
      (`DCML`, `Humdrum`, `ASAP`) and isolated local re-rendering of the `16`
      accepted non-test `TRAIN_DEV` pieces into `private_traindev`. Strictly
      enforce the `TEST` exclusion manifest: zero overlap with the `7` frozen
      `TEST` pieces (`gold-01` through `gold-06` and `gold-18`) by work
      identity or file digest.
    - Lock the default recognizer input domain to the restored US-Letter
      facsimile PDF (`~303 dpi`). Integrate both host `MuseScore 4.x` CLI
      (primary renderer, recording observed version and executable `SHA-256`
      per run without pinning host upgrades) and isolated `Verovio` (secondary
      renderer with SVG rasterization), and activate `Tier R`
      restored-facsimile residue degradation (residual warp, resampling/deblur
      blur, illumination/tint, show-through, gutter shading and unlabeled
      pencil/handwritten clutter) with co-warped `14.0 px`-per-staff-space
      system crops and YOLO boxes; defer bulk raw-camera `Tier P` generation
      unless a later Wave selects raw photo input.
    - Do not impose an artificial tool allowlist on local W5 system-detection
      experiments: benchmark the repository's automatic `comb + DP` detector
      (without manual box overrides), `kobimusic/copisteria`'s structure
      front-end (`Apache-2.0`), and isolated out-of-worktree `homr` (`uvx homr`
      or isolated `venv`, `AGPL-3.0`, invoked strictly as an external local CLI
      process without vendoring or importing `AGPL` code into repository
      packages) against the pixel-approved grand-staff boxes (`69` `TRAIN_DEV`
      pages for development/tuning with zero parameter tuning on `TEST`, plus
      at most one locked zero-tuning `92`-page per-book-group box-geometry
      count report that never reads `TEST` notation labels).
    - Retain #686's privacy boundary (`OMR-REQ-015`). Numeric download, disk
      and page-count bounds under External effects are accident circuit
      breakers sized above normal Stage S3 and W5 development.
- **Advancement and outcome:**
    - Accept project requirements and the W4/W5 synthetic-data and
      system-detection protocol defining: (a) `rights_variant` partitioning,
      `TEST`-exclusion verification, two-staff piano filtering, canonical
      MusicXML normalization and per-category coverage accounting (W4a);
      (b) labeled annotation injection (`N6` fingering, `N1/N2`
      dynamics/hairpins, `N3/N4` text/tempo, `N5` both pedal styles, `L2`
      slurs, `L3/L4` articulations/arpeggios, `P3` ottavas), rare-category
      source weighting and unlabeled clutter rules (W4b); (c) `MuseScore 4.x`
      and `Verovio` batch drivers, `G1`–`G7` layout-density matching,
      re-export `musicdiff` and `Text Score 2` round-trip label gates, `Tier R`
      degradation with co-warped `14.0 px` system crops and YOLO boxes, and
      sim-to-real crop statistics (W4c/W4d); (d) W5 off-the-shelf
      system-detection metrics (missed, merged and split systems per book
      group) and candidate carry-state system-assembly integrity checks; and
      (e) cumulative formal-execution accident-fuse ledgers, stop conditions
      and cleanup rules.
    - Implement the W4a–W4d synthetic engine, Stage S3 QA and throughput
      harness, W5 detector benchmark runner, and candidate carry-state
      system-assembly validator under `src/private/app/sheet-music-omr/`
      (including directly required record-routing or CI test-selection
      updates).
    - Execute the W5 system-detection benchmark and the W4 Stage S3 proof run,
      and publish sanitized evidence reports and the detector training
      decision on #1491 (#1493 through #1496), #1497 and #686.
- **Ordering and gates:** merge this entry before substantive W4 or W5
  implementation, external tool/package installation, tool or corpus network
  fetch, or runtime execution. Keep project records and implementation
  self-consistent at each merge into `main`, and accept the W4/W5 requirements
  and protocol on `main` under record-system review, research-evidence review
  and shared engineering guidance before any authorized W4/W5 external
  tool/package installation, any tool or corpus network fetch, any batch
  synthetic rendering/degradation run, or any W5 detector benchmark on staged
  private facsimile pages. Apply `GOV-011` finding disposition. New product,
  scope or feasibility tradeoffs return to the owner.
- **Acceptance and closure:** the tested W4a–W4d synthetic engine and W5
  candidate system-assembly validator are merged on `main`, and independently
  reviewed sanitized Stage S3 proof results (source/coverage counts by
  `rights_variant` with zero `TEST` overlap, `>= 50`-piece per-engine
  round-trip drop rates, `>= 100`-page per-engine throughput/storage metrics,
  synthetic-versus-real crop statistics and YOLO box checks) and W5
  system-detection benchmark results (missed, merged and split system counts
  with a concrete train-versus-no-train detector decision) are recorded on
  #1491 (#1493 through #1496) and #1497, with project portal/handoff records
  updated. Close this entry by deleting it in the final PR that completes
  acceptance, or through an explicit Wave-only closure PR after owner
  acceptance or owner stop.
- **External effects:** permit the design and planning entry's private-archive
  inspection and Issue effects; local read-only use of staged public corpora
  (`Lieder-v3.0.0.zip`, `PDMX.csv`, `mxl.tar.gz`), staged private restored
  facsimile PDFs, `gold-cache` (`TRAIN_DEV` read-only for optional
  `private_traindev` re-rendering and `TEST` exclusion digest verification;
  `TEST` gold never re-rendered or tuned against), existing alignment/crop
  artifacts, and cached `copisteria` environments; creation and use of
  out-of-worktree `RUN`, `DOWNLOADS`, segregated `rights_variant` proof outputs
  and isolated virtual-environment / `uvx` tool caches; host `MuseScore 4.x`
  CLI invocation recorded by observed version and `SHA-256` per run; and
  isolated out-of-worktree installation and local execution of rendering,
  image-processing and experimental system-detection tools (including
  `verovio`, `pyvips` / `libvips`, `opencv-python`, `scipy`, `pillow`,
  `pypdfium2`, `homr` via `uvx` or isolated `venv`, and related Python helpers,
  recorded by exact version/commit per run) plus optional `nc_sa` symbolic
  corpora (`DCML`, `Humdrum`, `ASAP`) into segregated `DOWNLOADS`, stopping to
  return to the owner before cumulative new W4/W5 network downloads exceed
  `15 GB` in `DOWNLOADS/w4-w5-download-ledger.json` (preserving earlier closed
  ledgers unchanged), before generated local W4/W5 proof outputs exceed `50 GB`
  on disk, before a single proof run renders more than `5,000` pages
  (`300`-second per-worker batch timeout), or before cumulative formal Stage S3
  and W5 runs exceed `25,000` rendered pages, `20` formal `69`-page `TRAIN_DEV`
  W5 detector benchmark starts, or `3` starts toward the single locked
  zero-tuning `92`-page W5 box-geometry count report (the `3`-start ceiling is
  a circuit breaker for failed or aborted attempts, not authorization to start
  another `92`-page pass after one succeeds) in a persistent
  `DOWNLOADS/w4-w5-run-ledger.jsonl` (never reset by output cleanup; ordinary
  local unit/scenario tests and repository-fixture checks remain
  unconstrained).
- **Exclusions:** no `20k`–`50k`-page bulk pilot generation (`> 5,000` pages
  per run) under this entry; no neural model training or weight fine-tuning
  (neither W6 recognizer SFT #1498 nor training a new YOLO system detector); no
  recognizer `TEST` scoring and no edit, reset or deletion of historical
  download or `TEST`-touch ledgers; no live transcription-workflow modification
  or selective-review policy change (W7, #1499); no vendoring or importing
  `AGPL` code (`homr`, `transcoda`) into repository source or committed
  dependencies; no ingestion or re-rendering of `TEST` pieces or unreviewed
  public image+label targets (`GrandStaff`, `OLiMPiC`, `FP-GrandStaff`,
  `Polish-scores`, `SMB`); no committed fixture other than self-authored,
  public-domain or `CC0` material with provenance; no commit or public upload
  of private paths, piece/book identities or copyrighted score content, and no
  upload of score material to external OMR, hosting or cloud-compute services;
  no modification to the private transcription repository or staged gold files;
  no unreviewed change to repository-locked tool/runtime versions or other
  projects' behavior, and no publication, release, credential or access change.

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
    2. Under #1503, decide which real-tool tests CI must run, updating affected
       test configuration and CI workflows as needed. In CI, those tests fail
       rather than skip when their tool is missing. CI results name skipped
       tests and their reasons.
- **Ordering and gates:** merge this entry before any of the work. Each pull
  request follows the owning project's engineering and OCR review gates,
  applicable record-system and research-evidence review, and `GOV-011` finding
  disposition. Any new feasibility or product tradeoff returns to the owner.
- **Acceptance:**
    - Each remaining external-executable test has a recorded disposition.
    - Tests that CI must run fail when their tool is missing.
    - Delete this entry when accepted.
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
  gates, applicable record-system review, and `GOV-011` finding
  disposition. A gated job that fails on `main` for a reason outside this entry
  returns to the owner.
- **Acceptance:**
    - Every other `ci.yml` job is a direct or transitive dependency of
      `Validate`, and the enforcing check passes.
    - The `main` ruleset requires only `Validate`.
    - Delete this entry when accepted.
- **External effects:** permit the owner-authorized `main` ruleset
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
  integration records, implement the tool dimension in planning, execution,
  result collection and affected CI workflows, and declare the required tools
  in the AzureAuth CredProvider and vscode-copilot-telegram-hook test projects
  whose tests launch Python, Node.js or npm.
- **Ordering and gates:** merge this entry before any of the work. Keep design
  record and code changes self-consistent at each merge into `main`. Start
  execution grouping only after the `main` ruleset no longer names
  `Build & Test (.NET 10)` under advancement 2 of the `Validate` entry. Each
  pull request follows the Workflow Delivery engineering and OCR review gates,
  applicable record-system review, and applicable research-evidence review
  (such as the #1501 cause analysis), with `GOV-011` finding
  disposition. New feasibility or product tradeoffs return to the owner.
- **Acceptance:**
    - The .NET execution group without declared tools launches no Python,
      Node.js or npm.
    - Each declaring test project runs against its locked tool versions.
    - #1501 records whether the timeout recurs once the doctor test runs
      against the locked Python. Toolcache prewarming is considered only if
      it does, and only by a further owner decision.
    - Delete this entry when accepted.
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
  records, commit the schedule data and its Renovate update mechanism,
  implement the planning, and replace the literal matrix in `ci.yml`.
- **Ordering and gates:** merge this entry before any of the work. Replace
  the matrix only after the `main` ruleset no longer names Node.js matrix
  jobs under the `Validate` entry. Each pull request follows the Workflow
  Delivery engineering and OCR review gates, applicable record-system review,
  and `GOV-011` finding disposition. A new scheduled workflow with
  write access, differing `engines` ranges whose combination is a product
  tradeoff, or another new feasibility or product tradeoff returns to the
  owner.
- **Acceptance:**
    - No Node.js version list remains hard-coded in `ci.yml`.
    - Changing `engines.node` or the committed schedule data changes the
      planned matrix without a workflow edit.
    - A schedule update arrives as an ordinary reviewable pull request.
    - Delete this entry when accepted.
- **Exclusions:**
    - No change to declared `engines` ranges or the locked Node.js version.
    - No change to existing mise tool installation in workflows (#1505).
    - No ruleset change beyond the `Validate` entry.
    - No release, proving or campaign dispatch, publication, credential,
      Environment, account, publisher or other access change.

### Restore and validate the Renovate scheduled workflow

- **Work carrier:** [Issue #1531](https://github.com/hcoona/three/issues/1531),
  with #1508 as the related Node-runtime history.
- **Accepted inputs:** the shared [engineering principles](engineering/engineering-principles.md);
  the `.github/workflows/renovate.yml`, `.github/workflows/ci.yml`,
  `renovate.json`, `.github/renovate/**`, `mise.toml`, `mise.lock`, and
  `eng/renovate/**` inputs; and Workflow Delivery's
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [repository and selection design](../src/private/app/workflow-delivery/docs/design/repository-and-selection.md),
  [CI design](../src/private/app/workflow-delivery/docs/design/ci.md),
  [execution boundaries](../src/private/app/workflow-delivery/docs/design/execution-boundaries.md),
  and [agent handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
- **Diagnosis:**
    - Scheduled run [41743250099](https://github.com/hcoona/three/actions/runs/41743250099)
      failed because Renovate 43.200.1 declares Node `^24.11.0` and the workflow
      ran it with locked Node 26.13.0.
    - The workflow installs Renovate globally from a version constant, without a
      lock for its transitive dependencies. The Renovate CLI itself must run on
      its declared Node range, while repository tools continue to use the
      repository's locked Node.
    - A pull request currently has no credential-free check of Renovate's
      startup and configuration validity. The Workflow Delivery `CiScope`
      classifies Renovate inputs as handled by the scheduled workflow, not by a
      retained PR check.
- **Owner decisions and risk:**
    - Keep Renovate's CLI package in an independent npm project under
      `eng/renovate/`, with a committed package lock; do not add it to the pnpm
      workspace. Renovate manages its own package version, and its compatible
      dependency updates may automerge after required checks pass.
    - Require a three-day npm `min-release-age` for Renovate's dependency
      resolution, including transitive dependencies. Verify that the selected
      npm installation enforces this for transitive updates; if it cannot be
      established, stop and return the limitation to the owner rather than
      silently dropping the delay.
    - Select the Renovate CLI runtime from its installed package's
      `engines.node`, independently of the repository's locked Node runtime.
    - Remove the `fullClone` patch, retain the existing full pre-clone, and
      accept that Renovate's rare fallback clone may be blobless and fail
      NBGV-dependent lock generation. Do not add a fallback log gate.
    - Keep existing Renovate GitHub App permissions unchanged. The PR
      compatibility check must not receive the App token or exercise
      credentialed repository writes.
- **Advancement and outcome:** add the isolated npm manifest, lock and
  three-day release-age configuration; make the scheduled workflow install
  from that lock and run Renovate using a runtime selected from its declared
  range while preserving the locked Node for repository tasks; remove the
  full-clone patch but retain pre-cloning; add a `renovate-compat` retained CI
  job selected by `CiScope` and gated by `Validate`; and update #1531 and #1512
  to reflect the reduced check scope and the future retained-job candidate.
  The PR check validates locked installation, CLI startup and strict
  `renovate.json` configuration without credentials.
- **Ordering and gates:** merge this entry before any implementation. Update
  #1531 to remove the obsolete patch and Node-satisfaction assertions and
  retain the credential-free compatibility check. Keep the new job within the
  existing `Validate` ruleset. Each implementation PR follows Workflow
  Delivery's applicable CI design, permission-boundary, engineering and OCR
  reviews, applicable record-system and research-evidence review, and `GOV-011`
  finding disposition. A need to expand App permissions, run Renovate with
  credentials in a PR, or weaken the three-day delay returns to the owner.
- **Acceptance:**
    - The scheduled Renovate CLI runs on a Node selected from its package's
      declared `engines.node`; repository tasks still use the locked Node.
    - Renovate and its resolved transitive dependencies install from the
      committed npm lock, and the three-day release age is demonstrated to
      apply to transitive updates.
    - Renovate package and lock updates are proposed and may automerge only
      after the required `Validate` checks, including `renovate-compat`, pass.
    - Changes to Renovate's package, lock, workflow, configuration, or runtime
      inputs select the retained job; it starts the CLI and validates the
      configuration without credentials.
    - The full-clone patch is absent and the existing pre-clone remains.
    - Delete this entry when accepted.
- **Exclusions:**
    - Existing scheduled Renovate activity and GitHub App permissions remain
      unchanged; no new token, credential, permission, repository access or
      credentialed PR validation.
    - No pnpm workspace membership or general `#1512` check-unit design or
      implementation.
    - No release, proving or campaign dispatch, publication, Environment,
      account, publisher or upstream feature request.
