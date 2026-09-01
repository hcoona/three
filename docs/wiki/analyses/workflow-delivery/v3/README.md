# Workflow Delivery v3

## Status

Workflow Delivery v3 is active and is the only normative Workflow Delivery design line.

The user-approved replacement normal-Live baseline is merged across the
requirements, HLD, glossary, five MLDs, migration policy, and first-slice LLD.
Disabled runtime contraction is separately authorized and in progress; Git and
pull-request state must be inspected rather than inferred from this page.

No normal Live workflow, Governance enablement, package access, deployment,
package mutation, or other external resource has changed. The runtime remains
disabled through `live_enabled: false`.

Normal Live is also activation-blocked by a destination capability gap:
standard `npm publish --tag` can overwrite a conflicting tag introduced after
Observation and is not an admitted primitive for the complete version-and-tag
projection. A separately reviewed supported primitive and its conditional
non-overwrite race acceptance are required before activation.

Before planning or editing v3 work, read the [Workflow Delivery v3 AI Agent Handoff](./agent-handoff.md). It is
operating guidance, not a second normative specification.

## Normative Hierarchy

Read the current v3 documents in this order:

1. [Requirements](./requirements.md)
2. [High-Level Design](./high-level-design.md)
3. [Architecture Glossary](./architecture-glossary.md)
4. Middle-level designs:
    - [Repository Model and Release Unit](./repository-model-release-unit-mld.md)
    - [Governance Integration](./governance-integration-mld.md)
    - [CI Qualification](./ci-qualification-mld.md)
    - [Release Delivery](./release-delivery-mld.md)
    - [Shared Foundation](./shared-foundation-mld.md)
5. [Migration and Document Policy](./migration-strategy.md)
6. [`hcoona-release-smoke-npm` LLD](./hcoona-release-smoke-npm-lld.md)

Higher layers constrain lower ones, and the current set must be reconciled if a conflict appears. v1 and v2 may supply
a mechanism only when a v3 document explicitly requires extraction and revalidation.

## Current First Slice

The first vertical slice is `hcoona-release-smoke-npm`:

- CI Qualification remains shadow/manual during coexistence;
- live Buddy targets GitHub Packages; and
- Official npmjs behavior remains simulation-only.

Prior retry-5 destination acceptance is complete historical evidence. Exact
`.17` through `.20` versions and tags remain intentionally retained, but their
chronology is not current architecture.

## Replacement Contract

- A normal Buddy dispatch may select any same-repository ref. GitHub resolves
  it to one exact SHA that is both the workflow/control revision and Release
  target. Protected Governance is read separately from `main`. Compatible
  selected-revision control must strictly admit the active replacement
  Governance schema; older parsers fail before any Environment job.
- The bounded static-reference policy has exactly `git-target`, `index`, and
  `worktree` source kinds. Only exact-SHA `git-target` output is Live evidence;
  the other two are HK feedback.
- Its closed surface is an exact selector-to-fact matrix. Each selector binds
  one Ecosystem Authority Graph composed from authoritative source artifacts,
  exact official libraries or CLIs, and published standards. Policy code
  consumes normalized facts and contains no competing grammar, schema, or
  authority-hardening layer.
- A clean static result prevents bounded accidental references. It does not
  prove universal consumer absence or package-token isolation.
- The target architecture has one authority-bearing Environment,
  `workflow-delivery-v3-buddy-approval`. It has no Capability Environment or
  generic Environment Profile.
- An exact-satisfied zero-action path has no approval, Publication
  Authorization, publisher, mutation marker, Publication Result, or Receipt.
  It repeats fresh protected Governance continuity checks before success.
- A one-action path may form only after the destination primitive proves
  conditional non-overwriting creation of the complete version-and-target-tag
  projection. It then durably prepares the Approval Bundle and reviewer
  summary before the Environment wait, emits the complete Publication
  Authorization, runs a separate publisher, persists the marker before
  mutation, performs one admitted compound action, and persists one Publication
  Result. Standard `npm publish --tag` does not satisfy this gate.
- Only a successful `published` Result embeds exactly one Receipt.
- `hcoona` self-approval is explicit operator confirmation inside the accepted
  writer/publisher TCB, not independent security review.
- Every authoritative normal-Live job independently requires
  `github.run_attempt == 1`. Normal-Live records omit run attempt; simulation
  retains its existing run-attempt identity and rerun semantics.
- Retry is a new manual dispatch with a new run ID and a complete rebuild,
  requalification, reobservation, and reapproval when an action remains.
  GitHub rerun is unsupported.
- Protected Governance is valid for at most 90 days. Identity is bound to the
  protected path and blob/content or generation; any later touch of that path
  invalidates the Attempt even after a revert. Unrelated `main` commits do not.
- First-slice artifact bytes must be deterministic. Resume of a
  nondeterministic sealed artifact is deferred.
- Caller-held Release Execution concurrency and publisher mutable-resource
  concurrency remain separate and required.
- Native Actions history is diagnostic only and supplies no publication
  authority.
- Activation follows design merge, separately authorized disabled
  implementation, validation/review/merge, separately authorized obsolete
  Environment cleanup, accepted destination-primitive race proof, fresh native
  and Governance evidence including repository retention of at least 45 days,
  one small Activation PR, readback, one API dispatch returning a run ID, and
  exact run readback. There is no Preparation PR, `main` freeze, activation
  SHA/tag, or blind redispatch after an ambiguous response.

## Current External Boundary

- Approval Environment `workflow-delivery-v3-buddy-approval` is ID
  `20895030723`, with reviewer rule `64124473`, sole reviewer
  `hcoona` / `712433`, `prevent_self_review: false`, zero wait, no secrets,
  no branch/tag restriction, `can_admins_bypass: false`, marker
  `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`,
  and zero deployments.
- Legacy Environment `workflow-delivery-v3-buddy-github-packages` is ID
  `20895037877`, with no reviewer or protection rule, zero wait, no secrets,
  no branch/tag restriction, `can_admins_bypass: false`, legacy marker
  `WDV3_CAPABILITY_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-github-packages/v1`,
  and zero deployments. It is inert in the replacement design but must not be
  deleted until implementation no longer treats it as input or authority and
  separate cleanup is authorized.
- The GitHub Packages credential principal is repository `hcoona/three`.
  Known reach includes production package `hexo-renderer-asciidoc` and
  disposable smoke packages. This accepted repository-principal blast radius
  is neither package isolation nor an exhaustive inventory.
- Package access remains unchanged. No normal Live dispatch, Approval
  deployment, publication, tag change, or package mutation has occurred.

## Delivery Boundary

Design delivery consists of:

1. validate the complete coherent design set;
2. complete independent multi-angle review and one-finding-at-a-time TP/FP
   adjudication;
3. fix and rereview until every reviewer reports zero findings;
4. create human-reviewable design commits and deliver a protected design PR;
   and
5. reconcile the merged design, then stop.

Runtime implementation, Environment cleanup, Governance refresh, activation,
dispatch, approval, and package mutation remain separate authorization
boundaries.

## Historical Source Rule

Git history and the append-only
[`docs/wiki/log.md`](../../../log.md) carry chronology. Current-state pages
describe current truth and must not reproduce retry ledgers, PR narratives,
test-count histories, artifact tables, or superseded mechanisms.
