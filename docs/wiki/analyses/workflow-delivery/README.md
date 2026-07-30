# Workflow Delivery Architecture Versions

This directory is the version boundary for workflow-delivery architecture.

## Normative Priority

1. [v3](./v3/README.md) is the active target architecture and the only
   normative source for new implementation work.
2. [v2](./v2/README.md) is an archived prototype and mechanism source.
3. [v1](./v1/README.md) is the historical `origin/main` baseline.

When documents disagree, v3 wins. v1 and v2 must not be used to fill a missing
v3 decision implicitly.

## Document Policy

- New normative architecture, requirements, contracts, and migration decisions
  belong under `v3/`.
- v2 normative design pages remain frozen at the v2 commit and are not copied
  into the clean v3 implementation line.
- Platform experiments may be ported only after separating observed platform
  facts from v2-specific design conclusions and revalidating assumptions that
  can change.
- Repository source digests may be ported selectively when the underlying fact
  remains current.
- The top-level wiki overview and index describe v3, not the archived v2
  prototype.
