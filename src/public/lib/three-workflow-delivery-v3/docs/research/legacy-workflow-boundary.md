# Retained Legacy Workflow Boundary

The [v3 migration policy](../migration-strategy.md) permits historical
mechanism extraction only with explicit v3 selection and revalidation.
The retained v1 Official/reusable workflow stack is a compatibility surface;
legacy Buddy is retired. The older v2 design is not an incremental base.
Maintainers and reviewers use this evidence to avoid reintroducing removed
routes, smoke fixtures, or release authority when reading old designs.

The legacy validator still sends readers to
[REFACTOR_PLAN.md](../../../../../../.github/workflows/REFACTOR_PLAN.md)
for channel migration examples and the unimplemented spoke design context.
That reader interface remains at the path embedded in runtime diagnostics.
The legacy [design prompt](../../../../../../.github/workflows/docs/DESIGN.prompt.md),
[v2 design](../../../../../../.github/workflows/docs/DESIGN.v2.md), and
[memory](../../../../../../.github/workflows/docs/MEMORY.md) paths retain
retirement notices because the
[retirement contract](../../tests/contracts/test_commit11_legacy_buddy_retirement.py)
reads them. Their replaced narrative remains in Git; those notices supply no
new implementation plan.

The four owner clarifications in the
[February workflow review](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/.github/workflows/REVIEW_20260217.md#review-baseline-and-clarifications)
remain historical evidence for maintenance of that retained stack:

1. **Official release may publish a beta/RC package while GitHub Release is marked as non-prerelease** (`prerelease=false`).
2. **First-party actions (`actions/*`) do not need SHA pinning**; third-party actions should be pinned.
3. **Default timeout behavior is acceptable**; no requirement to add `timeout-minutes` everywhere.
4. **Full fetch/history/tags are required** because NBGV-based version calculation depends on them.

These are scoped review clarifications, not a replacement for current v3
requirements or repository governance. The source review's optional
maintainability suggestions were not accepted implementation grants.
The old RubyGems script's caller-provided trusted-publishing credentials and
digest-based duplicate handling remain implementation facts at
[`publish_rubygems_org_idempotent.sh`](../../../../../../eng/scripts/publish_rubygems_org_idempotent.sh),
not a NuGet or v3 retry policy. Likewise
[`release_orchestrate_policy_publish_targets.sh`](../../../../../../eng/scripts/release_orchestrate_policy_publish_targets.sh)
retains legacy channel checks without granting a live legacy Buddy route.

The source is [`docs/wiki/log.md` at the accepted base](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/log.md),
Git blob `6764f2e8f6aede4894bc4ef826329fe81081e9f5`. Each dated heading and
its complete section below retain the source bytes, including then-pending
steps, failed observations, and later qualifications within that section.
The sections retain their original evidence level; they are not a new audit
or work authorization. The original operators and reviewers produced them;
v3 maintainers preserve them for implementers and reviewers at the linked
decision boundary. Referenced operator files and Actions artifacts have not
been downloaded or reverified by this record migration.

## [2026-08-21] lint | Clean Workflow Delivery v3 merge scope

- Restored production v1 CI exactly from base `7f8f41c2` and restored the v1
  Official/reusable release stack with only the approved fail-closed legacy
  Buddy retirement delta.
- Removed the inherited pre-v3 control plane, obsolete smoke projects, legacy
  descriptors, related scripts/tests/fixtures, and superseded design history
  while retaining the direct v3 package, first-slice npm project, Governance,
  CODEOWNERS/HK integration, and dedicated v3 workflows.
- Regenerated UV, PNPM, and Mise locks for the retained scope. This entry does
  not claim RC-001 final validation closure, and no live or package mutation
  operation ran.

## [2026-08-24] query | Begin Workflow Delivery v3 destination acceptance

- Recorded PR #552 merge `5a84bebd` and completed the immediate disabled Buddy
  cutover. Both legacy workflow identities are `disabled_manually`, no
  nonterminal executions remain, both files are absent from `main`, and real
  old-ref dispatch requests receive disabled-workflow rejection.
- Authorized destination acceptance without normal Live activation. Protected
  finalization binds the one-time workflow to implementation merge `5a84bebd`
  while the Governance attestation remains `live_enabled: false`.
- Created the temporary reviewer-protected acceptance Environment before
  finalization, with `hcoona` as required reviewer, self-review permitted for
  the single-operator topology, and a custom `main` deployment branch policy.
- Retained transition evidence: run `32693641797` terminated with zero jobs,
  and run `32693679161` executed only the read-only default-branch refusal
  stub. Neither entered a publication path.

<!-- End of verbatim source sections. -->
