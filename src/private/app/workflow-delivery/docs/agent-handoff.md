# Workflow Delivery Agent Handoff

## Authority and Purpose

Read this page before Workflow Delivery work. It routes work and retained
operation limits; it is not a second specification. The [portal](README.md)
identifies the owning records, and the [application interface](../README.md)
owns current commands, callers and local validation. The
[change routing](#change-routing) table names the sections to read before a
change.

The accepted [Wave](../../../../../docs/delivery-wave.md) is the sole positive
work authority. Further substantive work requires an accepted entry. A PR,
proposed document, research result, local observation or merged source cannot
grant itself broader effects. Do not use Workflow v1/v2 to fill a V3 decision
gap. Internal formats may break; there is no requirement to retain old readers
in the new core. Historical readers use
[actual producer-qualified source](validation/historical-readers.md); no
compatibility tree, runtime replay service or parallel source authority is
retained.

## Change Routing

Read the sections for the change; read the whole requirements, HLD and MLD only
for an architecture or requirement change, which changes a requirement's meaning
or an HLD responsibility or boundary. The MLD consists of its component overview
and the five [scoped designs](middle-level-design.md#scope-routes); an architecture
or requirement change reads that whole design, not just the overview.
Changing one destination's concrete
conditions within those requirements uses the Release row. Use actual native
sources for operations; the [source layout](../README.md#source-layout) maps
the code. When several rows apply, read their union. A shared file may
act as a native shared input, a retained-job list entry in `CiScope` and an
ecosystem root input at once; check each applicable
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

Root [agent instructions](../../../../../AGENTS.md) route the accepted
governance, record policy, Wave and shared engineering guidance.
[Agent execution and recovery](../../../../../docs/engineering/agent-execution.md)
owns inspecting worker ownership, worktree state, checks and unresolved findings
and exact-session recovery, and
[GOV-011](../../../../../docs/governance/governance-system.md#gov-011-separate-findings-from-disposition)
owns independent classification of material findings. Workflow Delivery adds:

1. Inspect the actual Git branch, worktree/index state, accepted target and PR
   checks. Preserve unrelated user changes.
2. Confirm affected requirements, native responsibility, transfer/permission
   boundaries, concrete integration contract and validation basis. New
   feasibility or material product/trust/cost tradeoffs return to the owner;
   do not compensate by implementing missing lower-layer guarantees.
3. Prepare and validate complete affected changes, including applicable tests,
   root HK, hooks and `git diff --check`, before independent engineering/OCR review,
   record-system and research-evidence review.
4. Keep terminal outcomes, originals and source/run identities intact. Await
   terminal tool/worker output and verify evidence before dependent effects.

Return fixes to original reviewers until clear. For Workflow Delivery reviews,
use the open-code-review (OCR) delegate skill, the APM dependency
`open-code-review-delegate` declared in the root `apm.yml` and deployed as the generated
`.agents/skills/open-code-review-delegate/SKILL.md`, including the complete Git
manifest when OCR filtering omits documentation. After every two review
iterations, after changes and reviews, and before PR preparation, review the
whole change again and remove anything unnecessary, duplicated or wordy. "Every merged Wave change" in the root agent instructions means a merged
change to `docs/delivery-wave.md` ([record policy](../../../../../docs/governance/record-system.md#work-authorization-and-concurrency)). The pinned
[NuGet delivery retrospective](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/research/nuget-delivery-retrospective.md#next-agent-guide)
retains practical Windows/PowerShell, timestamp, helper-basename and
draft-path delivery lessons as historical lessons, not rules.

## Authorization and Closed Campaigns

All npm, NuGet, Python and Ruby proving campaigns are complete and stopped.
Their record-specific terms and original outcomes remain in the
[historical campaign records](validation/historical-readers.md#retired-campaign-records).
Their operation slots, failed lifetimes and dispatch/recovery ceilings remain
spent. No source change, artifact download, requirements revision, Wave
contraction or evidence acceptance authorizes a new campaign generation, rerun,
release/proving dispatch, publication, authentication, Environment, account,
publisher or access change. Existing external resources and publisher
registrations are unchanged by source retirement. Native experiments still
require their exact accepted bounded protocol and effects authorization.

Ordinary CI/PR delivery is separate from a release request. Local or controlled
checks establish only their actual application/tool/environment scope. They do
not establish deployed destination behavior, OIDC or publication. Changed
source, fixture paths or inherited configuration requires its own ordinary
qualification; an earlier passing native run cannot qualify a different source.
No manually initiated postmerge validation is implied.

### npm retained limits

The original npm acceptance uses the real Actions repository token, not a PAT
substitute. The accepted sole-writer publisher trust boundary does not isolate
the credential to a smoke package: packages granting Actions access to
`hcoona/three` remain in effective reach. Intended-coordinate checks do not
establish narrower credential reach or protect against a malicious accepted
writer. Environment self-approval is not independent security review.

The completed v2 generation and both normal-Live runs are spent. Preserve the
first failed run and the second actual publication independently. By owner
disposition (@hcoona, 2026-10-22), the original D administrative matter is
closed: the deleted original D is retained as a tombstone record and the active
replacement remains as-is. No deletion, restoration, recreation or compensation
is required or authorized.
Active non-overwrite does not promise lifetime-global reservation after
administrative deletion. The removed legacy Environment must not be recreated.
[Original native and publication evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/native-and-normal-live-evidence.md)
retains exact identities, external observations, accepted reach and counterexamples.

### NuGet retained limits

Native R4, ready historical configuration, actual publication and the joined
postpublication audit retain their original source and support scope. The
[normal-Live evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/nuget-normal-live-evidence.md) and
[research evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/research/nuget-smoke-evidence.md) preserve separate profile,
fixture/provider, prebuilt consumer, native suite and normal-run lifetimes.
Do not replay, rebuild historical helpers as current qualification, dispatch,
republish, change feed/package access or repeat completed verification.
Future exceptional or live operations require their own concrete authorization.

### Python retained limits

The first native generation remains failed and incomplete. Later corrected
preparation and caller evidence does not relabel it, refund its slots or
replace its actual producer. [Retired native acceptance](validation/historical-readers.md#retired-python-native-acceptance),
[bootstrap](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-bootstrap.md),
[ordinary recovery](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-hosted-recovery.md) and
[normal-Live evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/python-normal-live-evidence.md) retain their
actual subjects and spent ceilings. Python's native per-file guarantees do not
promise an atomic wheel/sdist set or permanent registry retention.

### Ruby retained limits

The [operation protocol](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/ruby-operation-protocol.md) and
[normal-Live evidence](https://github.com/hcoona/three/blob/0bd6f5049f683d9b9963e1855f21a87e64849556/src/private/app/workflow-delivery/docs/validation/ruby-normal-live-evidence.md) retain stopped
destinations, original publication/consumer observations and spent bootstrap,
native, normal and recovery slots. Linux caller qualification does not claim
Windows Ruby operations or deployed authentication/destination behavior.
The old operation CLI and bootstrap entry are not fallback execution paths.
