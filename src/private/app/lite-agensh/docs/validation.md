# LiteAgensh Validation Basis

This record owns the evidence needed for the
[requirements](requirements.md) and [architecture](architecture.md).
The [upstream map](upstream-migration.md) supplies source provenance, not runtime
acceptance. Current implementation status belongs in the [README](../README.md);
actual commands/results and native counterexamples belong in the governing PR.

## Evidence strategy

Use scenario tests for ordinary cooperation, lifecycle and integration behavior.
Use focused unit tests for board matching/admission, claim expiry, retry/deadline
transitions and candidate/check classification. Use contract tests for the
serialized local MCP/native SDK boundary. Do not lock orchestration to private class
layouts, mock call counts or incidental call order.

Prefer the least costly evidence that establishes each property. SQLite and Git
integration tests use disposable local state and real storage/Git, not a
requirement to launch models. Scripted native workers exercise coordinator
decisions and failures. Actual Windows Copilot acceptance is separate: a
scripted success cannot establish real SDK/MCP interoperability, native
cancellation or session persistence.

Use the internal coordinator native-worker factory for in-process controlled
cooperation, resume and failure scenarios, without binding a test listener or
adding another SDK transport. Obtain controlled preflight/calibration receipts
by invoking coordinator operations. Results and receipts automatically identify
the substitute, and native SDK admission must reject those receipts. Retain
real SQLite credit admission and association validation, including unknown
usage and unsettled-stop failures. Test the real worker's association atomicity
and dependency-use invariants separately where practical; actual SDK settings,
wire interoperability, billing, cancellation and persistence still require
their applicable native observations. Do not silently drop those obligations
when replacing obsolete ACP-specific tests.

Rely on documented SQLite, Git and host contracts within their scope rather
than re-proving their internals. Test the application's use of those contracts
and assumptions that could invalidate its design.

## Required scenarios

| Scenario                             | Requirements                       | Required result and least useful evidence                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------ | ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Local run preflight                  | LA-REQ-001, LA-REQ-002, LA-REQ-003 | Bad config, missing capability, incompatible state or ambiguous ownership is visible before a replacement writer starts; native contract substitute plus local state/process scenarios                                                                                                                                                                                                   |
| Human-editable initialization        | LA-REQ-017                         | YAML/Markdown and legacy JSON resolve into the expected snapshot independently of launch cwd; later goal edits do not reload it. Invalid input fails before state creation; focused input/CLI scenarios and schema/example checks, without model calls.                                                                                                                                  |
| Two peers cooperate                  | LA-REQ-004, LA-REQ-005, LA-REQ-006 | Workers see tasks, exchange team/direct/thread messages, publish findings, tolerate multiple advisory claimants and complete independent changes; scripted journey first, then native acceptance                                                                                                                                                                                         |
| Planner release                      | LA-REQ-004                         | Peers wake on integrated plan or configured timeout without an intelligent central scheduler; controlled clock/event scenario                                                                                                                                                                                                                                                            |
| Board history and query              | LA-REQ-006                         | Caps/admission, expiry, replacement/invalidation, recent/history distinction, boolean/literal query and expansion agree with pinned cases; focused policy tests plus SQLite round trip                                                                                                                                                                                                   |
| Concurrent operations and restart    | LA-REQ-003, LA-REQ-007             | Domain writes and events remain atomic, dedup survives restart and interrupted sending requeues within source retry bounds; real SQLite scenario with controlled delivery failures                                                                                                                                                                                                       |
| Automatic contention evidence        | LA-REQ-018                         | A real held SQLite write transaction produces retained failure evidence with the operation, phase, timing, primary/extended error and available competing activity; commit/rollback remove completed activity, missing coverage stays unknown, and persistence failure preserves the original error. Controlled local storage scenarios; no model calls or historical lock-holder claim. |
| Tool-result notices                  | LA-REQ-008, LA-REQ-009             | Snapshots/leases/shown ledger/cursors reconcile without ordinary duplicate presentation; no claim of arbitrary command interruption; MCP contract scenario and native tool call                                                                                                                                                                                                          |
| Truthful capability surface          | LA-REQ-005, LA-REQ-009             | All 29 source tools have a treatment; exposed names/descriptions implement only their local scope, with bound actor/run and explicit failures; allowlist/map inspection plus contract tests                                                                                                                                                                                              |
| Preserve original repository         | LA-REQ-010                         | Initial checkout/index/untracked files/global config and tracked instructions remain unchanged through the run; real disposable Git scenario with dirty initial state                                                                                                                                                                                                                    |
| Check and review exact candidate     | LA-REQ-011, LA-REQ-012             | Two valid submissions integrate serially; conflicts/stale heads/reviews/checks cannot bypass merged-candidate validation; real Git/check scenario                                                                                                                                                                                                                                        |
| Broken integration and final failure | LA-REQ-012, LA-REQ-015             | Health policy follows source timing, including its 60-minute lift, without changing failed check truth; final failure remains failed delivery; controlled clock and command outcomes                                                                                                                                                                                                     |
| Cancellation and ownership           | LA-REQ-003, LA-REQ-013, LA-REQ-014 | Stop dispatch, cancel and settle owned processes; silence/timeout cannot cause duplicate writers or broad cleanup; process substitute/local child scenario plus native cancellation                                                                                                                                                                                                      |
| Reconnect versus explicit resume     | LA-REQ-003, LA-REQ-013             | Exact SDK session resume verifies identity/remaining cap/usage with pending work disabled; fresh-summary recovery remains labeled and budget-gated; explicit resume uses fresh sessions; scripted contracts plus native observation                                                                                                                                                      |
| Budget and task closure              | LA-REQ-014                         | Manual stop, finite deadline and actor-aware task closure produce correct end reasons, not fabricated passed acceptance or a token-budget claim; controlled-clock scenarios                                                                                                                                                                                                              |
| Retain delivery and explicit cleanup | LA-REQ-015                         | Local branch/report exposes exact commits/checks/unresolved work; pause/finalize retain state and cleanup removes only identified owned resources; local Git/storage/lifecycle scenario                                                                                                                                                                                                  |
| Trusted-host and source boundary     | LA-REQ-016                         | No generated target-instruction overwrite, credential redistribution, forced native-policy override or automatic diagnostic upload; source/config review and scenario evidence for application-owned effects                                                                                                                                                                             |

Passing these does not imply other backends, operating systems, security
isolation, model-proxy guarantees, remote delivery or benchmark equivalence.
For unimplemented scenarios, the result is **not run**, not inferred success.

Shared-credit scenarios additionally require a peer to exceed its former equal
share while others start through ramp-up, without exceeding backed run/campaign
authorization; unused completed-turn grants become available to another peer.
Exercise simultaneous requests against real SQLite, temporarily committed
credit, cancellation while waiting, unsupported/mismatched cap application,
partial or regressing usage and exact/fresh recovery. Preserve legacy
per-session behavior and reject invalid allocation input before initialization.
Use focused ledger invariants and controlled journeys, not tests of private
method order or native implementation internals.

Exercise a completed user turn whose background compaction is still pending:
its complete observed subtotal must not return backing. After compaction
settles, a fresh complete cumulative reading includes its reported charge,
closes the window and makes unused backing available to a peer. Cover work
changing during a usage read, cancellation/stop with compaction outstanding,
failed or missing terminal evidence and bounded-wait failure. Final staging
must not become valid merely because the shared host later exits. Keep native
compaction in the native dependency and preserve ordinary no-compaction and
legacy per-session behavior.

Exercise the existing exact-session recovery with a previously uncertain,
dispatched, closed-window reservation and a monotonic complete final increase.
Use real SQLite and controlled native session/host settlement to establish
eligibility, exact delta/overshoot accounting and idempotence without grants or
prompts. Reject a live changed reading, a final flag without settled evidence,
regression, revised reconciled totals, partial usage, mismatched associations
and failed ownership/background/session/host settlement. Preserve unrelated
unknowns, source/receipt bindings and paid-admission gates. Do not test this
through pre-dispatch cancellation or add an incident-specific recovery command.

For the [SDK-native recovery boundary](implementation-contracts.md#sdk-native-recovery-boundary),
exercise an original undelivered send and cancelled compaction followed by
matching routine native termination, then fresh complete nonregressing usage
and settled replacement-boundary work/session/host. Preserve the original
failures while booking only the fresh cumulative delta. Cover complete pinned
history across short pages, original-lifetime association and the public amount
lower bound. Reject missing/error/later-recovery termination, expired or
incomplete paging, regressing or below-original amounts, nonlocal/active work,
unsupported APIs and work changing during or after the read. Rejection creates
no prompt/grant, raw-file/reflection fallback or accounting disposition.
Use controlled SDK-shaped boundaries for these guards, not provider-invoice
simulations. Installed-native typed history retrieval and new-boundary
settlement remain separately unobserved until the owning exact operation.

Exercise `reconnect` through the public coordinator boundary for a closed
failed run with dispatched uncertain usage and stale build-bound paid receipts.
It must settle exact native usage and return a distinct non-paid result
without opening a segment, creating worker loops, sending prompts, replacing
receipts or changing the original failed outcome/report. Use real SQLite and
the existing controlled native factory; retain before/after preservation,
delta/overshoot and a no-native-I/O idempotent repeat. Cover mixed successful
and rejected reservations, missing/mismatched associations, partial usage and
failed ownership or session/host settlement. A later receipt failure is not
the expected success outcome. Preserve normal unfinished crash reconnect,
closed non-unresolved rejection, fresh-summary resume and all paid gates.
Installed-native effectiveness still needs the owning operational observation.

For exact non-dispatch recovery, exercise already-used credits greater than
remaining backing with a supported retained zero-origin receipt and its
unchanged authorized absolute ceiling. The controlled native boundary must
reject a requested ceiling below already-used credits, not silently accept the
remaining difference. Cover missing or conflicting origin/start receipts,
nonzero origins, mismatched lineage, lagging or incompatible applied limits
and native rejection/readback mismatch. Rejection preserves uncertainty and
does not create backing, reset baselines or book error-derived usage. Controlled
success establishes application translation, not installed-native support.

Exercise the selected CLI 1.0.107 pin without changing the SDK package:
actual typed status must match exactly, not merely exceed the SDK's historical
default. Cover both same-selected-runtime recovery and the explicitly admitted
original-1.0.105/current-1.0.107 bridge with real retained startup/association
evidence and SDK-shaped live metadata. Preserve original runtime provenance
while the fresh live reading reports the selected runtime. Reject other version
pairs, changed SDK identity, conflicting original lineage, mismatched live
version/cap and unsettled or incomplete/regressing usage. Preserve receipts,
baselines, allowances, failed lifecycle and unrelated uncertainty in each case.
These controlled scenarios protect the application's use of its version and
coordinate contracts, not vendor compatibility or provider billing. Separately
review and observe the installed 1.0.107 exact-session operation under the
user-audit protocol before claiming native bridge effectiveness or resuming
paid admission; a release note or successful managed build is insufficient.

Native evidence must separately retain actual model selection, live limit
readback/mutation, exhausted-window renewal, cumulative baselines and final
settlement. A source-level API or scripted grant does not establish those
observations; retain any naturally unexercised renewal as not observed.
For compaction, retain the native start/terminal ordering, complete cumulative
reading after settlement, grant closure and final accounting. Managed event
substitutes alone do not establish the installed runtime's callback/usage
ordering or a successful repaired user journey.

Shared-runtime scenarios additionally exercise independent peer conversations,
working directories, actor-bound MCP configuration, events, limits and usage
on one owned host. Changing or closing one session must not change its peer's
settings or dispose the common client. Exercise overlap, a failed admission,
exact-load fallback and cohort cancellation. Keep real SQLite reservation
state conservative until the shared process-settlement barrier; a failed
session stop or host settlement must not produce early final reconciliation.
Include a later operation and legacy per-session credit allocation without
adding another production backend.

Use controlled session/owned-host scenarios for application behavior and
review the production use of the pinned SDK contracts. The separate non-model
preflight must observe distinct sessions, session-scoped readback/mutation and
host lifetime before relying on sharing in paid work. Neither those empty
sessions nor managed tests establish native concurrent reasoning, complete
billing, memory savings or 32-peer success; retain those as actual-journey
observations under the accepted operational protocol.

Verified pre-dispatch cancellation scenarios protect LA-REQ-003, LA-REQ-013 and
LA-REQ-014 under the
[owning credit contract](implementation-contracts.md#verified-pre-dispatch-cancellation).
Use the real worker/cohort lifecycle with controlled native sessions and real
SQLite to cancel a fresh never-dispatched session only after the common-host
barrier. Preserve raw zero/absent-status readings without a synthetic measured
report, return backing once and keep a peer with actual complete usage correct.
Retain the existing unstarted and unopened exact-load baseline behavior.

Reject positive or racing dispatch, positive/missing/nonfinite usage or requests,
partial/unavailable status, mismatched associations, reused sessions, missing
source lineage, session-stop failure and unsettled or ambiguous ownership.
Use a focused concurrent ledger scenario to show cancellation and admission
cannot both win; do not test incidental private method order.

The public accounting-only path additionally covers ended unresolved state,
exclusive run ownership, exact live/reused/unknown old process identities,
unchanged campaign binding, current-state rechecks, legacy compatible evidence
and rejection of unsupported or mismatched proof. Repeated cancellation is
idempotent. Old lifecycle/segments/leases, reports, events, cumulative spend,
other reservations and model dispatch remain unchanged. Scripted receipts or
claimed report success must not become native proof or paid admission.

The independently reviewed retained actual08 evidence supports only application-
path non-dispatch under trusted same-user assumptions. It does not establish
provider-final zero, historical tamper resistance or a fresh live fence.
Verify the accepted public path against fresh owned evidence before its bounded
disposition; no paid billing probe is required or authorized by these scenarios.

## Implementation and evidence ordering

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md) selects the
current bounded work. It does not turn a proposed design/protocol into an
accepted prerequisite or runtime evidence, and a completed migration grant
does not authorize later refinement.

Before a slice starts, accept its relevant requirements, architecture/contracts,
validation basis and unsupported cases. Resolve concrete native/MCP package/framing
and operation-schema choices in the applicable design change before dependent
implementation, without inventing a generic adapter/plugin framework.

The useful implementation boundaries are local state/collaboration/MCP;
Git/check/integration; and native host/lifecycle/recovery. They may overlap only
where accepted dependencies permit. Track actual tasks, dependencies, progress
and review results in [Issue #1542](https://github.com/hcoona/three/issues/1542)
for refinement; [Issue #1526](https://github.com/hcoona/three/issues/1526)
retains the original implementation and acceptance,
not a second repository plan/status ledger.

Integrate source/tests with existing .NET build, CPM, NBGV, formatting, HK and
CI selection. Put project scenario tests in the canonical `tests/` layout and
document the real commands once they exist. Do not create a separate framework,
toolchain or CI workflow merely for establishment. No source project, package
installation, test skeleton or live agent run is required by these records.

## Native observation protocol admission

The owner's real 32-peer repository audit and execution/repair loop follows the
separately accepted [user-audit protocol](user-audit-protocol.md) and its own
Delivery Wave grant. It is normal product use, not a repeat of the former
development acceptance campaign. Its declared repository input and private
outputs must not be presented as a public fixture acceptance result. Retain
the operational protocol's continuing ledger, stops and evidence limits;
neither a test pass nor mechanical finalization establishes audit completeness.

This validation basis is **not** an executable native-run protocol and does not
approve a paid/model run. The separate [native protocol](native-acceptance-protocol.md)
owns the concrete initial Windows subject and owner-approved credit effects.
Before such observation, accept that concrete protocol
under independent research-evidence review containing:

- Exact implementation/build identity, Windows environment, .NET/Git/Copilot
  versions, relevant negotiated capabilities and official-interface rechecks.
- A disposable self-authored repository, explicit worker/run state, known
  process ownership, configured backend/model and existing native permissions.
  Identify the external provider effect without exposing account/credential
  data; no real user/private repository is used as the public evidence input.
- Exact actions and expected observations for real cooperation and local MCP,
  candidate validation/integration, cancellation, crash recovery and fresh
  explicit resume. State which cases are established by cheaper evidence and
  which depend on actual native behavior.
- Finite runs/repetitions, worker counts, wall-clock and cumulative-effect
  limits, owner-approved provider run/spend bounds, stop conditions and owned
  cleanup. Numeric limits are accident fuses sized for the accepted scenario,
  not arbitrary restrictions on normal development.
- Evidence to retain, counterexamples and evidence limits. Sanitize
  observations; never upload native session transcripts, authentication
  artifacts, private repository contents or raw sensitive diagnostics.

The SDK adapter additionally needs controlled scenarios for actual typed
limit/identity readback, incomplete/zero usage, cumulative-delta deduplication,
unexpected limit changes, explicit cancellation and owned-runtime settlement,
and resume with remaining allowance and pending work disabled. These protect
the application's use of SDK contracts, not the SDK's implementation.
Native observation must separately establish actual limit application, the
initial tool-disabled billing calibration, persistence and recovery. Public
SDK definitions or an empty-session metric cannot substitute for those results.

If a backend cannot expose reliable usage or enforce the protocol's required
spend bound, return that effects decision to the owner rather than substituting
wall-clock time for an exact token cutoff. Protocol acceptance and a Wave grant
must both remain current; repeated runs within those accepted bounds do not
need a new approval for each repetition.

## Acceptance carrier

Each implementation PR retains the exact reviewed base/candidate, actual
commands and results, record/HK checks, source rechecks, independent applicable
engineering/record/evidence review, independent finding classification and any
owner dispositions. Refresh affected evidence when the candidate or relied-on
authority changes.

The acceptance PR links the scenario results, reproducible environment and
native observation evidence. It names unresolved/unsupported behavior and
updates the [README](../README.md) to actual implementation status. The owner
decides acceptance from this evidence. Stop reasons and merged documentation
do not establish product success; publication/release is outside this outcome.
