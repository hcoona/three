# Workflow Delivery Agent Handoff

## Authority and Purpose

Read this page before Workflow Delivery work. It routes the current internal
application and retained operation limits; it is not a second specification.
The [portal](README.md) identifies the owning records. Read
[requirements](requirements.md), [HLD](high-level-design.md),
[MLD](middle-level-design.md) and [integration contract](migration-strategy.md)
before affected design, implementation or caller changes. Use the current
[application interface](../README.md) and actual native sources for operations.

The accepted [Wave](../../../../../docs/delivery-wave.md) is the sole positive
work authority. Further substantive work requires an accepted entry. A PR,
proposed document, research result, local observation or merged source cannot
grant itself broader effects.
Do not use Workflow v1/v2 to fill a V3 decision gap. Internal formats may break;
there is no requirement to retain old readers in the new core.

## Current Application and Callers

CI and Release share native mechanisms in `src/private/app/workflow-delivery/`
while retaining distinct business decisions. The existing root CI owns the
native .NET, PNPM, Python and Ruby groups, selected retained checks and source
conformance. Each group executes its original candidate-bound plan and collects
all required results; it does not replan or silently broaden failed analysis.
A Windows CI job runs the three native NuGet preparation rows when the existing
.NET/Workflow scope selection selects it. The four sensitivity/fresh-recovery rows run in the
Windows reproducibility workflow on owner dispatch and on `main`
build-infrastructure changes; reproducibility is not per-PR evidence.

Ordinary npm Buddy, Official npm simulation, NuGet Buddy, Python and Ruby
callers compose the implemented internal CLI. Control precedes project
execution. Native observation, conditional publication, separate registry readback
of exact coordinates and original bytes, and truthful completion replace the former dynamic admission, approval mirror,
marker and proof chain. Effective native Environment/identity/destination
configuration owns publication authorization. Existing external resources and
publisher registrations are unchanged by source retirement. Ordinary Ruby/Python/npm/NuGet
caller inputs now belong to the existing C# CLI; native helpers retain native
library operations. [Static wire descriptions](../Native/contracts/) are generated
from consumed serializer types and have no compatibility promise.

Finite CI callers also live in the existing C# application. Actions/native shell
owns first control build, configured tools and artifact transfer; the C# scope
caller reads record-catalog bindings and native task metadata. Ruby helper
behavior runs in the quality-adopted native RSpec carrier. Linux managed Python/
Ruby preparation scenarios run directly through .NET with native runner minimum
counts and required Validate ownership; pytest is not their admission wrapper.

Control input selection uses native source/configuration/reference facts without
a fresh basis build or task-log interpreter. The separate default Debug
control-resource query remains necessary because the full graph has different
CI globals. The [integration contract](migration-strategy.md) owns these bounds.

Products live under `tests/private/app/workflow-delivery/fixtures/products/`
with ordinary native manifests, coordinates and quality declarations.
`nbgv-python` and its sample remain independent. The old public Python control
package, specialized provider/authority/consumer projects, scanner, admission
configuration and closed campaign workflows are retired. Current historical
readers use [actual producer-qualified source](validation/historical-readers.md).
No compatibility tree, runtime replay service or parallel source authority is
retained.

## Starting Work

1. Inspect the actual Git branch, worktree/index state, accepted target, active
   worker ownership, PR checks and unresolved findings. Preserve unrelated user
   changes. A missing process alone does not prove a worker is idle elsewhere.
2. Read accepted repository AGENTS, governance, record policy and Wave before
   depending on proposed changes. Follow the shared engineering guidance and
   concern-specific review skills through their canonical paths.
3. Confirm affected requirements, native responsibility, transfer/permission
   boundaries, concrete integration contract and validation basis. New
   feasibility or material product/trust/cost tradeoffs return to the owner;
   do not compensate by implementing missing lower-layer guarantees.
4. Prepare and validate complete affected changes before independent review.
   Use the exact persisted session and bounded worktree when recovering a
   worker, following [agent execution](../../../../../docs/engineering/agent-execution.md).
5. Keep terminal outcomes, originals and source/run identities intact. Await
   terminal tool/worker output and verify evidence before dependent effects.

## Authorization and Closed Campaigns

All npm, NuGet, Python and Ruby proving campaigns are complete and stopped.
Their operation slots, failed lifetimes and dispatch/recovery ceilings remain
spent. No source change, artifact download, requirements revision, Wave
contraction or evidence acceptance authorizes a new campaign generation, rerun,
release/proving dispatch, publication, authentication, Environment, account,
publisher or access change. Native experiments still require their exact
accepted bounded protocol and effects authorization.

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
replace its actual producer. [Native readiness](validation/python-native-readiness.md),
[bootstrap](validation/python-bootstrap.md),
[ordinary recovery](validation/python-hosted-recovery.md) and
[normal-Live evidence](validation/python-normal-live-evidence.md) retain their
actual subjects and spent ceilings. Python's native per-file guarantees do not
promise an atomic wheel/sdist set or permanent registry retention.

### Ruby retained limits

The [operation protocol](ruby-operation-protocol.md) and
[normal-Live evidence](validation/ruby-normal-live-evidence.md) retain stopped
destinations, original publication/consumer observations and spent bootstrap,
native, normal and recovery slots. Linux caller qualification does not claim
Windows Ruby operations or deployed authentication/destination behavior.
The old operation CLI and bootstrap entry are not fallback execution paths.

## Design, Testing and Delivery Discipline

Rely on maintained native platforms and ecosystem interfaces within their
actual contracts. Application bindings and results remain its responsibility;
platform trust does not invent missing guarantees. Add abstractions only for
concrete independent responsibilities. Keep unsupported necessary shapes
explicit, rather than adding an unbounded queue, cache, scheduler, dynamic
policy/admission service or general recovery engine.

Persist data for an actual permission/process/transfer/interruption consumer.
Use typed values inside a trusted invocation and immutable native artifact
identity across jobs. Consumers and project code have no publication capability.
Native source metadata varies by ecosystem and cannot replace required original
byte/state checks. Same-input reproducibility does not imply cross-commit byte
equality. Ordinary recovery rebuilds at the original source/control revision;
it cannot combine latest control with an older target or erase earlier failure.

Validate complete affected business scenarios, native integration assumptions,
real transfer contracts, missing/cancelled/unknown outcomes and selected scope.
Use focused tests for impact and outcome logic. Do not retain tests solely for
retired internal records or re-proving supported platform approval/scheduling.
Run applicable complete tests, root HK, hooks and `git diff --check` before
independent engineering/OCR, record-system and research-evidence review.

Authors/implementers cannot satisfy their own independent gate. Every material
finding requires distinct GOV-011 classification before it drives repair;
unresolved owner decisions stay open. Return fixes to original reviewers until
clear. Use `open-code-review-delegate`, including the complete Git manifest
when OCR filtering omits documentation. Contract after every two review
iterations, after changes/reviews and before PR preparation.

Keep repository code, comments, commits and records in English. Current pages
own current interfaces and route to canonical contracts. Git and PR/Issue
carriers retain chronology; do not add retry ledgers, test-count histories or
session machinery to current pages or the Wave. Preserve unique human facts,
licenses, actual producer source and original failures before retirement.
Use the accepted Wave and record policy for work authorization and its
termination.
