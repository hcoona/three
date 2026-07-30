# Workflow Delivery v3

## Status

Active and normative.

v3 is a clean implementation line. It does not evolve the v2 control
architecture in place. Proven v2 mechanisms may be ported only through v3
Shared Foundation adapters and must not leak v2 domain or authority types into
the v3 kernel.

## Normative Pages

- [Target Architecture](./target-architecture.md)
- [Architecture Glossary](./architecture-glossary.md)
- [Migration and Document Policy](./migration-strategy.md)

## Next Design Work

1. define v3 requirements against the confirmed target architecture;
2. define Component and Release Unit authoring;
3. define Trusted Decision Kernel contracts and Authority Epoch promotion;
4. define CI Qualification contracts;
5. define Release Plan lineage contracts;
6. define Shared Foundation adapter interfaces;
7. implement one end-to-end vertical slice; and
8. expand by ecosystem and destination.
