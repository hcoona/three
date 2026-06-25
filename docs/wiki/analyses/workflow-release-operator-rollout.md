# Workflow Release Operator Rollout Runbook

## Status and Scope

This runbook is the implementation-readiness checklist for entering real
`workflow-release` testing after the Group 10 hardening pass. It translates the
low-level design, the checked-in workflows, and the acceptance gate into an
operator sequence. It is not a replacement for the design baseline in
[Workflow Release Low-Level Design](./workflow-release-low-level-design.md); if a
behavioral conflict appears, stop and rebaseline the design before running a live
release.

The intended operator is a repository maintainer who can configure GitHub
repository settings, GitHub environments, package registry trusted publishers,
and package ownership records. The workflows and planner must not create or
repair those external settings automatically.

## External References Consulted

The rollout guidance below is grounded in these external documents:

- GitHub Actions environment documentation and source: a job that references an
  environment must satisfy protection rules before it is sent to a runner, and
  environment creation can configure required reviewers, prevent self-review, and
  deployment branch or tag restrictions.
- GitHub Actions workflow syntax documentation and source: workflow and job
  `permissions` constrain `GITHUB_TOKEN` scopes, job-level permissions can be
  used for least privilege, unspecified permissions become `none`, and
  `id-token` supports `write` or `none`.
- GitHub Actions OIDC security hardening documentation: GitHub Actions can mint
  short-lived OIDC tokens for jobs with the appropriate job permission, and token
  claims include repository, workflow, run, ref, SHA, and environment context.
- npm trusted publishers documentation: npm trusted publishing uses OIDC,
  prefers short-lived credentials over long-lived tokens, requires npm CLI
  11.5.1 or later and Node.js 22.14.0 or later, and for GitHub Actions stores
  organization or user, repository, workflow filename, and optional environment
  name.
- PyPI trusted publishers documentation: PyPI trusted publishing uses OIDC to
  exchange the GitHub Actions identity token for a short-lived project-scoped API
  token, avoiding long-lived API tokens and requiring owner-side trusted
  publisher configuration.

## Manual Configuration Prerequisites

Complete these checks before enabling any official live publication path.

### GitHub Repository and Environment

- The checked-in workflow filenames for the active split topology are present and
  unchanged: `.github/workflows/buddy.yml`, `.github/workflows/official.yml`,
  `.github/workflows/release-orchestrate.yml`,
  `.github/workflows/release-resolve.yml`,
  `.github/workflows/release-build-python.yml`,
  `.github/workflows/release-build-node-pack.yml`,
  `.github/workflows/release-build-dotnet.yml`,
  `.github/workflows/release-build-ruby-gem.yml`, and
  `.github/workflows/release-build-wxt.yml`,
  `.github/workflows/release-create-github-release.yml`, and
  `.github/workflows/release-prepare-release-notes.yml`. The deleted
  `.github/workflows/release-build-variant.yml` and
  `.github/workflows/release-publish-node.yml` paths are superseded and are not
  rollout prerequisites.
- `.github/workflows/release-buddy.yml` and
  `.github/workflows/release-official.yml` are compatibility /
  dispatch-registration stubs only. They intentionally remain manually
  dispatchable, fail closed, and direct operators to `buddy.yml` or
  `official.yml`; do not configure registry trusted publishers, environments, or
  runbook entry steps against those stub filenames.
- The GitHub approval environments used by official release orchestration exist
  before official live side-effect testing.
- Active release environments (`github-release`, `pypi`, `npmjs-gate`, `npmjs`,
  and `rubygems` as applicable) exist before their corresponding live publish
  paths are enabled. They have required reviewers where the workflow expects human
  approval, prevent self-review, and are restricted to trusted release refs
  according to repository policy. Do not rely on a generic active `release`
  environment for registry publication.
- The registry-specific OIDC environments `pypi`, `npmjs`, and `rubygems` exist
  before their corresponding live publish paths are enabled. `pypi` and
  `rubygems` may carry required reviewers when used as the human gate; npm uses
  `npmjs-gate` as the required-reviewer human approval gate and `npmjs` only for
  OIDC token scoping. These environment names are part of the registry trusted
  publisher identity and must not be replaced by entry-workflow handoff jobs.
- Repository Actions settings allow these workflows to run and allow the
  job-level permissions declared by the workflow files.
- No long-lived PyPI, npmjs, or RubyGems.org publishing token is required for
  the current OIDC paths; do not add one as a fallback for this rollout.
  NuGet.org is deferred/future-only until NuGet catalog instances and publish
  routing are restored; this does not block the active `release-build-dotnet.yml`
  GitHub Release asset path.

### Repository Variables

Set the non-secret repository variable
`THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS` only after the corresponding
trusted-publisher settings have been manually verified. The value is a comma or
newline separated allowlist of exact tokens:

```text
<target-instance-ref>#<project-id>#<planner-frozen-package-name>
```

A missing or empty value enables no external OIDC targets. Wildcards are not
supported. Malformed tokens, empty token components, wildcard tokens, and tokens
whose target-instance ref is unknown or is not an OIDC target fail closed before
plan and execution-set artifacts are published. Extra well-formed tokens for a
known OIDC target-instance ref but an unmatched project or package are ignored.
When an active official external OIDC publish node's exact required token is
absent, the run fails closed with `REQ_EXTERNAL_TARGET_DISABLED`.

GitHub Release and GitHub Packages targets are not controlled by this variable;
they are guarded by workflow permissions, tag gates, attestations, and the
registry-specific environment rules where applicable.

### Registry Trusted Publishers

Configure registry-side trusted publishers only for the workflow identity the
registry validates for the token-minting job. For npmjs `workflow_call`, that is
the direct caller workflow rather than the reusable workflow that runs
`npm publish`.

| Surface         | Required owner-side setup before live testing                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PyPI            | For each enabled project, configure a PyPI trusted publisher for owner `hcoona`, repository `three`, workflow filename `release-orchestrate.yml`, and environment `pypi`. Do not configure `official.yml`, `release-build-variant.yml`, or `release-publish-node.yml` for PyPI in the active split topology.                                                                                                                                                                                                                       |
| npmjs           | For each enabled official package, configure a trusted publisher for owner `hcoona`, repository `three`, workflow filename `official.yml`, and environment `npmjs`. Confirm the package can use the npm CLI and Node.js versions required by npm trusted publishing. The reusable `publish-node-npmjs` job still runs `npm publish`, but npm validates the direct caller workflow for `workflow_call`; if buddy live npmjs publishing is enabled later, add a separate `buddy.yml` trusted publisher and caller `id-token: write`. |
| RubyGems.org    | For each enabled gem, configure trusted publishing for repository `hcoona/three`, workflow filename `release-orchestrate.yml`, and environment `rubygems`, using same-repository workflow settings. Do not require the deleted `release-publish-node.yml` path.                                                                                                                                                                                                                                                                    |
| NuGet.org       | Deferred/unavailable in the active catalog. `families.nuget.instances: []` means no smoke or real package may publish `nuget/nuget-org` until a reviewed dotnet/NuGet workflow path, target catalog instances, token-minting identity, and NuGet.org trusted-publishing setup are re-enabled. Keep real package NuGet.org publication such as `hjg-pngcs` disabled until that package path is explicitly brought into scope.                                                                                                       |
| GitHub Release  | No external trusted publisher exists. Pre-create and protect the active `github-release` environment for enabled `github-release/public` targets, and verify `contents: write` on the live GitHub Release mutation job only; keep attestation `attestations: write` / `id-token: write` guidance scoped to separate attestation jobs.                                                                                                                                                                                              |
| GitHub Packages | No external trusted publisher exists. Verify package ownership and `packages: write` only on live mutation jobs.                                                                                                                                                                                                                                                                                                                                                                                                                   |

## Local and CI Gate Criteria Before Live Testing

Do not start actual live/manual external testing until all local implementation
gates and the final global overview checks have passed. The minimum current gate
is:

1. `uv run python eng/scripts/workflow_release_acceptance_gate.py`
2. Relevant HK or direct lint checks for changed files, especially markdown and
   workflow files when they changed.
3. `git diff --check`
4. The final global overview checks planned after Group 10.

Treat the Group 9 acceptance gate as the focused `workflow-release` regression
suite. The HK step `workflow-release-control-tests` runs that gate when release
workflow, descriptor, control-script, workflow-release package, matrix, or
control-test files are touched.

### Deferred Official PyPI Full-Success Acceptance

The official Python smoke full-success PyPI acceptance item is intentionally
deferred, not forgotten. Continue all other local, CI, dry-run, GitHub-hosted,
and non-PyPI validation without treating final PyPI publication success as a
current blocker. The buddy Python smoke has passed.

Previous official break-glass development-ref runs reached PyPI trusted
publishing, but PyPI rejected the uploads because public PyPI does not accept
local-version identifiers produced from non-public refs. Do not treat those
prior runs as proving a current entry-hosted OIDC path. Under the active split
topology, acceptance evidence must show that the token-requesting signer and
publisher identity is `.github/workflows/release-orchestrate.yml` with the
registry environment `pypi` for PyPI or `rubygems` for RubyGems. For npmjs,
the Trusted Publisher entry must name the direct caller workflow
`.github/workflows/official.yml` with environment `npmjs`; the token is still
requested and the publish command still runs in `.github/workflows/release-orchestrate.yml`
under the `npmjs` environment.

After these workflow changes are merged to `main` and all other validation is
complete, run the official full-success PyPI validation from a proper NBGV
public release ref. That post-merge run is the required acceptance evidence for
real PyPI publication success.

## Staged Validation Order

Use this order. Do not skip ahead after a failure.

1. **Local acceptance**: run the focused acceptance gate and inspect any pytest
   failure before using GitHub Actions.
2. **Workflow-dispatch input rehearsal**: dispatch the active `Official Release`
   workflow only with supported inputs: `project`, `version`, optional `target`,
   and `force_update_tag=false`. The former `dry-run` and `validation-build`
   dispatch inputs are not available on the active workflow; do not use this
   rollout step as a no-side-effect dry run.
3. **Pre-live validation**: use local acceptance plus workflow runs against a
   non-publishing or zero-target project shape where available. If a future
   workflow revision reintroduces dry-run validation, document that input contract
   before relying on it operationally.
4. **Zero-target or all-skip live-shaped run**: use a selected project set that
   produces no active live side-effect nodes or only `skip-satisfied` nodes.
   Confirm the run does not request external OIDC credentials and does not enter
   unexpected live publish paths.
5. **Manual configuration review**: re-check the active registry environments,
   trusted-publisher entries, package ownership, and
   `THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS` exact tokens.
6. **Single-target live GitHub-hosted path**: test one GitHub Release or GitHub
   Packages target first, with only the expected live jobs receiving write
   permissions.
7. **Single-target live external OIDC path**: enable exactly one external OIDC
   token and run one selected project. Confirm the token-requesting job is hosted
   by `.github/workflows/release-orchestrate.yml` and runs under the expected
   registry environment (`pypi`, `npmjs`, or `rubygems`); for npmjs also confirm
   the trusted publisher names the `official.yml` caller workflow. Defer the
   official Python smoke full-success PyPI variant of this stage until the
   post-merge public-ref acceptance window described above.
8. **Broader live rollout**: add external OIDC tokens one package at a time. Keep
   each run small enough that a failed target can be diagnosed from one plan,
   report, and registry response.

## Artifact and Diagnostic Interpretation

Use positive receipts as proof and missing expected receipts as failure evidence.
Do not infer success from logs alone.

| Evidence                                                     | How to interpret it                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `planner-diagnostics.json`                                   | Structured fail-closed planner or readiness-gate diagnostics. True pre-plan entry or authoring failures stop before a plan exists. Invalid external OIDC allowlists, disabled external targets, and unsupported external OIDC topology are fail-closed pre-artifact-publication readiness-gate diagnostics: the gate reads the generated plan and execution sets, but the public plan and execution-set artifacts are intentionally not uploaded after the gate fails.                                                               |
| `release-plan.json`                                          | Authoritative selected commit, profile, selected projects, graph, resolved publish identity, target snapshots, and publish dispositions. If the public artifact is absent, diagnose entry authorization, authoring validation, true pre-plan readiness failures, or pre-artifact-publication readiness gates first.                                                                                                                                                                                                                  |
| `execution-sets.json`                                        | Authoritative workflow routing selectors. Empty arrays can be valid for zero-target or all-skip cases. Dry-run empty arrays are historical/future-only because active workflows do not expose dry-run dispatch inputs. Unexpected active external selectors indicate a gate or descriptor issue.                                                                                                                                                                                                                                     |
| `entry-publish-handoff.json`                                 | Superseded entry-hosted OIDC bridge artifact. It is not expected as current positive evidence for PyPI, npmjs, or RubyGems under the active split topology; investigate any non-empty handoff as stale routing.                                                                                                                                                                                                                                                                                                                      |
| `build-result.json`                                          | Positive live build receipt. Historical dry-run validation-build receipts, if encountered from older drafts, are not immutable publication proof.                                                                                                                                                                                                                                                                                                                                                                                    |
| `github-release-result.json`                                 | Positive GitHub Release receipt uploaded by the active `release-create-github-release.yml` path. It records the release tag, target commit, release id, whether the release already existed, and planned asset names/sizes/SHA-256 digests. Existing releases are accepted only when live assets exactly match those planned assets.                                                                                                                                                                                                 |
| `skip-result.json`                                           | Positive synthetic receipt for a planner-approved skip. It is not a publish receipt.                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `publish-request.json`                                       | Materialized instruction for one publish node. It is useful for diagnosing an executor failure but is not proof of publication.                                                                                                                                                                                                                                                                                                                                                                                                      |
| `publish-result.json`                                        | Positive receipt for one successful live publish node. If a publish job failed and this receipt is missing, the release report should name the failed publish node from the execution set.                                                                                                                                                                                                                                                                                                                                           |
| `immutable-proof.json` and `github-release-asset-proof.json` | Attempt-scoped current-run accelerators within GitHub Actions artifact retention limits. `immutable-proof.json` remains retention-bound package replay evidence; `github-release-asset-proof.json` is an optional wrapper around the verified GitHub Release asset attestation check, not the durable authority. After that wrapper expires, the GitHub Artifact Attestation service remains authoritative for GitHub Release asset provenance verification. Validation-build receipts must not generate admissible immutable proof. |
| `release-report.json`                                        | Final run summary. Use `run.conclusion`, `jobs`, `counts`, missing expected build variants, and missing expected publish nodes as the first diagnostic index.                                                                                                                                                                                                                                                                                                                                                                        |

## Failure Diagnostics

Use the first failing stage to choose the next action.

- **Entry authorization failure**: inspect the authorization job logs and
  `planner-diagnostics.json`. `official` requires `maintain+`; `buddy` requires
  `write+`.
- **Invalid external OIDC allowlist**: treat this as a fail-closed
  pre-artifact-publication readiness-gate diagnostic, not as a pre-plan
  failure. Fix the repository variable. Do not approve any active registry
  environment; the run should fail before public plan or execution-set artifact
  upload and before any side-effect job requests `pypi`, `npmjs-gate`, `npmjs`,
  or `rubygems`.
- **External target disabled**: add exactly the required token from diagnostics
  only after registry-side trusted publisher setup has been verified. This is a
  readiness-gate diagnostic after planning; diagnostics or the report may exist,
  but public plan and execution-set artifacts are not uploaded when the gate
  fails.
- **Unsupported external OIDC topology**: fix the descriptor or target mapping
  named by diagnostics. This is a readiness-gate diagnostic after planning, so
  use `planner-diagnostics.json` or `release-report.json` if present; do not
  expect public plan or execution-set artifacts from the failed run.
- **Trusted-publisher mismatch**: compare the registry's configured owner,
  repository, workflow filename, and environment to the topology table above and
  the job that actually requests the token.
- **Package metadata mismatch**: inspect the build receipt and package metadata.
  The executor must fail closed rather than rewriting package identity during
  publish.
- **Tag conflict**: verify the existing tag peels to the selected commit. The
  non-force path must not retarget tags; use `force_update_tag=true` only when
  the reviewed rollout step intentionally authorizes tag retargeting.
- **Missing publish receipt**: inspect the publish job logs, registry response,
  `publish-request.json` if present, and `release-report.json` failed publish
  node list. Do not treat partial logs as publication proof.
- **Cancellation**: use GitHub Actions run history as best-effort evidence.
  Positive receipts from completed jobs remain useful, but missing receipts after
  cancellation are not success evidence.

## Rollout Guardrails

- Do not rely on a `dry-run` dispatch input in the active workflows; keep live
  registry enablement disabled until local acceptance, CI acceptance, and final
  global overview checks are green.
- Keep `THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS` empty until the matching
  registry trusted-publisher entry has been verified manually.
- Enable external OIDC targets one exact package token at a time.
- Approve only the expected active environment jobs for the current stage.
- Do not rename stable release workflow files during rollout.
- Do not add long-lived registry publish tokens as fallback credentials.
- Do not rerun failed live jobs blindly after a partial side effect; inspect
  receipts and registry state first, then rerun the whole release only when the
  replay rules classify the state safely.

## When Actual Testing Should Begin

Actual live/manual external testing should begin only after Group 10 changes pass
local validation, the focused workflow-release acceptance gate, any relevant HK
checks, and the final global overview checks. At that point, perform the manual
repository and registry configuration review, confirm mock/local and CI
acceptance are already green, then proceed through the staged validation order
above. The active workflows do not expose dry-run or validation-build dispatch
inputs.

In short: begin live testing after implementation and documentation are green in
local and CI gates, after final overview checks, and after the active registry
environments plus registry trusted-publisher settings are manually in place. The
first live run should be narrow and low-blast-radius: one selected project, one
target class, and one explicitly enabled external OIDC token only when that stage
is reached. Live side effects are not assumed reversible; after
any partial side effect, operators must stop and inspect receipts plus registry
state before proceeding or rerunning.

## Related Pages

- [Workflow Release Low-Level Design](./workflow-release-low-level-design.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release OIDC Publish Topology Research](./workflow-release-oidc-publish-topology.md)
