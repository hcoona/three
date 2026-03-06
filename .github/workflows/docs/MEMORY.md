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
- unprivileged `force=true`
- reusable workflow contracts that omit caller `permissions`

Current assumptions:

- build and publish jobs default to `secrets: {}`
- OIDC trust matches the called reusable publish workflow
- official tag validation is two-phase: structure first, semantic validation after checkout
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- release identity tag format is `release/<project-name>/v<version>`
- Python unofficial preview uses `github:release`
- stable GitHub Releases use `github:official`
- same-tag stable GitHub Release is idempotent, not a hard fail
- `official.yml` includes `preflight-check` for `environment: production`
- `force=true` requires protected approval
- reusable workflows must not declare `permissions:` blocks
- reusable publish docs must list required caller permissions

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
