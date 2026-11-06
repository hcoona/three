# LiteAgensh

LiteAgensh is a C# single-machine adaptation of
[microsoft/Agensh](https://github.com/microsoft/Agensh). It keeps the upstream
peer-cooperation approach while replacing hosted collaboration infrastructure
with SQLite, local Git worktrees and existing native agents.

**Current state:** the C# application uses the official Copilot .NET SDK with
SQLite, local Git worktrees and worker-bound stdio MCP. Controlled scenarios
and Windows native observations cover cooperation, checked delivery, active
cancellation, fresh-summary resume and exact-session crash reconnect.
[PR #1530](https://github.com/hcoona/three/pull/1530) retains exact builds,
commands, reported credits, independent reviews and repository acceptance gates.
These results do not establish another operating system or native backend.
The accepted shared-credit design is specified in the
[credit contract](docs/implementation-contracts.md#shared-spending-windows).
The application implements opt-in shared allocation with controlled accounting
and lifecycle scenarios; installed-native limit mutation and renewal remain
unobserved until a separately recorded user-audit run. Per-session allocation
remains the default for existing inputs. Do not treat the implementation
or the owner's one-time ledger transition as a successful autonomous audit.
The [settlement contract](docs/implementation-contracts.md#usage-evidence-and-settlement)
also distinguishes user-turn completion from background compaction and bounds
complete final recovery of an uncertain closed window. These refinements still
require implementation and validation; the retained failed audit and independent
finding classifications are carried by [#1552](https://github.com/hcoona/three/issues/1552).
The MCP 2.3.0 upgrade is covered by controlled stdio contracts, not a new
native acceptance run; PR #1530 retains the earlier dependency baseline.

The [native-host design](docs/architecture.md#native-sdk-mcp-and-instruction-boundary)
specifies one owned runtime/client with independent peer sessions and a
cohort-settlement barrier. The application shares the operation's runtime and
client, stages peer usage until common-host settlement, and checks session
independence in its non-model preflight. Controlled scenarios are not evidence
of lower native memory use or a completed 32-peer audit; those claims still
require separately recorded observations of the accepted implementation.

The earlier failed ACP startup remains retained evidence, not measured zero
cost. The owner-approved [SDK baseline](docs/native-acceptance-protocol.md#sdk-campaign-baseline)
uses one new persistent ledger. Unknown usage in that active ledger blocks
further paid work; repeated runs do not reset it.

The [credit contract](docs/implementation-contracts.md#verified-pre-dispatch-cancellation)
specifies a distinct verified cancellation before first dispatch, not measured
zero billing. The application implements it with an accounting-only `reconcile`
command and controlled rejection, preservation and idempotence scenarios.
Earlier unknown reservations are not automatically released: each retained case
requires the owning protocol's authorization and fresh live proof checks.

## User and primary journey

The user is a developer who wants several coding agents to cooperate on one
local repository without deploying a Git server, chat server, database server
or container stack. The developer supplies a goal, a base revision, workers,
checks and a finite run budget; agents select work, communicate findings and
submit changes; the developer inspects a checked local delivery branch and can
intervene, pause or resume without losing partial work.

This context motivates the [requirements](docs/requirements.md). Their
acceptance evidence belongs in the [validation basis](docs/validation.md), not
in this overview.

## Running and checking the application

Use the repository-selected .NET SDK and the existing Copilot CLI version
selected by the [implementation contracts](docs/implementation-contracts.md).
SDK CLI acquisition and native automatic updates are disabled. Keep the
existing native authentication and default model unless an explicitly selected
supported model is required by the owning operational protocol; no provider
proxy is needed.

Edit [the YAML example](examples/run.yaml) and its separate
[Markdown goal](examples/goal.md). Set the target checkout, owned run state,
checks, explicit native permission policy, finite allowances and one persistent
campaign ledger. Relative repository, state, ledger and goal paths resolve from
the YAML file's directory, not the terminal directory. Keep state and ledger
outside the target checkout. The complete goal is shared by all peers; the
example starts ten workers with no privileged initial planner or fixed roles.

Set `workerStartIntervalSeconds` to control ramp-up. The example uses 30 seconds;
omitting it or setting zero retains immediate eligibility. With a positive
interval, the first peer starts immediately and later native workers start
gradually while earlier peers cooperate. Waiting workers do not hold started
native sessions. Startup and ramp-up count against the finite run deadline;
a stop may occur before every configured worker is admitted. This constant
interval is not the upstream experiment's two-stage deployment schedule.

Set `creditAllocation: shared` to let active peers draw renewable windows of
up to 100 credits from the same run/campaign pool. Cold peers do not reserve
fixed shares. Complete, idle turns return unused backing; peers wait cancellably
when other in-flight work holds the pool. Native caps remain soft, and an
unsupported limit change or unknown usage stops further paid admission.
The YAML example opts in; omitted values retain `per-session` behavior.

The YAML example's modeline associates [the JSON Schema](schemas/run.schema.json)
with the standard Red Hat YAML extension in VS Code for completion, hover and
validation. No editor extension is required at runtime. If copying the example
elsewhere, adjust its schema pointer and paths, or configure a YAML schema
association in the editor. Runtime validation also checks paths and cross-field
conditions that editor validation cannot establish.

YAML uses `goalFile`, not inline `goal`; JSON configuration remains supported
with its existing inline `goal` and absolute paths. Initialization snapshots the
resolved configuration and entire Markdown goal in SQLite. Editing the input
files later does not change resume/reconnect; initialize a new run to use new
inputs. See the [input contract](docs/implementation-contracts.md#configuration-input).

Worktrees separate edits, not host permissions. Build once and keep that exact
binary for preflight, calibration and subsequent lifecycle commands:

```powershell
dotnet build src\private\app\lite-agensh\LiteAgensh.csproj
$app = (Resolve-Path src\private\app\lite-agensh\bin\Debug\net10.0\LiteAgensh.dll).Path
dotnet $app init --config C:\Work\LiteAgensh\run.yaml
dotnet $app preflight --state C:\Work\LiteAgensh\run
dotnet $app calibrate --state C:\Work\LiteAgensh\run
dotnet $app run --state C:\Work\LiteAgensh\run
```

Preflight sends no model prompt. Calibration is a paid, tool-disabled turn
with a 30-credit native soft cap; successful complete usage is required before
ordinary dispatch. A changed binary or relied-on configuration needs matching
receipts. Soft caps can overshoot by an in-flight response.

Use `status` or request `pause` from another terminal with the same `--state`.
`run` stays in the current terminal and owns native worker subprocesses; it does
not open ten interactive terminals. Closing that terminal is not a controlled
pause.
Wait for actual coordinator/child settlement before `resume`, which creates
fresh sessions with durable summaries. After a coordinator crash, `reconnect`
resolves owned children and attempts the exact saved sessions; it does not
silently replace unknown usage. `finalize` retains local delivery/evidence;
`cleanup` is a separate explicit operation.

For an authorized retained reservation, use the targeted offline accounting path:

```powershell
dotnet $app reconcile --state C:\Work\LiteAgensh\run --reservation RESERVATION_ID
```

Legacy state without a dispatch-contract marker additionally requires
`--evidence C:\Work\LiteAgensh\owned-proof.json`. Only the supported source lineage
and matching referenced observations qualify. The command holds run ownership,
checks the exact old process identities and atomically rechecks the live ledger.
It never starts a native session, queries accounts, runs checks, resumes work or
exports a report. A successful cancellation preserves absent billing completeness
and unestablished measured actual usage; status lists it separately from measured
reconciliation. Missing or contradictory proof leaves uncertainty in place.

Run the controlled suite without native model calls:

```powershell
dotnet test --project tests\private\app\lite-agensh\LiteAgensh.Tests.csproj
```

## Records and authority

| Reader question                                                     | Authority                                                                                                                                                      |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| What must the local product do, and what is unsupported?            | [Requirements](docs/requirements.md)                                                                                                                           |
| How do C#, SQLite, native processes, MCP and Git fit together?      | [Architecture and integration boundaries](docs/architecture.md)                                                                                                |
| What replaces each upstream responsibility and collaboration tool?  | [Complete migration map and source evidence](docs/upstream-migration.md)                                                                                       |
| What evidence permits implementation and an eventual support claim? | [Validation basis](docs/validation.md)                                                                                                                         |
| Where is upstream attribution retained?                             | [Third-party notice](THIRD-PARTY-NOTICES.txt)                                                                                                                  |
| What concrete boundaries do independent implementation units share? | [Implementation contracts](docs/implementation-contracts.md)                                                                                                   |
| What exact native observation and credit effects may be executed?   | [Native acceptance protocol](docs/native-acceptance-protocol.md)                                                                                               |
| What governs the owner's actual 32-peer user-audit execution loop?  | [Real user audit protocol](docs/user-audit-protocol.md); separate from the closed development acceptance campaign                                              |
| What work is currently authorized?                                  | Repository [Delivery Wave](../../../../docs/delivery-wave.md)                                                                                                  |
| Where are progress, reviews and delivery dependencies?              | [Refinement #1542](https://github.com/hcoona/three/issues/1542), [original acceptance #1526](https://github.com/hcoona/three/issues/1526) and their linked PRs |

The README owns purpose, user context, current implementation status and routing.
Requirements own behavior; architecture owns realization; the migration map owns
source findings and treatment rationale; validation owns evidence obligations.
None of these grants work or replaces the accepted repository policies.

## Record admission and maintenance

The repository owner maintains these records with project authors. Implementers
and independent reviewers consume requirements before selecting behavior,
architecture before changing dependency/recovery boundaries, the migration map
before translating upstream mechanisms, and validation before claiming a journey
works. Missing any of those would respectively permit scope drift, an
incompatible platform substitution, an untracked semantic omission or an
unsupported acceptance claim. They need distinct carriers because their
questions and change/review points differ; this portal cannot serve as all four
authorities.

Independent implementation units also consume the implementation contracts;
without them their separately produced components could disagree at integration.
The native executor/reviewer consumes the applicable exact acceptance or
user-audit protocol because a
general validation basis cannot approve concrete provider effects or credit
accounting. The user-audit protocol separates current real-repository operation
from the closed fixture-only campaign. These contracts and protocols have
distinct implementation and observation review points, and are maintained by
the owner with their authors.

The source baseline and license stay with the map and notice. Review execution,
checker reports, mutable-source retrievals, findings and progress stay in Issues
and PRs. When implementation changes status, update this README rather than
creating a second progress ledger.
