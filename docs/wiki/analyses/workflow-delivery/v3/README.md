# Workflow Delivery v3

## Status

Active and normative.

v3 is a clean implementation line. It does not evolve the v2 control
architecture in place. Proven v2 mechanisms may be ported only through v3
Shared Foundation adapters and must not leak v2 domain or authority types into
v3 CI or Release decision models.

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

## Next Design Work

1. extract the Shared Foundation MLD from confirmed CI and Release mechanisms;
2. create a brief LLD for the first end-to-end vertical slice;
3. implement and validate that vertical slice; and
4. expand by ecosystem and destination.
