# Workflow Release CI Affected Validation No-Authoritative-Plan Experiment

## Scope

This Group 3 experiment covers the failure path where planning is broken enough
that no authoritative validation plan artifact is produced.

It intentionally does not implement the full CI affected-validation low-level
design. It also does not depend on receipt artifacts, writer observations, or
artifact instance replay from the Group 1 and Group 2 experiments.

## Experiment Entry

The experiment reused the existing default-branch `release-buddy.yml` dispatch
entry and ran the branch workflow from `dev/shuaizhang/design-workflows`. No
main-branch mock workflow was added.

Run:

- Workflow: `Release Buddy`
- Run ID: `25887422010`
- Run URL: <https://github.com/hcoona/three/actions/runs/25887422010>
- Branch: `dev/shuaizhang/design-workflows`
- Head SHA: `7842f979aaffb9d993e7d0e45664aa1aac8853e3`
- Run attempt: `1`
- Dispatch inputs:
    - `requested-project-ids`: `__no-authoritative-plan-probe__`
    - `dry-run`: `true`
    - `validation-build`: `true`
    - `force`: `false`

The invalid project id made the planner fail before it uploaded a release plan.
This is not a CI validation workflow, but it exercises the same platform
properties needed for a no-authoritative-plan CI failure path: downstream jobs
that depend on plan outputs, an always-running reporting job, visible job
conclusions, and diagnostic/report artifacts.

## Commands Used

```bash
gh workflow run release-buddy.yml \
  --repo hcoona/three \
  --ref dev/shuaizhang/design-workflows \
  -f requested-project-ids='__no-authoritative-plan-probe__' \
  -f dry-run=true \
  -f validation-build=true \
  -f force=false

gh run watch 25887422010 --repo hcoona/three --exit-status --interval 20

gh run view 25887422010 \
  --repo hcoona/three \
  --json databaseId,status,conclusion,url,workflowName,headBranch,headSha,attempt,event

gh api \
  'repos/hcoona/three/actions/runs/25887422010/attempts/1/jobs?per_page=100'

gh api \
  'repos/hcoona/three/actions/runs/25887422010/artifacts?per_page=100'

gh run download 25887422010 \
  --repo hcoona/three \
  --pattern 'release-*'
```

## Observed Job Conclusions

| Job                                                                     | Conclusion |
| ----------------------------------------------------------------------- | ---------- |
| `Authorize buddy release entry`                                         | `success`  |
| `Orchestrate buddy release / Validate release authoring`                | `success`  |
| `Orchestrate buddy release / Collect .NET planner metadata`             | `success`  |
| `Orchestrate buddy release / Plan release`                              | `failure`  |
| `Orchestrate buddy release / Build variants`                            | `skipped`  |
| `Orchestrate buddy release / Emit skip receipt`                         | `skipped`  |
| `Orchestrate buddy release / Ensure GitHub Release tags`                | `skipped`  |
| `Orchestrate buddy release / Verify GitHub Release tags`                | `skipped`  |
| `Orchestrate buddy release / Publish reusable-hosted ...`               | `skipped`  |
| `Orchestrate buddy release / Summarize orchestration stage conclusions` | `success`  |
| `Orchestrate buddy release / Summarize reusable publish conclusion`     | `success`  |
| `Publish entry-hosted node`                                             | `skipped`  |
| `Upload entry proof artifact`                                           | `skipped`  |
| `Report buddy release`                                                  | `success`  |

The overall workflow conclusion was `failure` because the planning job failed.
The report job still ran with `if: always()` and completed successfully after
capturing the failed planning conclusion.

## Observed Artifacts

| Artifact ID  | Name                                                     |
| ------------ | -------------------------------------------------------- |
| `7005476171` | `release-dotnet-planner-metadata-input-v1-25887422010-1` |
| `7005528503` | `release-dotnet-planner-metadata-v1-25887422010-1`       |
| `7005535001` | `release-planner-diagnostics-v1-25887422010-1`           |
| `7005542566` | `release-report-v1-25887422010-1`                        |

No `release-plan.json`, execution-set artifact, entry publish handoff, skip
receipt, build receipt, tag result, or publish result artifact was produced.

The planner diagnostics artifact contained the blocking diagnostic:

```json
{
    "code": "REQ_PROJECT_NOT_FOUND",
    "blocking": true,
    "message": "requested project is not an in-scope releasable project",
    "phase": "validation",
    "project-id": "__no-authoritative-plan-probe__"
}
```

The report artifact did not fabricate a plan. It recorded:

```json
{
    "plan": {
        "plan-id": null,
        "selected-project-ids": null
    },
    "jobs": {
        "plan": {
            "conclusion": "failure"
        },
        "build": {
            "conclusion": "skipped"
        },
        "publish": {
            "conclusion": "skipped"
        }
    },
    "run": {
        "conclusion": "failure"
    }
}
```

## Diagnostic Visibility

The platform provided readable diagnostics without an authoritative plan:

- The failed `Plan release` check was visible in the check list and job log.
- The planner diagnostics artifact named the blocking reason and project id.
- The always-running report job uploaded a compact report artifact with
  `plan-id: null`, `selected-project-ids: null`, and
  `jobs.plan.conclusion: failure`.
- The report helper appends a job summary when `GITHUB_STEP_SUMMARY` is present.

One important limitation is that the `Report buddy release` check itself
concluded `success`. That is acceptable for this release-workflow probe because
the whole run still failed through the plan job. It is not sufficient for CI
affected validation if branch protection binds only to the final required
context `CI Validation / aggregate-evidence`.

## Dependency and Needs Findings

The existing release workflow demonstrates three useful downstream patterns:

1. Plan-output-dependent work such as build, skip receipts, tags, and publish
   can be safely skipped when the plan job fails before setting outputs.
2. A small stage-conclusion job can run with `if: always()` and preserve the
   failed planning conclusion as structured output for reporting.
3. A final reporting job can run with `if: always()`, consume whatever artifacts
   exist, and emit diagnostics without manufacturing a plan.

For CI affected validation, the better topology is:

- materialization and work-group fan-out should not run from missing plan
  outputs;
- `aggregate-evidence` should run with `if: always()` after plan/materialization
  are attempted;
- `aggregate-evidence` should classify a missing, unreadable, or invalid plan as
  `invalid-plan`;
- `aggregate-evidence` should upload a report or sentinel diagnostic artifact;
- `aggregate-evidence` must fail its own job when the plan is missing, because
  that job is the required check context.

## Relationship to Groups 1 and 2

This no-plan path should deliberately avoid Group 1 and Group 2 mechanisms:

- It should not enumerate or replay validation receipts, because no plan exists
  to authorize expected receipt names or selectors.
- It should not depend on writer-observation records, because no materialized
  selector assignment exists to bind trusted receipt writers.
- It should not rely on receipt artifact instance replay. Diagnostic or sentinel
  artifact lookup for the current run attempt still inherits the Group 1
  enumeration safeguards.

The only artifacts that matter before an authoritative plan exists are
diagnostic/report artifacts produced by trusted control-plane jobs. A missing
plan must remain missing in the evidence model; diagnostics must explain the
absence instead of replacing the plan with a fake authoritative payload.

For any no-plan diagnostic or sentinel artifact lookup, aggregation should
enumerate run-scoped artifacts, group them as `physical name -> [instances]`,
compute the attempt-specific expected physical name, require exactly one live
instance before admission, prefer downloading by artifact ID when possible, and
record artifact ID, creation time, and digest evidence in the diagnostic report.

## LLD and Implementation Impact

The current low-level design direction is viable but should be sharpened for the
missing-plan case:

1. Keep the final required context on the aggregate job, not on planning or
   fan-out jobs.
2. Require the aggregate job to run with `if: always()` and to fail when it
   reports `invalid-plan`.
3. Define a first-class no-plan diagnostic record in the aggregation report,
   including run id, run attempt, plan artifact logical ref, expected physical
   name, observed artifact count, planning job conclusion, and any planner
   diagnostics artifact.
4. Do not model the diagnostic record as an authoritative validation plan.
5. Treat materialization and fan-out jobs as skipped when plan outputs are
   absent, unless a future implementation chooses a dedicated materialization
   sentinel. If such a sentinel exists, it must be clearly diagnostic-only.
6. Acceptance evidence should include a run where no validation plan artifact
   exists, the final aggregate check fails, and the report remains readable.

## Follow-Up Implementation Recommendations

- Add a minimal `invalid-plan` aggregation report shape before implementing full
  receipt aggregation.
- Make the missing-plan report artifact name attempt-specific, following the
  Group 1 physical-name guidance.
- Include planner/request failure diagnostics in the aggregate report when a
  trusted diagnostics artifact exists.
- Avoid parsing receipt or writer-observation artifacts in the missing-plan
  branch.
- Add a targeted acceptance test or workflow-dispatch probe that asserts the
  final aggregate job fails while still uploading a readable diagnostic report.
