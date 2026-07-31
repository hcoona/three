# Wiki Overview

This page holds the current top-level synthesis of the wiki.

## Current Architecture Version

Workflow delivery architecture **v3** is active and normative.

- [Architecture version entry point](./analyses/workflow-delivery/README.md)
- [v3 requirements](./analyses/workflow-delivery/v3/requirements.md)
- [v3 high-level design](./analyses/workflow-delivery/v3/high-level-design.md)
- [v3 Repository Model and Release Unit MLD](./analyses/workflow-delivery/v3/repository-model-release-unit-mld.md)
- [v3 architecture glossary](./analyses/workflow-delivery/v3/architecture-glossary.md)
- [v3 migration and document policy](./analyses/workflow-delivery/v3/migration-strategy.md)

v1 is the historical `origin/main` baseline. v2 is an archived prototype at
commit `8824df2a12c78a1f3a851a3c2763bcb9e64f2412`. Neither version is normative
for new v3 implementation work.

## Confirmed v3 Shape

- CI Qualification and Release Delivery are peer bounded contexts.
- Delivery Governance is an external authority boundary.
- CI and Release each own same-revision planning, Evidence Admission, and
  finalization while GitHub Governance supplies authority.
- Shared Foundation providers and adapters supply repository modeling, build,
  quality, and destination mechanisms without owning business policy.
- Decision, Build and Qualification, and Side-Effect Zones are separate runtime
  trust boundaries.
- Release Unit and Qualification Target are the core domain objects. Project
  Nodes and dependency relationships are discovered technical facts.
- CI and Release share Build Definitions and adapters but do not share runtime
  Plans, Evidence, artifacts, or verdicts.
- Release uses one logical Plan lineage with immutable Qualification and
  Publication snapshots.
- Buddy is an isolated distributable preview channel. Official is canonical,
  authoritative publication.
- Release retry uses whole-release replay and normal pre-side-effect
  Remote-State Observation.
- Break-Glass Remediation is independently authorized and append-only.
- Platform-native retention is used without assuming a permanent Release ledger.

## Implementation Direction

v3 will be built on a clean implementation line.

- Do not evolve the v2 control architecture in place.
- Preserve v2 at its immutable commit as a design and mechanism archive.
- Port only reviewed mechanism assets behind v3 adapters.
- Rewrite requirements, architecture layers, contracts, runbooks, and rollout
  plans for v3.
- Start with one end-to-end vertical slice before expanding across ecosystems
  and destinations.
- Keep v1 as the production compatibility baseline until v3 governance and
  workflow identities are ready for atomic activation.

Parallel implementation is acceptable. Parallel authoritative CI decisions or
parallel live publishers are not.

## Documentation Boundary

New normative delivery pages belong under
`docs/wiki/analyses/workflow-delivery/v3/`.

The v2 normative corpus remains in the archived v2 commit and must not be copied
into the v3 line. Platform experiments may be extracted only after separating
observed facts from v2 design conclusions and revalidating assumptions that may
have changed.

## Next Architecture Work

1. Review and confirm the Repository Model and Release Unit MLD.
2. Define the Governance Integration and Shared Decision Primitives MLD.
3. Define the CI Qualification MLD.
4. Define the Release Delivery MLD.
5. Define the Shared Foundation MLD.
6. Select the first vertical slice and create its brief LLD.

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Workflow Delivery Architecture Versions](./analyses/workflow-delivery/README.md)
