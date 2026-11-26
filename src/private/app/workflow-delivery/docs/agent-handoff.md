# Workflow Delivery Agent Handoff

## Authority and Purpose

Read this page before Workflow Delivery work. It routes domain records and
retained operational limits; the [portal](README.md) identifies owning records,
and the [application interface](../README.md) owns current commands, callers,
and local validation. Follow [`AGENTS.md`](../../../../../AGENTS.md),
[`record-system.md`](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency),
and [`docs/delivery-wave.md`](../../../../../docs/delivery-wave.md) for
repository-wide authorization and review rules.

Do not fill V3 decision gaps from retired v1/v2 designs unless a V3 record
explicitly requires mechanism extraction and revalidation. Internal formats need
no legacy readers; historical evidence uses
[producer-qualified readers](validation/historical-readers.md).

## Change Routing

Read the union of applicable rows below; read the complete
[requirements](requirements.md), [HLD](high-level-design.md), and
[MLD overview plus all five scopes](middle-level-design.md#scope-routes) only
when changing a requirement's meaning or an HLD component boundary. A shared
file may act as a native shared input, a `CiScope` retained-job entry, and an
ecosystem root input simultaneously—check each
[mechanism](design/repository-and-selection.md#shared-inputs-and-retained-job-scope).

| Change                                   | Read                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| One ecosystem's CI group or native facts | That ecosystem's integration file: [.NET](integration/dotnet.md#native-net-ci-group), [Node](integration/node.md#native-node-ci-group), [Python](integration/python.md#native-python-ci-group), [Ruby](integration/ruby.md#native-ruby-ci-group); MLD [CI work and results](design/ci.md#ci-work-and-result-contract); [CI qualification](requirements.md#ci-qualification)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Shared selection or planning             | [Repository and selection](design/repository-and-selection.md); [CI work and results](design/ci.md#ci-work-and-result-contract); [CI qualification](requirements.md#ci-qualification)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Shared file or its selection effect      | MLD [shared inputs and retained-job scope](design/repository-and-selection.md#shared-inputs-and-retained-job-scope), then the mechanism it names                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Package build or consumer                | [Build and qualification](design/build-and-qualification.md), including versions and original bytes; the ecosystem's package section                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Release caller or destination            | Concrete Release section and destination table: [NuGet](integration/dotnet.md#concrete-nuget-release-integration), [npm](integration/node.md#concrete-npm-release-integration), [Python](integration/python.md#concrete-python-release-integration), [Ruby](integration/ruby.md#concrete-ruby-release-integration), [GitHub Releases](integration/github-release.md#concrete-github-release-integration), [browser stores](integration/browser-store.md#destinations-and-completion); MLD [post-publication completion](design/release.md#post-publication-completion), [decision table](design/release.md#decision-table), [Release operations](design/release.md#release-operations-and-state), [registry observation](design/release.md#registry-observation-and-retention) and [review core](design/release.md#release-integration-review); [Official channel scope](integration/common.md#official-channel-scope); requirements [Release delivery](requirements.md#release-delivery), [channels](requirements.md#buddy-and-official-channels) and [recovery](requirements.md#observation-retry-and-recovery) |
| CI workflow or job wiring                | [CI cross-job view](design/ci.md#workflow-realization), [workflow callers](design/execution-boundaries.md#workflow-caller-boundaries), [permission and transfers](design/execution-boundaries.md#workflow-permission-and-transfer-boundaries); [CLI payloads](integration/common.md#cli-and-boundary-payloads)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Research evidence                        | The record a failing native contract test names; a service or decision-time check the record names, routed by the [portal](README.md#native-integration-evidence)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Records or docs only                     | The [portal](README.md) and the [record policy](../../../../../docs/governance/record-system.md); [historical readers](validation/historical-readers.md) for retired records                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Architecture or requirement change       | [Requirements](requirements.md) including its [change review](requirements.md#requirement-change-review), [HLD](high-level-design.md), and the [MLD overview and all five scopes](middle-level-design.md#scope-routes)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |

## Starting Work

1. Confirm affected requirements, native responsibilities, permission/transfer
   boundaries, concrete integration contracts, and validation basis; return
   material feasibility or product/trust tradeoffs to the owner rather than
   compensating for missing lower-layer guarantees.
2. Validate affected suites, root HK, and `git diff --check`; run implementation
   review via `open-code-review-delegate` (`.agents/skills/open-code-review-delegate/SKILL.md`),
   supplying the full Git manifest when OCR filtering omits documentation, plus
   applicable `record-system-review` and `research-evidence-review`.
3. Treat the pinned
   [NuGet delivery retrospective](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/research/nuget-delivery-retrospective.md#next-agent-guide)
   as historical context rather than workflow policy.

## Authorization and Closed Campaigns

Routine release preparation, owner-governed `workflow-delivery-v3-*-release.yml`
dispatches, and existing publisher/storefront maintenance for admitted public
product release units (`src/public/**` in
[Official channel scope](integration/common.md#official-channel-scope),
excluding `hcoona-release-smoke-*` fixtures) follow
[work authorization](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency);
agents never autonomously dispatch release workflows, approve Environments, or
run publication commands without an explicit owner instruction for that specific
operation. Local and CI checks establish tested-source conformance only, not
deployed destination or OIDC behavior.

All npm, NuGet, Python, and Ruby `hcoona-release-smoke-*` proving campaigns are
closed with spent budgets
([retired campaign records](validation/historical-readers.md#retired-campaign-records));
any future smoke/proving or native experiment requires its own accepted
`Delivery Wave` entry and bounded protocol:

- **npm:** Actions repository-token publishing reaches every package granting
  Actions access to `hcoona/three` (coordinate checks do not narrow token reach,
  and Environment self-approval is not independent review). By owner disposition
  (`@hcoona`, 2026-10-22), the original-D tombstone matter is closed and the
  removed legacy Environment remains retired
  ([evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/native-and-normal-live-evidence.md)).
- **NuGet:** Native R4, profile/fixture/host captures, and normal-Live
  postpublication audits retain separate source and run identities
  ([normal-Live](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/nuget-normal-live-evidence.md),
  [smoke research](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/research/nuget-smoke-evidence.md)).
- **Python:** The failed first native generation remains failed and incomplete,
  and per-file PyPI/TestPyPI uploads do not guarantee atomic wheel+sdist sets or
  permanent registry retention
  ([retired native acceptance](validation/historical-readers.md#retired-python-native-acceptance),
  [bootstrap](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-bootstrap.md),
  [hosted recovery](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-hosted-recovery.md),
  [normal-Live](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-normal-live-evidence.md)).
- **Ruby:** Linux caller qualification does not establish Windows Ruby or
  deployed destination behavior
  ([operation protocol](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/ruby-operation-protocol.md),
  [normal-Live](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/ruby-normal-live-evidence.md)).
