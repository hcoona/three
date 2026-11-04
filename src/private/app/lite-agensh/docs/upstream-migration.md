# Upstream Migration and Evidence

This record owns the source-to-local responsibility inventory and its evidence
limits. Required behavior belongs in [requirements](requirements.md);
realization belongs in [architecture](architecture.md). A treatment describes
the initial product outcome, not implementation progress or a support claim.

## Baseline and evidence levels

The public source baseline is
[`microsoft/Agensh@2dae4e14a653da48549fd9aa093f834841475d6c`][upstream].
Source code and current component guides take precedence over the explicitly
draft `docs/task_pack_design.md`. Source findings establish what the upstream
implements; they do not prove that the local port works.

C#, SQLite, local worktrees, Copilot first, no additional sandbox and truthful
local MCP names are owner decisions. The preceding preparation preference for
a Node/TypeScript host is superseded; the product does not depend on importing
the upstream TypeScript/Python runtime wholesale.

**Port** means translate applicable policy into C#; **Replace** means satisfy a
responsibility through the local platform; **Adapt** means adapt cooperation
text or existing dependency use; **Omit** means no equivalent is required here;
**Defer** marks a capability outside this initial outcome, not a secretly
equivalent implementation. These names do not require one class/service per row.

## Source locations

All relative paths in the responsibility tables refer to the immutable baseline
above. These direct source links and named behaviors locate the supporting
implementation; the source is reviewable without a private session cache.

| Source area             | Stable entry points and relevant behavior                                                                                                                                                                                                                                                      |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Storage and dispatch    | [EventStore][state], especially `MAX_ATTEMPTS`, retry/claim/ack/recovery methods; [Dispatcher][dispatcher], serialized prompts and post-turn acknowledgement; [router startup][router], interrupted-send recovery                                                                              |
| Board policy            | [SharedLessons][board], types/caps/expiry/recent view; [verifier][verifier], text admission; [query][query] and [expansion][unfold], history matching and details; [board routes][board-main], snapshots, leases, shown ledger and persistence                                                 |
| Cooperation and notices | [Agent instructions][prompts], cooperation cycle; [notice shim][notices], wrapped infrastructure tool results; [group context][groups], bounded inline/backlog handling; [allowlist][allowlist], all 29 collaboration tools                                                                    |
| Git and quality         | [Software-project hooks][hooks], prepare/sync/export/evaluate; [quality][quality], `TEAM_ALERT_AFTER`, `LIFT_AFTER`, HealthGate/review; [backlog][backlog], multiple advisory claimants                                                                                                        |
| Lifecycle and host      | [TeamRun][team-run], `drive`, `resume_drive`, planning, watchdog and lifecycle; [schedule][schedule], timing; [project][project] and [pack][pack], configuration; [ACP channel][acp-channel], session reconnect; [Linux bridge][bridge] and [archive][archive], process and archive boundaries |
| Model transport         | [Model proxy][proxy] and [usage meter][usage], request rewriting and recorded-spend checks; these are not part of the selected local native-auth boundary                                                                                                                                      |

For example, `board-service/shared_lessons.py` lines 14-23 define the types,
100/300-character caps, 2,000-entry recent view and one-hour default claim TTL.
`mini-router/src/state.ts` lines 20-26 define 1/5/30/120-second retry delays and
five total attempts. `agensh/quality.py` lines 25-26 define the 15/60-minute
health transitions. `TeamRun.resume_drive` explicitly creates fresh sessions;
the ACP channel's crash reconnect is a different mechanism.

## Platform and process host

| ID  | Upstream responsibility/source                                                       | Treatment    | Local replacement or retained mechanism                                                                                                                   |
| --- | ------------------------------------------------------------------------------------ | ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P01 | MySQL event, cursor, agent KV and board tables; `state.ts`, router SQL, board routes | Replace      | SQLite with equivalent identity/state. Translate DDL, JSON SQL, upserts, row locks and matching semantics; end transactions before native/Git/check work. |
| P02 | Gitea repository hosting and clone/fetch/push URLs                                   | Replace      | Local Git, a dedicated integration branch/worktree and one branch/worktree per worker; no hosting server.                                                 |
| P03 | Gitea issues, PRs, reviews and statuses                                              | Replace      | SQLite task/change/review/check records and real operations bound to candidate commits, not merely chat messages.                                         |
| P04 | Mattermost accounts, channels, threads and messages                                  | Replace      | Run-scoped members, team/direct/threaded messages and searchable SQLite history.                                                                          |
| P05 | Gitea Actions and act-runner                                                         | Replace      | Declared host check subprocesses and serialized integration; retain command, candidate identity, outcome and log location.                                |
| P06 | FastAPI board HTTP service                                                           | Replace      | Shared local board operations through SQLite and stdio MCP; no separate HTTP service.                                                                     |
| P07 | Router sidecar per worker; `mini-router/src/main.ts`                                 | Replace      | Per-worker serialized dispatch loops inside one C# coordinator.                                                                                           |
| P08 | ACP Unix socket and Linux bridge                                                     | Replace      | Native Copilot ACP stdio with negotiated capabilities; no Unix socket, UID change or `/proc` dependency.                                                  |
| P09 | Worker image, entrypoint, private Node and image-version tests                       | Replace      | Installed executable discovery and contract/version preflight; no upstream experimental provider environment or runtime patches.                          |
| P10 | Compose, Make/profile generation and port/subnet allocation                          | Replace/Omit | Local lifecycle operations and persistent run directory replace startup/control; no images, service ports or subnets.                                     |
| P11 | Service bootstrap, secret-service and collaboration-service credentials              | Omit         | No collaboration-service accounts. Existing native backend configuration owns authentication.                                                             |
| P12 | Nginx edge and Gitea SSE/form authentication                                         | Omit         | No HTTP Git/chat edge or collaboration-service login remains.                                                                                             |
| P13 | Network overlay, egress-filter, cgroups and systemd guards                           | Omit         | No added isolation/service installation. Preserve actual runtime ownership before replacement.                                                            |
| P14 | Root container HOME and global Git credentials/config                                | Replace      | Run/session/worktree associations and command-local identity; preserve host global configuration.                                                         |

## Cooperation, board and events

| ID  | Upstream responsibility/source                     | Treatment    | Local replacement or retained mechanism                                                                                                                      |
| --- | -------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| C01 | Agent/objective/focus/work-cycle/delivery prompts  | Adapt        | Preserve peer selection and observe/claim/build/verify/merge/deliver discipline; change tool vocabulary, not target-repository instructions.                 |
| C02 | `render_agents.sh`, prompt rendering helpers       | Port         | Compose the same applicable layers as runtime context without Bash/envsubst or overwriting tracked `AGENTS.md`.                                              |
| C03 | Optional `TeamRun.planning_phase`                  | Port         | Initial planner wakes first; peers wake on integrated plan or planning timeout; no new intelligent scheduler.                                                |
| C04 | `shared_lessons.py`, `verifier.py`                 | Port         | Preserve types, caps, deterministic admission, patch replacement/invalidation, expiry and recent rendering; reported evidence text is not an executed check. |
| C05 | `CLAIM`, `backlog.py`                              | Port         | Advisory expiring claims and derived assignee sets; multiple claimants are valid.                                                                            |
| C06 | Admitted revision history versus recent board view | Replace/Port | SQLite retains admitted history separately from the bounded recent view.                                                                                     |
| C07 | `gquery.py`, `unfold.py`                           | Port         | Boolean/literal history search and labeled detail expansion; translate storage-dependent matching, not the user semantics.                                   |
| C08 | Unified events and rendering                       | Port         | Event identity, target, type, time and bounded summary; local operations emit events directly.                                                               |
| C09 | Gitea/Mattermost event listeners                   | Replace      | Atomic local domain/event changes and durable polling; no REST catch-up, SSE or WebSocket clients.                                                           |
| C10 | `dispatcher.ts`                                    | Port         | One in-flight turn per worker, batch order, shared-context prefix and acknowledgement after completed turn.                                                  |
| C11 | EventStore dedup, retries and session KV           | Replace/Port | Recipient dedup, source states, five-attempt schedule and restart recovery; no exactly-once action guarantee.                                                |
| C12 | Board snapshot/pending/shown/lease/cursor paths    | Port         | SQLite notice leases and monotone cursors; distinguish fetch, display and turn completion; reconcile queued delivery.                                        |
| C13 | `mcp_notice_shim.cjs`                              | Port         | Append eligible direct/urgent messages and fresh gists to local collaboration tool results, not arbitrary native commands.                                   |
| C14 | `group_messages.ts`                                | Port         | Bounded inline group context with full backlog access; replace Mattermost and Linux-specific file operations.                                                |
| C15 | MCP allow/deny/metadata configuration              | Replace/Port | Reviewed local capabilities/descriptions and applicable recursive-spawn filtering; no entire hosted API clone.                                               |
| C16 | Chat edits, deletions and reactions                | Defer        | Post/read/search/threads are included; complete interactive chat UX is not.                                                                                  |

## Git, validation, lifecycle and operator control

| ID  | Upstream responsibility/source                          | Treatment    | Local replacement or retained mechanism                                                                                                                    |
| --- | ------------------------------------------------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L01 | Software-project prepare/full-history import            | Replace      | Exact local base and dedicated run/worker refs; preserve original dirty checkout and history.                                                              |
| L02 | Empty-project seed                                      | Port         | Explicit local initialization when requested, not a requirement to start from an empty project.                                                            |
| L03 | Worker branches and PR creation/merge                   | Replace      | Local candidate-bound change operations and serialized Git integration; return conflicts without losing work.                                              |
| L04 | `acceptance.pr`, `.main`, `.final`                      | Port/Replace | Separate candidate, integration and final checks through declared supported host commands rather than hardcoded Bash.                                      |
| L05 | `quality.py` HealthGate                                 | Port         | Fix-main period, 15-minute alert and 60-minute restriction lift; not permanent fail-closed protection.                                                     |
| L06 | Peer review, CODEOWNERS and protected paths             | Port/Replace | Local integration review decisions; cooperative workflow, not server-enforced protection against direct same-user Git/DB edits.                            |
| L07 | Candidate movement during integration                   | Replace      | Checks/reviews bind to exact candidates and integration head; checking the merged candidate is a Lite design choice.                                       |
| L08 | Scheduled/resume/final upstream sync                    | Port         | Explicit configured local base ref, conflict reporting and bounded retries; no implicit remote fetch.                                                      |
| L09 | Final export/delivery hook                              | Replace      | Retained local branch/report with base/final/check identities; no automatic remote push or GitHub PR.                                                      |
| L10 | TeamRun segments, control file and lifecycle CLI        | Port/Replace | SQLite lifecycle/segment/end-reason state and local operator requests.                                                                                     |
| L11 | `schedule.py` and lifecycle prompts                     | Port         | Wall-clock budget, wake/idle timing, notices and reminders where applicable.                                                                               |
| L12 | Native-journal watchdog and queue inspection            | Replace/Port | Owned processes, ACP activity and queue state; silence alone does not establish process exit.                                                              |
| L13 | Router reconnect/crash recovery                         | Port         | Resolve ownership and attempt exact-session load when advertised; explicit fresh-summary recovery when unavailable.                                        |
| L14 | Explicit pause/resume                                   | Port         | Retain work, then start fresh sessions with durable state summary; not crash reconnect.                                                                    |
| L15 | Native cancellation/child cleanup                       | Replace      | Cancel, bounded wait and owned-process cleanup using host APIs; no broad name/UID killing.                                                                 |
| L16 | Full HOME/workspace tar and sealed teardown archive     | Replace      | Preserve local worktrees, SQLite and session associations; separate explicit cleanup. Not portable sealed-backup equivalence.                              |
| L17 | Human owner messages, tasks and approval                | Replace/Port | Local human identity, urgent operator events and review decisions; other work can continue.                                                                |
| L18 | Root task closure and fixed-length runs                 | Port         | Actor-aware closure and configured fixed-length behavior; closure is a stop signal, not passed acceptance.                                                 |
| L19 | Status/digest and container logs                        | Port/Replace | SQLite/Git/native-association status and retained local process/check logs; no container collection.                                                       |
| L20 | `project.py`, `pack.py`, profile/environment precedence | Port/Omit    | Goal/base/workers/backend/model/checks/timing and applicable coordination options; remove image/network/account/port fields and generic pack plugin scope. |

## Native model runtime and optional capabilities

| ID  | Upstream responsibility/source                                           | Treatment            | Local replacement or retained mechanism                                                               |
| --- | ------------------------------------------------------------------------ | -------------------- | ----------------------------------------------------------------------------------------------------- |
| R01 | Native reasoning, shell/edit tools and compaction                        | Adapt dependency use | Existing Copilot runtime remains responsible; Lite is not a second model/tool loop.                   |
| R02 | Experimental provider setup and HTTP credential/rewrite/retry proxy      | Omit                 | Native provider transport/authentication/retry settings; do not inspect or rewrite model traffic.     |
| R03 | Uniform output/context/tool-result cap normalization and runtime patches | Defer                | Only supported native settings; no cross-backend equivalence. Lite-owned board/message bounds remain. |
| R04 | Usage proxy and aggregate token stop                                     | Defer guarantee      | Report usage only through a verified interface; wall-clock/manual stop is not a token-request cutoff. |
| R05 | Proxy model rewriting per worker                                         | Replace              | Documented native process/session model settings, where supported.                                    |
| R06 | `/raw` and `/raw-final` model routes                                     | Defer                | No Lite model-API proxy, equivalent shared metering or key-hiding service.                            |
| R07 | Codex/Pi/Claude adapters                                                 | Defer                | Reference inputs only; no other-backend support in this outcome.                                      |
| R08 | Snapshot/sanitize import and private evaluator mounts                    | Defer                | No history-hiding or evaluator-only access guarantee in shared local worktrees.                       |
| R09 | ProgramBench cleanroom/reference binary/scoring                          | Omit                 | Not benchmark reproduction; no score or performance equivalence claim.                                |
| R10 | All-service UI, generic discovery and multiple teams                     | Defer/Omit           | One bound local repository/run and its known members; see the tool map.                               |

## Collaboration MCP map

The [source allowlist][allowlist] has 13 Gitea, 12 Mattermost and 4 board
tools. Every entry appears below. Local names describe capabilities; operation
schemas are accepted with their implementation slice. This is semantic
coverage, not a frozen wire-compatible shim.

| Upstream tool                      | Local capability vocabulary         | Meaning or explicit removal                                                          |
| ---------------------------------- | ----------------------------------- | ------------------------------------------------------------------------------------ |
| `gitea.actions_run_read`           | `check_read`                        | Local check status and retained logs.                                                |
| `gitea.get_me`                     | `agent_info`                        | Run identity, not a service account.                                                 |
| `gitea.issue_read`                 | `task_read`                         | Task body/comments/state/claim-derived assignees.                                    |
| `gitea.issue_write`                | `task_write`                        | Local create/update/comment/close operations.                                        |
| `gitea.list_issues`                | `task_list`                         | Local backlog.                                                                       |
| `gitea.list_my_repos`              | Remove; repository in `run_info`    | No arbitrary hosted-repository catalog.                                              |
| `gitea.list_pull_requests`         | `change_list`                       | Local change requests.                                                               |
| `gitea.pull_request_read`          | `change_read`                       | Candidate, diff, checks, comments and reviews.                                       |
| `gitea.pull_request_review_write`  | `change_review`                     | Review bound to the exact candidate.                                                 |
| `gitea.pull_request_write`         | `change_write`                      | Submit/update/comment/request integration; coordinator records actual merge outcome. |
| `gitea.search_issues`              | `work_search`                       | Search tasks/changes without erasing their distinction.                              |
| `gitea.search_repos`               | Remove; repository in `run_info`    | Bound local repository only.                                                         |
| `gitea.search_users`               | `agent_list`                        | Known run members only.                                                              |
| `mattermost.create_direct_channel` | Remove; recipient in `message_send` | No synthetic remote-channel creation step.                                           |
| `mattermost.get_channel`           | `channel_read`                      | Local channel metadata.                                                              |
| `mattermost.get_channel_by_name`   | `channel_read`                      | Resolve local team channel.                                                          |
| `mattermost.get_channel_messages`  | `message_read`                      | History and cursor.                                                                  |
| `mattermost.get_me`                | `agent_info`                        | Same run identity.                                                                   |
| `mattermost.get_thread`            | `message_thread`                    | Reply/thread history.                                                                |
| `mattermost.get_user`              | `agent_info`                        | Known member by ID.                                                                  |
| `mattermost.get_user_by_username`  | `agent_info`                        | Known member by name.                                                                |
| `mattermost.list_my_channels`      | `channel_list`                      | Current run's channels.                                                              |
| `mattermost.list_teams`            | Remove; team in `run_info`          | No multi-team membership platform.                                                   |
| `mattermost.post_message`          | `message_send`                      | Team/direct/reply messages and applicable mentions.                                  |
| `mattermost.search_messages`       | `message_search`                    | Local history search.                                                                |
| `board.board_grep`                 | `board_search`                      | Boolean/literal history query.                                                       |
| `board.board_read`                 | `board_recent`                      | Recent live board view.                                                              |
| `board.board_unfold`               | `board_expand`                      | Detail expansion by label.                                                           |
| `board.board_write`                | `board_publish`                     | Typed gist admission under bound run/worker identity.                                |

## Verification, tooling and provenance

| ID  | Upstream responsibility/source                  | Treatment           | Local replacement or retained mechanism                                                                                          |
| --- | ----------------------------------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| V01 | Board/query/schedule/backlog/quality unit cases | Port cases          | Focused core-policy tests without freezing translated classes or private call order.                                             |
| V02 | Dispatcher/group/urgent/context scenarios       | Port cases          | Local event/store/tool scenarios protect ordering and dedup behavior.                                                            |
| V03 | MySQL storage and HTTP integration boundaries   | Replace evidence    | New SQLite transaction, cursor, matching and restart scenarios; upstream green tests cannot validate this translation.           |
| V04 | Docker smoke and scripted ACP worker            | Replace harness     | Local scripted workers exercise cooperation/check/integration without model calls or services.                                   |
| V05 | Live native harness                             | Replace environment | Accepted Windows native protocol must establish actual ACP/MCP/cooperation/cancellation/recovery behavior. Stubs cannot.         |
| V06 | Profile/render/config/CLI snapshots             | Replace cases       | Local configuration/instructions and original-checkout preservation, without deployment-profile compatibility.                   |
| V07 | Upstream CI/install/build/release scripts       | Replace             | Existing monorepo .NET/CPM/NBGV/HK and project-test paths, not PNPM as a product host or upstream image pipelines.               |
| V08 | MIT notice and research/demo assets             | Retain attribution  | [Retained notice](../THIRD-PARTY-NOTICES.txt), pinned source links and honest evidence scope; papers/demos are not Lite results. |

## Mutable contracts

These are public interface findings retrieved on 2026-11-04, not observed
integration results:

- [Copilot ACP documentation][copilot-acp]: public preview; native stdio uses
  NDJSON, supports cwd/MCP configuration, and places tool filters/reasoning
  options at server startup rather than arbitrary `session/new` fields.
  Existing enterprise policy can override a requested no-sandbox option.
- [ACP session setup][acp-sessions]: initialization precedes sessions; loading
  an exact saved session requires advertised `loadSession` support. Do not
  infer that newer optional protocol methods exist in a particular Copilot
  version.
- [Microsoft.Data.Sqlite transactions][sqlite-transactions],
  [errors/timeouts][sqlite-errors] and [async limitations][sqlite-async]:
  one pending writer, thread-unsafe connection/command/reader objects, provider
  busy retries bounded by timeout, and synchronous async-ADO.NET execution.
  WAL improves reader/writer concurrency; it does not add parallel writers.

Local metadata observation on that date: `copilot --version` reported
`GitHub Copilot CLI 1.0.105`; `git --version` reported
`git version 2.53.0.windows.1`. These commands did not create an agent session,
negotiate ACP, call a model or test Git integration.

The implementing author rechecks the relevant official contract and installed
or selected package/executable version before first dependent implementation,
after a dependency/CLI upgrade or a protocol mismatch. The independent
research-evidence reviewer evaluates the result and affected conclusions in the
PR; changed/uncertain contracts pause only dependent work until the design and
validation agree. Each merged Wave change is the fallback applicability review.
The same trigger/evaluator/action/fallback covers SQLite/MCP packages when their
slice selects versions. The Agensh baseline is immutable; choosing another
revision requires a source-delta review, not a freshness poll.

## Evidence boundary and material differences

The 68 responsibility rows and 29 tool rows are a migration inventory, not a
line-by-line port or a completeness proof for runtime behavior. No local
SQLite, process, ACP/MCP, cancellation, merge or native cooperation observation
has been completed. [Validation](validation.md) owns how those gaps close.

Preserve these differences in all downstream claims:

- Local workflow gates do not equal Gitea's protection against bypass.
- No model proxy means no equivalent aggregate token-request cutoff or
  credential hiding; even upstream checks recorded spend before requests,
  rather than reserving an exact no-overshoot budget.
- Persistent same-host state does not equal a sealed portable archive.
- Explicit fresh-session resume is distinct from exact-session crash reconnect.
- Merged-candidate checking is a local design choice, not a source finding.
- Deferred backends, hosted delivery, chat UI, model routes and evaluator
  guarantees are unsupported, not unimplemented details of a parity claim.

The Microsoft MIT notice is retained for derived material. This record does
not relicense unrelated upstream assets or import their demos/benchmarks.
Implementation ports must retain applicable source notices as well.

[upstream]: https://github.com/microsoft/Agensh/tree/2dae4e14a653da48549fd9aa093f834841475d6c
[state]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/src/state.ts
[dispatcher]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/src/dispatcher.ts
[router]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/src/main.ts
[board]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/board-service/shared_lessons.py
[verifier]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/board-service/verifier.py
[query]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/board-service/gquery.py
[unfold]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/board-service/unfold.py
[board-main]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/board-service/main.py
[prompts]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/prompts/agents.txt
[notices]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/mcp_notice_shim.cjs
[groups]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/src/group_messages.ts
[allowlist]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agent/config/mcp_allowed_tools.json
[hooks]: https://github.com/microsoft/Agensh/tree/2dae4e14a653da48549fd9aa093f834841475d6c/tasks/software-project/hooks
[quality]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/quality.py
[backlog]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/backlog.py
[team-run]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/team_run.py
[schedule]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/schedule.py
[project]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/project.py
[pack]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agensh/pack.py
[acp-channel]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/mini-router/src/acp_socket_channel.ts
[bridge]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agent/acp_bridge.py
[archive]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/agent/archive.py
[proxy]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/scripts/strip_metadata_proxy.py
[usage]: https://github.com/microsoft/Agensh/blob/2dae4e14a653da48549fd9aa093f834841475d6c/scripts/usage_meter.py
[copilot-acp]: https://docs.github.com/en/copilot/reference/copilot-cli-reference/acp-server
[acp-sessions]: https://agentclientprotocol.com/protocol/v1/session-setup
[sqlite-transactions]: https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions
[sqlite-errors]: https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors
[sqlite-async]: https://learn.microsoft.com/dotnet/standard/data/sqlite/async
