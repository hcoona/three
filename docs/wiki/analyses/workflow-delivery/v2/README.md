# Workflow Delivery v2 Archive

## Status

Archived prototype and mechanism source. Not normative for v3.

The canonical v2 snapshot is commit
[`8824df2a12c78a1f3a851a3c2763bcb9e64f2412`](https://github.com/hcoona/three/tree/8824df2a12c78a1f3a851a3c2763bcb9e64f2412).

The v2 branch contains the complete design corpus, implementation, tests,
fixtures, platform experiments, and rollout records. Keeping that immutable
snapshot is preferable to copying superseded normative pages into v3.

## Do Not Port as v3 Normative Design

- requirements baseline and requirements-phase review;
- design direction and architecture model;
- descriptor schema and `three.release.plan/v1alpha1`;
- workflow and executor boundaries;
- low-level design, implementation plans, and rollout runbooks;
- CI affected-validation requirements and HLD/MLD/LLD;
- Buddy same-tag promotion, `FORCE`, and v2 replay semantics; and
- v2 control-plane workflow and report contracts.

## Candidate Mechanism Sources

The following may be ported behind v3 adapters after independent review:

- canonical JSON, digests, and closed-schema utilities;
- artifact enumeration and producer-admission logic;
- ecosystem build and package execution;
- repository dependency and fact discovery;
- provenance and attestation verification;
- remote-state and digest comparison algorithms;
- destination-specific OIDC and idempotency knowledge; and
- smoke projects, fixtures, and mechanism-level tests.

## Candidate Research Sources

Observed platform facts may be extracted from these v2 records, but v2 design
conclusions remain archived:

- [OIDC publish topology](https://github.com/hcoona/three/blob/8824df2a12c78a1f3a851a3c2763bcb9e64f2412/docs/wiki/analyses/workflow-release-oidc-publish-topology.md)
- [Artifact enumeration experiment](https://github.com/hcoona/three/blob/8824df2a12c78a1f3a851a3c2763bcb9e64f2412/docs/wiki/analyses/workflow-release-ci-affected-validation-artifact-enumeration-experiment.md)
- [Producer identity experiment](https://github.com/hcoona/three/blob/8824df2a12c78a1f3a851a3c2763bcb9e64f2412/docs/wiki/analyses/workflow-release-ci-affected-validation-producer-identity-experiment.md)
- [No-authoritative-plan experiment](https://github.com/hcoona/three/blob/8824df2a12c78a1f3a851a3c2763bcb9e64f2412/docs/wiki/analyses/workflow-release-ci-affected-validation-no-authoritative-plan-experiment.md)
- [Platform spike summary](https://github.com/hcoona/three/blob/8824df2a12c78a1f3a851a3c2763bcb9e64f2412/docs/wiki/analyses/workflow-release-ci-affected-validation-platform-spike-summary.md)
