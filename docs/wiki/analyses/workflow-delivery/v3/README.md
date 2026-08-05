# Workflow Delivery v3

## Status

Active and normative.

v3 is a clean implementation line. It does not evolve the v2 control
architecture in place. Proven v2 mechanisms may be ported only through reviewed
v3 Provider, Adapter, or client boundaries and must not leak v2 domain or
authority types into v3 CI or Release decision models.

## AI Agent Handoff

Agents continuing v3 work must read the
[Workflow Delivery v3 AI Agent Handoff](./agent-handoff.md)
before planning or editing.

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
- [Shared Foundation MLD](./shared-foundation-mld.md)

## Current Design Work

The first vertical slice is `hcoona-release-smoke-npm`:

- CI Qualification;
- live Buddy publication to GitHub Packages; and
- Official npmjs dry-run.

Next:

1. confirm the slice scenarios and brief LLD;
2. obtain explicit LLD approval and remaining implementation instructions;
3. implement and validate the slice; and
4. expand by ecosystem and destination only after acceptance.
