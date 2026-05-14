# Workflow Release CI Affected Validation Platform Spike Summary

## Scope

This page summarizes the Group 1, Group 2, and Group 3 GitHub Actions platform
capability experiments for workflow-release CI affected validation.

These records are platform capability experiments only. They are not a complete
low-level design implementation, do not add workflow behavior, and should not be
read as acceptance evidence for the final CI affected-validation workflow.
Group 4 did not trigger new GitHub Actions runs; it only synthesized the
existing experiment pages.

## Source Experiment Records

| Group | Source page                                                                                                       | Commit                                     | Run                                                                       |
| ----- | ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------ | ------------------------------------------------------------------------- |
| 1     | [Artifact Enumeration Experiment](./workflow-release-ci-affected-validation-artifact-enumeration-experiment.md)   | `57d444987bd47038f6f52df20211de913114ff60` | [`25885824704`](https://github.com/hcoona/three/actions/runs/25885824704) |
| 2     | [Producer Identity Experiment](./workflow-release-ci-affected-validation-producer-identity-experiment.md)         | `87318c468acc6884dfb7cd63ce49f07f6af9fcaa` | [`25886359951`](https://github.com/hcoona/three/actions/runs/25886359951) |
| 3     | [No-Authoritative-Plan Experiment](./workflow-release-ci-affected-validation-no-authoritative-plan-experiment.md) | `336ee365270f366111f174a8d6bd18437a473d33` | [`25887422010`](https://github.com/hcoona/three/actions/runs/25887422010) |

## Validated Platform Assumptions

### Artifact enumeration

Group 1 validated that the GitHub Actions run artifacts API exposes stable
artifact-instance evidence that is useful for replay and diagnostics:

- artifact `id`, `node_id`, `name`, timestamps, size, download URL, expiration
  state, and `digest`;
- run-level linkage through `workflow_run.id`, head branch, head SHA, repository
  id, and head repository id;
- instance-oriented enumeration where each returned artifact has its own id and
  name.

The same experiment also showed the important negative capability: artifact
objects do not expose per-artifact `run_attempt`. Run-scoped enumeration
continues to return old attempt artifacts after a rerun.

### Attempt-specific physical names

Group 1 and Group 2 both support using attempt-specific physical artifact names.
Exact-name lookup did not silently download an older attempt artifact when the
current attempt name was absent, and attempt-specific names allowed rerun
artifacts to coexist with attempt-1 artifacts.

The proposed physical-name shape,
`three-ci-validation-` plus lowercase SHA-256 of the logical artifact reference,
is safe for GitHub artifact names when the logical preimage includes all
non-colliding dimensions, especially contract version, logical role, repository
identity if needed, run id, run attempt, and logical work group or receipt key.

### Producer and matrix identity

Group 2 validated that the artifact API does not bind artifacts to uploader job
identity. It does not expose uploader job id, YAML job id, matrix values, or
reusable workflow path on artifact objects.

Job API ids and check-run external ids are diagnostic row ids. They change
across job reruns and can be regenerated for carried-over jobs, so they do not
prove that a producer executed in a given attempt. The stable logical matrix
variant remains the repository-defined variant or matrix value, not the REST job
row id.

### Writer observation

Group 2 showed that weak writer observation is feasible only when the trusted
producer records its own context plus the uploaded artifact id. The upload log
exposes the artifact id after `actions/upload-artifact@v4` finalizes an upload,
and the producer can record run id, run attempt, workflow file/ref/SHA, reusable
workflow inputs, logical job id, matrix dimensions, physical artifact name, and
artifact id.

Aggregation from the REST artifact list alone cannot prove trusted writer
authority.

### No-authoritative-plan failure path

Group 3 validated that a workflow can fail when no authoritative plan exists
while still exposing diagnostics:

- dependent work can skip when plan outputs are missing;
- an `if: always()` reporting path can run and upload readable diagnostics;
- a report can carry `plan-id: null` and must not forge an authoritative plan;
- planner diagnostics and job conclusions remain visible.

For CI affected validation, the final required
`CI Validation / aggregate-evidence` check must fail itself when it reports
`invalid-plan`. A successful reporting job is insufficient if branch protection
depends only on that final required context.

## Design and Implementation Constraints

The experiments imply these constraints for the future LLD or implementation:

1. Model artifact enumeration as `physical name -> [artifact instances]`, not as
   a lossy `name -> id` dictionary.
2. Compute current-attempt physical names and reject older-attempt artifacts by
   name mismatch.
3. Require exactly one live artifact instance for each expected physical name
   before admission.
4. Preserve artifact ids, creation time, update time, digest, and physical name
   in aggregation evidence.
5. Prefer ID-addressed downloads after enumeration; name-addressed downloads are
   acceptable only after exact-count validation.
6. Treat REST job ids and check-run external ids as diagnostics only, not as
   stable producer identity or execution proof.
7. Define trusted writer identity as a repository contract over workflow file,
   workflow SHA/ref, reusable boundary, logical job id, and matrix dimensions.
8. Require producers to emit writer-observation records that bind producer
   context to the uploaded artifact id.
9. Keep the no-plan path separate from receipt replay: no plan means no
   authorized expected receipts or selector assignments.
10. Make no-plan diagnostics first-class and diagnostic-only, with a null plan
    id, planning conclusion, expected plan artifact reference, observed counts,
    and any trusted planner diagnostics.
11. Ensure the final aggregate required check runs with `if: always()` and fails
    its own job for `invalid-plan`.
12. Apply the Group 1 enumeration safeguards even to diagnostic or sentinel
    artifact lookup in the no-plan path.

## Remaining Risks and Non-Validated Areas

- Same-run duplicate physical artifact names were not observed. The design must
  still fail closed because enumeration is instance-oriented and duplicates are a
  plausible control-plane conflict.
- GitHub's artifact API does not provide a cryptographic or API-native
  artifact-to-uploader-job binding. Any stricter producer-authority requirement
  needs separate trust-boundary review.
- The experiments used the existing `Release Buddy` workflow as platform probes,
  not the final CI affected-validation workflow.
- No end-to-end acceptance run has proven the final
  `CI Validation / aggregate-evidence` required check in all success,
  failed-receipt, duplicate-artifact, rerun, and no-plan branches.
- Diagnostic and sentinel artifact lookup still depends on exact-count,
  current-attempt enumeration logic that has not been implemented here.

## Scope-Out Observations for OA

- Group 1 observed a release rerun interaction where attempt 2 tried to download
  attempt-2 .NET planner metadata that did not exist while attempt-1 metadata
  remained enumerable. This is release rerun hardening, not Group 4 summary
  implementation work.
- Group 2 observed a second `release-tag-result` artifact produced during
  attempt 2 while its name still contained attempt `1`. This is also release
  rerun hardening outside the CI affected-validation platform-spike summary.
- If OA requires strict producer authority against untrusted workflow changes,
  the current platform evidence is not enough; that needs a separate security
  and trust-boundary decision.
- OA should separately schedule final CI affected-validation acceptance probes
  once an implementation exists. Group 4 intentionally did not trigger new
  workflow runs.

## Readiness Recommendation

Recommendation: **go for LLD refinement and implementation planning under the
validated constraints, but no-go for any design that relies on unavailable
artifact API fields or REST job ids as authority**.

The platform assumptions needed for safe artifact enumeration, attempt-specific
artifact separation, diagnostic no-plan reporting, and weak writer observation
are validated. The implementation must fail closed on artifact cardinality,
preserve artifact ids and digests, require producer-authored writer observations,
and make the aggregate check itself authoritative for branch protection.

The remaining risks are manageable as explicit design constraints or separate OA
follow-up items. They do not block proceeding to implementation design, but they
do block treating these platform spikes as complete LLD implementation or
production readiness evidence.

## Related Pages

- [Workflow Release CI Affected Validation Requirements](./workflow-release-ci-affected-validation-requirements.md)
- [Workflow Release CI Affected Validation High-Level Design](./workflow-release-ci-affected-validation-high-level-design.md)
- [Workflow Release CI Affected Validation Middle-Level Design](./workflow-release-ci-affected-validation-middle-level-design.md)
- [Workflow Release CI Affected Validation Low-Level Design](./workflow-release-ci-affected-validation-low-level-design.md)
