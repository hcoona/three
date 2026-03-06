This repository is a polyglot monorepo. I need you to design the best GitHub Actions workflow combination for the release and validation model described below.

The externally exposed entry workflows must remain exactly these 3 files:

1. `ci.yml`: triggered on pull requests, used for code-quality validation and test execution.
2. `buddy.yml`: triggered manually, used for unofficial releases.
3. `official.yml`: triggered manually with a formal release tag as input, used for official production releases.

Behind these 3 entry workflows, you should assume a shared execution layer made of reusable workflows. The split axis is ecosystem and packaging tool, not release channel. For example, NuGet-to-GPR and NuGet-to-NuGet.org should reuse the same publish workflow with different destination parameters.

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
6. Create or update a traceability tag.

`buddy.yml` is manually triggered. The operator specifies which project to release. The workflow must resolve project metadata from repository state rather than hard-coding per-project logic into the entry workflow.

Even within the same language, packaging and publishing strategy can differ by project. For example, a C# project may publish:

1. an `.exe` or `.msi` to GitHub Releases,
2. a `.nupkg` to GitHub Packages,
3. or both.

Because of this, publish targets must be read from per-project release metadata rather than inferred from language alone. The current design uses a strict `<project-root>/release.json` contract with `schemaVersion: 1` and a `targets` array using values like `nuget:gpr`, `nuget:official`, `github:release`, and `github:official`.

`buddy.yml` should:

1. accept `project-name` and `force` inputs,
2. validate `project-name` against a safe character set,
3. resolve `language`, `project-path`, and version using existing repository scripts,
4. require full git history where NBGV-derived versioning is needed,
5. validate `release.json` strictly before filtering targets,
6. filter to unofficial targets only after validation succeeds,
7. run project-scoped HK checks,
8. run exactly one ecosystem-specific build workflow through static conditional jobs,
9. run publish jobs as separate static jobs per ecosystem-destination pair,
10. use idempotent publish scripts for duplicate-version handling,
11. create the traceability tag `release/<project-name>/v<version>` after successful unofficial publication.

Because GitHub Actions resolves `uses:` statically, both build dispatch and publish dispatch should be modeled as multiple conditional jobs rather than a single dynamically selected reusable workflow call.

Buddy is allowed to release from development branches. It is not a promotion prerequisite for official release. It is an independent unofficial release channel.

`force=true` is a privileged overwrite path. The design should allow it only for unofficial overwrite scenarios such as re-pointing a traceability tag or replacing a pre-release, while still treating stable-release overwrite as a hard failure.

## `official.yml`

`official.yml` is similar to `buddy.yml`, but it is the production release channel.

In the current design, it is not triggered automatically by `push: tags:`. Instead, it is triggered manually with a `tag-name` input, and then it checks out `refs/tags/<tag-name>` to release the exact tagged commit.

The release identity tag format must be:

1. `release/<project-name>/v<version>`

This workflow should:

1. perform a preflight check that `environment: production` already exists and has required reviewers configured,
2. validate the structural shape of `tag-name` before checkout,
3. check out the exact tag ref with full history,
4. resolve `project-name`, `language`, `project-path`, and version after checkout,
5. run the correct version validator only after the ecosystem is known,
6. verify that the tagged commit is reachable from protected release sources such as `main` or the correct maintenance branch,
7. read and strictly validate `release.json`, then filter to official-only targets,
8. rebuild and retest from the tagged commit instead of reusing prior unofficial artifacts,
9. publish to official registries and/or stable GitHub Releases using dedicated publish jobs.

Official publishing differs from buddy publishing. For example, a NuGet package may go to NuGet.org instead of GitHub Packages, npm packages may go to npmjs, Python packages go to PyPI, and Ruby packages go to RubyGems.org.

`official.yml` and `buddy.yml` are independent channels rather than a sequential promotion pipeline. A buddy release is not required before an official release.

## Other clarifications

1. Known GitHub Actions behavior: pushes or tags created with the workflow `GITHUB_TOKEN` do not trigger new workflow runs.
2. The release-identity tag format must be `release/<project-name>/v<version>`.
3. HK is repository-wide rather than language-specific. It runs at repo scope and selects checks based on file types and configuration.
4. Reusable workflows must not declare their own `permissions:` blocks. Permission grants should stay in the entry workflows.
5. The default security posture should be `permissions: {}` at workflow level, job-level least-privilege escalation, and `secrets: {}` for build/test and publish reusable workflows unless a destination truly requires an explicit secret.
6. Official registry publishing should prefer OIDC Trusted Publishing, while GitHub Packages should use `GITHUB_TOKEN` with `packages: write`.
7. All shell steps must treat workflow inputs as untrusted: map them through `env:` first, then reference quoted shell variables.
8. All third-party actions must be pinned to full commit SHA.
9. Python has no unofficial package registry target in this design. If a Python project needs a buddy preview, it should use `github:release`.
10. Stable GitHub Releases are idempotent for the same release identity, but rebinding a stable release to a different tag or commit is forbidden.
