# Review of `PLAN_1.md` (Release workflow refactor)

> This review evaluates `PLAN_1.md` against the decisions in `CLARIFY.md` and `CLARIFY_1.md`, and also cross-checks the plan against the current root workflows under `/.github/workflows/*.yml`.
>
> Repository policy reminder: all changes discussed here apply only to the root workflows under `/.github/workflows/*.yml`.

## Executive summary

`PLAN_1.md` is directionally aligned with the refactor goal (reduce duplication via reusable workflows and a stable `out/` artifact contract), but it conflicts with several **explicit** decisions made after the plan:

1. **`dist_dir` / `dist_glob` outputs must be removed** from the new reusable resolve workflow and callers must stop reading them.
2. **Buddy “non-clobber” must be enforced in `buddy.yml`** _before_ calling `release-create-github-release.yml`.
3. **npm `dist-tag` derivation must use NBGV metadata** (`PrereleaseVersionNoLeadingHyphen`), not SemVer parsing, and buddy + official must be identical.
4. **Buddy Node must pack first, then publish from the packed `.tgz`, and upload that same `.tgz`** as the GitHub Release asset.
5. **WXT artifact collection must be shallow** (`.output/*.zip` only), must not assume naming, and must fail on basename collisions when copying into `out/`.
6. **Build reusable workflows must not generate attestations**. GitHub attestation policy is entry-workflow-specific: `official.yml` does, `buddy.yml` does not.
7. **Node quality checks must run by default in both official and buddy flows**, with `--if-present` allowing projects to omit scripts.

These items require updating the plan before implementation.

## Compatibility check vs current workflows

### Current state (root workflows)

- `official.yml`
    - `resolve` currently emits `dist_dir` and `dist_glob` outputs and downstream jobs consume them.
    - npm `dist-tag` is derived by parsing the SemVer string (not NBGV metadata).
    - GitHub attestation is performed in the entry workflow.
    - WXT zip collection searches multiple depths and filters by `${PROJECT}.zip` / `${PROJECT}-*.zip`.
- `buddy.yml`
    - No guard exists to prevent modifying an existing non-prerelease release.
    - Node publish uses `pnpm publish` from the working tree, and only packs after publishing.
    - npm `dist-tag` is derived from SemVer parsing (different logic than official).
    - WXT zip collection mirrors official (nested search + naming filter).
- `release-create-github-release.yml`
    - Always uploads assets with `--clobber` and will edit an existing release.
    - Flat layout enforced: directories under `out/` cause failure.

This reality makes the clarifications (especially buddy non-clobber and Node pack/publish consistency) **necessary** and not optional.

## Findings vs `CLARIFY.md` / `CLARIFY_1.md`

### 1) Resolve outputs: remove `dist_dir` / `dist_glob`

- **Plan status:** `PLAN_1.md` still models resolve outputs as a “superset”, and build/publish steps use `dist_dir` / `dist_glob` in several places.
- **Clarified requirement:** `release-resolve.yml` must **not** emit `dist_dir` or `dist_glob`; callers must use `${{ github.workspace }}/out` and `${{ github.workspace }}/out/*` directly.
- **Required plan change:** explicitly remove these outputs from the contract, and include a migration step updating all root workflows to stop referencing them.

### 2) Buddy non-clobber guard placement

- **Plan status:** `PLAN_1.md` states the rule but does not mandate the exact guard placement.
- **Clarified requirement:** implement guard in **`buddy.yml` before calling** `release-create-github-release.yml`.
- **Required plan change:** specify a dedicated pre-release-check step/job in `buddy.yml`:
    - if tag exists and a release exists and `prerelease=false` → **fail fast**
    - if release doesn’t exist → allow
    - if release exists and `prerelease=true` → allow
    - same-SHA is **not** an exception

### 3) Node dist-tag derivation via NBGV metadata

- **Plan status:** `PLAN_1.md` mentions “use NBGV metadata” but does not pin down the exact canonical variable or mapping rules.
- **Clarified requirement:** use `dotnet tool run nbgv get-version -v PrereleaseVersionNoLeadingHyphen -p <PACKAGE_DIR>`.
    - empty → `latest`
    - non-empty → `channel = first segment before '.'`
    - invalid/empty channel → fail fast with guidance to fix `version.json`
- **Required plan change:** remove all SemVer-string parsing for dist-tag from both flows and document the shared function/rules.

### 4) Buddy Node “pack first, publish from `.tgz`, upload same `.tgz`”

- **Plan status:** `PLAN_1.md` treats buddy and official Node flows as “may remain intentionally different”, and the proposed build/publish split does not ensure the buddy order-of-operations.
- **Clarified requirement:** buddy must:
    1. apply GPR scope/name adjustment first
    2. `npm pack` exactly once to `${GITHUB_WORKSPACE}/out/`
    3. publish **from that `.tgz` file**
    4. upload **the same `.tgz`** as release asset
    5. restore temporary scope/name changes after publishing
- **Required plan change:** either:
    - keep the buddy Node pack+publish as a single atomic sequence (even if extracted into a reusable workflow), or
    - if splitting build/publish, ensure the ordering and “restore after publish” requirement are still satisfied (this is tricky across jobs).

### 5) WXT artifacts: `.output` shallow copy + collision safety

- **Plan status:** `PLAN_1.md` says “collect zips” but does not specify:
    - shallow-only (`.output/*.zip`)
    - no zip-name assumptions
    - collision handling in a flat `out/`
- **Clarified requirement:**
    - assume WXT output is `.output` (no config discovery)
    - only copy `.output/*.zip` (no recursion)
    - copy **all** zips (no naming filter)
    - fail if a copy would overwrite a file in `out/`
- **Required plan change:** incorporate these constraints into the WXT build design and acceptance checks.

### 6) Attestation policy vs build/publish split

- **Plan status:** reusable build workflows accept an `attest` boolean and run `actions/attest-build-provenance`.
- **Clarified requirement:** build workflows produce artifacts only; they must not generate attestations.
    - `buddy.yml`: no GitHub attestations for release assets
    - `official.yml`: do generate GitHub attestations for release assets (`out/*`)
- **Required plan change:** remove the `attest` option from reusable build workflows. If attestation remains desired:
    - implement it in `official.yml` (entry workflow) as a separate step/job over `${{ github.workspace }}/out/*` (or over downloaded artifacts), and do not add any in buddy.

### 7) Quality checks default policy (Node pack)

- **Plan status:** `PLAN_1.md` introduces `run_quality_checks` as a parameter but doesn’t enforce default behavior.
- **Clarified requirement:** both `official.yml` and `buddy.yml` Node pack builds must run quality checks by default using `pnpm --if-present`.
- **Required plan change:** treat quality checks as non-optional default behavior; only allow explicit opt-out if there is a strong reason (none stated).

## Suggested edits to `PLAN_1.md` (delta)

The following are the minimum changes needed to make `PLAN_1.md` implementable under `CLARIFY.md` + `CLARIFY_1.md`:

1. **Update Non-goals**
    - Remove/adjust “Converging official and buddy Node publish flows” non-goal.
    - It is now a requirement that dist-tag and pack/publish semantics align.

2. **Update `release-resolve.yml` contract**
    - Remove `dist_dir` / `dist_glob` outputs.
    - Add explicit guidance: callers use `${{ github.workspace }}/out` and `${{ github.workspace }}/out/*`.

3. **Add a Buddy preflight guard section**
    - Must run before `release-create-github-release.yml`.
    - Must implement the exact allow/deny matrix from `CLARIFY_1.md`.

4. **Replace dist-tag derivation algorithm**
    - Document the canonical NBGV variable and mapping.
    - Remove SemVer parsing from both official and buddy.
    - Add fast-fail requirements and error message guidance.

5. **Tighten WXT artifact collection rules**
    - Only `.output/*.zip`.
    - Copy all zips.
    - Fail on basename collisions into `out/`.

6. **Remove attestation from build workflows**
    - Ensure “build workflows” do only artifact production + artifact upload.
    - Place official GitHub asset attestation in `official.yml` only.

7. **Quality checks default**
    - Ensure both entry workflows run quality checks by default for Node (non-WXT) packaging.

## Updated acceptance criteria (recommended)

In addition to `PLAN_1.md`’s current acceptance criteria, add these checks:

1. **No workflow consumes `dist_dir` / `dist_glob` outputs**.
2. **Buddy non-clobber enforced:**
    - existing release + `prerelease=false` → buddy fails before any modifying operation.
3. **Dist-tag derived via NBGV metadata** in both flows, identical rules.
4. **Buddy Node uses same `.tgz` for publish + GitHub Release asset**.
5. **WXT zip collection:**
    - only `.output/*.zip`
    - no naming assumptions
    - collision-safe copy into `out/`
6. **Attestation:**
    - official produces GitHub attestations for `out/*`
    - buddy does not

## Overall verdict

- **Approved with required changes**: the architecture is workable, but `PLAN_1.md` must be updated to match the explicit decisions in `CLARIFY.md` and `CLARIFY_1.md` before implementation.
- **Highest-risk areas if unaddressed:** buddy non-clobber guard, buddy Node pack/publish ordering, and dist-tag derivation drift.
