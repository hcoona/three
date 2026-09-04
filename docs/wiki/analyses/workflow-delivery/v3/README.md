# Workflow Delivery v3

## Status

Workflow Delivery v3 is active and is the only normative Workflow Delivery design line.

The user-approved replacement normal-Live baseline is merged across the
requirements, HLD, glossary, five MLDs, migration policy, and first-slice LLD.
The static-reference and record-model contractions are merged. The disabled
Governance/authorization contraction is validated, independently rereviewed,
implemented, and merged. The Publication/Finalizer design contraction is
complete locally, independently rereviewed to zero findings, and final-shrink
clean. Git and pull-request state must be inspected rather than inferred from
this page.

No Publication/Finalizer runtime, Governance enablement, package access,
deployment, package mutation, or other external resource has changed. The
merged runtime and protected document remain disabled through
`live_enabled: false` and still use the superseded v1 Governance schema until
the implementation migration.

Normal Live remains activation-blocked by the unimplemented
Publication/Finalizer runtime and missing fresh native acceptance for the exact
Destination Operation Profile. The design admits a pinned standard
`npm publish --tag ... --fetch-retries=0` profile only after its native suite
proves the required creation, conflict, tag-race, and deleted/restorable
tombstone behavior. Exact package-version bytes, digests, and witness are
authoritative; the target-derived tag is a non-authoritative routing side
effect.

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
  and zero deployments. It is inert in the merged replacement runtime but must
  not be deleted until exact repository inspection proves no retained
  dispatchable ref can treat it as input or authority.
- The GitHub Packages credential principal is repository `hcoona/three`.
  Known reach includes production package `hexo-renderer-asciidoc` and
  disposable smoke packages. This accepted repository-principal blast radius
  is neither package isolation nor an exhaustive inventory.
- Package access remains unchanged. No normal Live dispatch, Approval
  deployment, publication, tag change, or package mutation has occurred.

## Delivery Boundary

The Governance/authorization implementation boundary is merged. The
Publication/Finalizer requirements, HLD, MLD, glossary, migration, and
first-slice LLD contraction is complete locally, independently rereviewed to
zero findings, and final-shrink clean. It has not yet changed runtime code,
workflows, protected Governance, or external resources.

Git and pull-request delivery state must be inspected live.
The next dependency-ordered gate is design delivery, followed by disabled
runtime implementation, native profile acceptance, Governance v2 activation,
and exactly one auditable real dispatch.

## Historical Source Rule

Git history and the append-only
[`docs/wiki/log.md`](../../../log.md) carry chronology. Current-state pages
describe current truth and must not reproduce retry ledgers, PR narratives,
test-count histories, artifact tables, or superseded mechanisms.
