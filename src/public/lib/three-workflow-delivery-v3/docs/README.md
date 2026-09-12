# Workflow Delivery v3

This project owns the v3 requirements, design, research and validation records.
Repository-wide governance follows the [record system](../../../../../docs/governance/record-system.md);
current work requires an accepted [Delivery Wave](../../../../../docs/delivery-wave.md)
grant in addition to the domain prerequisites below. The records were relocated
from the accepted v3 set at `0659eb96d916d4c64059b5c981ac2d8d399e60e5`;
source history remains in Git. Relocation does not authorize runtime work.

## Status

Workflow Delivery v3 is active and is the only normative Workflow Delivery design line.

The user-approved replacement normal-Live baseline is merged across the
requirements, HLD, glossary, five MLDs, migration policy, and first-slice LLD.
The implementation includes active-only Observation, fresh
exact-satisfied finalization proof, profile-bound one-shot publication,
immutable marker/Result terminal transport, and a read-only current-DAG
Finalizer with one tagged predecessor. It was protected-delivered through
PR #653 with Live disabled, whole-group validation, independent rereview, final contraction,
exact merged-tree verification, and post-merge checks complete.

PR #660 protected-delivered strict ready Governance v2, `live_enabled: true`,
and the exact independently audited native v2 contract. Post-merge checks and
platform readback passed. PR #661 delivered the supported package-level npm
metadata reader, and PR #662 delivered the transport-basename correction.
The separately authorized second real run emitted an authoritative `published`
Outcome with `possibly-mutated: false` and exact post-action readback.
Independent audit verified exact native bytes/witness and only the intended
version/tag delta; auditable proving is complete. Both dispatch authorizations are spent;
the first failed run is preserved rather than relabeled.
The v1 reservation contract was rejected by an observed deleted-version
counterexample. The active-lifecycle correction was protected-delivered through
PR #658, and its tooling through PR #659. The new native generation passed
without deletion, restoration, or deleted-state reads. Package access is
unchanged; each normal-Live run used its own Snapshot and Approval.
Retained-ref proof and obsolete-Environment cleanup
are complete. Inspect current Git and operational state through the handoff
before continuing.

Normal Live requires the admitted native contract and fresh protected ready
Governance. Native acceptance is not a proving Live outcome. The
design admits a pinned standard
`npm publish --tag ... --fetch-retries=0` profile only after its native suite
proves the required active creation, duplicate-conflict, and tag-race behavior.
Administrator deletion ends an active lifetime; retained deleted records do
not reserve the coordinate. Exact package-version bytes, digests, and witness are
authoritative; the target-derived tag is a non-authoritative routing side
effect.

Before planning or editing v3 work, read the [Workflow Delivery v3 AI Agent Handoff](./agent-handoff.md). It is
operating guidance, not a second normative specification.

## NuGet Second-Slice Implementation

The [NuGet second-slice handoff](./nuget-smoke-research-handoff.md) retains the
completed research and current requirements packet. The user selected
`Hcoona.ReleaseSmoke.GithubPackages` and confirmed its operator-controlled
smoke use. Scope, trust, and acceptance are confirmed in `WD-NUGET-*`;
the HLD and affected MLDs define the design, followed by
the [NuGet LLD](./hcoona-release-smoke-github-packages-lld.md) and its local
and native admission gates. Protected design delivery is complete. The user
subsequently delegated implementation through verified go-live without
intermediate confirmation. The disabled native Provider, frozen Build/Quality,
authoring/compiler, one-shot HTTP mechanisms, and blocked NuGet Governance
foundation is protected-delivered. Explicit NuGet Release records and
qualification integration bind frozen inputs, original archive bytes, and
separate content/consumer Evidence. Disabled destination integration adds
native eligibility, no-tag observation, zero-or-one action materialization,
and the shared Approval, Authorization, marker, Result, and Outcome contracts.
The native control CLI connects immutable Provider/Model admission and current
Eligibility/Attempt bindings. Native Build/Qualification commands connect
matched planning, frozen Build, original-package upload binding, separate
content/consumer Evidence, and the shared Finalizer. Native observation and
approval commands connect supported reads, zero-or-one action materialization,
a native reviewer summary, and current-Attempt Approval/Authorization with
fresh Governance. Native publication entries connect fresh zero-action proof,
original-archive preparation, durable-marker one-shot execution, and current-DAG
finalization. They reuse the shared terminal admission and resolution commands.
Separate manual and reusable NuGet workflows connect the native commands on
Windows with immutable helper/record transport and a build-free publisher.
Governance remains disabled. Actual Windows workflow execution and native/Live
proving remain pending; missing service-owned atomic non-overwrite assurance
blocks activation.
The [fixture and operator contract](./hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
defines native-tooling preparation. The handoff defines concrete operation and
delivery discipline.
The offline paired-fixture component implements local construction and
inspection. Its separate Windows preparation entry binds the current request,
trusted helper, admitted native/source facts and original dependencies through
immutable transport. Local scenarios do not establish actual Windows execution;
native operator integration and platform evidence remain subsequent work.
This destination has its own explicit threat/cost decision and cannot inherit
the npm slice's exceptions or evidence.

## Normative Hierarchy

Read the current v3 documents in this order:

1. [Requirements](./requirements.md)
2. [High-Level Design](./high-level-design.md)
3. [Architecture Glossary](./architecture-glossary.md)
4. Middle-level designs:
    - [Repository Model and Release Unit](./repository-model-release-unit-mld.md)
    - [Governance Integration](./governance-integration-mld.md)
    - [CI Qualification](./ci-qualification-mld.md)
    - [Release Delivery](./release-delivery-mld.md)
    - [Shared Foundation](./shared-foundation-mld.md)
5. [Migration and Document Policy](./migration-strategy.md)
6. Applicable slice LLD: [npm](./hcoona-release-smoke-npm-lld.md) or
   [NuGet](./hcoona-release-smoke-github-packages-lld.md)

Higher layers constrain lower ones, and the current set must be reconciled if a conflict appears. v1 and v2 may supply
a mechanism only when a v3 document explicitly requires extraction and revalidation.

## Current First Slice

The first vertical slice is `hcoona-release-smoke-npm`:

- CI Qualification remains shadow/manual during coexistence;
- live Buddy targets GitHub Packages; and
- Official npmjs behavior remains simulation-only.

Prior retry-5 destination acceptance is complete historical evidence. Exact
`.17` through `.20` versions and tags remain intentionally retained, but their
chronology is not current architecture.

## Current External Boundary

- Approval Environment `workflow-delivery-v3-buddy-approval` is ID
  `20895030723`, with reviewer rule `64124473`, sole reviewer
  `hcoona` / `712433`, `prevent_self_review: false`, zero wait, no secrets,
  no branch/tag restriction, `can_admins_bypass: false`, marker
  `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`,
  and the two recorded deployments listed in the handoff.
- Legacy Environment `workflow-delivery-v3-buddy-github-packages`, ID
  `20895037877`, was removed after retained-ref compatibility and exact
  no-authority-reference proof. Other Environment configurations were unchanged.
- The GitHub Packages credential principal is repository `hcoona/three`.
  Known reach includes production package `hexo-renderer-asciidoc` and
  disposable smoke packages. This accepted repository-principal blast radius
  is neither package isolation nor an exhaustive inventory.
- Package access remains unchanged. Authorized native probes created
  disposable versions and tags, and one original scenario version was
  deleted. A duplicate publish created a new same-version object; the original
  remains deleted. The first normal-Live run failed before publication, and
  the second published. No explicit restoration occurred.

## Delivery Boundary

The v1 native tooling and exact storage-origin correction are protected-delivered.
The subsequent real suite rejected its reservation contract because publishing an
identical deleted version succeeded and created a replacement object instead
of leaving the complete state unchanged. This violates the required native
v1 primitive even though the replacement bytes match.

The approved lifecycle correction retains active-version non-overwrite and
accepts possible coordinate reuse after administrator deletion, including
consumer/cache disagreement across lifetimes. Normal runtime remains
active-only and performs no administrative or history-compensation operation.
The protected v2 tooling completed five probes, three versions, two tags, and
six complete active captures. Independent audit confirmed actual native
provenance, both empty duplicate deltas, and the bounded W/V tag race.
Its admission, ready Governance, and publisher correction are protected-delivered.
The real publisher used the qualified logical basename and emitted a `published`
Outcome. Independent final audit passed, including fresh actual destination
bytes and current-run lineage. No further runtime or proving action remains;
do not repeat the completed native suite or either real run.

The [operator runbook](./agent-handoff.md#local-native-operator) describes the
clean-checkout, credential, approval, and evidence-retention prerequisites.
Preserve both failed v1 generations and the original D; their recovery is a
separate operator decision, not authorized by this model change.
The five revised acceptance probes are separate from normal-Live dispatches;
any failed gate stops further mutation without automatic repair. The handoff
records the exhausted dispatch authorization and separate recovery boundary.

## Historical Source Rule

Git history and delivery work carriers retain chronology. Current-state pages
describe current truth and must not reproduce retry ledgers, PR narratives,
test-count histories, artifact tables, or superseded mechanisms.

[Retained native and normal-Live evidence](./validation/native-and-normal-live-evidence.md)
preserves the current evidence consumers' source observations and provenance.
[NuGet research evidence](./research/nuget-smoke-evidence.md) preserves the
research inventory, operator confirmation, and source-supported NuGet design.
These are historical source extracts, not new acceptance verdicts or grants.

[CI shadow evidence](./validation/ci-shadow-evidence.md) retains the canonical
failure and bounded historical check projection without certifying a later
candidate. [Legacy workflow boundary](./research/legacy-workflow-boundary.md)
explains retained reader/contract interfaces and the v1 compatibility limit.
[GitHub Packages support research](./research/github-packages-supported-registries.md)
retains the dated ecosystem evidence and its recheck boundary.

Generic record lifecycle and contribution procedure route to the
[repository record policy](../../../../../docs/governance/record-system.md) and
[contribution guide](../../../../../CONTRIBUTING.md); the
[Delivery Wave](../../../../../docs/delivery-wave.md) owns repository work grants.
The domain handoffs retain the separate design, implementation, native, and
publication gates.
