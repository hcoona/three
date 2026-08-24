# Workflow Delivery Architecture Versions

This directory is the version boundary for workflow-delivery architecture.

## Normative Priority

1. [v3](./v3/README.md) contains the active requirements and architecture and
   is the only normative source for new implementation work.
2. v2 is an archived prototype and mechanism source at immutable commit
   `8824df2a12c78a1f3a851a3c2763bcb9e64f2412`.
3. v1 is the production compatibility baseline represented by base
   `7f8f41c2ecb53e43848d7db4b7d17a8f46f10283`.

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
