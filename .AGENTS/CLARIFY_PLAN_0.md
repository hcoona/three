# CLARIFY_PLAN_0: Maintainer questions for RubyGems release support

<!-- markdownlint-disable MD029 -->

These items require human decisions or confirmation before implementation can be considered robust.

## 1) RubyGems.org publishing model (OIDC)

1. Decision: publish the prebuilt `.gem` artifacts to RubyGems.org (no rebuild)
    - Approach: use `rubygems/configure-rubygems-credentials` (OIDC) in the publish job, then run `gem push <downloaded .gem files>`.
    - Rationale: `rubygems/release-gem@v1` assumes Bundler release tasks (typically `bundle exec rake release`) and commonly rebuilds; this conflicts with the repo’s “build once, publish from artifact” model.
    - Required job permissions: `id-token: write` (OIDC). `contents: write` should not be required if we do not run `rake release` or push git tags in the publish job.

2. Decision: Trusted Publishing configuration
    - We will NOT configure a static RubyGems API token (`api-token`) and will NOT use `role-to-assume`.
    - We will use default `gem-server` and default `audience` values.
    - Implication: RubyGems Trusted Publishing must be configured on RubyGems.org for this repository/workflow/job (and optionally an environment constraint).

3. Decision: GitHub Actions environment name
    - Use `rubygems`.

4. Decision: publishing policy
    - Buddy releases: publish to GitHub Packages (RubyGems registry) only.
    - Official releases: publish to both RubyGems.org and GitHub Packages (RubyGems registry), aligning with Node official publishing to both npmjs.org and GitHub Packages.

## 2) Version policy for Ruby gems

5. Decision: Ruby version compatibility / normalization
    - We will add Ruby version adaptation support rather than requiring strict SemVer 2.0.0.
    - Normalization rules (at minimum):
      - Convert SemVer prerelease separator `-` to RubyGems-style `.` where needed.
      - Drop SemVer build metadata (`+...`) for RubyGems publishing.
    - Implication: the resolver/build pipeline must define a single canonical RubyGems version used for `gem build` verification and `gem push`, derived from the tag version.

6. Decision: tag format
    - Yes. Tags remain `release/<project>/v<version>`.

## 3) Project detection and naming

7. Decision: project name matching
    - Yes. The `project` name must match:
      - the release tag segment (`release/<project>/v<version>`),
      - the gemspec filename (`<project>.gemspec`), and
      - the gem name (as read from the built `.gem` specification).

8. Decision: ambiguity behavior
    - If multiple matches exist (e.g. multiple `<project>.gemspec` files, or multiple project kinds match the same `project`), the workflow must fail with an “ambiguous project” error.

## 4) GitHub Packages RubyGems registry auth

9. Decision: primary auth method
    - Yes. Use `${{ github.token }}` with `permissions: packages: write`.

10. Decision: fallback secret
    - Do not add a fallback secret. If `GITHUB_TOKEN` is rejected by GitHub Packages RubyGems in this repo/org, publishing should fail with a clear error.

## 5) Ruby toolchain and tests

11. Decision: Ruby version policy
    - Match the Node pattern: define `RUBY_VERSION` in the entry workflow `env:` (similar to `NODE_VERSION`), export it via the `versions` job, then pass it through to build/publish jobs via outputs.
    - Initial pinned version: `3.3` (even though `.mise.lock` currently pins `3.3.10`).

12. Decision: build-time Ruby checks (align with npm/Node)
    - Run lint: `bundle exec standardrb`.
    - Run tests: `bundle exec rspec`.
    - Notes:
      - `asciidoctor-latexmath` CI uses `bundle exec rspec` (not `rake spec/test`).
      - Checks should run in the Ruby build workflow/job (before producing/uploading `.gem` artifacts), similar to Node checks running in `release-build-node-pack.yml`.
      - Build should treat tests as required when a `Gemfile` is present; if there is no Bundler context, skip tests with a clear log message.

## 6) Release gating rules

13. Decision: official release gating (align with Node/npm)
    - GitHub Release creation is gated on successful publishing to both registries.
      - For Ruby: RubyGems.org AND GitHub Packages (RubyGems registry).
      - (This mirrors Node official where the GitHub Release is gated on the `publish-node` job that publishes to both npmjs.org and GitHub Packages.)

14. Decision: buddy release behavior (align with Node buddy)
    - Must not clobber an existing official (non-prerelease) GitHub Release for the same tag (use the existing guard pattern).
    - Publish only to GitHub Packages (RubyGems registry).
    - Create a prerelease GitHub Release.
