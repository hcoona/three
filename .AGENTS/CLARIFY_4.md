# Clarifications for implementing the `PLAN_3.md` refactor (CLARIFY_4)

<!-- markdownlint-disable MD013 -->

This file captures **additional** questions that are not explicitly decided by `.AGENTS/CLARIFY_0.md`–`.AGENTS/CLARIFY_3.md`, but are likely needed to implement the refactor cleanly without reintroducing duplication.

Scope reminder: root workflows only under `/.github/workflows/*.yml`.

## 1) Node pack workflow: how should tarballs be identified after download?

Context:

- `PLAN_3.md` introduces a reusable `release-build-node-pack.yml` that produces `.tgz` artifacts under `${GITHUB_WORKSPACE}/out`.
- `official.yml` must publish **from those packed `.tgz` files** to both:
    - GitHub Packages (scoped package name), and
    - npmjs.org (public package name).

Question:

- What is the stable contract for selecting the correct `.tgz` after downloading `official-<project>-dist`?

Options:

A. Rename tarballs in the pack workflow to fixed names (recommended for simplicity):

- `${GITHUB_WORKSPACE}/out/npmjs.tgz`
- `${GITHUB_WORKSPACE}/out/gpr.tgz`

B. Emit workflow outputs with the tarball filenames:

- `npmjs_tgz_name`
- `gpr_tgz_name`

C. Infer by filename patterns (least recommended; can be fragile across npm pack behavior changes).

Requested decision:

- Choose A or B (or a different explicit convention) so the publish job does not need to guess.

Decision:

- Use **filename suffix-based identification** by renaming tarballs in the pack workflow after `npm pack`.
- Concretely, adopt **Option A** (fixed, deterministic names are acceptable even if they are not derived from the original pack name):
    - `${GITHUB_WORKSPACE}/out/npmjs.tgz` (the tarball to publish to `registry.npmjs.org`)
    - `${GITHUB_WORKSPACE}/out/gpr.tgz` (the tarball to publish to `npm.pkg.github.com`)

Notes:

- Implementation difficulty is **low**: `npm pack` does not allow controlling the output filename directly, but it is straightforward to rename the produced `.tgz` files after packing.
- Do **not** drop GitHub Packages publishing as part of this refactor. Removing GPR publish would be a behavior change and is out of scope for `PLAN_3.md`.

## 2) Dist-tag derivation: how to handle NBGV metadata mismatch?

Context:

- `PLAN_3.md` standardizes dist-tag derivation on:
    - `dotnet tool run nbgv get-version -v PrereleaseVersionNoLeadingHyphen -p <PACKAGE_DIR>`
- Empty prerelease metadata produces `latest`.

Question:

- If the resolved `version` string is a prerelease (contains `-...`) but `PrereleaseVersionNoLeadingHyphen` is empty, should the workflow:

A. Fail fast (recommended to avoid accidentally publishing a prerelease under `latest`), or
B. Continue and use `latest` anyway.

Requested decision:

- Choose A or B.

Decision:

- Choose **A (fail fast)**.
- If the resolved `version` is a prerelease (contains `-`) but `PrereleaseVersionNoLeadingHyphen` is empty, the workflow must error with guidance to fix version configuration (typically `version.json`).

## 3) `release-resolve.yml` (source=tag): should the entry workflow also pass the tag ref?

Context:

- `CLARIFY_3.md` requires passing `ref_name` when `source=tag`.
- The resolve workflow still needs to resolve `target` (commit SHA) corresponding to the tag.

Question:

- Should the entry workflow pass an additional input such as `ref` (e.g., `${{ github.ref }}`) to make resolving `target` independent of any implicit context inside `workflow_call`?

Requested decision:

- Either:
    - Require only `ref_name` (and resolve using `refs/tags/<ref_name>` after a full fetch), or
    - Require both `ref_name` and `ref`.

Decision:

- **Require both** `ref_name` and `ref`.
- Rationale: avoid relying on implicit reusable-workflow context; `ref` makes resolving the target SHA deterministic (e.g., via `git rev-list -n1 "${ref}"` after `fetch-depth: 0`).
