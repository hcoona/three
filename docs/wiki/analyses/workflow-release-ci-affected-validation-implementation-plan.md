# Workflow Release CI Affected Validation Implementation Plan

## Purpose

This page records the grouped implementation plan and execution requirements for
the workflow-release CI affected-validation redesign. It is written for senior AI
engineering agents that may resume the work later and need the planning context,
governance rules, current completion state, and acceptance expectations without
reconstructing them from chat history.

This is a handoff document, not a new design layer. It preserves the signed-off
requirements, high-level design, middle-level design, and low-level design. When
the implementation plan appears to conflict with those documents, agents must
treat the design documents as authoritative and escalate instead of silently
rewriting an upstream decision.

## Source Basis

The plan is synthesized from the prior waterfall review and implementation
sessions. The durable design inputs are:

- [Workflow Release CI Affected Validation Requirements](./workflow-release-ci-affected-validation-requirements.md)
- [Workflow Release CI Affected Validation High-Level Design](./workflow-release-ci-affected-validation-high-level-design.md)
- [Workflow Release CI Affected Validation Middle-Level Design](./workflow-release-ci-affected-validation-middle-level-design.md)
- [Workflow Release CI Affected Validation Low-Level Design](./workflow-release-ci-affected-validation-low-level-design.md)

The latest recorded implementation milestones now include Group 1
release-validation authority hardening and Group 2 CI topology/runtime
optimization. Group 2 and the final repair commits listed in
[Group 2 Hosted Acceptance Evidence](#group-2-hosted-acceptance-evidence) are
part of the durable completion record for this handoff.

## Terminology Note: Two Grouping Schemes

Two different grouping schemes were used during the work. Future agents must not
merge them accidentally.

| Scheme                     | Purpose                                                                                                                                                        | Current status                                                                                                            |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| LLD implementation groups  | Original execution-batch CI implementation decomposition: contracts, materialization, execution, aggregation, workflow topology, cleanup, and topology repair. | The original G1-G6 implementation path and later G7 topology repair were completed before the authority-hardening pass.   |
| Governance priority groups | Correctness-first stabilization after review found that release-validation authority had to be hardened before runtime optimization.                           | Group 1 authority hardening is complete; Group 2 optimization is complete with hosted acceptance evidence recorded below. |

When this page says **Group 1 release-validation authority**, it refers to the
governance priority group, not the older LLD implementation Group 1 contract
work.

## Fixed Requirements and Design Constraints

Agents implementing or reviewing later groups must preserve these constraints:

1. Requirements, HLD, and MLD remain preserved. No design layer was overturned by
   the CI runtime investigation.
2. CI affected validation is validation-only. It must never receive publication
   credentials, mutate tags, mutate GitHub Releases, perform registry uploads, or
   create release immutable proof.
3. The final required check context remains `CI Validation / aggregate-evidence`.
4. Unknown or unclassifiable inputs fail closed.
5. Selected validation obligations must not silently downgrade, disappear, or be
   replaced by a weaker smoke proxy.
6. The workflow-release validation plan is authoritative for classification,
   selected subjects, logical work groups, evidence expectations, and verdict
   intent.
7. Work groups and selectors are logical obligations, not GitHub Actions jobs,
   matrix rows, command lines, or runner allocations.
8. Execution batches may coalesce compatible logical obligations, but they must
   preserve per-selector outcomes and evidence rows.
9. Batch evidence is validation-grade only and must remain separate from release
   immutable proof.
10. Final aggregation is responsible for strict artifact namespace closure,
    expected artifact checks, evidence admission, and verdict computation.
11. Dry-run and validation-only release paths must have no live side effects.
12. Producer authority and artifact-boundary validation must fail closed.

## Performance and Topology Targets

The performance targets are observable implementation goals, not permission to
weaken correctness:

| Target area                  | Goal                                                                           |
| ---------------------------- | ------------------------------------------------------------------------------ |
| Broad, full, or global run   | Target at most 12 minutes when runner availability and cache state are normal. |
| Top-level GitHub jobs        | Cap at 18 total jobs.                                                          |
| Windows jobs                 | Cap at 8 Windows jobs.                                                         |
| Validation artifacts         | Target at most 20 artifacts.                                                   |
| Final aggregation duration   | Target 1 to 2 minutes.                                                         |
| Execution-batch artifact cap | With 7 expected non-bundle artifacts, allow at most 13 batch evidence bundles. |

If these targets are missed, agents should first investigate topology,
artifact/evidence fan-in, repeated setup, cache misses, and release-shaped
validation reuse before proposing any requirements or design change.

## Original LLD Implementation Groups

The original LLD implementation plan decomposed the execution-batch redesign into
the following groups:

| Group | Scope                        | Required outcome                                                                                                                                                |
| ----- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| G1    | CI validation contracts      | Add execution-batch manifest, batch evidence bundle, aggregate evidence manifest, aggregate summary contracts, artifact naming, validators, and contract tests. |
| G2    | Execution-batch materializer | Add materialization from logical work groups to bounded execution batches and matrix/manifest outputs.                                                          |
| G3    | Batch execution evidence     | Run execution-batch matrices or orchestrator slots and write one evidence bundle per batch.                                                                     |
| G4    | Evidence aggregation         | Aggregate the frozen plan, batch manifest, and batch bundles into the aggregate evidence manifest and aggregate summary.                                        |
| G5    | Workflow topology switch     | Wire CI into normalize, plan, materialize-execution-batches, runner-family execution DAG, and aggregate-evidence.                                               |
| G6    | Cleanup and validation       | Remove obsolete per-work-group receipt and writer-observation authority paths, then finalize acceptance and performance checks.                                 |
| G7    | Topology DAG repair          | Remove arbitrary layer caps and avoid layer-wide matrix barriers that serialize unrelated batch dependencies.                                                   |

These groups are historical implementation decomposition. Later agents should
use them to understand why the current CI shape exists, but should not reopen
completed groups unless a new review finding directly requires it.

## Governance Priority Groups

After adversarial review found release-validation authority risks, the go-forward
plan was reprioritized:

| Group | Scope                                          | Completion status                                                                                                                                                                                                  |
| ----- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1     | Release-validation authority correctness       | Complete. Fixed fail-closed authority semantics, invalid-plan handling, producer verification, dry-run/publish safety, final artifact validation, and acceptance-gate coverage.                                    |
| 2     | CI topology and runtime optimization           | Complete. Hosted acceptance run `27111512179` passed with authority and hard-cap evidence recorded below; the 12-minute timing target remains a documented follow-up.                                              |
| 3     | GitHub Actions UI and observability, if needed | Not separately required by the accepted Group 2 repair set unless a future review asks for additional operator-facing clarity.                                                                                     |
| Final | Global overview and cross-group consistency    | Complete. After commit `9f1791d`, the OA process recorded two consecutive raw-clean final global overview rounds, `global-closure-r1-*` and `global-closure-r2-*`, before this docs-only closure-recording update. |

Group 1 deliberately did not optimize runtime. It addressed correctness before
performance because an untrusted or self-authorizing release-validation result is
more dangerous than a slow validation result.

## Mandatory Review and Execution Protocol

All future implementation groups must follow this protocol unless the human
explicitly changes it:

1. Keep orchestration and acceptance as human/OA-only decisions. Subagents may
   implement, review, or triage, but they must not self-launch acceptance,
   self-approve closure, or replace the OA's serialized process control.
2. Use the required agent roles for auditable implementation and review work:
   Coder=`gpt-55-coder`; Reviewer=`gpt-55-reviewer`.
3. Keep code-changing and doc-changing Coder subagents serialized. Do not run two
   Coder subagents against the same worktree concurrently.
4. Use independent senior-agent review before implementation when a design or
   scope decision is unclear.
5. Explain any requirements, HLD, or MLD change to the human before editing.
6. Use multiple independent adversarial Reviewer agents after each implementation
   round.
7. Use a separate independent triage agent for every raw review finding before
   classifying it as true positive, false positive, or partial true positive.
8. Treat any raw finding as resetting the clean-round counter, even if later
   triage marks it stale or false positive.
9. Accept a group only after two consecutive independent raw-clean review rounds.
10. From Group 2 onward, also run group-interface or inter-group reviews until
    two consecutive raw-clean rounds.
11. After all groups, run global overview review until two consecutive raw-clean
    rounds.
12. Escalate to the human for major design decisions, design-layer conflicts,
    unresolved ABAB oscillation, or any proposed change that would overturn
    requirements, HLD, or MLD.
13. Commit each accepted group with substantive changes, using the repository
    commit-message rules and the required Copilot co-author trailer.

For review outputs, `RAW_FINDINGS: none` and equivalent unambiguous clean
statements count as clean only when every required reviewer for that round is
clean. Mixed clean/finding rounds are not clean.

## Group 1 Completion Record

Group 1 release-validation authority hardening is complete.

Completion evidence:

- Two consecutive independent clean rounds were achieved:
    - R246 was the first clean round.
    - R247 was the second clean round.
- R247 authority, workflow, coverage, and scope reviewers returned
  `RAW_FINDINGS: none`.
- The completion rule was satisfied only after the final accepted fixes for:
    - invalid-plan evidence with `producer-verified=false`;
    - required bound `final-producer-unverified` evidence;
    - acceptance-gate mandatory pin presence, not merely nodeid collectability.
- Commit `241416c fix(ci): Harden Release Validation Authority` records the
  accepted Group 1 changes.

Group 1's accepted semantics include:

- Failed or skipped producer-boundary checks map to explicit `false`.
- Omitted or non-boolean producer verification maps to explicit `false`.
- Workflows must pass explicit `true` or `false`; a bare producer-verified flag
  is not accepted.
- Invalid or no-authority final manifests must not self-authorize from
  summary-derived or recomputed authority.
- Missing or unreadable execution-batch manifests must not preserve
  caller-supplied producer verification.
- No-authority invalid plans clear final manifest identity fields, preserve
  `producer-verified=false`, and strip unbound `final-producer-unverified` rows.
- Bound invalid-plan manifests that retain a claim with `producer-verified=false`
  must carry matching bound `final-producer-unverified` failure evidence.
- Dry-run blocks side-effecting release operations, including tag jobs and
  reusable publish jobs.
- Acceptance-gate meta-tests verify mandatory Group 1 pins are present.

## Group 2 Optimization Record

Group 2 CI topology and runtime optimization has been implemented and validated
on hosted GitHub Actions. Future agents should not treat this section as an
unstarted work queue. The original guidance and execution plan remain below as
historical context and as the checklist used to judge the accepted work.

The accepted result achieved the correctness, authority, topology, and hard-cap
requirements. It did **not** achieve the 12-minute broad/full/global timing
target; that miss is recorded explicitly in
[Timing Target Analysis and Follow-up](#timing-target-analysis-and-follow-up).

### Historical Group 2 Guidance

Group 2 should optimize CI topology and runtime without weakening Group 1.

Recommended starting checks:

1. Reconfirm that the current final check context and authority semantics remain
   stable after Group 1.
2. Measure current CI runtime by phase: planning, materialization, runner-family
   execution, artifact download/admission, and aggregation.
3. Identify avoidable serialization, especially layer-wide barriers and repeated
   artifact scans.
4. Prefer bounded runner-family orchestrators, compatible batch coalescing, and
   O(batch count) aggregation over per-selector job fan-out.
5. Preserve Windows-specific validation where required, but avoid mapping every
   logical Windows obligation to a separate GitHub job.
6. Keep release-shaped validation aligned with release build machinery where that
   improves fidelity and runtime, while maintaining no-publish and validation-only
   boundaries.
7. Run group-interface review against Group 1 before accepting any optimization.

Optimization is acceptable only if the aggregate verdict remains fail-closed,
artifact admission remains authority-bound, and selected obligations still
produce inspectable evidence.

### Group 2 Execution Plan

This was the pre-implementation waterfall execution plan for Group 2. It is
retained to preserve the decision trail and acceptance criteria, but it no
longer means that Group 2 is future or unstarted. The approved requirements,
HLD, MLD, and LLD remain authoritative. Escalate any design-layer conflict,
unclear decision, or proposed change to those layers before editing
implementation files.

#### Entry Gate and Frozen Invariants

Before any runtime optimization work, reconfirm the Group 1 authority invariants:

1. The final required check context remains
   `CI Validation / aggregate-evidence`.
2. CI affected validation remains validation-only: no publication credentials,
   tag mutation, GitHub Release mutation, registry upload, or release immutable
   proof creation.
3. Unknown or unclassifiable inputs fail closed.
4. Selected obligations, selectors, per-selector outcomes, and inspectable
   evidence must not silently downgrade, disappear, or be replaced by weaker
   smoke proxies.
5. Artifact namespace closure, producer-boundary verification, explicit boolean
   producer verification, final uploaded-byte verification, dry-run gating, and
   acceptance-gate pins remain intact.
6. Unless a human-approved design change explicitly says otherwise, preserve the
   implementation budgets of at most 18 pre-final validation artifacts, 20 total
   validation artifacts, 13 execution-batch bundles, 18 top-level jobs, and 8
   Windows jobs.

#### Phase 1: Non-Mutating Current-Topology Baseline

Start with a fresh GitHub Actions baseline for the current topology. Use only
non-mutating evidence when possible: GitHub run and job timestamps, the workflow
file at the recorded SHA, logs, uploaded artifacts, aggregate summary and
manifest files, and GitHub API metadata.

A baseline run is admissible only if it proves the current pre-change topology
signature at the recorded SHA:

1. Seven top-level CI jobs.
2. One Windows top-level job.
3. No `strategy.matrix` execution.
4. Runner-family orchestrators for Ubuntu, Windows, and macOS.
5. Thirteen sequential slots per runner family.
6. `aggregate-evidence` fan-in from all runner-family orchestrators.
7. Thirteen-batch, 18-pre-final-artifact, and 20-total-artifact caps.
8. Explicit producer-verified final aggregation.
9. Final uploaded-byte verification.

Reject stale `materialize-work-groups` or per-work-group runs as Group 2
baselines. They may be used only as historical motivation. If the current
evidence lacks reliable phase or slot timing, stop before bottleneck analysis.
Either use coarser non-mutating timing with explicit uncertainty, or ask the
human to approve a serialized measurement-only change. If instrumentation is
added, treat the instrumented run separately and do not call it the pristine
baseline without an explicit waiver.

Record the baseline run ID, commit SHA, branch, event and inputs, CI mode,
affected range or synthetic scope, selected obligations and selectors, evidence
obligations, batch and runner-family distribution, selected batch count,
per-family selected slot count, queue and provisioning time separated from job
execution where possible, phase timings, total job count, Windows job count,
validation artifact count, failed or retried jobs, skipped jobs, cache-state
signals, and aggregate duration.

#### Phase 2: Bottleneck Analysis

Classify runtime cost before proposing changes. At minimum, separate:

1. workflow-topology overhead;
2. per-slot or real validation workload runtime;
3. artifact and evidence upload, download, and admission overhead;
4. repeated setup and cache overhead;
5. cross-family dependency or artifact polling overhead;
6. aggregate fan-in overhead;
7. runner queueing or provisioning externality;
8. measurement uncertainty.

Do not infer optimization value from old-topology runs. Preserve logical
obligations, per-selector outcomes, and inspectable evidence as hard constraints
throughout analysis.

#### Phase 3: Design Alternatives and Decision Gate

Present alternatives, tradeoffs, and predicted impact before implementation.
Allowed optimization themes include compatible batch coalescing tuning,
runner-family slot scheduling or concurrency shape, setup or cache reuse,
artifact download batching and admission improvements, and generated workflow
topology changes.

Reject any alternative that weakens Group 1 invariants, exceeds the artifact,
batch, job, or Windows-job budgets, weakens explicit artifact-ID downloads,
weakens producer-boundary proof, removes final uploaded-byte verification, or
obscures per-selector evidence. Use independent senior-agent review for unclear
choices. Obtain human approval before any code, test, workflow, documentation,
or design-layer change.

#### Phase 4: Serialized Implementation After Approval

Keep code-changing work serialized in one worktree. If measurement extraction is
approved and needed, implement it first as a neutral isolated change, verify that
it does not weaken Group 1, and collect an instrumented baseline separately.
Then implement topology or runtime changes in small groups with focused tests
for the changed surface.

If artifact fan-in, download, or admission changes, explicitly test
producer-verified final aggregation and final uploaded-byte verification. Keep
HK and acceptance-gate coverage for workflow-release control surfaces intact.

#### Phase 5: Validation and Acceptance

Produce comparable before and after GitHub Actions run IDs and a metric table.
The before run must prove the current pre-change topology signature. After runs
must prove the approved post-change topology signature. Workload comparability
requires the same event class, CI mode, affected range or equivalent synthetic
input, selected obligations and selectors, and no weaker evidence obligations.

Record total wall-clock time including and excluding queue or provisioning time
where possible, phase timings, total job count, Windows job count, pre-final and
total artifact counts, batch count, aggregate duration, selected-obligation and
runner-family distribution, transport shape, and cache or runner notes. If batch
or runner-family distribution, slot count, or transport shape differs because of
approved coalescing, scheduling, or topology optimization, first prove that the
selected logical workload and evidence obligations remain equivalent. Only then
may the comparison explain and normalize the transport-shape difference. If the
topology change materially alters the logical validation work or evidence
obligations, the runs are not comparable.

Compare the result against the documented 12-minute broad/full/global target,
18-job cap, 8-Windows-job cap, 20-total-artifact and 18-pre-final-artifact
limits, and 1-to-2-minute aggregate target. The artifact, batch, total-job, and
Windows-job caps are acceptance gates: breaching any of them fails Group 2 unless
the human explicitly approves a waiver or design-layer change before acceptance.
Explanations for missed targets apply to timing variance, queueing, cache state,
real validation workload, controllable topology/transport overhead, or
measurement uncertainty; they do not convert a cap breach into an accepted
optimization.

Focused Group 1 regression evidence must cover fail-closed producer
verification, invalid-plan or no-authority behavior, dry-run side-effect
suppression, final artifact validation including uploaded bytes,
acceptance-gate pin coverage, and explicit artifact-ID downloads.

After each implementation round, run multiple independent adversarial
implementation reviews. For every raw finding, run a separate independent triage
agent. Any raw finding resets that review stream's clean counter even if later
triaged false or stale. Because this is Group 2, also run group-interface reviews
against Group 1 under the same triage and reset rule. Accept Group 2 only after
two consecutive raw-clean implementation review rounds and two consecutive
raw-clean group-interface review rounds.

### Group 2 Hosted Acceptance Evidence

The durable hosted acceptance package for Group 2 is:

| Field                   | Final accepted run                                                                                                     |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| GitHub Actions run      | `27111512179`                                                                                                          |
| URL                     | https://github.com/hcoona/three/actions/runs/27111512179                                                               |
| Event                   | `workflow_dispatch`                                                                                                    |
| Workflow inputs         | No explicit `workflow_dispatch` inputs; `.github/workflows/ci.yml` defines none for this workflow.                     |
| CI mode                 | `scheduled_full`, resolved by the workflow for `workflow_dispatch` and recorded in `ci-validation-request.json`.       |
| Branch                  | `dev/shuaizhang/design-workflows`                                                                                      |
| Head SHA                | `782571d535d4cd09795ad9a96955180aad279edb`                                                                             |
| Affected range or scope | Synthetic/global scheduled-full scope; affected range is `not-applicable` with no base/head SHA or changed-files hash. |
| Status / conclusion     | `completed` / `success`                                                                                                |
| Created / started       | `2026-06-08T01:47:39Z`                                                                                                 |
| Updated / completed     | `2026-06-08T02:22:57Z`                                                                                                 |
| Total wall-clock basis  | 35m18s from created/started to updated/completed; queue and provisioning were not separately isolated in this package. |

Post-hosted closure-update disposition:

| Item                         | Disposition                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Hosted behavior evidence     | Workflow and code behavior evidence remains anchored at GitHub Actions run `27111512179` for SHA `782571d535d4cd09795ad9a96955180aad279edb`.                                                                                                                                                                                                                                                                                                                              |
| Docs-only closure updates    | Commit `9ef6a8c docs: Record Release Validation Acceptance Evidence`, the later docs-only review/waiver repair through commit `9f1791d`, and this closure-recording update are post-hosted documentation updates. They are covered by the local hook-validation path when they do not alter workflow or code behavior.                                                                                                                                                    |
| Hosted rerun waiver boundary | Hosted rerun is waived for low-risk docs-only closure-recording updates after `782571d535d4cd09795ad9a96955180aad279edb` when local hook validation passes, because they do not alter workflow or code behavior. This prevents documentation closure notes from creating an infinite requirement for another hosted run. This waiver does not apply to later workflow, code, validation-script, or behavior-changing edits, which still need appropriate hosted evidence. |

Final accepted run job evidence:

| Job                                | Result  | Duration           |
| ---------------------------------- | ------- | ------------------ |
| `normalize-input`                  | success | 14s                |
| `plan`                             | success | 21s                |
| `materialize-execution-batches`    | success | 23s                |
| Ubuntu runner-family orchestrator  | success | 782s               |
| macOS runner-family orchestrator   | success | 471s               |
| Windows runner-family orchestrator | success | 1889s              |
| `aggregate-evidence`               | success | 153s job wall time |

Final accepted run verdict and evidence:

| Evidence area           | Final accepted value                                                         | Result        |
| ----------------------- | ---------------------------------------------------------------------------- | ------------- |
| Aggregate verdict       | `passed`                                                                     | Pass          |
| Diagnostics             | `[]`                                                                         | Pass          |
| Failures                | `[]`                                                                         | Pass          |
| Evidence results        | 207 satisfied / 0 failed                                                     | Pass          |
| Execution batches       | 13 valid / 0 invalid                                                         | Pass          |
| Top-level jobs          | 7                                                                            | Pass          |
| Windows top-level jobs  | 1                                                                            | Pass          |
| Execution-batch bundles | 13                                                                           | Pass          |
| Artifacts               | 19 total: 17 pre-final validation artifacts plus 2 final aggregate artifacts | Pass          |
| Aggregate evidence job  | success                                                                      | Pass          |
| Artifact total size     | 316,925 bytes                                                                | Informational |

Final accepted workload and no-weaker evidence obligations:

| Field                        | Final accepted value                                                                                                                                                                                                                                    |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Selected scope basis         | Scheduled-full plan with 66 selected subjects and `lightweight-only=false`; there were no affected-file impacts because the affected range was not applicable.                                                                                          |
| Selected work groups         | 208 logical work groups: 100 release-shaped artifact, 66 ecosystem gate, 29 descriptor validation, 12 workflow-release tooling, and 1 evidence aggregation.                                                                                             |
| Runner-family distribution   | Logical work groups: 113 Ubuntu, 91 Windows, 4 macOS. Execution batches: 10 Ubuntu, 2 Windows, 1 macOS.                                                                                                                                                 |
| Selected obligations         | 178 validation obligations, 100 artifact obligations, 29 descriptor obligations, and 12 detail profiles in the frozen validation plan.                                                                                                                  |
| Selectors/evidence count     | The plan admitted 207 evidence expectations; final aggregation reported 207 satisfied and 0 failed.                                                                                                                                                     |
| No-weaker evidence statement | Acceptance uses the final validation plan's selected logical workload and all 207 evidence expectations. No selected obligation was replaced by a weaker smoke proxy, and no evidence obligation was waived or silently downgraded in the accepted run. |

Durable review and triage evidence for acceptance:

| Review stream                            | Raw findings and disposition                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Clean-counter and final evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Group 2 implementation                   | Initial slot-chaining implementation rounds reported 0 raw findings. Later staging-scope review found unrelated `uv.lock` and Docker Compose changes mixed with Group 2; both were TP and were split into separate commits.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Counter reset after the staging findings. Final Group 2 implementation review then reached two consecutive raw-clean rounds.                                                                                                                                                                                                                                                                                                                                                       |
| Group 2 interface with Group 1 authority | Interface rounds against release-validation authority reported 0 raw findings after the Group 2 implementation scope was isolated.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            | Reached two consecutive raw-clean rounds before the Group 2 commit.                                                                                                                                                                                                                                                                                                                                                                                                                |
| Scheduler repair implementation          | Baseline run `27086933851` exposed a cross-family scheduler TP. Subsequent implementation reviews found TPs for missing gate coverage, no-work prompt return, non-finite timeout handling, ran-without-upload waiting, mixed terminal/waitable blockers, terminal cross-family admission classification and diagnostics, same-family waitability propagation, same-family inadmissible upload state, terminal-blocker priority, later-terminal scanning, and deferred admission side effects. Repeated empty `.pytest-workflow-release-control/` reports were FP until validation later created real unignored scratch files; that later scratch finding was TP and fixed by ignoring/cleaning the directory. | Counter reset after every raw finding, including FPs. After fixes, scheduler implementation reached two raw-clean rounds; side-effect follow-up used `scheduler-sidefx-r1-*` / `scheduler-sidefx-r2-*` review labels and ended raw-clean.                                                                                                                                                                                                                                          |
| Scheduler repair interface               | Interface reviews found TPs for no-work prompt return, non-finite timeout and local terminal blockers, same-family pending-chain handling, terminal diagnostics, same-family inadmissible upload state, and terminal-blocker priority.                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | Counter reset after each TP; final interface rounds reached two raw-clean rounds.                                                                                                                                                                                                                                                                                                                                                                                                  |
| Pyrefly repair implementation            | Hosted run `27105923041` failed pyrefly gates for `html-sm-processor` and `workflow-release-contracts`; the repair was accepted after local pyrefly/test validation and no remaining raw implementation findings.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Final pyrefly implementation rounds reached raw-clean before accepted hosted run `27111512179`.                                                                                                                                                                                                                                                                                                                                                                                    |
| Pyrefly repair interface                 | Interface review confirmed the type-check repair did not weaken CI validation authority, selected-obligation evidence, or acceptance-gate coverage.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Final interface evidence was raw-clean before hosted acceptance.                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Documentation repair implementation      | Documentation repair reviews found TPs that the LLD omitted macOS runner-family coverage, the acceptance record lacked auditable review/triage evidence, the LLD trigger contract omitted `workflow_dispatch`, and the acceptance record prematurely claimed raw-clean docs repair rounds. Independent triage classified those raw findings as TP under docs-repair triage set `docs-repair-tp-triage`.                                                                                                                                                                                                                                                                                                       | Complete. Counter reset after docs TPs; the OA process then recorded two consecutive raw-clean implementation rounds for the docs repair group: `docs-repair-impl-clean-r1` and `docs-repair-impl-clean-r2`. The accepted docs repair was committed as `9ef6a8c docs: Record Release Validation Acceptance Evidence`.                                                                                                                                                              |
| Documentation repair interface           | Interface review found stale wording that contradicted bounded cross-family wait/admission semantics and family-count-agnostic aggregation. Independent TP/FP triage classified the interface raw finding as TP under `docs-repair-interface-triage`; the repaired LLD and acceptance record were then rechecked for interface consistency.                                                                                                                                                                                                                                                                                                                                                                   | Complete. Counter reset after the interface TP; the OA process then recorded two consecutive raw-clean group-interface rounds for the docs repair group: `docs-repair-interface-clean-r1` and `docs-repair-interface-clean-r2`. This completed docs repair implementation/interface closure before commit `9ef6a8c`; it does not claim final-global closure.                                                                                                                       |
| Final global review                      | Final global review round 1 TPs were stale Group 2 state, missing hosted evidence, target-miss explanation gaps, missing durable acceptance evidence, incomplete OA/agent-role auditability, and incomplete post-hosted docs-only rerun-waiver wording.                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Complete. Hosted run `27111512179` passed at SHA `782571d`; docs repair implementation/interface closure is complete through commit `9ef6a8c`, and later durable audit/waiver repair is complete through commit `9f1791d`. After `9f1791d`, the OA process recorded two consecutive raw-clean final global overview rounds, `global-closure-r1-*` and `global-closure-r2-*`. The 12-minute timing miss remains recorded as non-blocking follow-up, not hidden acceptance evidence. |

Hard-cap and target comparison for the final accepted run:

| Requirement or target               | Final accepted run                                                               | Comparison                                                                                                                                                              |
| ----------------------------------- | -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Broad/full/global run timing target | 35m18s wall-clock                                                                | Missed the 12-minute target.                                                                                                                                            |
| Top-level GitHub job cap            | 7 jobs                                                                           | Pass: below 18.                                                                                                                                                         |
| Windows job cap                     | 1 top-level Windows job                                                          | Pass: below 8.                                                                                                                                                          |
| Total validation artifact cap       | 19 artifacts: 17 pre-final validation artifacts plus 2 final aggregate artifacts | Pass: below 20.                                                                                                                                                         |
| Pre-final validation artifact cap   | 17 pre-final validation artifacts                                                | Pass: below 18.                                                                                                                                                         |
| Execution-batch bundle cap          | 13 execution batches / bundles                                                   | Pass: at the 13-bundle cap.                                                                                                                                             |
| Final aggregation target            | 153s aggregate job wall time; aggregate-summary duration 34s                     | Mixed by metric basis: the aggregate-summary operation is within 1-2 minutes, while the aggregate job wall time is 33s above a strict 2-minute job-wall interpretation. |

Baseline comparability status:

- Run `27086933851` at old SHA
  `a5e26546cc585f5d273c8a775712b1793ea038a6` failed in the scheduler before
  comparable aggregate evidence could complete. It is retained as hosted
  scheduler-defect evidence, not as a clean runtime A/B baseline.
- Run `27105923041` at SHA
  `497bb5c149a666f0e0c72411646c616ec5029d32` used the same
  `workflow_dispatch` event class and reached successful orchestrators, but its
  aggregate failed on two pyrefly gates. It is post-change repair evidence, not a
  fully accepted baseline.
- Therefore the accepted package is not a clean comparable timing baseline. The
  documented acceptance note is that impactful workflow changes received actual
  hosted GitHub Actions testing: the failed old-SHA run proved the scheduler
  defect, and final run `27111512179` proved the repaired post-change acceptance
  workload. No claim of a clean runtime A/B baseline is made.

Human/OA acceptance disposition for the missing clean baseline:

- The baseline run was not clean or comparable because it exposed the
  cross-family scheduler defect that the repair stream had to fix.
- Hosted validation was still required and performed because the accepted changes
  affect CI topology, runner-family scheduling, evidence admission, and runtime.
- Acceptance therefore uses the failed old-SHA hosted run as defect evidence and
  final green hosted run `27111512179` as correctness, authority, and hard-cap
  evidence.
- A clean runtime A/B optimization comparison is explicitly waived or deferred as
  a timing follow-up. This waiver does not claim clean A/B equivalence and does
  not hide the missed 12-minute target.
- Any future statement that the runtime target is satisfied, or that runtime
  improved relative to a clean baseline, must cite newer comparable hosted runs.

Earlier hosted evidence used during repair:

| Run           | SHA                                        | Outcome                                              | Durable lesson                                                                                                                                                                                                                             |
| ------------- | ------------------------------------------ | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `27086933851` | `a5e26546cc585f5d273c8a775712b1793ea038a6` | Failed in the old scheduler path.                    | The macOS orchestrator failed at slot 00 with `runner-family orchestrator could not select a dependency-ready batch` while waiting for a cross-family dependency. This was a scheduler correctness failure, not accepted runtime evidence. |
| `27105923041` | `497bb5c149a666f0e0c72411646c616ec5029d32` | All orchestrators succeeded, but aggregation failed. | Aggregate evidence reported 205 satisfied / 2 failed because the `html-sm-processor` and `workflow-release-contracts` pyrefly gates failed. This proved the topology repair needed Python type-check repairs before acceptance.            |

Relevant pushed commits in the Group 2 and repair trail:

| Commit    | Subject                                      | Role in acceptance trail                                         |
| --------- | -------------------------------------------- | ---------------------------------------------------------------- |
| `12be048` | Optimize Release Validation CI Slot Chaining | Main Group 2 runtime/topology optimization.                      |
| `5262e27` | chore: Refresh Python Lockfile               | Dependency lockfile refresh needed by the repair stream.         |
| `3cc787f` | style: Format Docker Compose Port Mapping    | Formatting repair in the same evidence trail.                    |
| `497bb5c` | fix: Wait for Cross-Family Release Batches   | Scheduler repair for cross-family batch dependencies.            |
| `782571d` | fix: Resolve Python Type Check Gates         | Final pyrefly gate repair for accepted hosted run `27111512179`. |

The low-level design's execution-batch admission sections have been reconciled
with the accepted scheduler repair: cross-family batch readiness is now described
as bounded wait/admission with prompt no-work and terminal-blocker handling,
while terminal failures, timeout, and invalid evidence still fail closed and
aggregation remains the authority.

### Timing Target Analysis and Follow-up

The final accepted run achieved the correctness, authority, and hard-cap
requirements, but it missed the documented 12-minute broad/full/global target.
The run took approximately 35m18s wall-clock from `2026-06-08T01:47:39Z` to
`2026-06-08T02:22:57Z`. The Windows runner-family orchestrator took 1889s, or
approximately 31m29s, which dominated the total runtime.

In user-facing terms, the accepted hosted behavior run was green but slow. It
finished end-to-end in about 35m18s, with Windows accounting for about 31m29s of
that time. The aggregate-summary processing itself was short at about 34s, while
the containing GitHub `aggregate-evidence` job took about 153s wall time. The
remaining work is therefore performance optimization and evidence hygiene, not
correctness closure.

The evidence points to actual Windows validation workload and hosted runner
execution time as the dominant overrun source, not uncontrolled topology
fan-out. The accepted topology used 7 top-level jobs, 1 Windows top-level job,
13 execution batches, 17 pre-final validation artifacts, and 19 total artifacts,
all within the hard caps. The remaining
timing problem should therefore be tracked as follow-up optimization of Windows
validation workload, cache/setup behavior, or hosted-runner execution cost,
without hiding the target miss and without weakening the evidence or authority
model.

The final aggregation timing must be read with an explicit metric basis. The
aggregate-summary operation duration was 34s, which is within the 1-to-2-minute
target. The `aggregate-evidence` GitHub job wall time was 153s, or 2m33s, which
is above a strict 2-minute job-wall interpretation by 33s. Future comparisons
must state which metric is being compared instead of calling the summary
operation itself a 153s operation.

This record is not a blanket waiver of the 12-minute target for future work. It
records that the accepted Group 2 repair passed correctness, authority, and hard
caps while leaving the 12-minute runtime miss visible as a follow-up
optimization item. Any later claim that the timing target is satisfied must cite
a newer comparable hosted run.

Remaining non-blocking follow-up tasks:

1. Optimize hosted runtime, especially Windows orchestrator, setup, and cache
   cost, to reduce the final run wall time.
2. Establish a clean comparable A/B hosted runtime baseline only if future work
   wants to claim 12-minute runtime-target compliance or runtime improvement
   relative to a clean baseline.
3. Keep the aggregate-summary processing duration distinct from GitHub
   aggregate-job wall time in future evidence.

### Final Review and Repair Summary

The final global review round 1 findings for this repair group were all
triaged true positive:

1. the handoff plan was stale and still described Group 2 and final review as
   future work;
2. the acceptance evidence package was not durably recorded in `docs/wiki`;
3. timing targets were missed without documented explanation;
4. the 12-minute target miss needed explicit analysis, waiver or follow-up
   disposition;
5. the acceptance record did not make the human/OA versus subagent roles
   sufficiently auditable for review ownership, triage ownership, and closure
   authority;
6. the post-hosted docs-only rerun-waiver wording was incomplete because it did
   not explicitly preserve final hosted evidence, avoid hiding the 12-minute
   timing miss, and prevent premature final-global closure claims.

This page now records Group 2 as completed, preserves the original execution
plan as historical context, adds the hosted acceptance evidence package, and
keeps the 12-minute miss visible with a follow-up disposition. It also records
the OA/agent-role audit boundary and the post-hosted docs-only rerun-waiver
limits so that documentation-only repairs do not replace hosted acceptance
evidence or prematurely claim final-global closure.

A later documentation repair review also triaged true positives in the durable
record itself: the LLD runner-family schema omitted the accepted macOS
orchestrator family, and the Group 2 acceptance package needed concise
review/triage evidence. Both documentation findings were addressed without
changing the accepted topology semantics.

Final-global closure is complete for the accepted hosted behavior package. After
commit `9f1791d`, the OA process recorded two consecutive raw-clean final global
overview review rounds, `global-closure-r1-*` and `global-closure-r2-*`, before
this closure-recording update.

This closure-recording update is documentation-only. Under the post-hosted
docs-only waiver above, it is covered by local hook validation and must not
create an infinite requirement for another hosted run, because it does not alter
workflow, validation-script, source-code, or test behavior. A hosted rerun remains
required for future workflow, code, validation-script, or behavior-changing
edits.

### Historical Group 2 Acceptance Checklist

The pre-implementation checklist below is preserved as historical context. Group
2 is no longer future work, but this checklist remains useful when auditing or
comparing the accepted evidence above.

1. Baseline measurement source:
    - GitHub Actions workflow run IDs for an admissible current-topology
      baseline, or an explicit human waiver explaining why GitHub-hosted evidence
      is unavailable;
    - proof that the baseline run used the current pre-change topology
      signature;
    - local reproduction commands only as supplemental evidence unless covered
      by an explicit human waiver;
    - commit SHA and branch;
    - event, inputs, CI mode, affected range or equivalent synthetic scope, and
      selected obligations or selectors;
    - phase timing table for normalization, planning, materialization,
      runner-family execution, artifact admission, and aggregation;
    - total GitHub job count;
    - Windows job count;
    - pre-final and total validation artifact counts;
    - selected batch count and runner-family distribution;
    - final aggregate duration.
2. Post-change measurement source with the same fields, based on GitHub Actions
   workflow run IDs, proving the approved post-change topology signature. Local
   reproduction is supplemental only unless the human explicitly waives
   GitHub-hosted measurement.
3. Explicit comparison against the documented 12-minute, 18-job, 8-Windows-job,
   20-total-artifact, 18-pre-final-artifact, and 1-to-2-minute aggregate targets.
4. Explanation for any missed target, including whether the miss is caused by
   runner availability, cache state, real validation workload, or avoidable
   topology/transport overhead. If batch or runner-family distribution, slot
   count, or transport shape changes under an approved optimization, prove that
   the logical workload and evidence obligations remain equivalent before
   normalizing the comparison. If the logical workload or evidence obligations
   materially change, the runs are incomparable. Cap breaches remain
   acceptance-failing unless covered by an explicit human-approved waiver or
   design-layer change.
5. Focused regression results for Group 1 authority semantics, including
   fail-closed producer verification, invalid-plan/no-authority behavior, dry-run
   side-effect suppression, final artifact validation including uploaded bytes,
   acceptance-gate pin coverage, and explicit artifact-ID downloads.
6. Group-interface review evidence proving the optimization did not weaken Group
   1 release-validation authority, evidence admission, or acceptance-gate
   coverage.
7. Review and triage evidence showing multiple independent adversarial
   implementation review rounds, separate independent triage for every raw
   finding, clean-counter reset after every raw finding, two consecutive
   raw-clean implementation review rounds, and two consecutive raw-clean
   group-interface review rounds.

If runtime improves but any authority, artifact-boundary, or selected-obligation
regression appears, Group 2 is not accepted.

## Out-of-Scope Items for This Plan

This page does not define:

- a new CI design;
- a replacement requirements baseline;
- exact helper function names or internal module decomposition;
- GitHub branch-protection administration steps;
- registry trusted-publisher setup;
- acceptance evidence for future live release publication.

Those topics remain governed by the existing design pages and operator runbooks.

## Related Pages

- [Workflow Release CI Affected Validation Requirements](./workflow-release-ci-affected-validation-requirements.md)
- [Workflow Release CI Affected Validation High-Level Design](./workflow-release-ci-affected-validation-high-level-design.md)
- [Workflow Release CI Affected Validation Middle-Level Design](./workflow-release-ci-affected-validation-middle-level-design.md)
- [Workflow Release CI Affected Validation Low-Level Design](./workflow-release-ci-affected-validation-low-level-design.md)
- [Workflow Release CI Affected Validation Platform Spike Summary](./workflow-release-ci-affected-validation-platform-spike-summary.md)
