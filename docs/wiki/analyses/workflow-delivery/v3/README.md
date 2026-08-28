# Workflow Delivery v3

## Status

Active and normative.

v3 is a clean implementation line. It does not evolve the v2 control
architecture in place. Proven v2 mechanisms may be ported only through reviewed
v3 Provider, Adapter, or client boundaries and must not leak v2 domain or
authority types into v3 CI or Release decision models.

## AI Agent Handoff

Agents continuing v3 work must read the
[Workflow Delivery v3 AI Agent Handoff](./agent-handoff.md)
before planning or editing.

## Normative Pages

- [Requirements](./requirements.md)
- [High-Level Design](./high-level-design.md)
- [Architecture Glossary](./architecture-glossary.md)
- [Migration and Document Policy](./migration-strategy.md)

## Middle-Level Design

- [Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md)
- [Governance Integration MLD](./governance-integration-mld.md)
- [CI Qualification MLD](./ci-qualification-mld.md)
- [Release Delivery MLD](./release-delivery-mld.md)
- [Shared Foundation MLD](./shared-foundation-mld.md)

## Current Design Work

The first vertical slice is `hcoona-release-smoke-npm`:

- CI Qualification;
- live Buddy publication to GitHub Packages; and
- Official npmjs dry-run.

The Release MLD identity decision was reopened and reconfirmed before LLD on
2026-08-05. Buddy npm uses the frozen native NBGV `npmPackageVersion`
unchanged. Separate manual requests create separate request and Intent records.
Each admitted, non-coalesced request creates a new Attempt in one Release
Execution only when it names the same channel, Release Unit, and target. Buddy
Release Execution Identity does not include any coordinate or projection-set
digest. Official Product Identity is channel, Release Unit, and canonical NBGV
version; Official Execution Identity adds immutable target. Different targets
create separate Executions even when they share Product Identity; no permanent
Product Identity-to-target ledger is required. Each candidate run attempt
branches to live Release or release simulation and compiles exactly one
same-revision, purpose-bound Repository Model Snapshot for its run attempt. The
resulting live Attempt or simulation pass reuses that Snapshot; a new run
attempt compiles a new one. NBGV-owning Providers remain pinned to the exact
target while fetching complete ancestry and tags with `fetch-depth: 0` or an
equivalent guarantee, and reject shallow or incomplete history before compiling
version facts. Simulation has separately namespaced request-scoped identity
derived only after its Snapshot validates and cannot enter live Product,
Execution, Attempt, authorization, capability, Receipt, or mutation lineage.
Successful approval alone produces the Authorization Record required by
capability groups. The first-slice GitHub rejection surface cannot produce
attempt-bound Approval Outcome Evidence, so rejection is unknown, replayable
incomplete, and non-authorizing. Cancellation or platform expiry while approval
is pending may terminate the run before a separate record or Finalizer outcome;
the platform conclusion proves no side effect only when no capability group
started, otherwise replay must reobserve. Pre-admission compilation closes
technical repository, version, build, and artifact facts; post-admission
live planning, or the corresponding simulation planning pass, selects and
freezes native projections and the deterministic pre-observation publication
basis. Build and observation precede materialization of actual live actions and
key sets in the Publication Snapshot. No Intent reserves an absent coordinate.

The first-slice Buddy trust decision was reopened and reconfirmed before LLD on
2026-08-06 as a bounded risk exception. Any same-repository selected ref may
supply the complete same-revision release stack, including workflow, Planner,
Finalizer, Providers, Adapters, compiler, clients, catalogs, capability
declarations, and publisher, without owner-reviewed eligibility. Dedicated
Buddy Environment approval after Publication Snapshot creation governs the
normal path. Workflow-level permissions remain empty or read-only;
`packages: write` is declared only on the `run-live-attempt` `uses`-only caller
job as the reusable-workflow ceiling and on the called
Environment-referencing publisher job as effective capability. The publisher
receives short-lived `GITHUB_TOKEN` with no PAT or `id-token: write`.
`evaluate-live-eligibility` receives only `contents: read`; effective
`actions: read` is confined to history admission and explicit `packages: read`
to the observer. All other jobs remain least-privilege, and the callee cannot
elevate beyond the caller. Approval is not independent semantic validation or a
malicious-writer permission ceiling. Every repository Write/Maintain/Admin actor
is inside the slice publisher TCB; if that trust assumption changes, the slice
blocks until the untrusted actor's access is reduced below
Write/Maintain/Admin or an independently enforced publisher boundary makes
package-write Capability and destination access unavailable to writer-authored
workflows. Ref restrictions and workflow governance alone are insufficient
remediation. The exception is limited to the disposable smoke package and
isolated GitHub Packages destination and is not inherited by Official or future
Buddy destinations or
production packages.

The first brief
[`hcoona-release-smoke-npm` LLD](./hcoona-release-smoke-npm-lld.md)
was **approved for implementation on 2026-08-06**. It fixes the clean
v3 package and workflow decomposition, strict binding inventory, first-slice
quality and release authoring, npm observation/publication boundaries,
acceptance plan, and dependency-ordered implementation commits. The revised
draft keeps first-slice CI shadow-only with manual `slice-validation`, holds
Release Execution concurrency through the reusable live Attempt, defines
job-scoped reusable-workflow permission ceilings with no workflow-wide package
write, requires distinct tarball content and install/import Evidence, binds
approval to an immutable reviewer-summary artifact, handles approval-pending
cancellation truthfully,
strictly admits retained same-Execution history as history-only, uses ID-only
artifact transport, adds exact path-triggered root-HK v3 tests, uses
`approval-finalizer` as the credential-free publisher admission gate, treats
GitHub rejection as diagnostic-only unknown state, requires full-SHA action
pins with the current Renovate-selected Node-24-compatible action major,
requires 45-day Release retention, defines caller-selected current/history
admission with same-run prior-attempt support and platform-aware historical
attribution, exact-target pre-Attempt Live Eligibility Decisions, fixed-source
protected-ref human TCB/access attestations with exact repository/ref/path
policy fields, required boolean `live_enabled`, `contents: read` fresh-source
validation, and bounded operational staleness, immediate pre-Capability
source/provenance/content revalidation with new-Attempt recovery,
workflow-run-unique physical artifact names across reruns, permanent consumer
policy, isolated frozen-version npm staging that updates and verifies the staged
manifest `files` allowlist, exact packed-tarball witness-path/content checks,
exact-target full-history/tag NBGV checkout with shallow-history rejection,
explicit target-specific npm dist-tag projections, a conservative shared
destination/package GitHub equality group that over-serializes while preserving
the complete coordinate-plus-tag resource-key set, exact final-match CODEOWNERS
coverage for the protected Governance document, and Governance re-attestation.
It sequences removal, disablement, and draining of both legacy Buddy identities
before a removable protected destination-acceptance bootstrap whose probes
require `github.run_attempt == 1` and whose terminal evidence capture uses
`always() && github.run_attempt == 1` to retain dependency failures and
ambiguous mutation state for reconciliation; retry requires a new reviewed
invocation and disposable coordinate/version. Failed acceptance leaves all
Buddy publication disabled. No legacy Buddy compatibility remains;
former Buddy projects are unsupported until migrated, while v1 Official and CI
assets remain unchanged. Legacy Buddy workflows, Buddy-specific tests/matrices,
and Buddy docs are excluded from that preservation and are retired or
rewritten. The publication-preparation/cancellation closure is complete. Do not
activate normal live delivery, run real acceptance probes, finalize the
sentinel target, mutate any package, or begin later-scope work without separate
explicit approval.

Buddy caller-held Release Execution concurrency is complete at `3a2df043`.
The canonical key derives only from channel, Release Unit, and immutable
target; request and workflow-run identity remain Attempt transport but do not
partition the concurrency group. The caller holds the group across the complete
reusable live Attempt with `cancel-in-progress: false`.

The historical PR #552 implementation baseline was `4fac140d`. After the
initial PR head, bounded repairs skipped Git LFS smudge only for Provider Git
subprocesses, hardened the acceptance proxy, made consumer-policy tokenization
linear, bound live checkouts and admission to the caller revision, and removed
the superseded release-build-variant workflow. Non-rewriting merge commit
`4fac140d` integrated `origin/main` at `191abc82` and preserved upstream
open-code-review 1.9.5 lock data exactly.

The bounded pre-coexistence CI bootstrap design is committed at `7c457b7c`,
and its implementation, tests, and review closure are committed at
`f0535989`. The canonical shadow Decision remains
`incomplete-model-plan`/`fix-model-plan-and-rerun`; no record is rewritten.
Only while the exact pull-request base tree lacks the canonical v3 CI workflow
may the enclosing non-authoritative check conclusion project success after
exact Plan, Decision, Summary, event-identity, and base-tree admission. Manual
validation, lane failures, mixed diagnostics, malformed records, explicit
supersession, and post-coexistence pull requests remain red. The exception
self-disables after merge because the workflow is then present in the base
tree. Before Phase 1 scope cleanup, the 14-file committed range through
`f0535989` passed the managed gates with 1,257 workflow-release tests and 3,234
Workflow Delivery v3 tests. The inherited workflow-release suite is no longer
retained, so those counts are historical rather than current repair evidence.
All three original policy, CLI, and workflow reviewers report no findings after
independent adjudication and repair.

Documentation closure is committed at `a9e8cbfa`. Non-rewriting merge commit
`30b793be` then integrates the latest `origin/main` at `7f8f41c2`, containing
only the upstream Biome 2.5.9 and Asciidoctor 4.0.10 dependency updates. The
frozen PNPM and UV lock checks pass after the merge.

The implementation review and PR-comment follow-up are complete at behavior
commit `9f97ef091e8a831f73d81fe91b441aa6ee0520c3`, tree
`69bec461fcb1047e7beb2ce13a9e9192e5cdf056`, after non-rewriting integration
of `main` commit `62ffb59bcfbe7845e580d7aea5337afafc88bdf8`. The exact tested merge is
`59ad1ef2bd9277dc6cc35f897d8230dcf807ecdb`. The prior `e9d812b2` RC-001
boundary is retained as superseded evidence. The complete repair and
supersession ledger is in the [AI Agent Handoff](./agent-handoff.md).

That historical PR #552 behavior head passed General CI run
[`32669623270`](https://github.com/hcoona/three/actions/runs/32669623270),
CodeQL run
[`32669623284`](https://github.com/hcoona/three/actions/runs/32669623284), and
dedicated v3 run
[`32669623261`](https://github.com/hcoona/three/actions/runs/32669623261), all
on attempt 1. All nine retained v3 payloads match their GitHub byte counts and
SHA-256 digests and pass canonical admission. Authenticated exact-identity
replay reproduces the Decision and Summary byte-for-byte and retains Finalizer
exit `1`, the expected non-authoritative
`incomplete-model-plan` / `fix-model-plan-and-rerun` result, 295 changed paths,
78 exclusively unclassified-path diagnostics, four empty lane results, and no
admitted Evidence or artifact digests. The exact bootstrap projection exits
`0` while explicitly retaining the canonical failure. Every review thread is
resolved with either a published repair or recorded false-positive evidence.

That historical PR #552 RC-001 evidence update was a strict documentation-only
child of the named behavior commit and did not name itself. Its checks were
external closure evidence and were not recursively documented.

[PR #552](https://github.com/hcoona/three/pull/552) merged as
`5a84bebd05407e1859fe76f400dcb4f4cbcd002e` on 2026-08-24. Normal v3 Live
remains disabled. Governance converted both legacy Buddy workflow identities
to `disabled_manually`, verified no nonterminal legacy executions, and proved
that real old refs now receive disabled-workflow rejection.

Retry-3 cleanup and documentation closure merged through PRs #600 and #601.
Repair PR #603 then merged as
`bf1748971f2717a8877852590c5436b4160a4fbf`. It retains closed request-bound
upstream diagnostics across the acceptance proxy, runner, Adapter, and
Governance while keeping those diagnostics non-authoritative. It also makes
the expected-one request reservation atomic. The complete v3 suite passed
3,782 tests, and focused Pyrefly, HK, independent review/adjudication, required
checks, and CodeQL passed. No destination-acceptance invocation followed the
merge before this documentation update, so all three historical attempts
remain unsuccessful and `.1`-`.12` remain consumed.

Next:

1. preserve all four attempts and their exact evidence as unsuccessful
   historical replay; do not infer acceptance from destination state or the
   new diagnostics;
2. retain retry-4 preparation merge `835b81be` and protected finalization merge
   `f3d53177` as provenance for the consumed profile. A fresh exact preflight
   passed before exactly one attempt-1 dispatch, run `33165777024`, from
   `main`;
3. retain run `33165777024` as unsuccessful evidence. It observed `.13`
   absent, started mutation, received a request-bound upstream HTTP 200, and
   exactly read back `.13`. The proof contract required HTTP 201, so no
   validated request proof formed. The first probe remained incomplete, the
   `.14`-`.16` probe was skipped, and terminal Governance evidence classified
   the run unknown. Authenticated reconciliation confirms exact tag
   `wdv3-acceptance-13`, tarball SHA-1
   `7f088ba1708310ef0dba5814da3ad4cf57d49062`, SHA-512
   `aafe86f3b48a7affc6c160f81bd81d69692fc3789149a7a01e620acd05052d0c7c0e87b7f552b19fc2192a90b6af1201b265cc2475ac28288cc1ab70bfbe7c71`,
   and target witness `835b81be`; `.14`-`.16` remain absent;
4. preserve immutable artifacts `9683508663`, `9683519655`, and `9683526452`.
   Their GitHub-recorded SHA-256 digests match the retained raw bytes, and the
   terminal Governance artifact passes canonical admission;
5. retain cleanup PR #610 as rebase-merged without bypass at `4e7e7ef6`.
   Post-merge CI and CodeQL passed. Fresh authenticated reconciliation
   confirms the temporary workflow source and workflow-only contract absent,
   workflow ID `344468231` `deleted`, Environment ID `20772100445` and
   acceptance refs absent, exactly one historical attempt-1 run, exact `.13`
   retained, and `.14`-`.16` absent. No post-deletion dispatch occurred. The
   cleanup-before-repair gate is satisfied, but any further attempt still
   requires a separately reviewed acceptance-only repair and fresh coordinate
   block starting from a fresh fetch of this cleanup merge or a later reviewed
   successor; and
6. retain response-status repair PR #612 as rebase-merged without bypass at
   `aed58191ce37defba8f7a7e44def03396c2c6824`. All protected PR checks,
   including Workflow Delivery v3 shadow CI, passed; post-merge Continuous
   Integration run `33190125517` and CodeQL run `33190125529` passed on that
   exact SHA. Fresh authenticated read-only reconciliation confirms no
   post-merge acceptance invocation, workflow ID `344468231` still deleted
   with exactly failed attempt-1 run `33165777024`, the temporary Environment
   absent, and package versions still limited to `.1`, `.5`, `.9`, and `.13`.
   Any retry-5 profile must start from freshly fetched and revalidated
   `origin/main` at this merge, or at a later reviewed, merged successor that
   contains it. For a strictly validated GitHub Packages npm publish exchange,
   proof authority may use exactly HTTP 200 or HTTP 201, must retain the actual
   status in response identity, and must still reject HTTP 202, HTTP 204, and
   every other status. New HTTP 200 diagnostics remain request-bound; the only
   historical unbound status compatibility remains HTTP 201 adjacent to a
   matching proof. The exact retry-4 terminal artifact remains unknown because
   it contains no validated request proof; and
7. retain work-base clarification PR #613 as rebase-merged without bypass at
   `8e6baf24ca476b449b5c97c21f14f3776e668b90`; its post-merge Continuous
   Integration run `33194078923` passed. The retry-5 preparation initially
   started from that exact `origin/main`. Before delivery, a fresh fetch found
   the later dependency-only merges #614 and #615 at
   `origin/main@c33ea9da5456ca0e915e39134ec111714ddc4ec8`; the preparation
   commits were rebased onto that reviewed successor without file overlap or
   conflict. It adds only the temporary manual workflow and closed
   Adapter/Governance profile for absent/exact `.17`, identical-race `.18`,
   differing-race `.19`, and lost-response `.20`, with the exact corresponding
   tags and confirmation digest. The production target remains forty ASCII
   zeroes, so validation rejects before Environment review or either
   package-write probe. The terminal fan-in retains canonical suite records
   across monotone failure/upload downgrade, treats missing artifact bindings
   as incomplete, and admits proof authority only for exact HTTP 200 or HTTP 201. Read-only preflight found `.17`-`.20` and their tags unused, but they
   remain unexecuted and unconsumed at preparation. No retry-5 Environment,
   dispatch, deployment, acceptance ref, package, tag, or Live mutation has
   occurred.
   After protected preparation merges, fresh external-state revalidation must
   pass before creation of a new protected Environment and a separate
   protected finalization PR bound to that exact merge SHA. Finalization, the
   sole `run_attempt == 1` dispatch and review, reconciliation, cleanup, and
   closure must each start from freshly fetched and revalidated `origin/main`
   containing the immediately preceding protected merge; the local branch, an
   attempt ref, an arbitrary fetched SHA, or a reviewed-but-unmerged head is
   not authority; and
8. keep `live_enabled: false`. Subsequent explicit user authorization covers
   the bounded acceptance-only repair/retry loop through genuine success,
   cleanup, and closure, but normal Live activation remains a separate
   production decision.
