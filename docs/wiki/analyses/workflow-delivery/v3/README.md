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

The published implementation baseline is `4fac140d`. After the initial PR
head, bounded repairs skip Git LFS smudge only for Provider Git subprocesses,
harden the acceptance proxy, make consumer-policy tokenization linear, bind
live checkouts and admission to the caller revision, and remove the superseded
release-build-variant workflow. Non-rewriting merge commit `4fac140d`
integrates `origin/main` at `191abc82` and preserves upstream
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

The current behavior head passes General CI run
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

This current evidence update is a strict documentation-only child of the named
behavior commit and does not name itself. Its checks are external closure
evidence and are not recursively documented.

[PR #552](https://github.com/hcoona/three/pull/552) merged as
`5a84bebd05407e1859fe76f400dcb4f4cbcd002e` on 2026-08-24. Normal v3 Live
remains disabled. Governance converted both legacy Buddy workflow identities
to `disabled_manually`, verified no nonterminal legacy executions, and proved
that real old refs now receive disabled-workflow rejection.

Next:

1. preserve the Adapter distinction between pre-action, post-action, and
   post-mutation-start runner failures while retaining incomplete
   classification; do not promote the reconciled exact post-state to acceptance
   success;
2. retain run `32769435970`, fixed version
   `0.0.0-wdv3-acceptance.1`, tag, tarball, and artifacts as failure and
   reconciliation evidence;
3. do not reuse the run, Environment review, or coordinate; no retry is
   currently authorized, and any retry requires new explicit authorization, a
   newly reviewed invocation, and a disposable coordinate/version; and
4. keep `live_enabled: false` until successful acceptance and separate human
   activation authorization.
