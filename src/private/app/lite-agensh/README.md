# LiteAgensh

LiteAgensh is a C# single-machine adaptation of
[microsoft/Agensh](https://github.com/microsoft/Agensh), retaining upstream's
equal-peer collaboration model while replacing hosted infrastructure with
SQLite, local Git worktrees, and worker-bound stdio MCP over the official
Copilot .NET SDK.

## Current status and evidence boundaries

- **Implemented capabilities (covered by controlled SQLite, Git, MCP, and SDK-seam scenarios):**
  one shared owned SDK runtime/client per operation with independent peer
  sessions and cohort-settlement staging
  ([native host](docs/architecture.md#native-sdk-mcp-and-instruction-boundary));
  editable YAML configuration (`examples/run.yaml` + `schemas/run.schema.json`)
  with separate Markdown goals (`examples/goal.md`) alongside legacy JSON
  ([input contract](docs/implementation-contracts.md#configuration-input));
  configurable cold-worker ramp-up (`workerStartIntervalSeconds`); opt-in
  `creditAllocation: shared` renewable windows (`per-session` default) with
  compaction-fenced settlement
  ([shared windows](docs/implementation-contracts.md#shared-spending-windows),
  [settlement](docs/implementation-contracts.md#usage-evidence-and-settlement));
  separate work-admission expiry and [bounded deadline drain](docs/implementation-contracts.md#bounded-deadline-drain);
  bounded `<database-path>.diagnostics` [SQLite contention and I/O capture](docs/implementation-contracts.md#sqlite-contention-evidence);
  non-dispatch [SDK-native recovery](docs/implementation-contracts.md#sdk-native-recovery-boundary)
  (`reconnect` on closed failed runs using the
  [zero-origin cap profile](docs/implementation-contracts.md#exact-recovery-limit-coordinates))
  and accounting-only `reconcile` for
  [verified pre-dispatch cancellation](docs/implementation-contracts.md#verified-pre-dispatch-cancellation);
  and normal reviewed CPM/Renovate SDK upgrades (currently MCP `2.4.0`;
  [recheck](docs/upstream-migration.md#mcp-240-interface-recheck)) plus use of
  the installed Copilot CLI without version allowlists while preserving exact
  original-session SDK identity checks
  ([dependencies](docs/implementation-contracts.md#application-and-dependencies),
  [CLI lineage](docs/implementation-contracts.md#retained-cli-10105-to-selected-cli-10107)).
- **Evidence limits:** [PR #1530](https://github.com/hcoona/three/pull/1530)
  ([Issue #1526](https://github.com/hcoona/three/issues/1526), under the closed
  [fixture protocol](docs/native-acceptance-protocol.md#sdk-campaign-baseline))
  retains initial two-worker Windows fixture observations. Controlled scenarios
  and configured-check substitutes
  ([evidence allocation](docs/validation.md#configured-check-and-process-evidence))
  establish application logic only—they do not establish other operating systems
  or backends, native timing or memory claims, installed-native recovery
  effectiveness, zero provider billing on unmeasured sessions, or a completed
  32-peer user audit (tracked on
  [Issue #1552](https://github.com/hcoona/three/issues/1552) under
  [`user-audit-protocol.md`](docs/user-audit-protocol.md)). Unknown reservations
  in an active persistent ledger block further paid admission without automatic
  release or reset.

## User and primary journey

A developer supplies a goal, base revision, worker count, checks, and finite run
budget to coordinate multiple coding agents on one local repository without a
Git, chat, database, or container server. Peers claim tasks, exchange findings,
and submit checked candidates to a local delivery branch while supporting
`status`, `pause`, `resume`, and crash `reconnect` without losing partial work
(motivating [`requirements.md`](docs/requirements.md); acceptance criteria live
in [`validation.md`](docs/validation.md)).

## Running and checking the application

Use the repository-selected .NET SDK and existing installed Copilot CLI under
[`implementation-contracts.md`](docs/implementation-contracts.md) (SDK runtime
acquisition and native auto-updates are disabled). Edit [`examples/run.yaml`](examples/run.yaml)
and [`examples/goal.md`](examples/goal.md): relative repository, state, ledger,
and `goalFile` paths resolve from the YAML file's directory; keep state and
ledger outside the target checkout (`init` snapshots configuration and goal into
SQLite). Worktrees isolate edits rather than host permissions.

Build once and reuse the exact binary across preflight, paid calibration (30-credit
tool-disabled soft cap), and execution:

```powershell
dotnet build src\private\app\lite-agensh\LiteAgensh.csproj
$app = (Resolve-Path src\private\app\lite-agensh\bin\Debug\net10.0\LiteAgensh.dll).Path
dotnet $app init --config C:\Work\LiteAgensh\run.yaml
dotnet $app preflight --state C:\Work\LiteAgensh\run
dotnet $app calibrate --state C:\Work\LiteAgensh\run
dotnet $app run --state C:\Work\LiteAgensh\run
```

`run` stays in the foreground terminal and owns worker subprocesses; invoke
`status` or `pause` from another terminal with `--state`, wait for process
settlement before `resume` (fresh summarized sessions), use `reconnect` after a
crash or for authorized [closed failed-run settlement](docs/implementation-contracts.md#closed-failed-run-reconnect),
and use `finalize` / `cleanup` to inspect or remove owned state. For an
authorized pre-dispatch reservation (`--evidence` required on legacy state
without a dispatch-contract marker):

```powershell
dotnet $app reconcile --state C:\Work\LiteAgensh\run --reservation RESERVATION_ID
```

Run the credential-free controlled test suite:

```powershell
dotnet test --project tests\private\app\lite-agensh\LiteAgensh.Tests.csproj
```

## Records and authority

| Reader question                                                     | Authority                                                                                                                                                                                                           |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| What must the local product do, and what is unsupported?            | [Requirements](docs/requirements.md)                                                                                                                                                                                |
| How do C#, SQLite, native processes, MCP and Git fit together?      | [Architecture and integration boundaries](docs/architecture.md)                                                                                                                                                     |
| What replaces each upstream responsibility and collaboration tool?  | [Complete migration map and source evidence](docs/upstream-migration.md)                                                                                                                                            |
| What evidence permits implementation and an eventual support claim? | [Validation basis](docs/validation.md)                                                                                                                                                                              |
| Where is upstream attribution retained?                             | [Third-party notice](THIRD-PARTY-NOTICES.txt)                                                                                                                                                                       |
| What concrete boundaries do independent implementation units share? | [Implementation contracts](docs/implementation-contracts.md)                                                                                                                                                        |
| What governed the closed fixture-only acceptance campaign?          | [Windows native acceptance protocol](docs/native-acceptance-protocol.md)                                                                                                                                            |
| What governs the owner's actual 32-peer user-audit execution loop?  | [Real user audit protocol](docs/user-audit-protocol.md)                                                                                                                                                             |
| What work is currently authorized?                                  | Repository [Delivery Wave](../../../../docs/delivery-wave.md) and [record policy](../../../../docs/governance/record-system.md)                                                                                     |
| Where are audit progress, closed refinement, and original runs?     | [User audit #1552](https://github.com/hcoona/three/issues/1552), closed [refinement #1542](https://github.com/hcoona/three/issues/1542), and closed [acceptance #1526](https://github.com/hcoona/three/issues/1526) |
