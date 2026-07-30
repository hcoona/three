# Workflow Delivery v3 Migration and Document Policy

## Decision

Build v3 on a clean implementation line and selectively port proven v2
mechanisms. Do not refactor the v2 control architecture in place.

v1 remains the production compatibility baseline until v3 governance and
workflow identities are activated. v2 never becomes an intermediate production
architecture.

## Why v2 Is Not an Incremental Base

v2 and v3 differ at their architectural roots:

- candidate-owned versus current-authority CI decisions;
- project-centric versus Component and Release Unit domain models;
- one pre-build Release Plan versus a two-snapshot Plan lineage;
- shared Buddy/Official identity versus isolated preview and canonical identity;
- mixed control and execution boundaries versus three runtime trust zones; and
- GitHub job rerun semantics versus whole-release replay.

Changing these in place would create long-lived intermediate states that mix
incompatible authority, identity, Evidence, and replay contracts.

## Implementation-Line Strategy

1. Preserve the v2 commit as the full archive and mechanism source.
2. Create the v3 branch from the current repository mainline for a clean diff;
   this is a Git baseline choice, not architectural reuse of v1.
3. Port this versioned v3 documentation first.
4. Create new v3 domain and kernel namespaces with no imports from v2 Plan,
   project, profile, proof, report, or control-plane types.
5. Port mechanisms through anti-corruption adapters.
6. Implement one vertical slice before expanding across ecosystems.
7. Switch required checks, workflow identities, Environments, and Registry trust
   only after v3 acceptance is complete.

## Documentation Selection

### Port

- v3 target architecture, glossary, and migration decisions;
- current repository facts required to model Components and Release Units;
- revalidated GitHub Actions and Registry platform observations;
- mechanism behavior needed to specify adapter contracts; and
- new v3 acceptance evidence.

### Rewrite

- product and system requirements;
- Component and Release Unit authoring;
- CI Qualification HLD/MLD/LLD;
- Release Delivery HLD/MLD/LLD;
- authority and governance design;
- operator runbooks; and
- implementation and rollout plans.

### Do Not Port

- v2 normative requirements and design pages;
- v2 implementation completion records;
- v2 rollout readiness claims;
- v2 wiki overview and index;
- v2 workflow documentation as active guidance; and
- the v2 wiki log as the v3 active chronology.

### Extract and Revalidate

Platform experiment pages must be rewritten as version-neutral observations.
The extracted page must distinguish:

- observed platform behavior;
- observation date and workflow/run evidence;
- assumptions that may expire;
- v2-specific interpretation; and
- the new v3 consequence.

## Code and Test Selection

Mechanism code may be ported when it can be expressed behind a v3 adapter
without importing v2 domain types.

Mechanism-level tests and fixtures may be ported with the code. Tests that assert
v2 workflow topology, schema shape, project identity, Buddy promotion, or
candidate-owned authority must remain in the v2 archive.

## External-State Inventory

Before v3 activation, inventory:

- GitHub Rulesets and required-check names;
- protected Environments and reviewers;
- OIDC workflow identities and claims;
- Registry trusted-publisher registrations;
- GitHub Packages permissions;
- concurrency identities; and
- any live v1 or experimental v2 publication state.

Parallel implementation is allowed. Parallel authoritative CI decisions or
parallel live publishers are not.
