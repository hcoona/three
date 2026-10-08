# Workflow Delivery v3

This project owns the v3 requirements, design, research and validation records.
Repository-wide governance follows the [record system](../../../../../docs/governance/record-system.md);
current work requires an accepted [Delivery Wave](../../../../../docs/delivery-wave.md)
grant in addition to the domain prerequisites below. The records were relocated
from the accepted v3 set at `0659eb96d916d4c64059b5c981ac2d8d399e60e5`;
source history remains in Git. Relocation does not authorize runtime work.

For a new session, use the [handoff entry](./agent-handoff.md#starting-a-new-session)
and its route to the [NuGet delivery retrospective](./research/nuget-delivery-retrospective.md).

## Status

Workflow Delivery v3 is active and is the only normative Workflow Delivery design line.

The [requirements](./requirements.md) own contraction behavior. The replacement
[HLD](./high-level-design.md) defines the smaller internal C# architecture and
implementation order. The [middle-level design](./middle-level-design.md) closes
component, selection, execution, transfer and outcome contracts. The
[implementation plan](./migration-strategy.md) owns the concrete consumer inventory,
integration contract outline and dependency-ordered caller transitions, including
the [native .NET CI group](./migration-strategy.md#native-net-ci-group). That
group is implemented in the private application and consumed by root CI, with
ordinary Windows Debug qualification in
[PR #1054](https://github.com/hcoona/three/pull/1054). The
[feasibility record](./research/contraction-feasibility.md) distinguishes local
observations from the owner's accepted Windows assumption. The
[transition](./requirements.md#requirements-and-implementation-transition) keeps
the old glossary, pinned former MLDs/migration plan and LLDs scoped to existing code and
retained evidence. The implementation and completed campaigns described below
do not claim conformity to the replacement. The
[Wave](../../../../../docs/delivery-wave.md) covers the full contraction.
The [private application entry](../../../../private/app/workflow-delivery/README.md)
documents CI selection/result, GitHub event comparison, control-test resource inputs,
native Node/.NET fact assembly and check execution. Root CI's existing Windows
scope job plans both .NET endpoints and transfers the original selected plan and
complete control distribution to its sole .NET execution/result owner. The [bounded native observation](./research/node-ci-native-execution.md)
qualifies the adopted fixture composition on Linux. Root CI owns the adopted Node
group and selected retained Node matrix, with ordinary hosted transfer and
execution qualification in [PR #1042](https://github.com/hcoona/three/pull/1042).
The exclusive npm CI shadow is retired. Other quality presets, Python/Ruby
execution cutover and Release remain pending.

The [native Python planning contract](./middle-level-design.md#native-python-planning-facts)
has passive extraction, a pinned Rust supplement, ordered configuration
preflight, metadata adaptation and operation dependency projection with controlled
unit boundaries. The private app's [bounded native pytest configuration component](../../../../private/app/workflow-delivery/README.md#native-pytest-configuration-component)
also has controlled tests; complete native ownership and query qualification
remain pending. The [paired Python producer-to-owner component](../../../../private/app/workflow-delivery/README.md#paired-python-producer-to-owner-component)
has controlled supplied-fact coverage. The
[controlled paired Python fact assembly](../../../../private/app/workflow-delivery/README.md#controlled-paired-python-fact-assembly)
binds supplied native targets and actual checks. The
[controlled native Python graph collector](../../../../private/app/workflow-delivery/README.md#controlled-native-python-graph-collection)
composes metadata, passive inputs and ordered native dependency answers with
caller operations bound to the same metadata. The separately accepted
[current-tool observation](./research/python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies exact complete ordinary/no-dev graph composition on its pinned Linux
subjects; its campaign and cleanup are complete, and remaining captures stopped. The [controlled Python repository input associations](../../../../private/app/workflow-delivery/README.md#controlled-python-repository-input-associations)
bind supplied actual check owners to repository engineering inputs; native queries
and full caller behavior remain pending. Package preset expansion and
preparation/execution remain pending. The [primitive Linux observation](./research/python-native-planning.md#primitive-native-observation)
qualifies the pinned helper composition. Native pytest/NBGV input composition, complete native
caller qualification and caller mapping remain pending.

The pre-contraction normal-Live baseline was merged across the
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

## Ruby Smoke Design

The owner selected the complete Ruby journey under [Issue #954](https://github.com/hcoona/three/issues/954).
[`WD-RUBY-*`](./requirements.md#ruby-smoke-slice), the [HLD extension](https://github.com/hcoona/three/blob/c56b1efa64637f056b63a497aabbdaf33c1fbf1f/src/public/lib/three-workflow-delivery-v3/docs/high-level-design.md#ruby-smoke-extension)
and [Ruby LLD](./hcoona-release-smoke-ruby-lld.md) define the minimal integration.
The [source evidence](./research/ruby-smoke-evidence.md) preserves unverified
GitHub duplicate behavior and RubyGems.org's conditional replay finding.
The [accepted normal Live evidence](./validation/ruby-normal-live-evidence.md)
records both destination completions, retained failures and evidence limits.
The [current status](../README.md#ruby-build-and-qualification) and
[handoff](./agent-handoff.md#ruby-delivery-entry) record stopped campaigns,
all-null request slots and retired external callers. The
[migration order](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#ruby-smoke-delivery) remains the
delivery contract; future work requires its own accepted Wave authority.

## Python Smoke Design

The [hosted partial-publication recovery](./validation/python-hosted-recovery.md)
is independently complete for TestPyPI `0.1.0b39`: a fresh Attempt rebuilt both
archives byte-identically, retained the existing wheel and uploaded only the
missing sdist. Both clean consumers passed; the seed Outcome remains failed.
The campaign is stopped and its temporary interruption control and operators
are retired. The [ordinary recovery contract](./hcoona-release-smoke-python-lld.md#approval-and-terminal-contract)
retains both exact single-file subsets; the inverse subset has local evidence
only. Preserve the stable workflow/publisher/Environment and historical evidence.
This recovery completion grants no further operation. The separate production
completion evidence below does not reopen this recovery campaign.

[Issue #843](https://github.com/hcoona/three/issues/843) tracks TestPyPI first,
then PyPI. The owner's confirmed [`WD-PY-*`](./requirements.md#python-smoke-slice),
HLD, five MLDs and [Python LLD](./hcoona-release-smoke-python-lld.md) adopt
platform reliance without duplicate/race proving. The
[source evidence](./research/python-smoke-evidence.md) distinguishes source
findings, the retained failed runs and that owner decision.
The [migration policy](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#python-smoke-delivery) routes the
disabled implementation, configuration/admission and actual publication
stages. Runtime and both protected files use strict Python Governance v2.
Normal publication and fresh clean wheel/sdist consumption are
[independently complete](./validation/python-normal-live-evidence.md) for
TestPyPI `0.1.0b26` and production PyPI `0.1.0b53`. The production
completion joins a failed uploading Attempt with a fresh `exact-satisfied`
Attempt and its native consumer audit. Both completion campaigns are stopped;
earlier failed Attempts remain failed and untouched. The
[handoff](./agent-handoff.md#starting-a-new-session) routes current operation limits.
The [retired native interface](./validation/python-native-readiness.md) retains
historical evidence-reader consumers only.

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
The [audited native generation and current Governance evidence](./research/nuget-smoke-evidence.md#2026-09-15-query--native-acceptance-and-governance-activation)
are bound by ready Governance with Live enabled. Windows normal-Live publication
and fresh destination consumption are [independently verified](./validation/nuget-normal-live-evidence.md).
Admission implements
the `WD-NUGET-006` dependency with the exact accepted six-field generation;
no separate GitHub concurrent-atomicity statement is required. The
[credential-free Windows profile observer](../README.md) supplies tooling for
a separately reviewed observation after complete preflight resource admission.
The [fixture and operator contract](./hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
defines native-tooling preparation. The handoff defines concrete operation and
delivery discipline.
The offline paired-fixture component implements local construction and
inspection. Its separate Windows preparation entry binds the current request,
trusted helper, admitted native/source facts and original dependencies through
immutable transport. A bounded Windows preparation run has passed independent
artifact-body and lineage audit. Its [retained evidence](https://github.com/hcoona/three/issues/676)
binds the original A/B pair and A's clean local consumer to the exact protected
source and current run. The [capture evidence component](../README.md)
connects bounded reads and retained complete or partial observations. The local
read operator adds an executable preflight, immutable-input comparisons, POSIX
process supervision and fixed cumulative capture allowances. The separate
[one-probe Windows entry](../README.md) binds the current run to the original
fixture/helper inputs, inspects without publication authority and retains a
durable marker plus HTTP or partial-failure evidence from its one-shot publisher.
Exact native requests and independent helper/runtime admission remain required.
The [destination consumer component](../README.md) adds bounded native restore
and exact installed-byte checks before credential-free build and marker
invocation. Its local controlled evidence supplies no native admission.
The fixed-suite operator implements the full three-probe sequence and six
capture/delta integration. Its actual destination consumption and native
acceptance passed independent audit. The separate [normal-Live evidence](./validation/nuget-normal-live-evidence.md)
closes publication and consumption of the actual Live package. Local command availability
and expected HTTP status do not admit native provenance or publication.
This destination has its own explicit threat/cost decision and cannot inherit
the npm slice's exceptions or evidence.

## Normative Hierarchy

New design starts with the [requirements](./requirements.md), then the
replacement [HLD](./high-level-design.md) and
[middle-level design](./middle-level-design.md). The HLD owns architecture and
implementation order; the MLD owns common component and behavior contracts.
The [implementation plan](./migration-strategy.md) maps the actual cutover groups
and their validation dependencies.
Local observations and the Windows assumption
are recorded in [contraction feasibility](./research/contraction-feasibility.md).
The [Python native planning source assessment](./research/python-native-planning.md)
examines UV metadata and native build-source association candidates for the next
Python integration.
The [PNPM native planning assessment](./research/pnpm-native-planning.md) compares
the pinned v12 library and CLI interfaces for replacement repository analysis.
The [Git native fact evidence](./research/git-native-facts.md) supports committed
revision inventory, direct endpoint changes and strict native path decoding.
The [MSBuild native item evidence](./research/msbuild-native-items.md) supports a
narrow evaluated resource-input projection for a specified project/dimension;
root CI consumes this resource projection as part of the native .NET group.
Its wider membership, facts and group qualification have their separate basis in
the [native group contract](./migration-strategy.md#native-net-ci-group) and
[PR #1054](https://github.com/hcoona/three/pull/1054).
The [NBGV native-input assessment](./research/nbgv-native-inputs.md) identifies
public configuration/filter APIs and bounded Linux application observations for
official-package delivery and the existing PNPM reader. Root Node/.NET integration
has separate hosted qualification; Python/Ruby execution cutover and Release
remain pending.
The [Node CI native execution observation](./research/node-ci-native-execution.md)
retains the completed adopted-fixture Linux composition/effects evidence,
independent reviews and stopped protocol. It does not establish caller cutover.

Load these implementation-baseline records only for their existing code or
historical-evidence consumers, under the
[transition](./requirements.md#requirements-and-implementation-transition):

1. [Pre-contraction glossary](./architecture-glossary.md)
2. Former middle-level designs, pinned at `72939154ab9ebfb8d313908c551fb66eeefae499`
   for existing runtime and evidence readers:
    - [Repository Model and Release Unit](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/repository-model-release-unit-mld.md)
    - [Governance Integration](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/governance-integration-mld.md)
    - [CI Qualification](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md)
    - [Release Delivery](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/release-delivery-mld.md)
    - [Shared Foundation](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/shared-foundation-mld.md)
3. [Pre-contraction migration policy](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)
4. Applicable slice LLD: [npm](./hcoona-release-smoke-npm-lld.md),
   [NuGet](./hcoona-release-smoke-github-packages-lld.md), or
   [Python](./hcoona-release-smoke-python-lld.md), and
   [Ruby](./hcoona-release-smoke-ruby-lld.md)

The five former MLD files are retired from the current tree. Their original
content, anchors and inter-document links remain at the pinned Git revision;
no historical contract is rewritten as the replacement design. Their historical
HLD references remain pinned to the implementation source. They cannot fill a
replacement-design gap with a retired requirement. v1/v2
mechanisms need explicit v3 extraction and revalidation before reuse.

## Current First Slice

The first vertical slice is `hcoona-release-smoke-npm`:

- root CI owns the adopted Node checks and selected retained Node matrix;
- live Buddy targets GitHub Packages; and
- Official npmjs behavior remains simulation-only.

Prior retry-5 destination acceptance is complete historical evidence. Exact
`.17` through `.20` versions and tags remain intentionally retained, but their
chronology is not current architecture.
The [project entry](../README.md) describes the retired fixed-coordinate
producer surface and the retained historical reader.

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

## Python First-Project Bootstrap

The [bootstrap protocol](./validation/python-bootstrap.md) defines the separately
authorized first-project resource path under `WD-PY-009` and its
[tooling interface](./validation/python-bootstrap.md#tooling-interface).
The stable bootstrap entry supports destination-bound TestPyPI and production
PyPI operations. Both protected bootstrap slots are null after the production
generation failed at wheel readback. Its original failure and partial/unknown
effects remain retained; no repeated bootstrap or automatic completion is
authorized. The separately completed [production journey](./validation/python-normal-live-evidence.md#production-pypi-completion)
does not repair that bootstrap version or revive its request.
The [Delivery Wave](../../../../../docs/delivery-wave.md) owns current work
authorization, and the [handoff](./agent-handoff.md#starting-a-new-session)
routes the accepted operation, configuration and remaining evidence gates.
