# Workflow Release CI Affected Validation Producer Identity Experiment

## Scope

This Group 2 experiment covers producer/job identity and writer-observation
feasibility for workflow-release CI affected validation.

It intentionally does not implement the full low-level design and does not cover
no-authoritative-plan behavior.

## Experiment Entry

The experiment reused the existing default-branch `release-buddy.yml` dispatch
entry and ran the branch workflow from `dev/shuaizhang/design-workflows`. No
main-branch mock workflow was added.

Run:

- Workflow: `Release Buddy`
- Run ID: `25886359951`
- Run URL: <https://github.com/hcoona/three/actions/runs/25886359951>
- Branch: `dev/shuaizhang/design-workflows`
- Head SHA: `7842f979aaffb9d993e7d0e45664aa1aac8853e3`
- Initial run attempt: `1`
- After one matrix job rerun: `2`
- Dispatch inputs:
    - `requested-project-ids`: `hcoona-release-smoke-wxt`
    - `dry-run`: `true`
    - `validation-build`: `true`
    - `force`: `false`

The selected project produced three validation-build matrix entries for browser
variants. One successful matrix job was rerun with `gh run rerun --job` to
observe job-rerun identity behavior.

## Commands Used

```bash
gh workflow run release-buddy.yml \
  --repo hcoona/three \
  --ref dev/shuaizhang/design-workflows \
  -f requested-project-ids='hcoona-release-smoke-wxt' \
  -f dry-run=true \
  -f validation-build=true \
  -f force=false

gh run watch 25886359951 --repo hcoona/three --exit-status --interval 20

gh api 'repos/hcoona/three/actions/runs/25886359951'
gh api 'repos/hcoona/three/actions/runs/25886359951/attempts/1/jobs?per_page=100'
gh api 'repos/hcoona/three/actions/runs/25886359951/artifacts?per_page=100'

gh run rerun 25886359951 --repo hcoona/three --job 76079574373
gh run watch 25886359951 --repo hcoona/three --exit-status --interval 20

gh api 'repos/hcoona/three/actions/runs/25886359951/attempts/2/jobs?per_page=100'
gh api 'repos/hcoona/three/actions/jobs/76079574373'
gh api 'repos/hcoona/three/actions/jobs/76079956659'
gh run view 25886359951 --repo hcoona/three --job 76079956659 --log
```

## Observed Run and Reusable Workflow Identity

The run API returned these run-level identity fields:

| Field                  | Observed value                                                                                     |
| ---------------------- | -------------------------------------------------------------------------------------------------- |
| `id`                   | `25886359951`                                                                                      |
| `run_number`           | `18`                                                                                               |
| `run_attempt`          | `2` after rerun                                                                                    |
| `event`                | `workflow_dispatch`                                                                                |
| `workflow_id`          | `269749708`                                                                                        |
| `path`                 | `.github/workflows/release-buddy.yml`                                                              |
| `head_branch`          | `dev/shuaizhang/design-workflows`                                                                  |
| `head_sha`             | `7842f979aaffb9d993e7d0e45664aa1aac8853e3`                                                         |
| `actor`                | `hcoona`                                                                                           |
| `triggering_actor`     | `hcoona`                                                                                           |
| `referenced_workflows` | `release-orchestrate.yml`, `release-publish-node.yml`, `release-build-variant.yml` at the same SHA |

`referenced_workflows` is useful for replay diagnostics because it records the
reusable workflow files, refs, and SHAs used by the run. It is run-level data,
not per-job data; the job API did not expose a structured reusable-workflow path
for each job.

## Observed Matrix Job Identity

Attempt 1 produced three successful `Build variants` matrix jobs:

| Variant ID suffix | Browser | Attempt 1 job ID | Attempt 1 artifact suffix  |
| ----------------- | ------- | ---------------- | -------------------------- |
| `854d2e92...`     | firefox | `76079574340`    | `25cfcaeb578a6b074d41c33f` |
| `c1e6acba...`     | edge    | `76079574373`    | `2b294331bf4c4cfdd2bea54d` |
| `fbb367d7...`     | chrome  | `76079574380`    | `1f79221e0f0bee69b4b60aee` |

The job API returned these fields for each job:

- `id`
- `run_id`
- `run_attempt`
- `node_id`
- `head_sha`
- `workflow_name`
- `name`
- `status`
- `conclusion`
- `started_at`
- `completed_at`
- `runner_id`
- `runner_name`
- `runner_group_id`
- `runner_group_name`
- `labels`
- `check_run_url`
- `html_url`
- step names, numbers, timestamps, statuses, and conclusions

The matrix value was not returned as a structured API field. It appeared only in
the composed job display name, for example:

```text
Orchestrate buddy release / Build variants (variant/c1e6acba..., ubuntu-... / Build variant/c1e6acba...
```

Inside the producing job, the workflow can still capture the structured matrix
or reusable-workflow inputs from GitHub Actions contexts and explicit workflow
inputs. Aggregation from the REST API alone should not parse matrix authority
from display names as the only source of truth.

## Job Rerun Observation

The `edge` matrix job was rerun with `gh run rerun --job 76079574373`.

| Dimension                          | Original job                           | Rerun job                              |
| ---------------------------------- | -------------------------------------- | -------------------------------------- |
| `run_id`                           | `25886359951`                          | `25886359951`                          |
| `run_attempt`                      | `1`                                    | `2`                                    |
| Job API diagnostic row `id`        | `76079574373`                          | `76079956659`                          |
| Check-run diagnostic `external_id` | `ab02ca03-8502-59bd-b7aa-92b45899a72d` | `23c0c938-468c-565f-8bed-b00900162b4a` |
| Job display name                   | unchanged for the same matrix value    | unchanged for the same matrix value    |
| Runner ID                          | `1000008371`                           | `1000008376`                           |

Findings:

- The run ID stayed stable across the job rerun.
- The run attempt changed from `1` to `2`.
- The job API ID and check-run external ID changed, so they are diagnostic
  attempt or check-run row identifiers, not stable logical producer IDs.
- The logical matrix identity stayed stable in the job name because the variant
  ID stayed stable.
- `gh run view --json jobs` after the rerun reported attempt-2 job IDs even for
  jobs whose timestamps came from attempt 1. The carried-over `chrome` and
  `firefox` matrix jobs received new diagnostic job rows (`76079957368` and
  `76079957453`) and new check-run external IDs
  (`8be81266-f364-5e43-bb62-18a883a17c32` and
  `1b7e80af-2af9-5a5b-b8f7-11393c25692a`) while preserving attempt-1 start
  times. These IDs therefore do not prove that a producer actually executed in
  that attempt. Attempt-specific job API endpoints are safer for replay
  diagnostics, but they still do not provide producer-execution proof by
  themselves.

## Artifact and Writer Observation Findings

After attempt 1, the run had 13 artifacts. The three matrix jobs uploaded six
build artifacts, two per variant:

| Browser | Bundle artifact ID | Result artifact ID | Attempt-specific name suffix |
| ------- | ------------------ | ------------------ | ---------------------------- |
| firefox | `7005142886`       | `7005143184`       | `25cfcaeb578a6b074d41c33f`   |
| edge    | `7005139792`       | `7005139979`       | `2b294331bf4c4cfdd2bea54d`   |
| chrome  | `7005139359`       | `7005139535`       | `1f79221e0f0bee69b4b60aee`   |

After rerunning only the `edge` matrix job, the run artifact list grew to 17
artifacts. The attempt-1 artifacts remained visible, and attempt-2 artifacts
were added for the rerun matrix job:

| Artifact ID  | Name                                                             |
| ------------ | ---------------------------------------------------------------- |
| `7005183492` | `release-build-bundle-v1-25886359951-2-2b294331bf4c4cfdd2bea54d` |
| `7005183651` | `release-build-result-v1-25886359951-2-2b294331bf4c4cfdd2bea54d` |
| `7005190428` | `release-tag-result-v1-25886359951-1-c56877384aa6a56fcdc86f83`   |
| `7005198373` | `release-report-v1-25886359951-2`                                |

The `actions/upload-artifact@v4` log for the rerun matrix job printed artifact
IDs immediately after upload:

```text
Artifact release-build-bundle-v1-25886359951-2-2b294331bf4c4cfdd2bea54d.zip successfully finalized. Artifact ID 7005183492
Artifact release-build-result-v1-25886359951-2-2b294331bf4c4cfdd2bea54d.zip successfully finalized. Artifact ID 7005183651
```

This shows that a producing job can capture enough after-upload information to
write a writer-observation record that includes the physical artifact name and
artifact instance ID. A producing job can also capture pre-upload context such
as run ID, run attempt, workflow name, workflow ref/SHA, logical job ID,
reusable-workflow inputs, and matrix values from the GitHub Actions context and
workflow inputs.

However, the artifact enumeration API still did not expose uploader job ID,
YAML job ID, matrix values, or reusable workflow path on each artifact object.
Therefore an aggregation-only replay cannot independently derive trusted writer
identity from artifact metadata alone.

## Relationship to Group 1

This experiment confirms and extends the Group 1 artifact-enumeration findings:

- Artifact enumeration is still run-scoped, not attempt-scoped.
- Artifact objects still do not carry a per-artifact `run_attempt` field.
- Old attempt artifacts remain visible after reruns.
- Attempt-specific physical names allowed the rerun build artifacts to coexist
  with the original attempt-1 build artifacts.
- Artifact `id`, `name`, `created_at`, `updated_at`, and `digest` remain useful
  aggregation evidence, but they are not enough to prove producer job authority.

## LLD Impact

The platform is sufficient for weak writer observation if the producing trusted
workflow records observation data itself. It is not sufficient for a design that
expects the aggregator to prove artifact producer authority only from the REST
artifact list.

Required LLD adjustments or confirmations:

1. Keep `run_id` and `run_attempt` in physical artifact names and evidence
   envelopes.
2. Treat job API IDs and check-run external IDs as diagnostic attempt or
   check-run row IDs, not as stable logical producer IDs and not as proof that a
   carried-over job actually executed in that attempt.
3. Define trusted writer identity as a repository contract over workflow file,
   workflow SHA/ref, logical job ID, reusable workflow call boundary, and matrix
   dimensions.
4. Have the producing job emit a writer-observation artifact immediately after
   the receipt upload, recording:
    - logical boundary and selector assignment;
    - run ID and run attempt;
    - workflow file/ref/SHA and referenced reusable workflow where applicable;
    - logical job ID and matrix dimensions from context or explicit inputs;
    - job API ID if discoverable or supplied later for diagnostics;
    - uploaded receipt physical name and artifact ID.
5. Have aggregation enumerate artifacts as a multimap by physical name, admit
   only the current-attempt physical names, and match receipt artifact IDs to
   writer-observation records.
6. Do not parse matrix values from REST job display names as an authority
   mechanism. Display names are useful diagnostics only.
7. If strict producer authority against untrusted workflow changes is required,
   re-evaluate the trust boundary. GitHub's artifact API does not provide a
   direct artifact-to-uploader-job binding.

## Follow-Up Implementation Recommendations

- Add a minimal CI-validation receipt upload helper that writes receipt and
  writer-observation records in the same trusted reusable workflow boundary.
- Give upload steps stable step IDs and persist the artifact ID or exact-name
  enumeration result into the observation record.
- Store the boundary identity map in planner/materialization output, not in each
  receipt payload.
- In replay tooling, query attempt-specific job endpoints for diagnostics and
  avoid `gh run view --json jobs` when attempt precision matters.

## Scope-Out Observation for OA

The existing release workflow generated a second
`release-tag-result-v1-25886359951-1-c56877384aa6a56fcdc86f83` artifact during
attempt 2 even though the name still contained attempt `1`. This is outside the
Group 2 CI affected-validation scope, but it is relevant to release rerun
hardening because it weakens attempt-specific naming consistency for that
release artifact class.
