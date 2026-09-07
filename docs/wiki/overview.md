# Wiki Overview

This page holds the current top-level synthesis of Workflow Delivery.

## Current Architecture Version

Workflow Delivery architecture **v3** is active and normative.

- [Architecture version entry point](./analyses/workflow-delivery/README.md)
- [v3 requirements](./analyses/workflow-delivery/v3/requirements.md)
- [v3 high-level design](./analyses/workflow-delivery/v3/high-level-design.md)
- [v3 Repository Model and Release Unit MLD](./analyses/workflow-delivery/v3/repository-model-release-unit-mld.md)
- [v3 Governance Integration MLD](./analyses/workflow-delivery/v3/governance-integration-mld.md)
- [v3 CI Qualification MLD](./analyses/workflow-delivery/v3/ci-qualification-mld.md)
- [v3 Release Delivery MLD](./analyses/workflow-delivery/v3/release-delivery-mld.md)
- [v3 Shared Foundation MLD](./analyses/workflow-delivery/v3/shared-foundation-mld.md)
- [v3 AI Agent Handoff](./analyses/workflow-delivery/v3/agent-handoff.md)
- [v3 architecture glossary](./analyses/workflow-delivery/v3/architecture-glossary.md)
- [v3 migration and document policy](./analyses/workflow-delivery/v3/migration-strategy.md)

v1 and v2 are historical and may supply mechanisms only when current v3
documents explicitly require extraction and revalidation. They are not
normative for new work.

## Confirmed Architecture Shape

- CI Qualification and Release Delivery are peer bounded contexts with
  independent Plans, Evidence, artifacts, state, and decisions.
- Delivery Governance is the external authority boundary. Shared Foundation
  supplies mechanisms and normalized facts, not business policy or verdicts.
- Release independently rebuilds and qualifies the complete Release Unit and
  consumes no CI Plan, Evidence, artifact, check, or verdict.
- A normal Buddy dispatch may select any same-repository ref. Its resolved
  exact SHA is both workflow/control revision and Release target; protected
  Governance is read separately from `main`. The selected revision must
  strictly admit the active replacement Governance schema; incompatible older
  control fails before any Environment job.
- Each request compiles one same-revision, purpose-bound Repository Model
  Snapshot. Live and simulation remain separate purposes.
- The bounded static-reference policy has `git-target`, `index`, and
  `worktree` sources. Only exact-SHA `git-target` output is Live evidence;
  index and worktree are HK feedback.
- Each retained static-reference selector has one exact Ecosystem Authority
  Graph composed from authoritative source artifacts, exact official libraries
  or CLIs, and published standards. Adapters emit normalized policy facts;
  handwritten ecosystem grammars or schemas and competing-authority hardening
  are excluded.
- A clean static result prevents bounded accidental references. It does not
  prove universal consumer absence or package-token isolation.
- The first-slice Publication Snapshot contains exactly zero or one action.
- Exact destination state takes the zero-action `exact-satisfied` path with no
  approval, Publication Authorization, publisher, marker, or Result.
  It repeats fresh Governance continuity, package-control, and actual
  exact-version tarball checks before success.
- Active exact-version absence may take one action only with a fresh admitted
  Package-Control Proof and a Governance-bound Destination Operation Profile
  whose native acceptance remains fresh. The path then uses a pre-wait
  Approval Bundle and summary, Approval Environment, complete Publication
  Authorization, separate publisher, durable pre-mutation marker, one pinned
  standard npm invocation without mutating retry, and one logical Publication
  Result for each controlled post-marker terminal state.
- Exact package-version bytes, digests, and witness are authoritative. The
  target-derived tag is a non-authoritative routing side effect. Receipt and
  `ActionResult` are retired; the current DAG carries one nullable scalar
  Result-preferred, marker-fallback publication terminal reference.
- Every authoritative normal-Live job independently requires
  `github.run_attempt == 1`; normal-Live records omit run attempt. Simulation
  retains its existing run-attempt identity and rerun behavior.
- Retry is a new manual dispatch with full rebuild, requalification,
  reobservation, and reapproval when an action remains. GitHub rerun is
  unsupported.
- Protected Governance expires within 90 days. Any later touch of its path
  invalidates the Attempt even after a revert; unrelated `main` commits do not.
- First-slice artifact bytes must be deterministic. Nondeterministic resume is
  deferred.
- Caller-held Release Execution concurrency and publisher mutable-resource
  concurrency remain distinct. Native Actions history is diagnostic only.

## First Slice and Accepted Risk

The first vertical slice is `hcoona-release-smoke-npm`: shadow/manual CI
Qualification, live Buddy publication to GitHub Packages, and Official npmjs
simulation.

`hcoona` is the sole accepted writer, Approval reviewer, and publisher TCB
member. Self-approval with `prevent_self_review: false` is explicit operator
confirmation, not independent review.

The GitHub Packages credential principal is repository `hcoona/three`. Known
reach includes production package `hexo-renderer-asciidoc` and disposable smoke
packages. The repository-principal blast radius is explicitly accepted for the
sole writer/publisher TCB; it is not package isolation and is not claimed
exhaustive. Exact action and coordinate checks govern intended behavior and
reconciliation, not a malicious accepted writer.

Prior retry-5 destination acceptance is complete historical evidence. Exact
`.17` through `.20` versions and tags remain retained and must not be reused.
Its detailed chronology belongs in Git and the append-only log.

## Current Disabled and External State

This revision implements the replacement runtime and strict Governance v2.
The governance document at
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` retains
state-only blocked activation and `live_enabled: false`.

- The implementation was protected-delivered through PR #653. Whole-group
  validation, independent rereview, contraction, exact merged-tree comparison,
  and post-merge checks are complete.
- The exact Destination Operation Profile has no admitted native acceptance
  generation. Local scenarios do not activate publication.
- Approval Environment `workflow-delivery-v3-buddy-approval`, ID
  `20895030723`, has rule `64124473`, sole reviewer
  `hcoona` / `712433`, self-review permitted, zero wait, no secrets, no
  branch/tag restriction, administrator bypass disabled, exact approval
  sentinel, and zero deployments.
- Legacy Environment `workflow-delivery-v3-buddy-github-packages`, ID
  `20895037877`, was deleted after protected delivery, retained-ref
  compatibility, and exact no-authority-reference proof. It had no deployments;
  the retained Approval Environment and all other Environments were unchanged.
- Package access is unchanged. No normal Live dispatch, Approval deployment,
  package publication, tag change, or package mutation has occurred.

## Delivery Boundary

The disabled runtime is protected-delivered and obsolete-Environment cleanup
is complete. Current native tooling provides canonical comparisons,
reproducible fixtures, and a one-shot pinned-profile probe with a distinct
manual Actions entry. Complete collection and suite orchestration remain
implementation work; protected delivery and actual execution are separate
gates. Old fixed-coordinate acceptance does not satisfy the new profile.
Establish the exact approved disposable coordinate and bounded administrative
execution authority before package mutation.

The remaining gates deliver and run the native suite, restore and verify its
original disposable object, install fresh ready Governance evidence including repository
retention of at least 45 days, merge one small Activation PR, read back
protected state, dispatch once through the run-ID-returning API, and verify the
exact run. There is no Preparation PR, `main` freeze, activation SHA/tag, or
blind redispatch after an ambiguous response.

## Historical Record Rule

Current-state pages describe current truth. Git history and the append-only
[Wiki Log](./log.md) carry PR, retry, test-count, artifact, provisioning, and
rollout chronology.

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Workflow Delivery Architecture Versions](./analyses/workflow-delivery/README.md)
