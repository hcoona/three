# LiteAgensh Validation Basis

This record defines the formal, unit, scenario, and native evidence obligations
for [requirements](requirements.md), [architecture](architecture.md), and
[`specs/`](../specs/) under the repository
[testing principles](../../../../../docs/engineering/engineering-principles.md#testing).

## Layered verification strategy

1. **Formal TLA+ refinement lattice (`specs/*.tla`):**
    - HK `tla-sany` checks syntax, semantics, and level rules on every commit.
    - `TLC` (`mise run tla:tlc`) exhaustively model-checks safety invariants,
      nontrivial vertical refinement mappings (`Tier 0 <- Tier 1 <- Tier 2`),
      horizontal sub-plane composition, and temporal liveness across all seven
      suites across the six modules:
      [`AtomicOrganization.tla`](../specs/AtomicOrganization.tla) (Tier-0 atomic
      `What` baseline),
      [`CausalStream.tla`](../specs/CausalStream.tla) (Tier-1 co-axial cursors &
      signed `chan` refining Tier-0 `acked` via `MappedAcked`),
      [`WorkspaceCas.tla`](../specs/WorkspaceCas.tla) (Tier-1 three-step CAS +
      `ancestors` crash reconciliation refining Tier-0 single-step
      `AtomicIntegrate` via `MappedProposals`),
      [`EpochLifecycle.tla`](../specs/EpochLifecycle.tla) (Tier-1 scopes,
      three-state `peerExec`, and sum-type `meter` refining Tier-0 `Spec`),
      [`AgenshOrganization.tla`](../specs/AgenshOrganization.tla) (simultaneous
      horizontal `SubPlane!SafetySpec` composition and vertical `HighOrg!Spec`
      safety + liveness refinement across `AgenshOrganization.cfg` and focused
      divergent-DAG/stale-head `AgenshOrganizationWorkspace.cfg`), and
      [`OsScopeRefinement.tla`](../specs/OsScopeRefinement.tla) (Tier-2 root
      fail-closed `PrePidAmbiguous`, pre-`Main` + surviving-POSIX `mcp Main`
      self-registration, sibling/check-descendant containment, and Windows/POSIX
      group-emptiness teardown refining Tier-1 `EpochLifecycle` via
      `Tier1Lifecycle!SafetySpec`).
2. **Focused unit tests (pure functions and predicates):**
    - Shared-context admission, claim TTL expiry, `replaces_seq` superseding,
      boolean/literal query evaluation, and detail expansion (`LA-REQ-006`).
    - `UnreadTurnIdx` (`dedup_key IS NULL` preservation + cross-cursor keyed
      folding without stale causal inversion), `ProtectedDiff` path-glob
      matching, YAML configuration validation, and sum-type `meter` transitions
      (`LA-REQ-007`, `LA-REQ-012`, `LA-REQ-014`, `LA-REQ-017`).
3. **Controlled refinement scenarios (`LiteAgensh.Tests`):**
    - Exercise the concrete refinement boundary—real WAL SQLite (`causal_log`),
      disposable local Git worktrees, `--channel` fenced stdio MCP framing, and
      an in-process SDK runtime substitute—without paid model calls.
    - Substitute configured check subprocesses with deterministic in-memory
      delegates in workspace scenarios while retaining one focused real-process
      suite for root `KILL_ON_JOB_CLOSE` / process-group containment (`runtime`
      and `verify`), `channel:<peer>` MCP helper `(pid, start_ticks)`
      self-registration and bottom-up settlement, timeout cancellation, and
      root write-ahead `Ambiguous` blocking (`#1756`, `#1862`).
4. **Live Windows native observation (Delivery Wave gated):**
    - Live multi-peer Copilot CLI/SDK runs require an accepted
      [Delivery Wave](../../../../../docs/delivery-wave.md) grant and operational
      protocol.

## Required scenario matrix

| Scenario                                    | Requirements                                   | Required refinement evidence                                                                                                                                                                                                                                                  |
| ------------------------------------------- | ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Declarative YAML init and preflight         | LA-REQ-001, LA-REQ-002, LA-REQ-017             | Invalid configuration creates no state; a partial workspace is not runnable. Non-model preflight checks an initialized run on a scoped temporary host, without dispatch or a saved peer session.                                                                              |
| Single causal log and signed `chan` fencing | LA-REQ-007, LA-REQ-008, LA-REQ-009             | `TLC` + concurrent SQLite/MCP scenarios verify co-axial `(baseCursor, immCursor)`, `NoKey` preservation, cross-cursor keyed folding, atomic `tasks`/`proposals` + `causal_log` SQLite commits, signed `chan` fencing preserving `needsBootstrap`, and zero-write abort rewind |
| Self-organized cooperation and ramp-up      | LA-REQ-004, LA-REQ-005, LA-REQ-006             | Equal peers claim tasks, exchange team/urgent direct messages, publish/search shared context in `causal_log`, stagger admissions, and receive autonomous idle wakeups                                                                                                         |
| Two-phase Git CAS and Peer Integrator       | LA-REQ-010, LA-REQ-011, LA-REQ-012             | `TLC` + real Git scenarios verify untouched base checkout, `PeerSyncMerge`, Git multi-base/conflict rejection, `ProtectedDiff` review, `Draining`/`Settling` CAS completion, and `ancestors` crash recovery                                                                   |
| Unified `run`, OS scopes, meter, `finalize` | LA-REQ-003, LA-REQ-013, LA-REQ-014, LA-REQ-015 | `TLC` + controlled scenarios verify start/`Paused`-rotation/reconnect, 3-state `peerExec` + sum-type `Quiescent -> Indeterminate` honesty, OS scope blocking, `GoalConverged`, and crash-idempotent `finalize`                                                                |
| Trusted-host and attribution boundary       | LA-REQ-016                                     | Preserves target `AGENTS.md`, enforces `deny`/`trusted` permission policy, and retains `THIRD-PARTY-NOTICES.txt`                                                                                                                                                              |

### Histories that distinguish a correct implementation

These are expected observations within the matrix above, **not results** or
additional test layers. Drive the public operation and inspect the resulting
Git history, SQLite state, command result or process lifetime; do not assert
a particular C# call graph.

| Interleaving or failure                                                                                                                                                                | Observable distinction                                                                                                                                                                                                                                                                     |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Two peers claim the same task; one claim expires. An urgent direct message arrives before a tool call races with `pause`.                                                              | Both claims were allowed; expiry changes only the active view. Only the addressed peer receives the immediate message. Either its entire tool mutation/event commits or neither does; an aborted turn does not advance either durable cursor.                                              |
| While `proposal_query` computes its patch, change the proposal from `H1` to `H2`, or advance only the target.                                                                          | The former returns `stale` without consuming an immediate slice. The latter can return a patch reproducible from the labeled base and `H1`, never mixed with `H2` metadata.                                                                                                                |
| Revoke approval before or after SQLite precommit; separately request `H2` while a failing check for `H1` completes.                                                                    | Revocation first blocks integration. Precommit first rejects revocation with no review or cursor change. The stale failure cannot consume `H2`'s request.                                                                                                                                  |
| Terminate the runtime tree just after a ref guard was prepared for a peer request.                                                                                                     | The helper exits, the coordinator's Git child releases the guard, and final verification can reacquire the target without deleting any Git lock. A fenced request cannot commit.                                                                                                           |
| Start with divergent branches (`C != H`). Interrupt after SQLite precommit, or after Git received `commit` but before SQLite completion. Also let another writer advance the target.   | Recovery waits out any outstanding ref writer. It records `integrated` iff the resulting tip contains `H`, otherwise `open`; either clears the request exactly once with its status event. No CAS replay or overwrite of a competing advance. A Git read error leaves the row recoverable. |
| Interrupt after durable `Running`, including before the send returns. Resume once with a complete original-session reading and once without it.                                        | Only a qualified delta can restore `Idle`; the same reading adds no second charge. Unknown usage cannot be made exact by a fresh conversation. No prompt or second helper starts on the strength of a metadata lookup.                                                                     |
| One completion in `Open` crosses the allowance while a second peer is in a turn; separately expire the clock during a candidate check at `H` with integration at `C != H` outstanding. | The completing peer alone commits cursors; both tokens are fenced. The existing evaluation can check `C` on the same runner and authorize precommit before the drain deadline. A live child blocks cleanup even if its parent PID appears gone.                                            |
| Interrupt `finalize` before the verdict commit or `cleanup` after one worktree was removed.                                                                                            | Final verification reruns all commands on a labeled target without changing the prior stop reason; a stored verdict is returned once published. Cleanup resumes only against that run's remaining resources.                                                                               |

The controlled SDK substitute can establish the controller's responses to
terminal, incomplete, duplicate, or late observations. It cannot establish
that a native host cold-resumes without work, starts only the requested
ephemeral helper, orders terminal events after tool exchanges, or exposes
settled original-session usage. Those are native acceptance questions, not
properties established by TLC or by a mocked successful turn.
