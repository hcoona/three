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

## Next Design Work

1. review and confirm the v3 requirements baseline;
2. define the Repository and Product Model MLD;
3. define the Trusted Decision Kernel and Governance Integration MLD;
4. define the CI Qualification MLD;
5. define the Release Delivery MLD;
6. define the Shared Foundation MLD;
7. create a brief LLD for the first end-to-end vertical slice;
8. implement and validate that vertical slice; and
9. expand by ecosystem and destination.
