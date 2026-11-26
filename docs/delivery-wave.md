# Current Delivery Wave

This record is the sole positive work-authorization authority for feature or
capability advancements, architectural or public-contract changes, and
non-routine external or risk-bearing operations under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency);
contract-preserving routine maintenance and owner-governed release operations
defined there execute without a Wave entry. Only `main`'s accepted version
grants Wave authorization. Within an authorized workstream, routine local
editing, builds, lint, tests, scratch harnesses, read-only inspection,
independent review, and ordinary Issue/PR/CI validation (including failed-job
reruns) are permitted by default with no attempt quota, and implementation may
evolve concurrently with its owning records so long as each merge into `main`
stays self-consistent. Entries declare bounded scope and outcome, exclusions,
and any explicitly permitted external or risk-bearing effects. Merge changes
authorization; deletion ends a grant. Git and the proposing PR retain the reason
and history.

## Authorized Advancements

### Unify Workflow Delivery release kernel and decouple release units

- **Work carrier:** [Issue #1899](https://github.com/hcoona/three/issues/1899)
  and its linked authorization and stacked implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  `.github/workflows/workflow-delivery-v3-*-release.yml`,
  `eng/scripts/release_triage.py`,
  `src/public/*/*/workflow-delivery.release-unit.yml`,
  `tests/private/app/workflow-delivery/fixtures/products/*/workflow-delivery.release-unit.yml`,
  and Workflow Delivery's [application interface](../src/private/app/workflow-delivery/README.md),
  [portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [HLD](../src/private/app/workflow-delivery/docs/high-level-design.md),
  [MLD and scoped designs](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [integration contracts](../src/private/app/workflow-delivery/docs/integration/common.md),
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
- **Advancement and outcome:** (1) consolidate the six Release destination
  families (`NuGet`, `npm`, `Python`, `Ruby`, `GitHub Releases`, and Browser
  Stores) onto a unified port-adapter `ReleaseEngine` (`IDestinationAdapter`),
  unified stage transfer envelopes, and parameterized CLI routing while
  preserving every destination's trust, redirect, non-overwrite, settle, and
  `WD-REL-008` boundaries, backed by zero-dependency native C# explicit-state
  BFS model-checking tests across reachable multi-artifact, transport-drop,
  post-commit timeout, visibility-lag, contention, and recovery transitions;
  (2) evolve `workflow-delivery/v3/release-unit` (`workflow-delivery.release-unit.yml`)
  and dynamic repository discovery so release units self-declare channel
  eligibility and project-local consumer/payload probes while deriving package
  identity, target frameworks, and companion build dependencies from native
  manifests and project graphs, retiring hardcoded product registries
  (`*ReleaseUnits.cs` and Python `PRODUCTS`), changing release workflow `unit`
  dispatch inputs from hardcoded `choice` lists to catalog-validated `string`
  inputs, and aligning `eng/scripts/release_triage.py`; and (3) contract
  duplicated product/path ownership rules across `CiScope.cs` and
  `NativePreparationQualification.cs` onto discovered release units and native
  graphs without modifying `record-families.yaml` bindings, backed by finite DAG
  partition-closure exhaustion tests, updating owning Workflow Delivery records
  atomically.
- **Ordering and gates:** merge this authorization before merging substantive
  implementation pull requests; implementation and owning records may evolve
  across stacked pull requests self-consistent at each merge into `main`. Retain
  applicable independent `engineering-guidance`, `record-system-review`, OCR and
  code review, `GOV-011` finding disposition, explicit-base `records:check`, HK
  conformance, and `Validate` CI.
- **Exclusions and closure:** no new external NuGet or tool dependencies,
  release or proving workflow dispatch, package/store/release publication,
  reopening of closed proving campaigns or smoke-fixture live operations,
  admitting new public release units or Official refs beyond the currently
  admitted set, editing `Platform/NativeProcess.cs` or
  `tests/private/app/workflow-delivery/Release/NuGetWorkflowCallerTests.cs` while
  owned by active sibling repair work, or ruleset/Environment/credential/access
  changes; TLA+ / `TLC` specification remains tracked separately under
  [Issue #1897](https://github.com/hcoona/three/issues/1897) (blocked by
  [Issue #1890](https://github.com/hcoona/three/issues/1890)). Close this entry
  atomically by deleting it in the final implementation pull request (or an
  explicit Wave-only closure pull request).

### Verify Hcoona.Nanopass formal specification and algebraic laws

- **Work carrier:** [Issue #1779](https://github.com/hcoona/three/issues/1779)
  and its linked authorization and implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  `Hcoona.Nanopass`'s accepted [package portal](../src/public/lib/Nanopass/README.md),
  [architecture](../src/public/lib/Nanopass/docs/architecture.md),
  [validation strategy](../src/public/lib/Nanopass/docs/validation.md), and
  [third-party notices](../src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt), and
  the public Nanopass Framework specification ([Keep & Dybvig, ICFP 2013](https://doi.org/10.1145/2500365.2500618),
  [`nanopass-framework-racket`](https://github.com/nanopass/nanopass-framework-racket),
  and [`nanopass-framework-scheme`](https://github.com/nanopass/nanopass-framework-scheme),
  MIT License).
- **Owner decisions and boundaries:**
    - Add `CsCheck` in `Directory.Packages.props` scoped to
      `tests/public/lib/Nanopass.Tests/Hcoona.Nanopass.Tests.csproj`
      (`Condition="'$(MSBuildProjectName)' == 'Hcoona.Nanopass.Tests'"`) and
      refresh affected NuGet lockfiles.
    - Author an independent, clean-room PEP 723 Python formal specification
      script (`src/public/lib/Nanopass/scripts/verify_formal_spec.py` with
      pinned `z3-solver`) directly from the upstream Nanopass specification
      without reading `Hcoona.Nanopass` implementation files during model
      construction, and register an on-demand `mise.toml` task (`uv run --script`)
      without adding a standalone `.github/workflows/*.yml` pipeline or daily
      Python CI suite.
    - Do not introduce TLA+, F\*, Lean, Rocq, or `FsCheck` / `FSharp.Core`
      dependencies, and do not expand `Hcoona.Nanopass`'s public API surface
      beyond contract-preserving fixes for discrepancies uncovered by
      differential verification.
- **Advancement and outcome:**
    - Formalize and check mathematical self-consistency of the core Nanopass
      relational and inductive invariants in Z3 (incremental grammar algebra and
      entry-reachability pruning fixed point, nonterminal subtype inclusion
      shortest-path uniqueness versus `NP1003` ambiguity, processor and clause
      conditional auto-synthesis fixed point versus `NP1001`/`NP1002`, and
      4-phase S-expression dispatch priority), execute a model-driven
      differential audit against `Hcoona.Nanopass`, and remediate any uncovered
      semantic or diagnostic defects.
    - Add a deterministic, shrinking-enabled `CsCheck` algebraic property test
      suite in `tests/public/lib/Nanopass.Tests/` covering the Identity Pass
      Law, Metadata and S-Expression Round-Trip Law, and Analyzer–Compiler
      Soundness Alignment Law (zero leaked `CSxxxx` compiler errors when
      `NPxxxx` diagnostics are absent), and update
      `src/public/lib/Nanopass/docs/architecture.md`,
      `src/public/lib/Nanopass/docs/validation.md`, and `mise.toml` atomically.
- **Ordering and gates:** merge this authorization before substantive
  implementation. Keep implementation, dependency manifests and lockfiles,
  `mise.toml`, and owning Nanopass records self-consistent at each merge into
  `main` across ordinary or stacked pull requests. Retain applicable independent
  `engineering-guidance`, `record-system-review`, code review, `GOV-011` finding
  disposition, explicit-base `records:check`, HK conformance, and `Validate` CI.
  Material scope or semantic contract conflicts return to the repository owner.
- **Acceptance and closure:** the Z3 formal specification script, `CsCheck`
  algebraic property suite, and any differential-audit fixes pass explicit local
  verification, `records:check`, HK conformance, and `Validate` CI with no
  unresolved material findings after final review and contraction. Close this
  entry by deleting it in the final implementation pull request (or an explicit
  Wave-only closure pull request).
- **Exclusions:** no TLA+, F\*, Lean, Rocq, or `FSharp.Core` toolchains; no new
  GitHub Actions workflow files, package or release publication, rulesets,
  Environments, credentials, authentication, or access changes; no change to
  `CurrentTargetFramework` (`net10.0`) for unrelated projects.

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

### Execute OMR GPU training and evaluation on local RTX 4090 (W6 Slice B)

- **Work carrier:** [Issue #1498](https://github.com/hcoona/three/issues/1498)
  (W6 recognizer and page-detector pilot) and
  [Issue #1491](https://github.com/hcoona/three/issues/1491) (W4 packaged
  system-crop and page-box milestones, with sub-issues
  [Issue #1494](https://github.com/hcoona/three/issues/1494),
  [Issue #1495](https://github.com/hcoona/three/issues/1495) and
  [Issue #1496](https://github.com/hcoona/three/issues/1496)), sub-issues of
  [Issue #686](https://github.com/hcoona/three/issues/686), and their linked
  pull requests. Following completion of CPU Slice A
  ([#1498](https://github.com/hcoona/three/issues/1498#issuecomment-7173082854),
  [#1498](https://github.com/hcoona/three/issues/1498#issuecomment-7185613521),
  [#1491](https://github.com/hcoona/three/issues/1491#issuecomment-7185614792)),
  the repository owner approved transitioning to the local RTX 4090 (`24 GB`)
  workstation to execute **W6 GPU Slice B**.
- **Accepted inputs:** those of the sheet-music design and planning entry, the
  accepted project records under `src/private/app/sheet-music-omr/`
  ([portal](../src/private/app/sheet-music-omr/README.md),
  [requirements](../src/private/app/sheet-music-omr/docs/requirements.md),
  [Text Score 2 format](../src/private/app/sheet-music-omr/docs/text-format.md),
  [comparison protocol](../src/private/app/sheet-music-omr/docs/comparison-protocol.md),
  [evaluation and real-data alignment protocol](../src/private/app/sheet-music-omr/docs/evaluation-protocol.md),
  [synthetic data and system-detection protocol](../src/private/app/sheet-music-omr/docs/synthetic-protocol.md),
  [training, bulk-packaging and page-detector protocol](../src/private/app/sheet-music-omr/docs/training-protocol.md)
  and [handoff](../src/private/app/sheet-music-omr/docs/agent-handoff.md)), and
  the reviewed W6 Slice A readiness, full-category supplement and per-system
  baseline reports recorded on #1498, #1491 and #686. Unreviewed findings in
  #686, #1491, #1494 through #1496 and #1498 through #1500 remain research
  inputs; any repository record that relies on one rechecks it under
  research-evidence review.
- **Owner decisions:**
    - Execute all GPU provisioning, `E0` zero-shot inference, Arm A / Arm B /
      page-detector training, `4`-fold `TRAIN_DEV` selection and calibration,
      matched-step local `TRAIN_DEV` replay ablation and the single final `TEST`
      evaluation strictly locally inside WSL2 on the repository owner's
      Windows/WSL2 physical workstation equipped with an NVIDIA GeForce RTX 4090
      (`24 GB` VRAM; `local_gpu_only`), with zero cloud-compute dispatch and
      zero upload of private or synthetic score material, crops, checkpoints or
      diagnostics to external services (`OMR-REQ-015`).
    - Support cross-host migration from the Slice A CPU workstation to the RTX
      4090 workstation by verifying immutable `SHA-256` digests of the `7`
      carried-forward campaign ledgers (`download-ledger.json`,
      `w2-download-ledger.json`, `w2-touch-log.jsonl`,
      `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
      `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`), the
      self-contained `w6-slice-a-package-formal-6/datasets/` tree (`w6-crops-20k`,
      `w6-crops-50k` and shared `assets/` PNGs), and the `16`-piece
      `TRAIN_DEV` starting-state directory (`102` leaves verified against
      committed `EXPECTED_TRAINDEV_PORTABLE_SHA256`), re-sealing (`0o500`
      directories, `0o400` leaves owned by `os.geteuid()`) and re-anchoring
      local `(st_dev, st_ino)` identities in Slice B's run ledger without
      scanning mixed `23`-piece directories or touching `TEST` during setup.
    - Treat installed host Windows `MuseScore 4.x` CLI versions and executable
      `SHA-256` digests as per-run provenance recorded in `RUN/environment.json`
      rather than gating execution on a committed `ACCEPTED_MUSESCORE_IDENTITIES`
      binary-hash allowlist: retire `ACCEPTED_MUSESCORE_IDENTITIES` blocking
      across W4/W6 while retaining `4.x` version verification, the `8` MusicXML
      preference checks (`importInferTextType=false`), automatic `1`-page
      `.mpos / 12.0 <= 2.0 px` pixel-scale calibration, mid-run executable and
      preference immutability checks, and per-sample round-trip gates, keeping
      public `permissive` score rendering on the host Windows `MuseScore 4.x`
      CLI under its local lock without host firewall or user-preference
      mutations while private-input and Linux worker subprocesses retain
      `unshare -rn` offline isolation.
    - Retain Slice A's strictly permissive (`Apache-2.0`, `MIT`, `BSD-3-Clause`)
      trainable model and weight matrix (`trust_remote_code=False`, zero remote
      `.py` fetches; keeping `AGPL`, `CC-BY-NC` and custom revenue-threshold
      model/code exclusions unchanged, while permitting standard out-of-worktree
      PyTorch CUDA and `nvidia-*` / `triton` runtime wheels in
      `DOWNLOADS/w6-gpu-venv` for local GPU execution without redistribution):
      run `E0` zero-shot `Text Score 2` baselines on the `69` `TRAIN_DEV` pages /
      `323` systems (`0` `TEST` touches) across the pinned permissive VLM
      foundation checkpoints (`Qwen/Qwen3-VL-2B-Instruct@89644892e4`,
      `Qwen/Qwen3.5-2B@15852e8c16`, `PaddlePaddle/PaddleOCR-VL-1.6@132c45f52c`)
      alongside Slice A's completed `copista-28m@44464d1` per-system crop
      baseline before comparing fine-tuned Arm A (`bf16` SFT/LoRA on
      `w6-crops-20k`, scaling the top candidate to `w6-crops-50k` and optionally
      testing `Qwen/Qwen3-VL-4B-Instruct@ebb281ec70` capacity), Arm B
      (`timm/convnext_tiny.fb_in22k_ft_in1k@bc48a87f11` `stride 16` vs.
      `stride 8+16` and `zenosai/MonkeyOCRv2-S@8efa4f856a` on `w6-crops-50k`)
      and the 2-class (`system`, `measure`) page detector
      (`ustc-community/dfine-small-coco@f79e65b5fb` at `L = 1024` vs. `1280` and
      `PekingU/rtdetr_v2_r18vd@5650961749` on `w6-crops-50k` `page-boxes.jsonl`).
    - Select the synthetic-only recognizer and page-detector finalists, fit
      nested out-of-fold temperature/Platt calibrators, and execute the
      matched-step local `TRAIN_DEV` replay ablation (`synthetic continuation`
      vs. `REAL_TRAIN + synth_train replay` with `open_reading_masks` loss
      masking) strictly across the pre-registered `4` plate-group-disjoint
      `TRAIN_DEV` folds (`0` `TEST` touches, labeled as cross-validated
      selection / conditional replay evidence), then execute **at most `1`**
      pre-registered `"W6-pilot-final"` evaluation pass on frozen `TEST` (`7`
      pieces, `111` systems, `432` measures, `23` pages), appending `1`
      immutable record to `DOWNLOADS/w2-touch-log.jsonl` (`1 / 3` cumulative
      `OMR-REQ-019` training-pilot `TEST` touches beyond the `6` preserved
      `S0-baseline` records).
- **Advancement and outcome:**
    - Amend the OMR synthetic and training protocols and `sheet_music_omr`
      harnesses to: (a) retire `ACCEPTED_MUSESCORE_IDENTITIES` binary-hash
      allowlist gating in favor of `MuseScore 4.x` interface/preference/pixel
      validation plus per-run provenance recording; and (b) commit the Slice B
      CUDA wheel and pinned permissive `.safetensors` checkpoint lock manifest,
      cross-host `SHA-256`-verified ledger/dataset/`--traindev-root` import and
      local `(st_dev, st_ino)` re-anchoring (`setup-w6-slice-b`), `E0`
      zero-shot runner, GPU training/evaluation/checkpoint runners for Arm A,
      Arm B and `D-FINE` / `RT-DETRv2`, `4`-fold calibration + matched-step
      `TRAIN_DEV` replay runners, and the single-pass `"W6-pilot-final"`
      frozen-`TEST` evaluation runner with persistent Slice B download, disk and
      run ledgers.
    - Provision `DOWNLOADS/w6-gpu-venv` and pinned permissive `.safetensors`
      foundation checkpoints on the local RTX 4090 (`24 GB`) WSL2 host, execute
      `E0` zero-shot `TRAIN_DEV` baselines, Arm A / Arm B / `D-FINE` &
      `RT-DETRv2` GPU training, `4`-fold `TRAIN_DEV` selection, nested
      calibration and matched-step `TRAIN_DEV` replay ablation (`0` `TEST`
      touches), and execute the single pre-registered `"W6-pilot-final"` frozen-
      `TEST` evaluation pass (`1` pilot touch in `w2-touch-log.jsonl`),
      publishing independently reviewed sanitized Slice B evidence reports and
      validated `omr-w7-sidecar-v1` / `omr-discrepancy-log-v1` handoff summaries
      on #1498, #1491 (#1494 through #1496) and #686.
- **Ordering and gates:** merge this entry before any Slice B protocol or
  implementation change, cross-host `setup-w6-slice-b` / private
  `--traindev-root` import or local `(st_dev, st_ino)` re-anchoring, CUDA wheel
  or foundation-checkpoint fetch, campaign diagnostic rendering, `E0` zero-shot
  inference, campaign GPU training, `TRAIN_DEV` replay or `"W6-pilot-final"`
  `TEST` execution. Keep project records and implementation self-consistent at
  each merge into `main`, and accept the Slice B protocol amendments and
  committed CUDA wheel + `.safetensors` checkpoint locks on `main` under
  record-system review, research-evidence review and shared engineering guidance
  before any cross-host `setup-w6-slice-b` / `--traindev-root` import or
  re-anchoring, any `DOWNLOADS/w6-gpu-venv` or `.safetensors` download, any
  campaign diagnostic synthetic rendering, any campaign GPU training or `E0`
  inference run, any `TRAIN_DEV` replay run, or the `"W6-pilot-final"` `TEST`
  pass (offline repository-fixture and synthetic-fixture unit/scenario/smoke
  checks with zero network fetches, zero staged-corpus batch rendering and zero
  private-input access remain unconstrained; formal campaign execution fails
  closed on any drifted wheel or model checkpoint digest until its license and
  interface mutable-source recheck is reviewed and accepted on `main`). Complete
  all `E0` baselines, synthetic-only `4`-fold `TRAIN_DEV` selection,
  matched-step `TRAIN_DEV` replay, out-of-fold production calibrator fitting,
  and pre-registration of the single `"W6-pilot-final"` model/detector/threshold
  configuration before reading or scoring frozen `TEST`. Apply `GOV-011` finding
  disposition. New product, scope or feasibility tradeoffs return to the owner.
- **Acceptance and closure:** the tested Slice B protocol amendments and GPU
  training/evaluation harnesses are merged on `main`, and independently reviewed
  sanitized Slice B reports (`E0` zero-shot vs. fine-tuned Arm A / Arm B
  results and `20k`->`50k` scaling, 2-class `D-FINE` / `RT-DETRv2` vs.
  `comb_dp_blind` page-detector metrics and downstream system assembly, nested
  out-of-fold calibration + `omr-w7-sidecar-v1` / `omr-discrepancy-log-v1`
  validation, matched-step `TRAIN_DEV` replay ablation, and the single
  `"W6-pilot-final"` frozen-`TEST` drafter and blind second-reader comparison
  against Stage S0 with `1 / 3` cumulative pilot `TEST` touches recorded) are
  published on #1498, #1491 (#1494 through #1496) and #686, with project
  portal/handoff records updated for W7 (#1499) and W8 (#1500). Close this entry
  by deleting it in the final PR that completes acceptance, or through an
  explicit Wave-only closure PR after owner acceptance or owner stop.
- **External effects:** permit local private-archive inspection and Issue
  effects as in #686 (including `formal-5/gold/mapping.json` read-only solely
  for `scan-public-report` `23`-identity `OMR-REQ-015` privacy checks);
  read-only cross-host import and `SHA-256` verification of preserved campaign
  ledgers (`download-ledger.json`, `w2-download-ledger.json`,
  `w2-touch-log.jsonl`, `w4-w5-download-ledger.json`, `w4-w5-run-ledger.jsonl`,
  `w6-slice-a-download-ledger.json` and `w6-slice-a-run-ledger.jsonl`),
  self-contained `w6-slice-a-package-formal-6/datasets/` (`w6-crops-20k`,
  `w6-crops-50k` and shared `assets/` PNGs), staged public archives if
  present, sealed `16`-piece `--traindev-root` (re-anchoring local `(st_dev,
st_ino)` after `102`-leaf `SHA-256` + `EXPECTED_TRAINDEV_PORTABLE_SHA256`
  verification once the Slice B protocol is accepted), and cached
  `w2-copista-venv` / `w4-synth-venv` when
  transferred; metered out-of-worktree installation into `DOWNLOADS/w6-gpu-venv`
  of locked CUDA PyTorch/vision/training wheels (from PyPI and PyTorch's
  official CUDA wheel index, including required `nvidia-*` / `triton` CUDA
  runtime wheels and locked transitive Python/native wheel dependencies) plus
  metered download into `DOWNLOADS/w6-model-weights/<slug>@<sha10>/` of pinned
  permissive `.safetensors` checkpoints and metadata for the approved W6 model
  matrix (`trust_remote_code=False`, zero remote `.py` files); local RTX 4090
  (`24 GB`) campaign CUDA training and offline `unshare -rn` `TRAIN_DEV`
  inference, calibration and replay runs; host Windows `MuseScore 4.x` CLI (on
  public `permissive` inputs only, recorded by version and `SHA-256`) and
  offline `Verovio` if bounded campaign diagnostic rendering is invoked; and **at
  most `1`** offline `unshare -rn` frozen-`TEST` evaluation pass
  (`purpose: "W6-pilot-final"`) writing and fsyncing **both** its pre-start
  `DOWNLOADS/w6-slice-b-run-ledger.jsonl` reservation **and** its immutable
  `DOWNLOADS/w2-touch-log.jsonl` record (`1 / 3` cumulative training-pilot
  `TEST` touches, retained on any subsequent failure or abort; never opening
  `TEST` if either write fails) **before** reading the `7` frozen `TEST` pieces
  (`23` restored facsimile pages, `111` systems, `432` measures, via the
  approved `w2-s0-formal-3` alignment/split/overlay manifests, `gold-cache`,
  `open-reading-masks.json`, `desk-study` and `req012-private-manifest.json`
  strictly read-only for that single pass), stopping to return to the owner
  before cumulative Slice B network downloads exceed `60,000,000,000` bytes
  (`60 GB`) in persistent `DOWNLOADS/w6-slice-b-download-ledger.json`, before
  generated Slice B checkpoints, predictions and run outputs (`RUNS/w6-slice-b-*`)
  exceed `150,000,000,000` bytes (`150 GB`) on disk, or before cumulative
  campaign runs in persistent append-only `DOWNLOADS/w6-slice-b-run-ledger.jsonl`
  (writing atomic pre-start reservations before every campaign GPU training run,
  every private-input `TRAIN_DEV` evaluation or replay run regardless of
  `--formal` flag or outcome, any optional campaign diagnostic rendering run,
  and the single `TEST` pass; only repository-fixture and synthetic-fixture
  unit/scenario/smoke checks with zero external downloads, zero staged-corpus
  batch rendering and zero private-input access are exempt) exceed `40` campaign
  GPU training runs (`8`-hour per-run wall-clock timeout), `30` private-input
  `TRAIN_DEV` evaluation runs (`300`-second per-page/system worker deadline),
  `5,000` optional campaign diagnostic synthetic pages, or `1` frozen-`TEST`
  evaluation pass (all Slice B ledgers and `w2-touch-log.jsonl` are permanently
  preserved and never deleted or reset).
- **Exclusions:** no paid or cloud GPU/compute dispatch, and no upload of
  private or synthetic score material, crops, checkpoints or diagnostics to
  external OMR, cloud-compute, hosting or storage services (`local_gpu_only`);
  no second Slice B `TEST` evaluation pass (`> 1` new `w2-touch-log.jsonl`
  pilot record) and no hyperparameter, architecture, prompt, calibrator or
  threshold tuning on `TEST`; no edit, reset or deletion of preserved W1,
  W2/W3, W4/W5 or W6 Slice A ledgers; no full `20k`–`50k`-page #1491 corpus
  scale-up, live transcription-workflow integration (W7, #1499), or multi-round
  flywheel deployment (W8, #1500) under this entry; no `AGPL`, `CC-BY-NC` or
  custom revenue-threshold code or model weights (`homr`, `transcoda`,
  `ultralytics`, `rokot-omr-2b`, `ConvNeXtV2` NC weights, `OXR-1.0`, `dinov3`,
  `aimv2`, `C-RADIOv3`), and no ingestion or training on `TEST` pieces or
  unreviewed public image+label targets (`GrandStaff`, `OLiMPiC`,
  `FP-GrandStaff`, `Polish-scores`, `SMB`); no committed fixture other than
  self-authored, public-domain or `CC0` material with provenance; no commit or
  public upload of private paths, piece/book identities or copyrighted score
  content; no modification to the private transcription repository or staged
  gold files; no unreviewed change to repository-locked tool/runtime versions or
  other projects' behavior; and no publication, release, credential or access
  change.

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

### Import AzureAuth Unofficial V2 into the monorepo

- **Work carrier:** [Issue #1892](https://github.com/hcoona/three/issues/1892)
  and its linked authorization and implementation pull requests.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md),
  [workspace setup](engineering/workspaces.md),
  [public release triage](engineering/public-release-triage.md), and the pinned
  source snapshot
  [`hcoona/microsoft-authentication-cli` `main-v2` at `f509276cad32c49b5764af906e8374f3f9b182ae`](https://github.com/hcoona/microsoft-authentication-cli/tree/f509276cad32c49b5764af906e8374f3f9b182ae).
- **Owner decisions and boundaries:**
    - Import `main-v2@f509276cad32c49b5764af906e8374f3f9b182ae` via a linear
      verbatim snapshot commit followed by monorepo adaptation commits under
      `src/public/app/azureauth-unofficial/` and
      `tests/public/app/azureauth-unofficial/` as an independent namespace
      separate from `src/private/app/azureauth-credprovider/`, dropping
      superseded repository-level governance, hook, toolchain, and workflow
      shells (`GOV-002`).
    - Register `src/public/app/azureauth-unofficial` in
      `NON_RELEASE_PUBLIC_DIRS` (`eng/scripts/release_triage.py` and
      [public release triage](engineering/public-release-triage.md)) while its
      portable-plus-symbols Windows prerelease archive shape remains outside
      Workflow Delivery v3 GitHub Release recipes; preserve monotonic
      `0.1.0-alpha.*` SemVer2 ordering beyond `0.1.0-alpha.37` via NBGV
      `versionHeightOffset` with `^refs/heads/main$` as `publicReleaseRefSpec`.
    - Relicense the sole-authored `main-v2` codebase and project records to the
      repository default `LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`,
      retaining only the embedded Microsoft `default.tb7` MIT notice inside
      `authentication-engine.tm7` and the bundled runtime/MSAL third-party MIT
      notices in `THIRD-PARTY-NOTICES.header.txt`.
- **Advancement and outcome:** place `Authentication.Core`,
  `Authentication.Windows`, `Authentication.Cli`, public JSON schemas, Windows
  packaging scripts, structured-record schemas, and project records under
  `src/public/app/azureauth-unofficial/` and scenario test suites plus
  `ResultValidation` under `tests/public/app/azureauth-unofficial/`; integrate
  root CPM (`Directory.Packages.props`), `dirs.proj`
  (`WindowsOnlyProjectReference` across `src/` and `tests/`), refreshed NuGet
  lockfiles, CI `WindowsHostTiming` test filtering, and per-process test scratch
  isolation; rehome project-scoped identity, `operational-identities.yaml`, and
  `rechecks.yaml` records, bind project records in
  `docs/governance/record-families.yaml`, and route from root `README.md` and
  `docs/README.md`.
- **Ordering and gates:** merge this authorization before merging substantive
  implementation into `main`. Keep implementation, lockfiles, inventory
  metadata, and owning records/routing self-consistent at merge into `main`.
  Retain applicable independent `engineering-guidance`, `record-system-review`,
  `research-evidence-review`, code review, `GOV-011` finding disposition,
  explicit-base `records:check`, HK conformance, and `Validate` CI. Material
  scope or feasibility tradeoffs return to the repository owner.
- **Acceptance and closure:** the imported projects, lockfiles, scenario tests,
  record bindings, and `public-release-inventory` check pass local validation
  and `Validate` CI with no unresolved material findings after review and
  contraction. Close this entry by deleting it in the implementation pull
  request (or an explicit Wave-only closure pull request).
- **Exclusions:** no Workflow Delivery v3 release-unit onboarding, live or
  dry-run package/release publication, build-provenance attestation,
  release/proving dispatch, code signing, real-account or WAM host
  authentication experiment, or `azureauth-credprovider` provider migration; no
  ruleset, Environment, credential, or access change.
