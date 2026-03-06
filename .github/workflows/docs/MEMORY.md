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
- OIDC trust matches the called reusable publish workflow
- official tag validation is two-phase: structure first, semantic validation after checkout
- official releases use a centralized control-plane model: tagged source, current protected workflow logic
- privileged official publish logic and helper code come from the protected control branch, not the tagged source commit
- reusable workflow shell steps must map `inputs.*` through `env:` before use
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- `release.json` has `schemaVersion: 1` and allows only `schemaVersion` plus `targets`
- release identity tag format is `release/<project-name>/v<version>`
- each `buddy.yml` / `official.yml` run releases exactly one project
- buddy intentionally allows releases from development branches
- official releases must come from protected `main` or protected maintenance branches `release/<project-name>/v<series>`
- maintenance branches are explicitly managed supported lines; missing non-default lines fail with operator guidance
- official ancestry derives `release/<project-name>/v<series>` from the version base release segment, ignoring prerelease/build/local suffixes
- official release tags under `refs/tags/release/**` must be protected
- Python unofficial preview uses `github:release`
- Ruby uses the repository's `validate_rubygems_version.py` subset policy rather than generic RubyGems version compatibility
- stable GitHub Releases use `github:official`
- same-tag stable GitHub Release is idempotent, not a hard fail
- `official.yml` includes `preflight-check` for `environment: production` with required reviewers
- `official.yml` also runs `static-analysis` symmetrically with `buddy.yml`
- `preflight-check` must hard-fail on GitHub API errors outside explicitly handled cases
- `preflight-check` must specifically require a `required_reviewers` protection rule, not just any environment protection rule
- buddy `force=true` is privileged by policy, but not yet separated by a workflow-enforced approval gate
- reusable workflows must not declare `permissions:` blocks
- build reusable workflows require caller `contents: read`
- build reusable workflows perform internal `fetch-depth: 0` checkout
- buddy publish jobs must depend on `static-analysis` directly
- `create-traceability-tag` must depend directly on `static-analysis` and all build jobs, not only publish jobs, so skipped publish jobs cannot hide upstream failures
- official publish jobs should gate explicitly on `resolve-tag.result == 'success'` and `static-analysis.result == 'success'`
- official, build, publish, and trusted control-plane helper files must be protected by `CODEOWNERS` review
- renaming the trusted control branch or moving `_publish-*.yml` requires registry-side OIDC trust updates
- reusable publish docs must list required caller permissions
- idempotent publish handling only treats duplicate-version outcomes as success when remote artifact identity matches; auth and upstream failures stay hard-fail
- reusable publish workflows must emit whether the run performed a new publish or an idempotent no-op

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
