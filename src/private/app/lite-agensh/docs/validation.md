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

Rely on documented SQLite, Git and host contracts within their scope rather
than re-proving their internals. Test the application's use of those contracts
and assumptions that could invalidate its design.

## Required scenarios

| Scenario                             | Requirements                       | Required result and least useful evidence                                                                                                                                                                                           |
| ------------------------------------ | ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Local run preflight                  | LA-REQ-001, LA-REQ-002, LA-REQ-003 | Bad config, missing capability, incompatible state or ambiguous ownership is visible before a replacement writer starts; native contract substitute plus local state/process scenarios                                              |
| Two peers cooperate                  | LA-REQ-004, LA-REQ-005, LA-REQ-006 | Workers see tasks, exchange team/direct/thread messages, publish findings, tolerate multiple advisory claimants and complete independent changes; scripted journey first, then native acceptance                                    |
| Planner release                      | LA-REQ-004                         | Peers wake on integrated plan or configured timeout without an intelligent central scheduler; controlled clock/event scenario                                                                                                       |
| Board history and query              | LA-REQ-006                         | Caps/admission, expiry, replacement/invalidation, recent/history distinction, boolean/literal query and expansion agree with pinned cases; focused policy tests plus SQLite round trip                                              |
| Concurrent operations and restart    | LA-REQ-003, LA-REQ-007             | Domain writes and events remain atomic, dedup survives restart and interrupted sending requeues within source retry bounds; real SQLite scenario with controlled delivery failures                                                  |
| Tool-result notices                  | LA-REQ-008, LA-REQ-009             | Snapshots/leases/shown ledger/cursors reconcile without ordinary duplicate presentation; no claim of arbitrary command interruption; MCP contract scenario and native tool call                                                     |
| Truthful capability surface          | LA-REQ-005, LA-REQ-009             | All 29 source tools have a treatment; exposed names/descriptions implement only their local scope, with bound actor/run and explicit failures; allowlist/map inspection plus contract tests                                         |
| Preserve original repository         | LA-REQ-010                         | Initial checkout/index/untracked files/global config and tracked instructions remain unchanged through the run; real disposable Git scenario with dirty initial state                                                               |
| Check and review exact candidate     | LA-REQ-011, LA-REQ-012             | Two valid submissions integrate serially; conflicts/stale heads/reviews/checks cannot bypass merged-candidate validation; real Git/check scenario                                                                                   |
| Broken integration and final failure | LA-REQ-012, LA-REQ-015             | Health policy follows source timing, including its 60-minute lift, without changing failed check truth; final failure remains failed delivery; controlled clock and command outcomes                                                |
| Cancellation and ownership           | LA-REQ-003, LA-REQ-013, LA-REQ-014 | Stop dispatch, cancel and settle owned processes; silence/timeout cannot cause duplicate writers or broad cleanup; process substitute/local child scenario plus native cancellation                                                 |
| Reconnect versus explicit resume     | LA-REQ-003, LA-REQ-013             | Exact SDK session resume verifies identity/remaining cap/usage with pending work disabled; fresh-summary recovery remains labeled and budget-gated; explicit resume uses fresh sessions; scripted contracts plus native observation |
| Budget and task closure              | LA-REQ-014                         | Manual stop, finite deadline and actor-aware task closure produce correct end reasons, not fabricated passed acceptance or a token-budget claim; controlled-clock scenarios                                                         |
| Retain delivery and explicit cleanup | LA-REQ-015                         | Local branch/report exposes exact commits/checks/unresolved work; pause/finalize retain state and cleanup removes only identified owned resources; local Git/storage/lifecycle scenario                                             |
| Trusted-host and source boundary     | LA-REQ-016                         | No generated target-instruction overwrite, credential redistribution, forced native-policy override or automatic diagnostic upload; source/config review and scenario evidence for application-owned effects                        |

Passing these does not imply other backends, operating systems, security
isolation, model-proxy guarantees, remote delivery or benchmark equivalence.
For unimplemented scenarios, the result is **not run**, not inferred success.

## Implementation and evidence ordering

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md) grants the
bounded complete migration. It does not turn a proposed design/protocol into an
accepted prerequisite or runtime evidence.

Before a slice starts, accept its relevant requirements, architecture/contracts,
validation basis and unsupported cases. Resolve concrete native/MCP package/framing
and operation-schema choices in the applicable design change before dependent
implementation, without inventing a generic adapter/plugin framework.

The useful implementation boundaries are local state/collaboration/MCP;
Git/check/integration; and native host/lifecycle/recovery. They may overlap only
where accepted dependencies permit. Track actual tasks, dependencies, progress
and review results in [Issue #1526](https://github.com/hcoona/three/issues/1526),
not a second repository plan/status ledger.

Integrate source/tests with existing .NET build, CPM, NBGV, formatting, HK and
CI selection. Put project scenario tests in the canonical `tests/` layout and
document the real commands once they exist. Do not create a separate framework,
toolchain or CI workflow merely for establishment. No source project, package
installation, test skeleton or live agent run is required by these records.

## Native observation protocol admission

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
