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

Current assumptions:

- build and publish jobs default to `secrets: {}`
- OIDC trust matches the called reusable publish workflow
- official tag validation is two-phase: structure first, semantic validation after checkout
- reusable workflow shell steps must map `inputs.*` through `env:` before use
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- `release.json` has `schemaVersion: 1` and allows only `schemaVersion` plus `targets`
- release identity tag format is `release/<project-name>/v<version>`
- official releases must come from protected `main` or protected maintenance branches `release/<project-name>/v<series>`
- official release tags under `refs/tags/release/**` must be protected
- Python unofficial preview uses `github:release`
- stable GitHub Releases use `github:official`
- same-tag stable GitHub Release is idempotent, not a hard fail
- `official.yml` includes `preflight-check` for `environment: production` with required reviewers
- buddy `force=true` is privileged by policy, but not yet separated by a workflow-enforced approval gate
- reusable workflows must not declare `permissions:` blocks
- build reusable workflows require caller `contents: read`
- reusable publish docs must list required caller permissions
- idempotent publish handling only treats duplicate-version outcomes as success; auth and upstream failures stay hard-fail

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
