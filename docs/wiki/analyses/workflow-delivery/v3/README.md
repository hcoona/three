# Workflow Delivery v3

## Status

Active and normative.

v3 is a clean implementation line. It does not evolve the v2 control
architecture in place. Proven v2 mechanisms may be ported only through v3
Shared Foundation adapters and must not leak v2 domain or authority types into
the v3 kernel.

## Normative Pages

- [Requirements](./requirements.md)
- [High-Level Design](./high-level-design.md)
- [Architecture Glossary](./architecture-glossary.md)
- [Migration and Document Policy](./migration-strategy.md)

## Middle-Level Design

- [Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md)

## Next Design Work

1. review and confirm the Repository Model and Release Unit MLD;
2. define the Trusted Decision Kernel and Governance Integration MLD;
3. define the CI Qualification MLD;
4. define the Release Delivery MLD;
5. define the Shared Foundation MLD;
6. create a brief LLD for the first end-to-end vertical slice;
7. implement and validate that vertical slice; and
8. expand by ecosystem and destination.
