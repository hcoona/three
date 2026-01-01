# Review: Release Workflows Refactor Plan (official.yml vs buddy.yml)

This review evaluates the refactor plan in `PLAN.md` whose goal is to reduce duplication between `.github/workflows/official.yml` and `.github/workflows/buddy.yml` while preserving current release behavior.

## Overall assessment

The plan is directionally solid: extracting shared logic into reusable workflows (`workflow_call`) is a good fit for the current structure (you already use reusable workflows for release notes and GitHub Release creation). The phased migration strategy is also appropriate because it limits blast radius.

However, there are a few **high-risk integration points** that the plan should call out explicitly, especially around **trusted publishing (OIDC) validation semantics**, **GitHub Environments**, and **configuration inheritance** (e.g., tool versions).

If those are addressed up front, the refactor should be low-risk and will materially reduce maintenance cost.

## What the current workflows actually do (key facts)

Based on the current `official.yml` and `buddy.yml`:

- Both have a nearly identical `resolve` job that:
    - resolves target commit and detaches to it
    - detects project kind (Python vs Node) and package directory
    - validates version format (PEP 440 for Python; SemVer2 for Node)
    - detects WXT
    - locates `CHANGELOG.md`
- `official.yml` supports **tag push** and **manual** triggers; `buddy.yml` is **manual** only.
- `official.yml` emits additional resolve outputs (`force_update_tag`, `dist_dir`, `dist_glob`) that downstream jobs use for provenance attestation.
- Python:
    - Official: builds, verifies, attests, publishes to PyPI, uploads dist.
    - Buddy: builds, verifies, uploads dist only.
- Node non-WXT:
    - Official: publishes to both GPR and npmjs.org, uses OIDC (no npm token), includes safety checks and provenance attestation.
    - Buddy: publishes to GPR only via a simpler flow, then packs a `.tgz` for GitHub Release.
- WXT:
    - Build steps are effectively identical, except official includes provenance attestation and requires `id-token` / `attestations` permissions.
- GitHub Release creation is already centralized in `release-create-github-release.yml`.

## Review of the proposed architecture

### 1) Keep two thin entry workflows

Good. Keeping `official.yml` and `buddy.yml` as entrypoints preserves the UX and existing triggers.

Recommendation:

- Keep **concurrency**, **top-level `permissions`**, and **trigger definitions** in the entry workflows.
- Treat reusable workflows as “implementation details” that do not redefine global policy.

### 2) Reusable `release-resolve.yml`

Good and very likely safe.

Key requirements to preserve behavior:

- Support both “source=tag” and “source=manual” modes.
- Preserve current version normalization rules:
    - Python may accept leading `v`/`V` on manual input and then strip it.
    - Node must be strict SemVer2 with **no leading v**.
- Preserve the “detach to target commit” behavior prior to running helper scripts.

Important improvement for the plan:

- **Tool version configuration must be addressed.** Once logic moves to reusable workflows, workflow-level `env:` values from the entry workflow are not guaranteed to behave like “global constants” for the called workflow.
    - Today, `PYTHON_VERSION`, `NODE_VERSION`, and `PNPM_VERSION` are defined in both entry workflows.
    - After extraction, those values either need to be:
        - defined inside each reusable workflow, or
        - passed as explicit inputs (preferred for single-source-of-truth), or
        - centralized in a single reusable constants workflow (not recommended).

Practical recommendation:

- Add inputs to reusable workflows for `python_version`, `node_version`, `pnpm_version`, and default them to current values.

Output contract recommendation:

- Emit a **superset** of outputs used by both channels:
    - `project`, `version`, `project_kind`, `is_wxt`
    - `tag_name`, `target`, `package_dir`
    - `has_changelog`, `changelog`
    - `release_title`, `run_url`
    - **also** `force_update_tag` (normalized), `dist_dir`, `dist_glob`

### 3) Reusable artifact workflows (Python / Node / WXT)

This is the right direction, but there are a few caveats.

#### Python (`release-python.yml`)

Conceptually fine.

Caveat: **GitHub Environments**

- `official.yml` sets `environment: pypi` at the job level today.
- Buddy does not use an environment.

In a called workflow, you must decide whether:

- the called workflow always declares `environment`, or
- the called workflow declares it conditionally, or
- you keep the environment at the entry workflow level.

Recommendation:

- Either (A) keep Python publishing as a job in `official.yml` (and only extract build steps), **or**
- (B) split into two workflows `release-python-official.yml` and `release-python-buddy.yml` to avoid complicated conditionals.

#### WXT (`release-wxt.yml`)

This extraction should be very safe because the build logic is already identical.

Recommendation:

- Make `attest` an input and gate the attestation step.
- Document required caller permissions:
    - official caller must grant `id-token: write` and `attestations: write`.
    - buddy caller should omit them.

#### Node non-WXT

The plan’s suggestion to start with two workflows (official vs buddy) is correct: the publish flows genuinely differ.

**High-risk item**: npm Trusted Publishing (OIDC) and workflow filename validation.

Your current `official.yml` includes messaging that the npm Trusted Publisher is configured against `official.yml`.

According to npm’s trusted publishing docs, configuration is tied to a **workflow filename**, and npm notes limitations when `workflow_call` is involved (validation may check the calling workflow’s name and mismatches can occur).

This means the refactor can accidentally break production publishing if:

- the workflow filename that npm validates changes, or
- multi-level `workflow_call` makes the “calling workflow” something other than `official.yml`.

Recommendation:

- Treat Node official publishing as a special case:
    - **Avoid an extra orchestration layer** (Phase 5) for the job that runs `npm publish`.
    - Prefer keeping the publish job in `official.yml` and extracting shared shell logic into a local composite action, OR ensure you validate (in a test package / test repo) that npm OIDC works when publishing occurs inside a called reusable workflow.
- If you do move the publish job into `release-node-official.yml`, update:
    - npm trusted publisher configuration (if needed)
    - the “Note about npm Trusted Publishers” message (it currently hardcodes `official.yml`).

## Review of the phased migration plan

### Phase 1 — Extract resolve

Approved, with the additions above (tool version inputs + superset outputs).

Also recommended:

- Add a “contract test” mindset: ensure output equivalence between old and new resolve for a few representative inputs.

### Phase 2 — Extract WXT build

Approved and low risk.

### Phase 3 — Extract Python build/publish

Approved, but decide early how to represent `environment: pypi`.

### Phase 4 — Node non-WXT workflows

Approved, but treat official npmjs publishing/OIDC as high-risk and validate carefully.

### Phase 5 — Optional orchestrator

This is nice-to-have, not necessary for most of the duplication removal.

Strong recommendation:

- If npm Trusted Publishing is in use, **do not introduce a multi-level call chain** for the job that performs `npm publish`, unless you have verified how npm validates the workflow identity in your exact setup.

A compromise that keeps the “thin wrapper” goal:

- Use an orchestrator only for:
    - resolve
    - prepare release notes
    - create GitHub Release
- Keep publish jobs in entry workflows (or call artifact workflows directly from entry workflows) to avoid “who is the caller” ambiguity.

## Additional suggestions (nice-to-have)

- Consider extracting shared _steps_ into **composite actions** for cases where:
    - you must keep a specific workflow filename for external identity (npm trusted publisher), or
    - you need to keep environments/permissions very explicit.
- Add a short “Outputs and invariants” section to `PLAN.md` documenting what must not change (tag format, version validation, artifact naming, dist upload layout).

## Conclusion

Proceed with the plan, but update it to explicitly address:

1. how tool versions are passed/centralized across reusable workflows,
2. GitHub Environment handling (especially Python/pypi and Node/npmjs),
3. npm Trusted Publishing workflow filename / caller identity risks (especially if introducing an orchestrator).

With those clarified, Phase 1 and Phase 2 should be straightforward and deliver immediate reduction in duplication.
