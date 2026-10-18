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
for an architecture or requirement change. Use actual native sources for
operations.

| Change                                   | Read                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| One ecosystem's CI group or native facts | That ecosystem's integration file: [.NET](integration/dotnet.md#native-net-ci-group), [Node](integration/node.md#native-node-ci-group), [Python](integration/python.md#native-python-ci-group), [Ruby](integration/ruby.md#native-ruby-ci-group); MLD [CI work and results](middle-level-design.md#ci-work-and-result-contract); [CI qualification](requirements.md#ci-qualification)                                                                                                                                                                                    |
| Shared selection or planning             | MLD [repository facts and impact selection](middle-level-design.md#repository-facts-and-impact-selection); HLD [repository facts](high-level-design.md#repository-facts-and-selection); [CI qualification](requirements.md#ci-qualification)                                                                                                                                                                                                                                                                                                                             |
| Package build or consumer                | MLD [builds, packages and consumers](middle-level-design.md#builds-packages-and-consumers); HLD [versions and original bytes](high-level-design.md#versions-original-bytes-and-provenance); the ecosystem's package section                                                                                                                                                                                                                                                                                                                                              |
| Release caller or destination            | The ecosystem's concrete Release section and destination table; MLD [Release operations](middle-level-design.md#release-operations-and-state), [registry observation](middle-level-design.md#registry-observation-and-retention) and [review core](middle-level-design.md#release-integration-review); [Official channel scope](integration/common.md#official-channel-scope); requirements [Release delivery](requirements.md#release-delivery), [channels](requirements.md#buddy-and-official-channels) and [recovery](requirements.md#observation-retry-and-recovery) |
| CI workflow or job wiring                | MLD [CI cross-job view](middle-level-design.md#ci-work-and-result-contract), [workflow callers](middle-level-design.md#workflow-caller-boundaries), [permission and transfer boundaries](middle-level-design.md#workflow-permission-and-transfer-boundaries); [CLI payloads](integration/common.md#cli-and-boundary-payloads); HLD [execution boundaries](high-level-design.md#execution-and-permission-boundaries)                                                                                                                                                      |
| Research recheck                         | The record's recheck section, routed by the [portal](README.md#native-integration-evidence)                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Records or docs only                     | The [portal](README.md) and the [record policy](../../../../../docs/governance/record-system.md); [historical readers](validation/historical-readers.md) for retired records                                                                                                                                                                                                                                                                                                                                                                                             |
| Architecture or requirement change       | [Requirements](requirements.md) including its [change review](requirements.md#requirement-change-review), [HLD](high-level-design.md) and [MLD](middle-level-design.md)                                                                                                                                                                                                                                                                                                                                                                                                  |

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
   root HK, hooks and `git diff --check`, before independent engineering/OCR (open-code-review delegate),
   record-system and research-evidence review.
4. Keep terminal outcomes, originals and source/run identities intact. Await
   terminal tool/worker output and verify evidence before dependent effects.

Return fixes to original reviewers until clear. For Workflow Delivery reviews,
use `open-code-review-delegate`, including the complete Git manifest when OCR
filtering omits documentation, and contract after every two review iterations,
after changes/reviews and before PR preparation. The pinned
[NuGet delivery retrospective](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/research/nuget-delivery-retrospective.md#next-agent-guide)
retains practical Windows/PowerShell, timestamp, helper-basename and
draft-path delivery lessons as historical lessons, not rules.

## Authorization and Closed Campaigns

All npm, NuGet, Python and Ruby proving campaigns are complete and stopped.
[Domain values](high-level-design.md#domain-values) define campaign, original D,
Native R4 and normal-Live, and note how generation, slot and lifetime are used. Their operation slots, failed lifetimes and dispatch/recovery ceilings remain
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
first failed run and the second actual publication independently. The original
D and active replacement remain an unresolved exceptional administrative
recovery matter; do not delete, restore, recreate or compensate for them.
Active non-overwrite does not promise lifetime-global reservation after
administrative deletion. The removed legacy Environment must not be recreated.
[Original native and publication evidence](validation/native-and-normal-live-evidence.md)
retains exact identities, external observations, accepted reach and counterexamples.

### NuGet retained limits

Native R4, ready historical configuration, actual publication and joined
postpublication audit retain their original source and support scope. The
[normal-Live evidence](validation/nuget-normal-live-evidence.md) and
[research evidence](research/nuget-smoke-evidence.md) preserve separate profile,
fixture/provider, prebuilt consumer, native suite and normal-run lifetimes.
Do not replay, rebuild historical helpers as current qualification, dispatch,
republish, change feed/package access or repeat completed verification.
Future exceptional or live operations require their own concrete authorization.

### Python retained limits

The first native generation remains failed and incomplete. Later corrected
preparation and caller evidence does not relabel it, refund its slots or
replace its actual producer. [Retired native acceptance](validation/historical-readers.md#retired-python-native-acceptance),
[bootstrap](validation/python-bootstrap.md),
[ordinary recovery](validation/python-hosted-recovery.md) and
[normal-Live evidence](validation/python-normal-live-evidence.md) retain their
actual subjects and spent ceilings. Python's native per-file guarantees do not
promise an atomic wheel/sdist set or permanent registry retention.

### Ruby retained limits

The [operation protocol](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/ruby-operation-protocol.md) and
[normal-Live evidence](validation/ruby-normal-live-evidence.md) retain stopped
destinations, original publication/consumer observations and spent bootstrap,
native, normal and recovery slots. Linux caller qualification does not claim
Windows Ruby operations or deployed authentication/destination behavior.
The old operation CLI and bootstrap entry are not fallback execution paths.
