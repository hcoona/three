# Workflow Design Memory

For AI agents editing workflow design docs.

Do not reintroduce these old patterns:

- blanket `secrets: inherit` for publish flows
- OIDC `job_workflow_ref` anchored only to `official.yml`
- pre-checkout selection of `semver2` vs `pep440` validators
- silent filtering of unknown `release.json` targets
- unofficial Python registry targets unless they are explicitly designed
- buddy -> official as a required promotion chain
- GitHub stable releases that depend on buddy promotion by default
- reusable workflows declaring their own `permissions` blocks
- documentation-only `environment: production` protection
- reusable workflow contracts that omit caller `permissions`
- action pinning rules that exempt first-party `actions/*`

Current assumptions:

- before implementation starts, design reviews should ignore mismatches between the current repo implementation and the target design unless the task explicitly asks to reconcile implementation
- build and publish jobs default to `secrets: {}`
- all actions, including `actions/*`, are pinned to full commit SHA
- dependency-update automation must cover `.github/workflows/**` so pinned action SHAs are refreshed intentionally rather than drifting indefinitely
- OIDC trust matches the called reusable publish workflow
- official tag validation is two-phase: structure first, semantic validation after checkout
- official releases use a centralized control-plane model: tagged source, current protected workflow logic
- the official protected control-plane branch set is `main` plus eligible protected maintenance branches `release/<project-name>/v<series>`
- privileged official caller workflow, reusable workflows, and helper code come from the same protected control-plane branch set, not the tagged source commit; source-owned lint config such as `hk.pkl` follows the tagged source commit instead
- reusable workflow shell steps must map `inputs.*` through `env:` before use
- shell hardening also applies to `${{ github.* }}` and `${{ needs.*.outputs.* }}` in `run:` steps; they must be mapped through `env:` first
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- `release.json` has `schemaVersion: 1` and allows only `schemaVersion` plus `targets`
- unsupported future `schemaVersion` values are hard failures; schema upgrades are coordinated and do not need backward-compatibility shims before implementation starts
- official release identity tag format is `release/<project-name>/v<version>`
- buddy traceability tag format is `buddy/<project-name>/v<version>`
- buddy traceability tags should also be protected against direct manual creation/update outside the workflow path to avoid pre-seeding and traceability poisoning
- `project-name` is case-sensitive, must resolve to exactly one project, and must reject `..` and trailing `.` for ref safety
- project resolution must emit exactly one workflow language in `{csharp, python, jsts, ruby}`; no match, ambiguous match, or unsupported language is a hard failure
- each `buddy.yml` / `official.yml` run releases exactly one project
- buddy intentionally allows releases from development branches
- official releases must come from protected `main` or protected maintenance branches `release/<project-name>/v<series>`
- official jobs that need trusted helper scripts plus tagged payload input use a caller-ref workspace for helper code and a separate tagged-source working path
- maintenance branches are explicitly managed supported lines; missing non-default lines fail with operator guidance
- official ancestry derives `release/<project-name>/v<series>` from the version base release segment, ignoring prerelease/build/local suffixes
- official release tags under `refs/tags/release/**` must be protected
- buddy traceability tags under `refs/tags/buddy/**` are separate from the official release-identity namespace
- tag protection must cover both tag creation and tag updates; legacy protection that only blocks deletion or force-push is insufficient for `refs/tags/release/**` and `refs/tags/buddy/**`
- Python unofficial preview uses `github:release`
- Ruby uses the repository's `validate_rubygems_version.py` subset policy rather than generic RubyGems version compatibility
- stable GitHub Releases use `github:official`
- same-tag stable GitHub Release is idempotent, not a hard fail
- `official.yml` includes `preflight-check` for `environment: production` with required reviewers
- `official.yml` also runs `static-analysis` symmetrically with `buddy.yml`
- `preflight-check` requires `actions: read`, not `contents: read`, because it reads GitHub Environments metadata
- `preflight-check` must hard-fail on GitHub API errors outside explicitly handled cases
- `preflight-check` must specifically require a `required_reviewers` protection rule, `prevent_self_review = true`, and a deployment branch policy restricted to the official protected control-plane branch set
- buddy `force=true` is privileged by policy, but not yet separated by a workflow-enforced approval gate
- reusable workflows must not declare `permissions:` blocks
- build reusable workflows require caller `contents: read`
- build reusable workflows perform internal `fetch-depth: 0` checkout with `persist-credentials: false` and accept `checkout-ref` so official callers can force the tagged release payload
- buddy publish jobs must depend on `static-analysis` directly
- buddy publish jobs also gate explicitly on `resolve-context.result == 'success'` and `static-analysis.result == 'success'`
- `create-traceability-tag` must depend directly on `static-analysis` and all build jobs, not only publish jobs, so skipped publish jobs cannot hide upstream failures
- official publish jobs should gate explicitly on `resolve-tag.result == 'success'` and `static-analysis.result == 'success'`
- `.github/CODEOWNERS`, official, build, publish, `eng/scripts/**`, `mise.toml`, `mise.lock`, and other trusted control-plane helper files must be protected by `CODEOWNERS` review, and protected control-plane branches must require code-owner review
- `environment: production` deployment branch policy allows only the official protected control-plane branch set
- OIDC trusted publisher configuration uses the strongest claim set each registry supports; the authoritative branch restriction is GitHub `environment: production` deployment branch policy, while registry-side branch-ref and caller-workflow claims are defense in depth where supported
- no portable wildcard future-branch trust is assumed; renaming a protected control-plane branch, adding or retiring an allowed protected maintenance branch ref, or moving `_publish-*.yml` requires registry-side OIDC trust updates and a same-change update to the checked-in OIDC trust inventory (for example `.github/oidc-trust-inventory.json`)
- official `resolve-tag` performs an OIDC inventory preflight against the checked-in trust inventory before any publish job becomes eligible
- reusable publish docs must list required caller permissions
- idempotent publish handling only treats duplicate-version outcomes as success when remote artifact identity matches; auth and upstream failures stay hard-fail
- reusable publish workflows must emit whether the run performed a new publish or an idempotent no-op
- `_publish-github.yml` receives buddy-only `force` explicitly, declares `default: false`, and enforces GitHub Release overwrite/idempotency at publish time; official callers do not use `force`
- `_publish-github.yml` also receives `project-name` explicitly so it can create deterministic release titles `<project-name> v<version>`
- official GitHub Release idempotency also requires matching remote asset identity
- read-only checkouts in resolve/static-analysis jobs use `persist-credentials: false`
- every workflow job must declare `timeout-minutes`; omission is a lint failure enforced through `hk`/`actionlint`
- reusable workflow `permissions:` prohibition is also lint-enforced through a custom `hk` check because `actionlint` does not cover it directly
- official `resolve-tag` depends explicitly on `preflight-check`
- official static-analysis intentionally evaluates `hk.pkl` from the tagged source ref
- PEP 440 epoch markers (`!`) are intentionally unsupported in release tag versions
- PEP 440 release-line derivation zero-pads the normalized release segment to at least three numeric components before replacing the final numeric component with `x` (for example `1.1 -> v1.1.x`)
- `mise.lock` is committed alongside `mise.toml`; jobs key caches by both files and use lockfile-backed digest verification where supported by the selected MISE backend
- release target validation is language-aware: `csharp -> nuget/github`, `jsts -> npm/github`, `python -> pypi/github`, `ruby -> rubygems/github`
- official `resolve-tag` keeps caller-ref trusted helper code in one workspace and checks out the tagged payload into a separate working path
- official GitHub Releases use deterministic release titles `<project-name> v<version>` so overwrite guards can detect same-version identity conflicts across tags
- build artifacts include a manifest of published files and SHA-256 digests; publish workflows verify that manifest before upload
- build workflows must produce reproducible package outputs for the same source commit and locked toolchain so rerun idempotency remains valid
- GitHub Packages versions are treated as immutable within workflow execution even though GitHub supports delete/restore with elevated package-admin privileges; the workflow design does not request delete/admin permissions and does not support delete-and-republish recovery
- recovery guidance distinguishes fresh dispatch from GitHub's Re-run button and covers partial official publishes plus preflight failures
- recovery guidance tells operators to check the original run's artifacts in the GitHub Actions run UI or API before choosing rerun versus fresh dispatch
- recovery guidance also distinguishes pre-publish validation/build failures from partial publish failures
- recovery guidance explicitly states that `force=true` only covers buddy GitHub pre-release assets and buddy traceability tags, not GPR package versions
- recovery guidance includes OIDC trust drift after control-plane branch or workflow-path changes

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
