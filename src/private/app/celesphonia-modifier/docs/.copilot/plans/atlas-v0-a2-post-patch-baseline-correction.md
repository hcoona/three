# Atlas V0 A2 Post-Patch Baseline Correction

**Status:** Proposed correction; no execution or private-run authority

**Increment:** A2R2 - Post-Patch Baseline Correction

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Correction base:** `112b05d80712469100dd834ecca74fd2acba4639`

**Historical unchanged-source record:**
`9edbd57b4f44e76de321e06be81a581ed11b0017`

**Planned plan-review record:**
`../reviews/atlas-v0-a2-post-patch-baseline-plan-review.md`

**Planned tool-safety record:**
`../reviews/atlas-v0-a2-post-patch-baseline-tool-safety-review.md`

## 1. Decision and rationale

The Atlas corpus baseline is the installed file tree that was observed after an off-tree patch was
applied. Atlas identifies and freezes that baseline through its approved roots, selection rules,
private manifests, and per-file copy evidence.

The origin of each installed byte is not part of A2 intake identity. A patch package, installer
hash, or installation-history attestation cannot prove the resulting installed tree and is
unnecessary for the trusted-local, human-operated, read-only discovery and copy workflow.

The earlier official-patch amendment made a category error: it treated descriptive source history as
a supply-chain authorization problem. Its proposed implementation added package hashing, repeated
attestation, request and review receipts, a child-process launcher, terminal custody, new schemas,
and recovery state machines without changing the corpus, read-only operation, or copy-safety need.

The project leader rejected that expansion before it was committed. This plan restores proportional
governance and the unchanged original A2 implementation.

## 2. Authority and supersession

This plan supersedes:

- every executable, schema, test, validation, and Git-chain requirement in
  `atlas-v0-a2-patch-provenance-amendment.md`;
- the amendment's package identity, private installer hash, installation attestation, repeated
  revalidation, request-preparation, review-receipt, fixed-launcher, custody, and recovery models;
- the amendment's suspension of the unchanged original A2 source; and
- the amendment-specific fields planned for later A2 records.

The public fact that the observed baseline followed an off-tree patch remains descriptive historical
context. It does not authorize or reject an intake run.

The following remain governing:

- the finite A0 roots, rules, counts, aliases, decisions, privacy, and reopening conditions;
- the original A2 trusted-local-filesystem profile and accepted residual risks;
- read-only source access and copying only into protected Git-ignored storage;
- exact private manifest approval by the project leader;
- per-file held-handle copy, length, and digest evidence;
- locator redaction, strict contracts, lifecycle preflight, and no deletion;
- no live-save writes, decoding, semantic claims, or future writer authority; and
- independent review and record-only release gates.

The committed patch-provenance plan and review record remain immutable historical evidence. They do
not authorize implementation or private execution.

## 3. Baseline model

The active A2 baseline model is:

```text
approved observed roots
  + frozen selection rules
  + reviewed private manifest
  + copied-file fidelity evidence
```

Steam application `1786790`, public build `13624401`, and game version `1.05` remain repository-safe
descriptive identifiers from A0. The patch name or source may be retained as non-authoritative
context, but A2 does not require it.

The baseline changes only when an A0 reopening condition changes or the observed private manifest
differs from the approved finite corpus. Package availability, package hash, installation sequence,
and later reconstruction of source history do not define baseline equality.

This model does not claim:

- that Steam plus a named patch can reproduce the installed tree;
- that a package hash proves installed-file identity;
- that all source files represent one simultaneous point in time;
- hostile-local-race resistance beyond `trusted-local-filesystem/v1`; or
- compatibility or write authority for a future save editor.

## 4. Scope

### In scope

- Correct the normative A0/A2 source-baseline wording.
- Mark the patch-provenance amendment and its review as historical for forward authority.
- Reject and remove the complete uncommitted 23-path implementation candidate.
- Restore a clean worktree whose production, schema, and test files equal the historical A2 source.
- Re-run the unchanged original A2 validation and all 248 synthetic tests.
- Independently review the exact corrected candidate.
- Publish a fresh record-only tool-safety decision before private discovery.

### Out of scope

- Any production, schema, project, package, lock, SDK, TFM, or test change.
- Any installer path, installer hash, package retention, or installation-history requirement.
- Any new CLI command, request, receipt, custody, launcher, state, or recovery contract.
- Any inspection of the installed game, live saves, retained installer, private workspace, or
  private request.
- Any A0 corpus change, new discovery result, confirmation, copy, cleanup, or private acceptance.
- Compatibility fingerprints, supported patch matrices, save writing, rollback, release signing,
  distribution, or updater provenance.

## 5. Exact plan candidate

The correction plan candidate may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-research-contract.md
    atlas-v0-a2-intake-safety-plan.md
    atlas-v0-a2-patch-provenance-amendment.md
    atlas-v0-a2-post-patch-baseline-correction.md
```

The plan-review record child may add only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-post-patch-baseline-plan-review.md
```

## 6. Execution procedure

After the plan-review record is committed, pushed, and verified:

1. remove only the exact six untracked files authorized by the rejected amendment;
2. restore only the exact 17 modified implementation paths to the committed plan-review candidate;
3. verify the worktree contains no remaining implementation candidate path;
4. compare the exact historical production, schema, and test path set with
   `9edbd57b4f44e76de321e06be81a581ed11b0017`;
5. stop if any source, schema, project, package, lock, SDK, or test byte differs;
6. run the original A2 validation from a clean worktree;
7. commit no source change because the source candidate is the plan-review commit itself;
8. obtain a fresh independent `No findings` review of that exact committed candidate; and
9. prepare, independently review, commit, push, and verify the record-only tool-safety child.

The cleanup command or script must name every file literally. It must not use recursive deletion,
wildcards, `git reset --hard`, or broad checkout commands.

## 7. Git evidence chain

The correction uses these exact roles:

- `B` is correction base `112b05d80712469100dd834ecca74fd2acba4639`.
- `C` is the pushed correction-plan candidate descended from `B`. `B..C` changes exactly the five
  plan-candidate paths in section 5.
- `D` is the immediate child of `C`. It adds only the independently reviewed
  `atlas-v0-a2-post-patch-baseline-plan-review.md` blob unchanged.
- `S` equals `D`. It is the corrected committed source candidate because no source byte changes.
- `T` is the immediate child of `S`. It adds only the independently reviewed
  `atlas-v0-a2-post-patch-baseline-tool-safety-review.md` blob unchanged.

Every role must be pushed and equal the shared upstream before the next role proceeds. `D` and `T`
must pass first-parent, exact-path, staged-blob, tree, upstream, and clean-worktree verification.

Any source, schema, project, package, lock, SDK, TFM, test, or unplanned documentation change
between `C` and `T` invalidates the chain.

## 8. Acceptance criteria

The correction passes only when:

1. `B..C` changes exactly the five paths in section 5;
2. the plan states that the observed post-patch file tree is the baseline;
3. no package or installation-history evidence is required for A2;
4. a fresh independent plan reviewer reports exact `No findings`;
5. `D` is the verified record-only child of `C`;
6. the rejected 23-path implementation is absent from the worktree and Git history;
7. the historical production, schema, and test path set is byte-identical to `9edbd57b`;
8. locked restore and warning-free build pass;
9. `dotnet format --verify-no-changes` passes for the library, CLI, and tests;
10. Microsoft.Testing.Platform reports exactly 248 passed, zero failed, and zero skipped;
11. the filtered direct-apphost smoke suite reports exactly 11 passed;
12. evaluated project and package references match the historical record;
13. candidate-path HK, LF, line-length, and `git diff --check` gates pass;
14. no private or original data is accessed during correction;
15. a fresh independent source reviewer reports exact `No findings`;
16. `T` is the verified record-only child of `S`; and
17. the branch equals upstream with a clean tracked and untracked worktree.

## 9. Stop conditions

Stop and return to planning if:

- restoring the rejected candidate would remove any path outside its exact 23-path boundary;
- a historical source, schema, test, project, package, lock, SDK, or TFM byte differs;
- the unchanged suite does not report exactly 248 passing tests;
- any A0 root, rule, count, alias, decision, or private-manifest expectation changes;
- private discovery or any private artifact access would be required to validate the correction;
- review finds that package provenance controls an actual A2 hazard not already covered by the
  observed baseline and copy-fidelity model; or
- any independent finding remains unresolved.

## 10. Outputs and handoff

Repository-safe outputs:

- this correction plan and its plan-review record;
- the unchanged-source validation result;
- the final record-only tool-safety review; and
- exact public Git commit, tree, parent, path, and test-count evidence.

There is no private output.

To resume, verify `C` and `D`, confirm the exact 23 rejected paths are the only dirty paths, then
continue at section 6. Do not inspect or execute any private request before verified `T`.
