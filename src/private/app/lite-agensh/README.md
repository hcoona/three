# LiteAgensh

LiteAgensh is a single-machine C# realization of the self-organized multi-agent
organization model from
[_Agensh: Scaling Organizational Intelligence to 1,024 Agents_](https://arxiv.org/html/2609.26781v1)
([`microsoft/Agensh`][upstream]). Equal peer coding agents execute a
prompt-owned cooperation loop (`Gather -> Claim -> Act -> Verify -> Merge`)
over a formally verified four-plane coordination kernel, refined onto SQLite,
isolated Git worktrees, actor-bound stdio MCP, and one shared owned Copilot
`.NET` SDK runtime per epoch.

[upstream]: https://github.com/microsoft/Agensh/tree/2dae4e14a653da48549fd9aa093f834841475d6c

## Current remodel status and evidence boundaries

- **Abstract-machine specification ([Issue #1943](https://github.com/hcoona/three/issues/1943)):**
  LiteAgensh's coordination semantics are specified as a **three-tier vertical
  refinement $\times$ three-plane horizontal composition** TLA+ lattice under
  [`specs/`](specs/) and exhaustively verified by `TLC`:
    - **Tier 0 (`What` — [`AtomicOrganization.tla`](specs/AtomicOrganization.tla)):**
      single-step observational specification over per-peer `acked` index sets,
      atomic `open -> integrated` + `targetRef` updates, and abstract
      epoch/meter states—with zero cursors, channel tokens, `committing` states,
      OS scopes, or ghost variables.
    - **Tier 1 (`How` — Horizontal Planes + Unified Composition):**
        - [`CausalStream.tla`](specs/CausalStream.tla): refines Tier-0 `acked`
          via `MappedAcked` over one append-only causal `log`, co-axial cursors
          `0 <= baseCursor <= immCursor <= |log|`, atomic `PeerToolCall`
          piggybacking, and `(needsBootstrap, signed chan, InTurn)`.
        - [`WorkspaceCas.tla`](specs/WorkspaceCas.tla): refines Tier-0 atomic
          integration via `MappedProposals` (`committing` linearized by
          `head \in ancestors[targetRef]`) over three-step cross-store CAS,
          `PeerSyncMerge`, and `ancestors` crash reconciliation.
        - [`EpochLifecycle.tla`](specs/EpochLifecycle.tla): refines Tier-0
          `Spec` (safety + liveness) over `runEpoch`, abstract `scopeState`,
          three-state `peerExec`, and unclamped sum-type `meter`.
        - [`AgenshOrganization.tla`](specs/AgenshOrganization.tla): simultaneously
          verifies horizontal sub-plane composition (`Spec => SubPlane!SafetySpec`)
          and vertical full-spec refinement (`Spec => HighOrg!Spec`).
    - **Tier 2 (OS/SQLite Scope Protocol — [`OsScopeRefinement.tla`](specs/OsScopeRefinement.tla)):**
      refines Tier-1 `EpochLifecycle` (`THEOREM Spec => Tier1Lifecycle!SafetySpec`)
      via `AbstractScope` onto root-only write-ahead `PrePidAmbiguous`
      (failing closed without a persisted PID), pre-`Main` `mcp` spawning +
      surviving-POSIX `mcp Main` `(pid, start_ticks, Active)` self-registration
      without pre-PID self-lock, sibling/check-descendant containment, and
      Windows `KILL_ON_JOB_CLOSE` / cooperative POSIX `setsid` group-emptiness
      teardown ([#1756](https://github.com/hcoona/three/issues/1756)).
- **Implementation transition:** [`docs/`](docs/) defines the downward
  refinement onto C#, SQLite, Git, MCP, and the Copilot SDK. `schemas/run.schema.json`,
  `examples/run.yaml`, the C# executable, and `LiteAgensh.Tests` transition onto
  these contracts in the stacked Layer 3 implementation PR under
  [Issue #1943](https://github.com/hcoona/three/issues/1943).
- **Historical and suspended operational evidence:** Paid 32-peer user-audit
  execution under [Issue #1552](https://github.com/hcoona/three/issues/1552) is
  suspended until the v2 runtime and refreshed audit protocol merge. Closed
  fixture observations ([#1526](https://github.com/hcoona/three/issues/1526) /
  [#1530](https://github.com/hcoona/three/pull/1530)) and retired v1 protocols
  remain in Git history at
  [`a83cd1da436d8df55f1290706c1c86b7f363a1a3`](https://github.com/hcoona/three/tree/a83cd1da436d8df55f1290706c1c86b7f363a1a3/src/private/app/lite-agensh/docs).

## Primary journey and CLI

A developer supplies a configuration and goal to coordinate equal coding peers
on one local repository (the current v1 executable exposes separate `run`,
`resume`, and `reconnect` subcommands alongside `init`, `preflight`, `finalize`,
and `cleanup`; the stacked Layer 3 implementation under
[Issue #1943](https://github.com/hcoona/three/issues/1943) transitions onto
YAML-only configuration and unified `run` per
[`docs/implementation-contracts.md`](docs/implementation-contracts.md)):

```powershell
dotnet build src\private\app\lite-agensh\LiteAgensh.csproj
$app = (Resolve-Path src\private\app\lite-agensh\bin\Debug\net10.0\LiteAgensh.dll).Path
dotnet $app init --config C:\Work\LiteAgensh\run.yaml
dotnet $app preflight --state C:\Work\LiteAgensh\run
dotnet $app run --state C:\Work\LiteAgensh\run
dotnet $app finalize --state C:\Work\LiteAgensh\run
```

Verify all TLA+ specifications and managed tests locally:

```powershell
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/AtomicOrganization.cfg src/private/app/lite-agensh/specs/AtomicOrganization.tla
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/CausalStream.cfg src/private/app/lite-agensh/specs/CausalStream.tla
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/WorkspaceCas.cfg src/private/app/lite-agensh/specs/WorkspaceCas.tla
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/EpochLifecycle.cfg src/private/app/lite-agensh/specs/EpochLifecycle.tla
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/AgenshOrganization.cfg src/private/app/lite-agensh/specs/AgenshOrganization.tla
mise run tla:tlc -- -workers auto -lncheck final -config src/private/app/lite-agensh/specs/OsScopeRefinement.cfg src/private/app/lite-agensh/specs/OsScopeRefinement.tla
dotnet test --project tests\private\app\lite-agensh\LiteAgensh.Tests.csproj
```

## Records and authority

| Reader question                                                        | Authority                                                                           |
| ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| What pure abstract machines and invariants govern coordination?        | [TLA+ specifications](specs/)                                                       |
| What capabilities must the product satisfy, and what is out of scope?  | [Requirements](docs/requirements.md)                                                |
| How does the four-plane abstract machine refine onto local primitives? | [Architecture and refinement mapping](docs/architecture.md)                         |
| What concrete CLI, YAML, SQLite, MCP, and SDK contracts apply?         | [Implementation contracts](docs/implementation-contracts.md)                        |
| How does this model relate to the Agensh paper, repo, and SDK sources? | [Conceptual provenance and interface evidence](docs/upstream-migration.md)          |
| What formal, unit, scenario, and native evidence is required?          | [Validation basis](docs/validation.md)                                              |
| Where is upstream license attribution retained?                        | [Third-party notice](THIRD-PARTY-NOTICES.txt)                                       |
| What work is currently authorized?                                     | [Delivery Wave](../../../../docs/delivery-wave.md) and [Issue #1943][remodel-issue] |

[remodel-issue]: https://github.com/hcoona/three/issues/1943
