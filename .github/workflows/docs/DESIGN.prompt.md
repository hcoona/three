# Historical Release Workflow Design Prompt

> Historical/superseded: this file captures an older design prompt and is
> not the active workflow topology. Current release workflow docs use
> `src/**/three.release.yml`, `eng/release/target-instances.yml`, slash-style
> target references such as `github-release/public`, and the
> `release-orchestrate.yml` split topology. NuGet registry targets, direct
> official publishing, `release.json`, `production-<project-name>` approval, and
> colon-style target keys in this prompt are historical unless reintroduced by a
> later reviewed design.

This repository is a polyglot monorepo. I need you to design the best GitHub Actions workflow combination for the release and validation model described below.

Before any implementation-oriented design detail: active projects now follow the canonical monorepo roots under `src/`, `src/lab/`, and `tests/`. The former `OneDotNet/` subtree has been migrated into those canonical roots. Release pipelines are still not set up, so the design must preserve release-pipeline implementation prerequisites without treating canonical-root migration as incomplete.

The externally exposed release and release-authority validation entry workflows must remain exactly these 3 files:

1. `ci.yml`: triggered on pull requests, used for code-quality validation and test execution.
2. `buddy.yml`: triggered manually, used for unofficial releases.
3. `official.yml`: triggered manually, used for official production releases.

Do **not** add extra triggered top-level workflows for readiness checks, health monitors, governance, drift detection, or similar control-plane tasks. If a capability is still needed, keep it inside one of the three entry workflows or make it checked-in repository state. `.github/workflows/codeql.yml` is allowed as a triggered top-level non-release security analysis workflow only when it has no release authority, cannot call release mutation workers, and cannot mint publish credentials or protected-ref bypass credentials. A scheduled, manually dispatched, or carefully dashboard-edit-triggered Renovate dependency-maintenance workflow is allowed only when it has no release authority, uses least-privilege permissions, cannot call release mutation workers, and cannot mint publish credentials or protected-ref bypass credentials.

Behind these 3 entry workflows, you should assume a shared execution layer made of reusable build/test workflows plus reviewed local composite actions and scripts. The split axis for build/test reuse is ecosystem and packaging tool, not release channel. Security-sensitive publication may remain direct in the entry workflows instead of going through same-repository reusable publish workflows.

This design is intentionally for a monorepo where each releasable project belongs to exactly one language ecosystem, and each `buddy.yml` or `official.yml` run releases exactly one project.

## `ci.yml`

Main responsibilities:

1. Run static analysis.
2. Compile when required by the language ecosystem.
3. Run unit tests.
4. Package when required by the ecosystem.

Because this is a polyglot monorepo, CI should avoid building everything on every PR. It should first detect what changed, then decide which language-specific test/build workflows to activate.

For shift-left validation, static analysis should be driven by HK (`jdx/hk`). HK is repository-wide rather than project-specific, and it selects the appropriate checks based on file types and repository configuration.

For test execution, there is no single cross-language test driver. The workflow should therefore:

1. detect which language ecosystems are affected,
2. treat infrastructure changes as a reason to run all language suites,
3. invoke the matching ecosystem-specific reusable workflows in parallel,
4. finish with a final gate job that can be used as the required branch-protection status check even when some language jobs are legitimately skipped.

The current design assumes reusable build-test workflows such as:

1. `_build-test-csharp.yml` on `windows-latest`
2. `_build-test-python.yml` on `ubuntu-latest`
3. `_build-test-jsts.yml` on `ubuntu-latest`
4. `_build-test-ruby.yml` on `ubuntu-latest`

## `buddy.yml`

Main responsibilities:

1. Run static analysis.
2. Compile when required.
3. Run unit tests.
4. Package when required.
5. Publish to unofficial destinations.

`buddy.yml` is manually triggered. The operator specifies which project to release. The workflow must resolve project metadata from repository state rather than hard-coding per-project logic into the entry workflow.

Even within the same language, packaging and publishing strategy can differ by project. For example, a C# project may publish:

1. an `.exe` or `.msi` to GitHub Releases,
2. a `.nupkg` to GitHub Packages,
3. or both.

Because of this, publish targets must be read from per-project release metadata rather than inferred from language alone. Use a strict `<project-root>/release.json` contract with `schemaVersion: 1` and a `targets` array using values like `nuget:gpr`, `nuget:official`, `npm:gpr`, `npm:official`, `pypi:official`, `rubygems:gpr`, `rubygems:official`, and `github:release`.

`buddy.yml` should:

1. accept `project-name` as input,
2. validate `project-name` against a safe character set,
3. resolve `language`, `project-path`, and version from checked-in project metadata and ecosystem identity at the selected ref,
4. require full git history where NBGV-derived versioning is needed,
5. validate `release.json` strictly before filtering targets,
6. filter to unofficial targets only after validation succeeds,
7. run project-scoped HK checks plus the shared buddy release control-plane files, including the reusable build workflows it may invoke,
8. run exactly one ecosystem-specific build workflow through static conditional jobs,
9. publish from direct jobs rather than relying on same-repository reusable publish workflows as the real authorization boundary,
10. use idempotent publish scripts for duplicate-version handling.

Because GitHub Actions resolves `uses:` statically, build dispatch should be modeled as multiple conditional jobs rather than a single dynamically selected reusable workflow call.

Buddy is allowed to release from development branches. It is not a promotion prerequisite for official release. It is an independent unofficial release channel.

Python has no unofficial package-registry target in this design. If a Python project needs a buddy preview, it should use `github:release`. `pypi:testpypi` is not a supported target.

## `official.yml`

`official.yml` is similar to `buddy.yml`, but it is the production release channel.

It is triggered manually with `project-name` as input. The branch selected in the `workflow_dispatch` UI is the single official trust root for that run: it supplies the trusted control-plane code, the checked-in release policy inputs, and the release payload source. The workflow must freeze the selected protected branch to an immutable source commit before downstream work begins. It resolves the version from that frozen commit, derives the official release tag `release/<project-name>/v<version>` internally, and creates that protected release tag itself before official publish jobs run.

The release identity tag format must be:

1. `release/<project-name>/v<version>`

This workflow should:

1. validate `project-name` against a safe character set,
2. check out the dispatch-selected protected source ref with full history, resolve and freeze its immutable source commit SHA, and use that frozen SHA everywhere downstream,
3. resolve `project-name`, `language`, `project-path`, and version from that frozen source commit,
4. run the correct version validator only after the ecosystem is known,
5. verify that the selected protected branch itself matches the resolved release line, using `main` for the current mainline release line and `release/<project-name>/v<release-line>` for supported maintenance lines,
6. read and strictly validate `release.json`, then filter to official-only targets,
7. perform all project canonicalization, existence, uniqueness, target-compatibility, and baseline-environment safety checks in a no-environment preflight job before entering any environment with secrets,
8. run official static analysis of both the project and the official release control-plane before entering any environment,
9. use an environment-backed audit/admission job that consumes validated preflight outputs rather than re-validating after environment entry,
10. use `production-<project-name>` as the authoritative human approval gate, and require that environment to be pre-created and protected,
11. subordinate any target-specific environment mechanics to that baseline gate rather than replacing it, and keep the baseline environment free of unnecessary publication credentials,
12. rebuild and retest from the frozen source commit instead of reusing prior unofficial artifacts,
13. derive and create the protected official release tag `release/<project-name>/v<version>` inside the workflow from the frozen source commit,
14. publish to official registries and/or GitHub Releases using direct jobs that consume only the frozen source commit.

Official publishing differs from buddy publishing. For example, a NuGet package may go to NuGet.org instead of GitHub Packages, npm packages may go to npmjs, Python packages go to PyPI, and Ruby packages go to RubyGems.org.

`official.yml` and `buddy.yml` are independent channels rather than a sequential promotion pipeline. A buddy release is not required before an official release.

Official static analysis must cover not only the project path but also the same kind of control-plane files buddy checks: official entry workflow, official reusable workflows, composite actions, helper scripts, root locks, and root config files used by the release path.

Official admission must not depend on unbounded historical workflow-run scanning or on freshness of extra scheduled workflows. Use bounded checked-in or otherwise explicitly materialized admission state instead.

Release runs for the same project must serialize across both `buddy.yml` and `official.yml` rather than racing each other. Tag creation and remote publish steps must have explicit idempotency, rerun, and partial-recovery rules, with the frozen release identity defined by `project-name`, resolved version, and frozen source commit SHA.

## Other clarifications

1. Known GitHub Actions behavior: pushes or tags created with the workflow `GITHUB_TOKEN` do not trigger new workflow runs.
2. The release-identity tag format must be `release/<project-name>/v<version>`.
3. HK is repository-wide rather than language-specific. It runs at repo scope and selects checks based on file types and configuration.
4. Reusable workflows must not declare their own `permissions:` blocks. Permission grants should stay in the entry workflows.
5. The default security posture should be `permissions: {}` at workflow level, job-level least-privilege escalation, and `secrets: {}` for build/test reusable workflows unless a destination truly requires an explicit secret.
6. Official registry publishing should prefer OIDC Trusted Publishing when supported, while GitHub Packages should use `GITHUB_TOKEN` with `packages: write`.
7. All shell steps must treat workflow inputs as untrusted: map them through `env:` first, then reference quoted shell variables.
8. All third-party actions must be pinned to full commit SHA.
9. `github:release` is the GitHub Release target. Buddy may use it for preview/prerelease publication, and official may use it for the protected production release identity.
