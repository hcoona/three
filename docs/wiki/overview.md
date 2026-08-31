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
- A clean static result prevents bounded accidental references. It does not
  prove universal consumer absence or package-token isolation.
- The first-slice Publication Snapshot contains exactly zero or one action.
- Exact destination state takes the zero-action `exact-satisfied` path with no
  approval, Publication Authorization, publisher, marker, Result, or Receipt.
  It repeats fresh Governance continuity checks before success.
- Absent state may take one action only after the destination primitive proves
  conditional non-overwriting creation of the complete version-and-tag
  projection. The path then uses a pre-wait Approval Bundle and summary,
  Approval Environment, complete Publication Authorization, separate
  publisher, durable pre-mutation marker, one admitted compound action, and one
  Publication Result. Only `published` embeds one Receipt.
- Standard `npm publish --tag` can overwrite a competing tag introduced after
  Observation and is not admitted. Normal Live remains disabled until a
  separately reviewed supported primitive passes that race acceptance.
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

The merged runtime is superseded by the replacement design but remains disabled
through
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` with
`live_enabled: false`.

- Approval Environment `workflow-delivery-v3-buddy-approval`, ID
  `20895030723`, has rule `64124473`, sole reviewer
  `hcoona` / `712433`, self-review permitted, zero wait, no secrets, no
  branch/tag restriction, administrator bypass disabled, exact approval
  sentinel, and zero deployments.
- Legacy Environment `workflow-delivery-v3-buddy-github-packages`, ID
  `20895037877`, has no reviewer/protection rule, zero wait, no secrets, no
  branch/tag restriction, administrator bypass disabled, its legacy marker,
  and zero deployments.
- The legacy Environment is inert in the replacement design but must remain
  until replacement implementation no longer treats it as runtime input or
  authority and separate cleanup is authorized.
- Package access is unchanged. No normal Live dispatch, Approval deployment,
  package publication, tag change, or package mutation has occurred.

## Delivery Boundary

The user-approved replacement requirements, HLD, glossary, all five MLDs,
migration policy, and first-slice LLD form one coherent design package. Its
delivery uses combined validation, independent multi-angle review and atomic
TP/FP adjudication, human-reviewable commits, a protected design PR, and
post-merge reconciliation. Git and pull-request state must be inspected rather
than inferred from this page.

After design merge, runtime contraction is a separately authorized disabled
implementation. Later gates separately authorize validated implementation
merge, obsolete-Environment cleanup, destination-primitive acceptance, fresh
native and Governance evidence including repository retention of at least 45
days, one small Activation PR, readback, one API dispatch returning a run ID,
and exact run readback. There is no Preparation PR, `main` freeze, activation
SHA/tag, or blind redispatch after an ambiguous response.

No workflow, Python, schema, test, descriptor, release policy, Governance JSON,
dependency, package access, or external resource changed in this design phase.

## Historical Record Rule

Current-state pages describe current truth. Git history and the append-only
[Wiki Log](./log.md) carry PR, retry, test-count, artifact, provisioning, and
rollout chronology.

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Workflow Delivery Architecture Versions](./analyses/workflow-delivery/README.md)
