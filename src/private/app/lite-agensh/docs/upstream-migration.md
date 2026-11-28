# Conceptual Provenance and Interface Evidence

This record links LiteAgensh's abstract coordination model to the upstream
research paper, reference repository, and relied-on SDK/MCP/SQLite contracts.
Normative product behavior belongs in [requirements](requirements.md),
[`specs/`](../specs/), [architecture](architecture.md), and
[implementation contracts](implementation-contracts.md).

## Conceptual abstraction versus upstream container reference

LiteAgensh models the organizational principles of
[_Agensh: Scaling Organizational Intelligence to 1,024 Agents_][paper]
(`arXiv:2609.26781v1`) and retains MIT attribution to the public reference
implementation [`microsoft/Agensh@2dae4e14a653da48549fd9aa093f834841475d6c`][upstream]
in [`THIRD-PARTY-NOTICES.txt`](../THIRD-PARTY-NOTICES.txt), rather than cloning
upstream's container topology or per-service glue tables:

| Upstream paper / reference concept                            | Upstream container realization                                           | LiteAgensh abstract plane & refinement                                                                                                                                                                  |
| ------------------------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Self-organized cooperation loop (`Gather..Merge`)             | Prompt-owned loop + optional `TeamRun.planning_phase`                    | Pure equal-peer prompt loop (`planning_phase` omitted); staggered admission (`workerStartIntervalSeconds`) and autonomous idle wake                                                                     |
| Message Interface & Shared Context (`FACT`/`FAIL`/`CLAIM`...) | Mattermost + FastAPI board + `mini-router` `EventStore` (`MAX_ATTEMPTS`) | [`CausalStream.tla`](../specs/CausalStream.tla): single causal `log` + co-axial `(baseCursor, immCursor)`, zero-write abort rewind, zero stream/entity double storage                                   |
| Mid-turn urgent DM & board notice delivery                    | `mcp_notice_shim.cjs` + board snapshot/shown/lease tables                | Signed `chan` + `InTurn` fenced `PeerToolCall` (preserving `needsBootstrap` and session memory) over concise 9-tool native MCP surface                                                                  |
| Shared Workspace & emergent Peer Integrator                   | Gitea repos/PRs/Actions + `quality.py` 15/60-minute `HealthGate`         | [`WorkspaceCas.tla`](../specs/WorkspaceCas.tla): partial-order DAG (`PeerSyncMerge`) + Git merge/`ProtectedDiff` oracle, 3-step CAS + `ancestors` crash reconciliation (`HealthGate` omitted)           |
| Single-agent harness hosting, liveness, and spend bounds      | Docker + ACP socket bridge + HTTP model proxy + `TeamRun` segments       | [`EpochLifecycle.tla`](../specs/EpochLifecycle.tla): unified `run` epoch, `KILL_ON_JOB_CLOSE` + `(pid, start_ticks)` `effect_scopes`, 3-state `peerExec`, and sum-type `Exact \| Indeterminate` `meter` |

[paper]: https://arxiv.org/html/2609.26781v1

## Relied-on external interface findings and rechecks

The following public package and documentation findings establish external API
surfaces; runtime behavior still requires [validation](validation.md) evidence:

- **SQLite (`Microsoft.Data.Sqlite`):** [Transactions][sqlite-transactions],
  [busy timeouts][sqlite-errors], and [synchronous ADO.NET execution][sqlite-async]
  provide single-writer ACID serialization and WAL reader concurrency without
  parallel writers. The [locked provider](../packages.lock.json) selects
  `Microsoft.Data.Sqlite` 11.0.0 with `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5;
  Microsoft's [bundle description][sqlite-bundle] includes FTS5. SQLite
  documents the [external-content index and query grammar][sqlite-fts5];
  maintaining the index in the event transaction is LiteAgensh's responsibility.
- **Git:** The [`update-ref --stdin` manual][git-ref-tx] specifies expected-old
  OIDs, locking at `prepare`, and automatic abort on session end without an
  explicit `commit`. The [implementation][git-ref-eof] distinguishes EOF in a
  started/prepared transaction from the implicit transaction mode. This is
  the basis for the ref guards in the design, not an observation of crash
  recovery on a supported host.
- **Copilot `.NET` SDK (`GitHub.Copilot.SDK`):** Initial ACP experimentation on
  CLI `1.0.105` failed (`--acp` rejected with `--usage-output-file`,
  [PR #1530 counterexample](https://github.com/hcoona/three/pull/1530#issuecomment-6622441256)).
  The lock moved from the earlier interface baseline `1.0.28` to `1.0.34`.
  Rechecked the [released NuGet archive][sdk-package] (SHA-256
  `640af39c65e5b47e67a7740ff2436d4c71068ff250a41668eb5a1bdd391e733b`),
  particularly `lib/net10.0/GitHub.Copilot.SDK.xml`. Its nuspec identifies
  `github/copilot-agent-runtime` commit `24d885babba905a2901e0dedd8303e139c9d0323`;
  the public SDK source links below are the `v1.0.34` mirror at
  `adf1da2c6db766f96ae6be36f3224e61ede010c4`, not that build commit.
  The package exposes `RuntimeConnection.ForUri`, session create/resume,
  [resume controls][sdk-resume] (`ContinuePendingWork`, `AllowTranscriptRecovery`),
  [session-scoped ephemeral MCP start with caller-supplied config][sdk-mcp],
  and [metrics on an opened session][sdk-metrics]. `GetSessionMetadataAsync`
  is an ID lookup, not a detached meter. `SessionLimitsConfig.MaxAiCredits`
  is a **current-window** cap; `UsageGetMetricsResult.TotalNanoAiu` is
  **session-wide**. `AiCreditsStatus.Complete` means every observed call
  supplied an amount (including explicit zero). These declarations do not
  prove that cold resume is idle, that a helper registered exactly once, or
  that an interrupted turn's last cost is observable before teardown.
- **Installed Copilot CLI provenance:** Version/help rechecks observed CLI
  [`1.0.107`][cli107] (`841e7a57ba6ec683d710826c113f3c0cdd8f0940`, binary
  SHA-256 `cd7f9eab17576455a0f6e33315150e33215dea0482f3cb629349a3bf4e14c29f` on
  2026-11-07) and [`1.0.112`][cli112] (`a0d26c24bac920219dccbd39ee99632d67fe1cbe`,
  binary SHA-256 `32d01e0b0100fcdeaa684fba2cc8d00b6f127ac408749ff3c1b1a6fa0b2b8698`
  on 2026-11-15, retaining `--no-auto-update`). Per owner policy in
  [implementation contracts](implementation-contracts.md#application-and-dependencies),
  CLI versions are recorded as provenance rather than gated by allowlists.
- **MCP C# SDK (`ModelContextProtocol` 2.4.0):** [Release `v2.4.0`][mcp240-release]
  (`4cbf42ef107763f0bc75c358af8cb2dfe47a769a`, [main][mcp240-package] SHA-256
  `163fe0d45ae6a0ee906f65afac21e5c01088ace5c5bec5b603dcb25e26df7cad`,
  [Core][mcp240-core-package] SHA-256
  `3b600797816a51297df17869249f7c710f24b7cc7e6434371d838863415d5986`) supplies
  low-level [`ListToolsHandler`/`CallToolHandler`][mcp240-handlers] and
  [`StdioServerTransport`][mcp240-stdio].

**Recheck trigger (`GOV-007`):** Before relying on a newly upgraded SDK/MCP
package or changed CLI behavior, the implementer rechecks the affected official
contract and `research-evidence-review` verifies the update in the governing PR
(with the next PR or Wave entry touching LiteAgensh native/MCP/SQLite boundaries
as the fallback review).

[upstream]: https://github.com/microsoft/Agensh/tree/2dae4e14a653da48549fd9aa093f834841475d6c
[cli107]: https://github.com/github/copilot-cli/releases/tag/v1.0.107
[cli112]: https://github.com/github/copilot-cli/releases/tag/v1.0.112
[sqlite-transactions]: https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions
[sqlite-errors]: https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors
[sqlite-async]: https://learn.microsoft.com/dotnet/standard/data/sqlite/async
[sqlite-bundle]: https://learn.microsoft.com/dotnet/standard/data/sqlite/custom-versions#bundles
[sqlite-fts5]: https://www.sqlite.org/fts5.html
[git-ref-tx]: https://git-scm.com/docs/git-update-ref/2.50.0
[git-ref-eof]: https://github.com/git/git/blob/v2.51.0/builtin/update-ref.c#L716-L742
[sdk-package]: https://api.nuget.org/v3-flatcontainer/github.copilot.sdk/1.0.34/github.copilot.sdk.1.0.34.nupkg
[sdk-resume]: https://github.com/github/copilot-sdk/blob/adf1da2c6db766f96ae6be36f3224e61ede010c4/dotnet/src/Types.cs#L5351-L5377
[sdk-mcp]: https://github.com/github/copilot-sdk/blob/adf1da2c6db766f96ae6be36f3224e61ede010c4/dotnet/src/Generated/Rpc.cs#L73446-L73457
[sdk-metrics]: https://github.com/github/copilot-sdk/blob/adf1da2c6db766f96ae6be36f3224e61ede010c4/dotnet/src/Generated/Rpc.cs#L77016-L77033
[mcp240-release]: https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0
[mcp240-package]: https://api.nuget.org/v3-flatcontainer/modelcontextprotocol/2.4.0/modelcontextprotocol.2.4.0.nupkg
[mcp240-core-package]: https://api.nuget.org/v3-flatcontainer/modelcontextprotocol.core/2.4.0/modelcontextprotocol.core.2.4.0.nupkg
[mcp240-handlers]: https://github.com/modelcontextprotocol/csharp-sdk/blob/4cbf42ef107763f0bc75c358af8cb2dfe47a769a/src/ModelContextProtocol.Core/Server/McpServerHandlers.cs#L24-L66
[mcp240-stdio]: https://github.com/modelcontextprotocol/csharp-sdk/blob/4cbf42ef107763f0bc75c358af8cb2dfe47a769a/src/ModelContextProtocol.Core/Server/StdioServerTransport.cs#L11-L35
