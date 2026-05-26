# Workflow Release CI Affected Validation Artifact Enumeration Experiment

## Scope

This experiment covers only artifact enumeration, artifact instance counting,
run-attempt separation, and the proposed CI validation physical artifact name
shape for workflow-release CI affected validation.

It intentionally does not evaluate producer or job identity, writer observation,
or no-authoritative-plan behavior.

## Experiment Entry

The experiment reused the existing default-branch `release-buddy.yml` dispatch
stub and ran the real branch workflow from
`dev/shuaizhang/design-workflows`. No main-branch mock workflow was added.

Run:

- Workflow: `Release Buddy`
- Run ID: `25885824704`
- Run URL: <https://github.com/hcoona/three/actions/runs/25885824704>
- Branch: `dev/shuaizhang/design-workflows`
- Head SHA: `7842f979aaffb9d993e7d0e45664aa1aac8853e3`
- Dispatch inputs:
    - `requested-project-ids`: `__artifact-enumeration-probe__`
    - `dry-run`: `true`
    - `validation-build`: `false`
    - `force`: `false`

The invalid project id made the run fail during planning after non-publishing
metadata handoffs. A failed-jobs rerun then created run attempt 2.

## Commands Used

```bash
gh workflow run release-buddy.yml \
  --ref dev/shuaizhang/design-workflows \
  -f requested-project-ids='__artifact-enumeration-probe__' \
  -f dry-run=true \
  -f validation-build=false \
  -f force=false

gh run watch 25885824704 --exit-status --interval 10
gh run rerun 25885824704 --failed
gh run watch 25885824704 --exit-status --interval 10

gh run view 25885824704 \
  --json databaseId,status,conclusion,url,workflowName,headBranch,headSha,attempt

gh api \
  'repos/hcoona/three/actions/runs/25885824704/artifacts?per_page=100'
```

## Observed Artifact Enumeration Fields

The run artifacts API returned enough stable per-artifact instance information
for enumeration and counting:

- `id`
- `node_id`
- `name`
- `size_in_bytes`
- `url`
- `archive_download_url`
- `expired`
- `created_at`
- `updated_at`
- `expires_at`
- `digest`
- `workflow_run.id`
- `workflow_run.head_branch`
- `workflow_run.head_sha`
- `workflow_run.repository_id`
- `workflow_run.head_repository_id`

It did not return `run_attempt` on each artifact object. The current run attempt
was available from `gh run view` as run-level field `attempt`, not as artifact
metadata.

## Observed Artifacts

After attempt 1, enumeration returned four artifacts:

| Artifact ID | Name                                                     | Created at           |
| ----------- | -------------------------------------------------------- | -------------------- |
| 7004860103  | `release-dotnet-planner-metadata-input-v1-25885824704-1` | 2026-05-14T21:09:21Z |
| 7004882766  | `release-dotnet-planner-metadata-v1-25885824704-1`       | 2026-05-14T21:10:38Z |
| 7004890350  | `release-planner-diagnostics-v1-25885824704-1`           | 2026-05-14T21:11:04Z |
| 7004898780  | `release-report-v1-25885824704-1`                        | 2026-05-14T21:11:32Z |

After the failed-jobs rerun, `gh run view` reported `attempt: 2`, and
enumeration returned five artifacts. The four attempt-1 artifacts remained
visible, and one new attempt-2 report artifact appeared:

| Artifact ID | Name                              | Created at           |
| ----------- | --------------------------------- | -------------------- |
| 7004922583  | `release-report-v1-25885824704-2` | 2026-05-14T21:12:52Z |

This confirms that run-scoped artifact enumeration includes old attempt
artifacts. Consumers must not assume the run artifacts endpoint is filtered to
the current attempt.

The attempt-2 plan job tried to download
`release-dotnet-planner-metadata-v1-25885824704-2` and failed with
`Artifact not found`. It did not silently fall back to the attempt-1 artifact
with the same logical role but a different physical name.

## Duplicate Instance Counting

The REST artifact list is instance-oriented: each artifact entry has its own
`id` and `name`. A consumer can group returned entries by physical name and
count instances without losing per-instance IDs.

No same-run duplicate physical artifact name was observed in this probe or in a
recent 100-artifact repository scan. The current `actions/upload-artifact@v4`
behavior and the existing workflow naming style also avoid duplicate names in
normal operation.

Design implication: the LLD should still treat duplicate physical names as a
possible control-plane condition and implement enumeration as a multimap
`name -> [artifact instance]`. A helper that collapses enumeration to
`name -> id` is insufficient for conflict detection because it would overwrite
duplicates if the platform returns them.

## Run-Attempt Separation

Findings:

- Artifact enumeration is run-scoped, not current-attempt-scoped.
- Artifact objects do not carry a direct `run_attempt` field.
- Old attempt artifacts stay enumerable after a rerun.
- Download by an exact attempt-specific name does not read an older attempt
  artifact with a different physical name.

Requirements for CI affected-validation LLD:

- Include `run_id` and `run_attempt` in the logical artifact ref preimage or in
  another non-ambiguous physical-name component.
- Record the run attempt in payload envelopes for diagnostics, but do not trust
  payload self-claims as the only binding.
- During aggregation, enumerate all run artifacts, group by physical name, and
  admit only artifacts whose expected physical name was computed for the current
  run attempt.
- Prefer downloading by artifact ID after enumeration when possible. Download by
  name is acceptable only after an exact-count check proves one live artifact
  instance for the expected physical name.

## Current Physical Artifact Name Strategy

The earlier digest-only candidate name
`three-ci-validation-{sha256(logical-ref)}` is superseded for current G5 CI
validation artifacts. Current artifacts use attempt-scoped physical names:

```text
three-ci-validation-{run-id}-{run-attempt}-{sha256(logical-ref)}
```

Local shape check:

```text
three-ci-validation-25887422010-1-4e3491070a1b2f9cf9a95c0bc4af00fac21acc94427772719d75a3b8166342af
```

The resulting name is variable-length because `run-id` and `run-attempt` are
variable-length decimal strings:

- fixed prefix length including its trailing hyphen: 20;
- workflow run id length: variable;
- separator between run id and run attempt: 1;
- workflow run attempt length: variable;
- separator before digest: 1;
- lowercase SHA-256 hex length: 64.

The character set is lowercase ASCII letters, digits, and hyphen. It avoids
GitHub artifact-name problem characters such as slash, backslash, quote, colon,
angle brackets, pipe, asterisk, question mark, and newlines, and remains well
below the practical artifact name length limit for GitHub Actions run ids and
attempt numbers.

This strategy keeps content/hash identity deterministic while making the
current attempt visible to bounded enumeration. Aggregation can ignore prior
attempt artifacts by physical-name prefix and still fail closed on unknown live
artifacts in the current attempt namespace.

## LLD Impact

The platform is sufficient for the LLD if the implementation does not depend on
artifact objects exposing `run_attempt`.

Required LLD adjustments:

1. Treat artifact enumeration as a list of instances, not a dictionary.
2. Compute attempt-specific physical names and reject artifacts from older
   attempts by name mismatch.
3. Count live instances per expected physical name and fail closed unless the
   count is exactly one.
4. Preserve artifact IDs in aggregation evidence and prefer ID-addressed
   downloads after enumeration.
5. Emit diagnostics mapping each logical artifact ref to physical name,
   artifact ID, creation time, and digest.

## Scope-Out Observation for OA

The failed-jobs rerun exposed a release-workflow attempt interaction outside
this group: attempt 2 tried to download an attempt-2 .NET metadata artifact that
was not present, while attempt-1 metadata artifacts remained enumerable. This is
not a CI affected-validation LLD implementation change, but it is relevant to
future release rerun hardening.
