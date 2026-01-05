# PLAN_REVIEW_0: Strict review of PLAN_0 (RubyGems release support)

This review evaluates `PLAN_0.md` against the repository’s existing release architecture (`.github/workflows/official.yml`, `buddy.yml`, `release-resolve.yml`) and the stated acceptance criteria.

## Executive summary

The plan is directionally aligned with the current Python/Node release system (resolve → build artifacts → publish → attest → GitHub Release). However, **as written it has several high-risk gaps** that will likely cause failed releases or inconsistent behavior across projects.

**Primary blockers / highest risk areas:**

1. **RubyGems.org publishing approach is likely incompatible with the “build once, publish artifacts” model.** The proposed `rubygems/release-gem@v1` typically assumes a Bundler + `rake release` workflow and may **rebuild** rather than publishing the already-built `.gem` artifact. This can create drift between the uploaded artifact and what is published.
2. **Version policy mismatch risk:** enforcing strict SemVer 2.0.0 (`MAJOR.MINOR.PATCH`) for Ruby may reject many real-world gem versions (e.g. `1.0`, `1.0.pre`, `2026.01.05`). If the repo intends SemVer-only gems, that’s fine—but it needs explicit confirmation.
3. **Project-kind ambiguity is not handled:** if both a `pyproject.toml` and `<project>.gemspec` exist for the same `project` name, the resolver would silently pick one based on detection order, which is a correctness and safety risk.

With targeted adjustments (mostly around publish mechanics, validation policy, and ambiguity handling), the plan can be made robust and consistent with the existing workflows.

## Alignment with current repo patterns

The existing release workflows have these strong conventions:

- `release-resolve.yml` resolves **(project, version, target commit, package_dir, project_kind)** and validates version.
- Build jobs produce artifacts under `out/*`.
- Publish jobs generally publish from the artifact (`out/*`) and do not rebuild.
- Release jobs always attach the artifact files to a GitHub Release.
- Attestations are performed over the artifact files with `actions/attest-build-provenance@v3`.
- Inputs are validated for shell safety.

The Ruby addition should follow the same shape to minimize surprises.

## Major issues (must address)

### 1) RubyGems Trusted Publishing: publishing likely rebuilds, diverging from artifacts

**Problem:** The plan proposes `rubygems/release-gem@v1` for RubyGems.org OIDC publishing. In common usage, this action runs Bundler release tasks (often `bundle exec rake release`) which can:

- rebuild the gem,
- generate a different `.gem` from what was built in `build-ruby`, and
- potentially tag/push (depending on project configuration).

This conflicts with the current repo’s model (build once, then publish those exact outputs).

**Recommendation:** Prefer an approach that publishes the **downloaded `.gem` artifact** to RubyGems.org using OIDC, without rebuilding.

- If RubyGems Trusted Publishing supports a “push these `.gem` files” flow (via an action or official tool), use that.
- If the ecosystem only supports OIDC through `release-gem`/`rake release`, then the plan must explicitly acknowledge that:
    - the publish job is the source of truth for the published gem,
    - the build artifact should be produced by the same mechanism (or the build job should be removed), or
    - the publish job should reuse the previously-built `.gem` (if supported).

**Acceptance criteria impact:** “Build the gem from a target commit and upload the built `.gem` file(s) as a workflow artifact” and “Create a GitHub Release with the built assets attached” strongly implies the artifact is authoritative.

### 2) Version validation for Ruby is likely too strict

Current `validate_semver2_version.py` is **strict SemVer 2.0.0**:

- requires `MAJOR.MINOR.PATCH`
- disallows leading `v`
- allows `-prerelease` and `+build`

RubyGems uses `Gem::Version` rules, which are _not identical_ to SemVer. Many gems publish versions that are valid in Ruby but invalid SemVer2.

**Recommendation:** Decide explicitly:

- **Option A (SemVer-only policy):** keep SemVer2 and document that all gems in this repo must use SemVer2.0.0.
- **Option B (RubyGems-native versions):** add a Ruby-specific validator (or use `ruby -e 'Gem::Version.new(ARGV[0])'`) and enforce RubyGems semantics.

If you keep SemVer2, add a preflight check in `release-build-ruby.yml` verifying the gem’s own version (from gemspec/specification) exactly equals the resolved version.

### 3) Resolver ambiguity and safety

`release-resolve.yml` currently tries Python, then Node, and errors if neither is found. Extending this to Ruby must avoid unsafe “first match wins” behavior.

**Concrete ambiguity cases:**

- A repo may contain `foo/pyproject.toml` with `project.name = "foo"` and also `bar/foo.gemspec` for a different project that coincidentally shares the same name.
- Over time, monorepos can accumulate samples/tests containing `*.gemspec`.

**Recommendation:**

- Make `find_ruby_project_path.py` return an error if multiple matches exist (same as “ambiguous matches”), not just pick shortest.
- Consider updating `find_python_project_path.py` similarly (it currently picks shortest match) _or_ handle ambiguity centrally in `release-resolve.yml` by:
    - attempting all detectors,
    - collecting all successful results, and
    - failing if more than one kind matches the same `project`.

This reduces the risk of publishing the wrong thing.

### 4) RubyGems registry publish auth: GITHUB_TOKEN may not be sufficient

The plan intends to push to GitHub Packages RubyGems registry using `${{ github.token }}` and `permissions: packages: write`. This is consistent with how Node publishing uses `github.token`.

However, GitHub Packages for RubyGems has historically required a **classic PAT** in some configurations.

**Recommendation:**

- Keep the `github.token` path as first attempt (best developer experience).
- Add a well-defined fallback:
    - a secret such as `GITHUB_PACKAGES_TOKEN` (classic PAT) if `github.token` fails,
    - and detect failure with a clear error message.

### 5) Official release dependencies: publish ordering and failure semantics

The plan suggests `build-ruby` → (publish-gpr in parallel) → publish-rubygems → attest → release.

This is sensible, but the current official workflow pattern is:

- for Node: publish to both registries in one job,
- for Python: publish to PyPI first, then release, with attestation as a gate.

**Recommendation:** Define a clear policy:

- Is GitHub Release creation gated on **both** RubyGems + GPR publish success?
- Or is GPR optional (best-effort) while RubyGems is required?

Right now the plan says “optionally publish to GPR” for official. This must be expressed in workflow job dependencies, otherwise it will either:

- block releases unnecessarily, or
- silently skip GPR without visibility.

## Medium issues (should address)

### 6) Ruby build workflow needs better “version-of-truth” checks

The plan proposes verifying:

- `gem specification <file>.gem version` equals `inputs.version`
- `gem specification <file>.gem name` equals `inputs.project`

This is good and consistent with Python’s `verify_python_artifact_version.py`.

Recommended additions:

- Ensure the built gem filename is stable and matches expectations (e.g. `${project}-${version}.gem`), but don’t hard-require if some gems include platform suffixes.
- If multiple `.gem` files appear, log them and verify each name/version pair.

### 7) Bundler caching and optional tests may be too optimistic

Not all gems have:

- a `Gemfile`,
- `bundle exec rake release`,
- `rspec`,
- `rake test` tasks.

**Recommendation:** In `release-build-ruby.yml`, treat tests as optional but make the behavior deterministic and explicit:

- If `Gemfile` exists → run `bundle install` (bundler-cache) and then attempt `bundle exec rake test/spec` only if tasks exist.
- If no `Gemfile` exists → skip bundler and tests (log “no Gemfile; skipping bundle/test”).

This avoids “best-effort” logic that still fails due to missing bundler context.

### 8) Owner casing and registry URLs

Node workflows deliberately lowercase the owner for npm scopes. For RubyGems GitHub Packages host (`https://rubygems.pkg.github.com/<OWNER>`), casing expectations are less clear.

**Recommendation:** normalize owner consistently (likely `${{ github.repository_owner }}` as provided) and document it. If lowercasing is required, do it explicitly.

### 9) Artifact layout expectations for GitHub Release

`release-create-github-release.yml` requires a **flat file layout** under `out/` (no directories). A Ruby build should therefore place only `.gem` files in `out/`.

The plan already aligns with this; make sure no intermediate folders are uploaded.

## Minor issues / nits

- `find_python_project_path.py` does not ignore `.git` / large dirs, while Node script does. The Ruby script proposal mentions ignoring; consider harmonizing.
- Pinning `rubygems/release-gem@v1` to a commit SHA is good; do the same for any other third-party actions.
- Consider adding `shellcheck`-friendly patterns and consistent `set -Eeuo pipefail` in all bash steps (this repo already does).

## Recommended concrete adjustments to PLAN_0

1. **Decide publishing mechanics for RubyGems Trusted Publishing:**
    - Either publish the built artifact via OIDC (preferred), or accept that the publish job is authoritative and remove/merge the “build” concept.
2. **Explicitly decide Ruby version policy:** SemVer2-only vs RubyGems-native.
3. **Add ambiguity detection:** fail if multiple project kinds match the same `project` name.
4. **Clarify official vs buddy gating:** which registries are required vs optional, and how failures affect GitHub Release creation.
5. **Document auth fallback for GitHub Packages RubyGems** if `GITHUB_TOKEN` isn’t accepted.

## Acceptance criteria traceability

- Resolve a Ruby gem project by `project` name: feasible with `<project>.gemspec` lookup; must handle ambiguity.
- Build gem from target commit + upload: feasible; version/name verification is good.
- Official publish to RubyGems via OIDC: **risk** unless publish-from-artifact is confirmed.
- Buddy publish to GitHub Packages only + prerelease GitHub Release: feasible.
- GitHub Release with built assets attached: feasible if artifact is the source of truth.
- Optional provenance attestations: aligns with existing pattern.
