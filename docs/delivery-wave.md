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

### Establish OMR evaluation baselines and real-data splits (W2 and W3)

- **Work carrier:** [Issue #1489](https://github.com/hcoona/three/issues/1489)
  (W2) and [Issue #1490](https://github.com/hcoona/three/issues/1490) (W3),
  sub-issues of #686, and their linked pull requests. Following W1 completion
  (#1488, #1721) and the owner dispositions recorded on #1489 and #1490, the
  repository owner requested authorization of W2 and W3.
- **Accepted inputs:** those of the sheet-music design and planning entry and
  the accepted project records under `src/private/app/sheet-music-omr/`
  ([portal](../src/private/app/sheet-music-omr/README.md),
  [requirements](../src/private/app/sheet-music-omr/docs/requirements.md),
  [Text Score 2 format](../src/private/app/sheet-music-omr/docs/text-format.md),
  [comparison protocol](../src/private/app/sheet-music-omr/docs/comparison-protocol.md)
  and [handoff](../src/private/app/sheet-music-omr/docs/agent-handoff.md)).
  Findings in #686, #1489 and #1490 are unreviewed research inputs; any
  repository record that relies on one rechecks it under research-evidence
  review.
- **Owner decisions:**
    - W2 and W3 code consists of experiments under
      `src/private/app/sheet-music-omr/` in Python integrated with existing
      workspace checks and CI. Heavy external reader dependencies
      (`copisteria`, `torch` and PDF/image helpers) install into an isolated
      out-of-worktree environment recorded per run so repository-locked
      toolchains remain unchanged. This does not select product language,
      packaging or release.
    - Freeze the `7`-piece (`432`-measure, `23`-page) piece-level test set
      (`gold-01` through `gold-06` in Book 1 plus `gold-18` in Book 2; `113`
      accepted-MusicXML `<print>` system segments, ~`111` printed grand-staff
      systems in workspace source maps pending W3 overlay QA) before any OMR
      model output touches the data; assign the remaining `16` accepted
      model-draft-free pieces (`1,601` measures, `69` pages across Book 1, Book
      2 and `G7`; `380` MusicXML `<print>` segments, ~`323` printed grand-staff
      systems pending W3 overlay QA) to the non-test development and training
      pool. Never split below the piece, and exclude test pieces from tuning,
      threshold selection and synthetic re-rendering.
    - Stage S0 baselines evaluate the existing initial agent drafts
      (`build-01` or `draft.musicxml` against accepted gold) and stock
      `copista-28m` (`kobimusic/copisteria` and `kobimusic/copista-28m`,
      `Apache-2.0`) in both drafter and blind second-reader modes on the staged
      restored US-Letter facsimile PDFs (`92` pages, plus raw `.heic` captures
      via stored page quads if later staged locally). Skip the paid
      commercial-tool bake-off and `rokot-omr-2b`/`Qwen3-VL` weights in this
      entry.
    - Layout-only (`C`-class) differences are reported separately and do not
      count as recognition (`A`-class) errors. Live transcription-workflow
      adoption and any selective-review policy remain deferred to W7 (#1499).
    - Public repository records, Issues and pull requests exclude private
      paths, piece or book identities and copyrighted score content, reporting
      only anonymized batch numbers (`gold-01` through `gold-23`), group
      identifiers (`G1` through `G7`) and aggregate counts or metrics. The
      owner's development agents may inspect local score pages, overlays,
      crops, drafts, reviews and accepted gold read-only through their model
      services; although the owner imposes no blanket prohibition on external
      compute for separately authorized training workstreams, W2 and W3 S0
      execution here runs locally without uploading score material to
      third-party OMR, hosting or cloud-compute services.
    - Numeric download and runtime bounds under External effects are accident
      fuses, not limits on normal development; test-set evaluation caps in the
      W2 protocol bound selection bias.
- **Advancement and outcome:**
    - Accept project records (or bounded sections/addenda where record
      admission warrants) defining: (a) the unified real-data alignment and
      split manifest schema covering the `23` pieces at page, grand-staff
      system crop, measure-ordinal range, box trust and documented open-reading
      mask levels, together with canonical per-system `Text Score 2` target
      slices; and (b) the W2 evaluation and Stage S0 baseline protocol covering
      test-set touch rules, B-normalized per-category recognition (`A`-class)
      scoring in drafter and blind second-reader modes, piece-cluster bootstrap
      intervals, `musicdiff` 6.0 normalized edit distance, review-pass model
      fitting, stock `copista-28m` execution/isolation, and the downstream
      discrepancy/logging schema.
    - Implement the alignment/cropping and overlay-QA tools, per-system slice
      verifier, B-normalized per-category W2 scorer, and isolated local
      `copista-28m` baseline runner and touch-log harness under
      `src/private/app/sheet-music-omr/` (including directly required
      record-routing or CI test-selection updates).
    - Produce the local 23-piece alignment and split manifest, complete overlay
      QA across the `92` restored facsimile pages, cut normalized system crops,
      verify that re-assembled per-system slices match all `23` accepted gold
      pieces with zero content differences, run Stage S0 baselines for initial
      agent drafts and stock `copista-28m`, and publish the sanitized S0
      baseline tables, pass-model re-fit and downstream logging schema on #1489
      and #1490.
- **Ordering and gates:** merge this entry before substantive W2 or W3
  implementation, package/tool installation, weight download or runtime
  execution. Keep W2/W3 records and implementation self-consistent at each
  merge into `main`, and accept the W2 and W3 schemas and
  evaluation/runtime/isolation protocols on `main` under record-system review,
  research-evidence review and shared engineering guidance before installing
  external reader or host packages (`kobimusic/copisteria`, its out-of-worktree
  `torch`/CV stack, or host `poppler-utils`), downloading `kobimusic/copista-28m`
  weights, generating real-page overlays or crops from private inputs,
  executing S0 measurements or running `copista-28m`. Apply `GOV-011` finding
  disposition. New product, scope or feasibility tradeoffs return to the owner.
- **Acceptance and closure:** the unified 23-piece system-alignment and split
  manifest, `92`-page overlay QA, zero-difference per-system slice verification,
  tested W2 scorer, and independently reviewed sanitized S0 baseline results
  (`build-01`/`draft` and stock `copista-28m` in drafter and second-reader
  modes, keeping `TEST` and `TRAIN/DEV` separated) and logging schema are
  recorded on #1489 and #1490, and project portal/handoff records are updated.
  Delete this entry when accepted or when the owner stops W2 and W3.
- **External effects:** permit the design and planning entry's private-archive
  inspection and Issue effects; local read-only use of the private transcription
  archive, staged restored facsimile PDFs and capture manifest (plus raw
  `.heic` photos if staged locally), `gold-cache` (including the #1705 staged
  `gold-01` copy read-only, with no further edits to any gold or archive file),
  and local desk-study archive; creation and use of out-of-worktree `RUN`,
  `DOWNLOADS`, alignment/crop/overlay output, and isolated virtual-environment
  and cache directories; host `MuseScore 4.x` command-line invocation recorded
  by exact version and executable SHA-256 per run; and installation in an
  isolated out-of-worktree environment (and/or host `poppler-utils` if needed
  for PDF rasterization) of `kobimusic/copisteria` (`Apache-2.0`) and its Python
  dependencies (`torch`, `torchvision`, `opencv-python`, `scipy`, `pillow`,
  `numpy`, `huggingface_hub`, `pypdfium2`), recorded by exact commit/version per
  run, and download of `kobimusic/copista-28m` weights (`Apache-2.0`), stopping
  to return to the owner before cumulative new W2/W3 downloads exceed `10 GB`
  (tracked separately from W1's closed `2,350,276,855`-byte ledger).
- **Exclusions:** no model training or weight fine-tuning (W6, #1498); no
  synthetic rendering or augmentation engine (W4, #1491); no standalone general
  system-detector model training (W5, #1497); no live workflow modification or
  selective-review policy change (W7, #1499); no paid commercial-tool purchase
  or `rokot-omr-2b`/`Qwen3-VL` weight download; no committed fixture other than
  self-authored, public-domain or CC0 material with provenance; no commit or
  public upload of private paths, piece/book identities or copyrighted score
  content, and no upload of score material to external OMR, hosting or cloud
  compute services under this entry; no modification to the private
  transcription repository or staged gold files; no change to existing
  repository-locked tool/runtime versions or other projects' behavior, and no
  publication, release, credential or access change.

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

### Decouple record-system-review triggers from namespace and domain skills

- **Work carrier:** [Issue #1738](https://github.com/hcoona/three/issues/1738)
  and its linked authorization and implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [checker contract](governance/checker-contract.md),
  [family catalog](governance/record-families.yaml),
  [control catalog](governance/controls.yaml),
  [documentation portal](README.md),
  [engineering principles](engineering/engineering-principles.md), root
  [`AGENTS.md`](../AGENTS.md), [`docs/AGENTS.md`](AGENTS.md),
  [`CONTRIBUTING.md`](../CONTRIBUTING.md),
  [`.github/pull_request_template.md`](../.github/pull_request_template.md),
  [record-system review](../.github/skills/record-system-review/SKILL.md) and
  its `evals/` fixtures,
  [`eng/scripts/check_repository_records.py`](../eng/scripts/check_repository_records.py),
  [`tests/eng/test_repository_records.py`](../tests/eng/test_repository_records.py),
  the [xUnit-to-MSTest migration skill](../.apm/skills/migrate-xunit-to-mstest/SKILL.md),
  and [`apm.lock.yaml`](../apm.lock.yaml).
- **Owner decisions and boundaries:**
    - Trigger `record-system-review` by governance, topology, and contract
      responsibility rather than literal `namespace: repository` or blanket
      `Agent Skill` packaging
      ([GOV-001](governance/governance-system.md#gov-001-consumer-backed-governance),
      [GOV-003](governance/governance-system.md#gov-003-minimum-sufficient-mechanism),
      [GOV-005](governance/governance-system.md#gov-005-controls-implement-policy)):
      scope it to structural record changes (additions, moves, splits,
      retirements/deletions, and catalog rebindings); repository governance,
      work-authorization, shared-principle, and root instruction/workflow
      authorities (`docs/governance/**`, `docs/delivery-wave.md`,
      `schemas/governance/**`, `docs/engineering/engineering-principles.md`,
      `AGENTS.md`, `docs/AGENTS.md`, `CONTRIBUTING.md`, and
      `.github/pull_request_template.md`); portal routing, generated-interface
      ownership or `apm.lock.yaml` source/deployment provenance (excluding
      content and hash regeneration of an already-bound local skill whose
      source mapping, owners, and deployed file set are unchanged), `hk`,
      control-governed workflows, and control-governed Agent Skills (skills
      implementing controls in `docs/governance/controls.yaml`, including their
      `evals/`, or repository governance routing, rather than domain/coding
      skills); and any record edit—inside or outside `src/`—that alters
      authority ownership, established requirement identifiers, or
      consumer/immutability/license contracts, or claims the Record-System
      Gate. Add no extra classification fields to `record-families.yaml` or
      `controls.yaml`, and keep `mise run records:check` covering in-place
      edits across all maintained records, portals, skills, `apm.lock.yaml`,
      and catalogs.
    - Replace the `namespace.startswith("src/")` restriction in
      `eng/scripts/check_repository_records.py` (and align
      `docs/governance/checker-contract.md` and
      [project namespaces](governance/record-system.md#project-namespaces) as
      needed) with the deterministic path and snapshot invariant that a
      non-`repository` binding `namespace` is a safe, existing, direct
      (non-symlink) repository directory
      ([GOV-004](governance/governance-system.md#gov-004-preserve-human-judgment),
      [GOV-005](governance/governance-system.md#gov-005-controls-implement-policy)),
      without hardcoded path-prefix allowlists or exclusion lists; keep
      semantic component-boundary review (`repository` versus an owning
      project, engineering tool, or standalone skill directory) in
      `record-system.md#project-namespaces` and `record-system-review`.
    - Rebind `xunit-to-mstest-guidance-source` in
      `docs/governance/record-families.yaml` from `namespace: repository` to
      `namespace: .apm/skills/migrate-xunit-to-mstest`, while retaining
      `public-landing` (`README.md`) and `shared-engineering`
      (`docs/engineering/*.md`) in `namespace: repository` as repository-wide
      records whose routine in-place maintenance no longer triggers
      `record-system-review`.
- **Advancement and outcome:** atomically update `AGENTS.md`,
  `docs/governance/record-system.md` (if needed),
  `docs/governance/checker-contract.md`, `docs/governance/controls.yaml`,
  `docs/governance/record-families.yaml`,
  `.github/skills/record-system-review/` (including affected `evals/`),
  `eng/scripts/check_repository_records.py`, and focused namespace tests in
  `tests/eng/test_repository_records.py`, closing this entry atomically in the
  implementation pull request.
- **Ordering and gates:** merge this authorization before substantive edits.
  Review the implementation pull request against the accepted target-branch
  policies, catalogs, and `record-system-review` Skill under
  [changing rules](governance/governance-system.md#changing-rules), retaining
  atomic alignment
  ([GOV-006](governance/governance-system.md#gov-006-atomic-governance-changes)),
  staged CI-profile HK checks, explicit-base `records:check`,
  `mise run records:test`, independent `record-system-review`, shared
  engineering guidance for the checker change, `GOV-011` finding disposition,
  ordinary CI (`Validate`), and explicit repository-owner disposition
  (`record-system-gate`).
- **Acceptance and closure:** affected policies, interfaces, catalogs,
  `record-system-review` Skill/evals, and checker implementation/tests agree
  atomically without new catalog metadata or path-prefix allowlists/blacklists;
  `xunit-to-mstest-guidance-source` uses
  `namespace: .apm/skills/migrate-xunit-to-mstest`; `mise run records:test`,
  zero-diagnostic explicit-base `records:check`, HK, and `Validate` pass; and
  independent review and owner disposition are complete. Delete this entry
  atomically on acceptance or owner stop.
- **Exclusions:**
    - No edits to `GOV-001` through `GOV-011` in
      `docs/governance/governance-system.md` or
      `docs/engineering/engineering-principles.md`.
    - No changes to `schemas/governance/*.schema.json`.
    - No `#1303` snapshot-output, JSON constant, or unchecked-reference changes.
    - No workflow, ruleset, dependency, lockfile, `apm.yml`, `apm.lock.yaml`,
      manual `.agents/skills/` / `.github/agents/`, or external, paid, or
      credentialed changes.
