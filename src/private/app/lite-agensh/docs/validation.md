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
      horizontal sub-plane composition, and temporal liveness across all six
      suites—one per module:
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
      safety + liveness refinement in one `.cfg`), and
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
| Declarative YAML init and preflight         | LA-REQ-001, LA-REQ-002, LA-REQ-017             | Strict YAML + Markdown `goalFile` resolution, schema parity, and fail-closed preflight before state or runtime creation                                                                                                                                                       |
| Single causal log and signed `chan` fencing | LA-REQ-007, LA-REQ-008, LA-REQ-009             | `TLC` + concurrent SQLite/MCP scenarios verify co-axial `(baseCursor, immCursor)`, `NoKey` preservation, cross-cursor keyed folding, atomic `tasks`/`proposals` + `causal_log` SQLite commits, signed `chan` fencing preserving `needsBootstrap`, and zero-write abort rewind |
| Self-organized cooperation and ramp-up      | LA-REQ-004, LA-REQ-005, LA-REQ-006             | Equal peers claim tasks, exchange team/urgent direct messages, publish/search shared context in `causal_log`, stagger admissions, and receive autonomous idle wakeups                                                                                                         |
| Two-phase Git CAS and Peer Integrator       | LA-REQ-010, LA-REQ-011, LA-REQ-012             | `TLC` + real Git scenarios verify untouched base checkout, `PeerSyncMerge`, Git multi-base/conflict rejection, `ProtectedDiff` review, `Draining`/`Settling` CAS completion, and `ancestors` crash recovery                                                                   |
| Unified `run`, OS scopes, meter, `finalize` | LA-REQ-003, LA-REQ-013, LA-REQ-014, LA-REQ-015 | `TLC` + controlled scenarios verify start/`Paused`-rotation/reconnect, 3-state `peerExec` + sum-type `Quiescent -> Indeterminate` honesty, OS scope blocking, `GoalConverged`, and crash-idempotent `finalize`                                                                |
| Trusted-host and attribution boundary       | LA-REQ-016                                     | Preserves target `AGENTS.md`, enforces `deny`/`trusted` permission policy, and retains `THIRD-PARTY-NOTICES.txt`                                                                                                                                                              |
