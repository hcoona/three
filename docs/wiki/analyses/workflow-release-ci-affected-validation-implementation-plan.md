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

The latest committed implementation milestone is Group 1 release-validation
authority hardening, committed as `241416c fix(ci): Harden Release Validation
Authority`.

## Terminology Note: Two Grouping Schemes

Two different grouping schemes were used during the work. Future agents must not
merge them accidentally.

| Scheme                     | Purpose                                                                                                                                                        | Current status                                                                                                          |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| LLD implementation groups  | Original execution-batch CI implementation decomposition: contracts, materialization, execution, aggregation, workflow topology, cleanup, and topology repair. | The original G1-G6 implementation path and later G7 topology repair were completed before the authority-hardening pass. |
| Governance priority groups | Correctness-first stabilization after review found that release-validation authority had to be hardened before runtime optimization.                           | Group 1 is complete and committed; Group 2+ remains future work.                                                        |

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

| Group | Scope                                          | Completion status                                                                                                                                                               |
| ----- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1     | Release-validation authority correctness       | Complete. Fixed fail-closed authority semantics, invalid-plan handling, producer verification, dry-run/publish safety, final artifact validation, and acceptance-gate coverage. |
| 2     | CI topology and runtime optimization           | Future work. Optimize runtime only after Group 1 authority correctness remains stable under review.                                                                             |
| 3     | GitHub Actions UI and observability, if needed | Future work. Improve operator-facing clarity without weakening evidence or check semantics.                                                                                     |
| Final | Global overview and cross-group consistency    | Future work. Required after later groups, using the same clean-round rule.                                                                                                      |

Group 1 deliberately did not optimize runtime. It addressed correctness before
performance because an untrusted or self-authorizing release-validation result is
more dangerous than a slow validation result.

## Mandatory Review and Execution Protocol

All future implementation groups must follow this protocol unless the human
explicitly changes it:

1. Use independent senior-agent review before implementation when a design or
   scope decision is unclear.
2. Explain any requirements, HLD, or MLD change to the human before editing.
3. Keep code-changing agents serialized. Do not run two code-changing agents
   against the same worktree concurrently.
4. Use multiple independent adversarial review agents after each implementation
   round.
5. Use a separate independent triage agent for every raw review finding before
   classifying it as true positive, false positive, or partial true positive.
6. Treat any raw finding as resetting the clean-round counter, even if later
   triage marks it stale or false positive.
7. Accept a group only after two consecutive independent raw-clean review rounds.
8. From Group 2 onward, also run group-interface or inter-group reviews until two
   consecutive raw-clean rounds.
9. After all groups, run global overview review until two consecutive raw-clean
   rounds.
10. Escalate to the human for major design decisions, design-layer conflicts,
    unresolved ABAB oscillation, or any proposed change that would overturn
    requirements, HLD, or MLD.
11. Commit each accepted group with substantive changes, using the repository
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

## Future Group 2 Guidance

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

Group 2 remains a waterfall execution group. Do not edit workflow, test, code, or
wiki files for Group 2 optimization until the human approves the proposed design
alternative. The approved requirements, HLD, MLD, and LLD remain authoritative.
Escalate any design-layer conflict, unclear decision, or proposed change to those
layers before editing implementation files.

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

### Group 2 Acceptance Evidence

A future Group 2 implementation is not accepted by plausibility or local code
inspection alone. It must leave an auditable evidence package that includes:

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
