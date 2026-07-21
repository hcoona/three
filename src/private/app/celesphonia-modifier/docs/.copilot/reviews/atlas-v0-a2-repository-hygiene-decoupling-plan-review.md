# Atlas V0 A2 Repository-Hygiene Decoupling Plan Review

**Lifecycle:** Active subordinate plan-review evidence

**Increment:** A2R6 - Repository-Hygiene Decoupling

**Outcome:** Implementation ready only after verified shared `R`

**Final independent result:** `No findings`

**Base:** `8a1cfd34a244c6a196528de75d641436f1c9e552`

**Final plan candidate:** `3f07c2480bad46baafef6317507a719d560e341b`

**Final plan tree:** `96f4e31c88c79d8493f8bfbcdc4c715804661d92`

**Final plan blob:** `c67518463d76f98e5c0a5f174597526af7f0f90d`

**Governing plan:**
`../plans/atlas-v0-a2-repository-hygiene-decoupling.md`

**Record reviewer:** `a2r6-plan-record-reviewer`

**Record-review result:** `No findings`

## 1. Exact plan binding

The immutable planning chain is:

```text
B   8a1cfd34a244c6a196528de75d641436f1c9e552
P   d70f3816de7c35c463203afe05d2378d3ab26aea
P2  84472fd431fd9174d042f7ea01851aad5129a053
P3  3f07c2480bad46baafef6317507a719d560e341b
```

Each role is the direct child of the preceding role. `P` added only the initial policy-correction
plan, `P2` changed only that plan, and `P3` deleted the superseded plan and added only the active
repository-hygiene decoupling plan. `P3` equaled the clean shared branch upstream at final review.

The plan candidates bind these tree and plan-blob pairs:

- `P`: tree `b6d08490aa3c0f8092d7a21c3b539cf4696cf10d`;
  blob `0f211a27584575585ded314175acefbabd74d992`.
- `P2`: tree `0d80957a2711c0d5d1f51b8aa4040d04e5ff0ddb`;
  blob `86cfcbbcbaebf8a365fabfeea16df718ae95b2c5`.
- `P3`: tree `96f4e31c88c79d8493f8bfbcdc4c715804661d92`;
  blob `c67518463d76f98e5c0a5f174597526af7f0f90d`.

## 2. Review iterations and disposition

Every iteration used a fresh GPT-5.6 Sol reviewer that did not author its reviewed candidate and
received only repository-safe public sources.

| Iteration | Reviewer                        | Result                      |
| --------: | ------------------------------- | --------------------------- |
|         1 | `a2r6-plan-reviewer`            | One high-severity finding   |
|         2 | `a2r6-plan-final-reviewer`      | One medium-severity finding |
|         3 | `a2r6-decoupling-plan-reviewer` | `No findings`               |
|    Record | `a2r6-plan-record-reviewer`     | `No findings`               |

Iteration 1 found that the proposed BOM guarantee was not proved by default production I/O.
`P2` corrected that issue, but iteration 2 found that strict malformed-UTF-8 handling would require
additional parser scope.

The project leader then rejected the underlying runtime responsibility: Atlas should validate game,
save, request, output, source, manifest, inventory, state, copy, and lifecycle safety, while
repository ignore policy belongs to tracked policy, wrappers, hooks, CI, and release gates. `P3`
superseded the parser direction rather than expanding it.

Iteration 3 traced all four runtime call sites and their operation-specific validators, reviewed the
dead layout property and I/O seam, compatibility token, candidate paths, tests, privacy boundary,
and release authority. It returned exact `No findings`.

## 3. Accepted scope

The accepted increment:

- removes repository `.gitignore` validation from discovery, confirmation, copy, and cleanup;
- deletes `ValidatePrivateWorkspace`, `PrivateGitIgnorePath`, and the policy-only `ReadAllText`
  seam;
- preserves every actual request, output, source, game/save, path, census, digest, copy, and
  lifecycle validator;
- retains `PrivateWorkspacePolicy = 8` and its fixed CLI mapping only for compatibility;
- removes only policy-specific synthetic setup and tests; and
- changes no tracked private policy, script behavior, schema, package, project, or private data.

## 4. Privacy and authority

Planning and review accessed no real `.private` artifact, request, game, save, manifest, inventory,
hash, listing, generated output, or private value. The project leader supplied only the
repository-safe fact that the tracked policy contained comments before its canonical effective
rules.

This plan grants no private-run authority.

This exact staged record must receive independent `No findings`, then be committed unchanged as
`R`, the direct child of `P3`. The `P3..R` diff adds only this file. `R` must be pushed and verified
as the clean shared branch tip before implementation begins.
