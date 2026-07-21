# Atlas V0 A2 Private-Workspace Policy Correction

**Lifecycle:** Active subordinate; planning-only before verified shared `R`

**Status:** Proposed scope; implementation blocked until plan review

**Increment:** A2R6 - Private-Workspace Policy Correction

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `8a1cfd34a244c6a196528de75d641436f1c9e552`

## 1. Observed fact and correction

The one authorized A2R5 retry returned only:

```text
Safety check failed: private-workspace-policy.
```

The reviewed script had already proved a clean worktree, and request preflight had already traversed
the same fixed-drive path through the parent private directory. The project leader then locally
verified that the tracked policy has comment-only lines followed by the required effective rules:

```text
*
!.gitignore
```

No byte-order mark, private payload, path value, hash, manifest, inventory, or game/save content was
reported.

`ValidatePrivateWorkspace` currently accepts only those two lines and therefore rejects harmless
Git comment lines. This is stricter than the governing requirement that the effective rules include
the two canonical rules. A2R6 corrects that mismatch without weakening the effective policy.

## 2. Exact behavior

After existing ordinary-file validation and newline normalization, policy validation must:

1. continue to reject a UTF-8 byte-order mark;
2. treat an empty line or a line whose first character is `#` as non-effective;
3. require the remaining effective lines, in order, to equal exactly `*` and `!.gitignore`; and
4. reject every missing, reordered, duplicated, escaped-comment, whitespace-bearing, or additional
   effective rule.

This accepts comments and blank lines because Git treats them as non-effective policy text. It does
not accept another inclusion, exclusion, negation, wildcard, or path rule.

The existing `PrivateWorkspacePolicy` stage and fixed CLI diagnostic remain unchanged. No new enum,
token, exception detail, or private-data output is added.

## 3. Exclusions

A2R6 does not:

- inspect, read, enumerate, hash, copy, decode, or modify any real `.private` artifact;
- rewrite the tracked policy or change the reviewed request-preparation script;
- change path-component, fixed-drive, reparse-point, device, or ordinary-file validation;
- change a request, manifest, inventory, state, copy, cleanup, or JSON schema;
- add a package, project, harness, telemetry, logging, tracing, or private fixture;
- alter canonical paths, workspace census, source validation, or publication; or
- authorize confirmation, copying, cleanup, deletion, private-content inspection, or live-save
  writes.

## 4. Exact repository candidates

`P` adds only this plan. `R` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-private-workspace-policy-correction-plan-review.md
```

`I` may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasIntakeContractTests.cs
```

`G` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-private-workspace-policy-correction-release-gate.md
```

The immutable chain is `B -> P -> R -> I -> G`, where
`B = 8a1cfd34a244c6a196528de75d641436f1c9e552`. A plan-review correction may insert `P2` before `R`.
Each exact staged `R` and `G` blob must receive independent `No findings`, be committed unchanged,
and then be pushed and verified as the clean shared branch tip. Every other role must also be pushed
and verified before the next role begins.

## 5. Acceptance evidence

The candidate is acceptable when:

1. the canonical two effective rules still pass with LF, CRLF, or CR separators and optional final
   newline;
2. comment-only and empty lines before, between, or after those rules pass;
3. a byte-order mark still fails;
4. missing, reordered, duplicated, escaped-comment, whitespace-bearing, and additional effective
   rules each fail;
5. the discover-level invalid-policy test still reports `PrivateWorkspacePolicy`;
6. existing path-policy, stage-token, command-isolation, privacy, and full-suite tests remain
   enabled and pass;
7. the current A2 plan and index document A2R6 without rewriting A2R5 history;
8. locked restore, warning-as-error build, format, focused tests, full tests, unchanged apphost
   smoke, reference, ref-bound HK, and Git candidate-integrity checks pass;
9. a fresh GPT-5.6 Sol reviewer returns exact `No findings` for committed `I`; and
10. independent reviewers return exact `No findings` for the staged `R` and `G` records before those
    exact blobs are committed unchanged.

## 6. Stop, authority, and resume

Stop and return to planning if accepting the tracked policy requires another effective rule, a
weaker path check, dynamic output, private inspection, a schema or package, or a new harness.

This plan grants no private-run authority. After verified shared `G`, update only the reviewed
session script's commit binding to `G`, independently review the exact script, and return it to the
project leader for one metadata-only retry.
