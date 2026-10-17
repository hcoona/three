# Workflow Delivery Agent Handoff

## Authority and Purpose

Read this page before Workflow Delivery work. It routes work and retained
operation limits; it is not a second specification. The [portal](README.md)
identifies the owning records, and the [application interface](../README.md)
owns current commands, callers and local validation. Read
[requirements](requirements.md), [HLD](high-level-design.md),
[MLD](middle-level-design.md) and [integration contract](migration-strategy.md)
before affected design, implementation or caller changes, and use actual native
sources for operations.

The accepted [Wave](../../../../../docs/delivery-wave.md) is the sole positive
work authority. Further substantive work requires an accepted entry. A PR,
proposed document, research result, local observation or merged source cannot
grant itself broader effects. Do not use Workflow v1/v2 to fill a V3 decision
gap. Internal formats may break; there is no requirement to retain old readers
in the new core. Historical readers use
[actual producer-qualified source](validation/historical-readers.md); no
compatibility tree, runtime replay service or parallel source authority is
retained.

## Starting Work

1. Inspect the actual Git branch, worktree/index state, accepted target, active
   worker ownership, PR checks and unresolved findings. Preserve unrelated user
   changes. A missing process alone does not prove a worker is idle elsewhere.
2. Read accepted repository AGENTS, governance, record policy and Wave before
   depending on proposed changes. Follow the
   [shared engineering guidance](../../../../../.github/skills/engineering-guidance/SKILL.md)
   and concern-specific review skills through their canonical paths.
3. Confirm affected requirements, native responsibility, transfer/permission
   boundaries, concrete integration contract and validation basis. New
   feasibility or material product/trust/cost tradeoffs return to the owner;
   do not compensate by implementing missing lower-layer guarantees.
4. Prepare and validate complete affected changes, including applicable tests,
   root HK, hooks and `git diff --check`, before independent engineering/OCR,
   record-system and research-evidence review. Use the exact persisted session
   and bounded worktree when recovering a worker, following
   [agent execution](../../../../../docs/engineering/agent-execution.md).
5. Keep terminal outcomes, originals and source/run identities intact. Await
   terminal tool/worker output and verify evidence before dependent effects.

Authors/implementers cannot satisfy their own independent gate. Every material
finding requires distinct GOV-011 classification before it drives repair;
unresolved owner decisions stay open. Return fixes to original reviewers until
clear. For Workflow Delivery reviews, use `open-code-review-delegate`,
including the complete Git manifest when OCR filtering omits documentation, and
contract after every two review iterations, after changes/reviews and before PR
preparation. The pinned
[NuGet delivery retrospective](https://github.com/hcoona/three/blob/f525bf29a581da2ad92f143827b0c06b9b818c03/src/private/app/workflow-delivery/docs/research/nuget-delivery-retrospective.md#next-agent-guide)
retains practical Windows/PowerShell, timestamp, helper-basename and
draft-path delivery lessons as historical lessons, not rules.

## Authorization and Closed Campaigns

All npm, NuGet, Python and Ruby proving campaigns are complete and stopped.
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
