# Research — Session-Aware IM-ACP Gateway

## Provenance

- Kind: implementation research and architecture note.
- Requested outcome: document how to implement an Instant Messaging (IM) to
  Agent Client Protocol (ACP) gateway with session management.
- Initial target scope:
    - IM side starts with **WeChat**.
    - Agent side starts with **GitHub Copilot CLI** through ACP.
- Long-term direction:
    - support multiple IM channels,
    - support multiple agents,
    - keep session management as a first-class product capability.
- Key external references reviewed for this note:
    - [GitHub Copilot CLI ACP server reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/acp-server)
    - [GitHub Copilot CLI session data / chronicle](https://docs.github.com/en/copilot/how-tos/copilot-cli/chronicle)
    - [Using GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/use-copilot-cli-agents/overview)
    - [Configure GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/configure-copilot-cli)
    - [Agent Client Protocol overview](https://agentclientprotocol.com/protocol/overview)
    - [ACP initialization](https://agentclientprotocol.com/protocol/initialization)
    - [ACP session setup](https://agentclientprotocol.com/protocol/session-setup)
    - [ACP session list](https://agentclientprotocol.com/protocol/session-list)
    - [ACP prompt turn lifecycle](https://agentclientprotocol.com/protocol/prompt-turn)
    - [ACP TypeScript SDK](https://agentclientprotocol.com/libraries/typescript)
    - [GitHub blog: ACP support in Copilot CLI public preview](https://github.blog/changelog/2026-01-28-acp-support-in-copilot-cli-is-now-in-public-preview/)
    - public repository artifacts for `@tencent-weixin/openclaw-weixin`:
        - [package README](https://github.com/hao-ji-xing/openclaw-weixin/tree/main/packages/openclaw-weixin)
        - [`packages/openclaw-weixin/package.json`](https://github.com/hao-ji-xing/openclaw-weixin/blob/main/packages/openclaw-weixin/package.json)
        - [`packages/openclaw-weixin/src/api/types.ts`](https://github.com/hao-ji-xing/openclaw-weixin/blob/main/packages/openclaw-weixin/src/api/types.ts)
        - [`protocol.md`](https://github.com/hao-ji-xing/openclaw-weixin/blob/main/protocol.md)
    - public technical write-up for the WeChat iLink / OpenClaw Weixin path:
      [weixin-bot-api.md](https://github.com/hao-ji-xing/openclaw-weixin/blob/main/weixin-bot-api.md)
    - [OpenClaw WeChat integration guide](https://openclawdoc.com/docs/channels/wechat/)

This document is a design-oriented research note. It records recommended
product and technical direction rather than a final implementation contract.

## Current discovery-stage scope assumptions

The current requirement and research stage is intentionally narrower than the
long-term product direction.

At the time of this revision, the working assumptions are:

- the immediate target is **personal use**, not a shared multi-tenant gateway,
- the first supported topology is **single user, single WeChat account, single
  device**,
- the primary IM track under investigation is **Personal WeChat via the iLink /
  ClawBot path**, using `@tencent-weixin/openclaw-weixin` as the strongest
  currently accessible public reference,
- the gateway should persist its own local state, and a local database such as
  **SQLite** is acceptable for the first version,
- the gateway and Copilot CLI should remain **separate processes** so that the
  gateway can persist state and evolve independently, and
- approvals are still in scope for the IM experience, even if the local Copilot
  process sometimes runs with aggressive permission settings.

## Executive summary

Building a session-aware IM-ACP gateway is **feasible** and **well aligned with
ACP**.

The most important architectural conclusion is this:

> The gateway should own the session and routing control plane. WeChat should be
> treated as a transport/channel adapter, and Copilot CLI should be treated as
> an ACP agent backend.

For the first version, the cleanest design is:

`WeChat Adapter -> Gateway Core -> ACP Client -> Copilot CLI ACP Server`

This is preferable to making OpenClaw the orchestration layer because:

1. ACP already provides the correct agent interaction model.
2. Session ownership must remain stable across future IM channels and future
   agent backends.
3. A gateway-controlled session registry is required for reply-based routing,
   permission handling, cancellation, and auditability.
4. OpenClaw Weixin material is highly useful as a **WeChat transport reference**
   but is not the best place to anchor the gateway's core session semantics.

## Problem statement

The product must allow a user to communicate with a coding agent from an IM
client and receive results back in the same IM client, while keeping multiple
concurrent sessions manageable and understandable.

For the initial target:

1. a user sends messages from WeChat,
2. the gateway routes them to Copilot CLI through ACP,
3. Copilot CLI returns streaming updates and final responses,
4. the gateway sends those updates back to WeChat, and
5. the user can continue or control a specific session by replying to a prior
   message or by using explicit session commands.

This implies the gateway is not just a text relay. It must manage:

- session identity,
- message-to-session correlation,
- session lifecycle,
- permission requests,
- turn cancellation,
- persistence,
- retries,
- operational safety, and
- future extensibility to more IMs and agents.

## Why ACP is the correct protocol layer

ACP is a good fit because the required user experience maps directly onto ACP's
session and turn model.

### Confirmed ACP properties that matter

From the official ACP protocol and Copilot CLI ACP documentation:

- ACP uses **JSON-RPC 2.0** semantics.
- Copilot CLI can run as an ACP server in **stdio** mode or **TCP** mode.
- The normal lifecycle is:
  `initialize -> session/new or session/load -> session/prompt -> session/update`.
- ACP supports:
    - multiple independent sessions,
    - streaming updates,
    - permission requests,
    - turn cancellation,
    - optional session loading,
    - optional session listing,
    - optional MCP server attachment.

### Why this matches the IM gateway problem

The gateway needs more than one-shot prompting.

It needs:

- ongoing sessions,
- incremental output delivery,
- controllable long-running turns,
- a way to approve or deny tool actions,
- support for future richer payloads,
- a stable contract across multiple agent implementations.

ACP provides these concepts directly. A plain CLI wrapper around a
single-command mode would not naturally cover the same lifecycle.

## Copilot CLI findings

The Copilot CLI ACP server is in **public preview**, but the official
documentation is already sufficient to support a structured gateway design.

### Relevant Copilot CLI capabilities

- Start in stdio mode:

    ```text
    copilot --acp --stdio
    ```

- Start in TCP mode:

    ```text
    copilot --acp --port 3000
    ```

- Use cases explicitly include:
    - custom frontends,
    - multi-agent systems,
    - IDE integrations,
    - CI/CD automation.

### Practical implications

- **Stdio** is the best starting transport for a local gateway-managed child
  process.
- The official TypeScript example and `@agentclientprotocol/sdk` make a
  TypeScript/Node.js implementation especially practical.
- Capability negotiation must be implemented correctly because features such as
  `session/load` are optional.
- The gateway should treat ACP server capabilities as **runtime-discovered**, not
  hard-coded assumptions.

### Officially confirmed session-resume boundary

Official GitHub documentation confirms that the **Copilot CLI product** stores
session data locally on the machine and supports resuming prior interactive
sessions through:

- `copilot --continue`,
- `copilot --resume [SESSION-ID]`, and
- `/resume`.

The official session-data documentation states that resuming a session loads the
full conversation history so the user can continue where they left off.

However, the official **Copilot ACP server** page does **not** separately
promise Copilot-specific support for `session/load` or for a session-list
operation. ACP itself defines `session/load` as an optional capability, so the
gateway should distinguish between:

- what is officially confirmed for the Copilot CLI product,
- what is defined at the ACP protocol level, and
- what must still be detected dynamically from the ACP server's advertised
  capabilities.

In addition, a direct `initialize` probe against `copilot --acp --stdio` in the
current research environment returned:

- `loadSession: true`,
- `sessionCapabilities.list: {}`,
- `agentInfo.name: "Copilot"`, and
- `agentInfo.version: "1.0.11"`.

That is a useful implementation fact for this specific environment and version.
It is stronger than mere speculation, but it should still be treated as an
**observed capability**, not as an unconditional promise for all future Copilot
CLI ACP releases.

## WeChat findings

Publicly accessible repository artifacts for `@tencent-weixin/openclaw-weixin`
provide stronger evidence than generic secondary write-ups alone. In the
published repository:

- the package metadata lists `author: "Tencent"`,
- the README documents QR-code login, local credential persistence, and
  multi-account usage,
- the exported message and API types document long polling, `context_token`,
  message/session identifiers, quoted-message structures, and media upload
  flows, and
- the repository also publishes WeChat ClawBot terms describing the service as a
  Tencent-provided bridge between WeChat and a third-party AI service selected
  by the user.

Taken together, these public artifacts strongly suggest that the current
Personal WeChat path under investigation is based on a real iLink / ClawBot
integration path and can support:

- QR-based login,
- long-polling inbound messages,
- outbound message send APIs,
- multi-account state,
- text and media payloads,
- context tokens for reply correlation.

### Evidence strength for the Personal WeChat route

The evidence for **Personal WeChat via iLink** is meaningful, but it is still
important to distinguish its confidence level from the confidence level of
official GitHub ACP and Copilot CLI documentation.

- The strongest evidence currently available in this research pass is the
  **official-team package and public repository artifacts** for
  `@tencent-weixin/openclaw-weixin`.
- The official OpenClaw WeChat documentation retrieved during this research pass
  is stronger for **Official Account** and **WeCom / Enterprise WeChat**
  integration than it is for Personal WeChat.
- As a result, the Personal WeChat route is a reasonable primary research track,
  but claims about it should be phrased as **package-backed and repo-backed**
  rather than as universally documented first-party platform contract.

### Important WeChat-specific observations

Based on the public repository artifacts, public technical write-up, and related
references:

- inbound messages are polled through a cursor-like update mechanism,
- outbound replies require a conversation correlation field
  (`context_token` in the published analysis),
- media transfer introduces extra complexity such as upload steps and encrypted
  content handling,
- the transport is useful, but it should remain behind a clean
  `WeChatAdapter` boundary in the gateway.

### Reply-routing evidence discovered

The public WeChat plugin artifacts provide encouraging evidence that reply-aware
routing is plausible:

- the published message schema exposes `message_id`, `session_id`,
  per-item `msg_id`, `context_token`, and quoted-message `ref_msg` data,
- the published runtime message-processing code actually uses `context_token`
  and quoted-message/media handling instead of merely declaring those fields in
  types,
- quoted-message text is explicitly reconstructed from `ref_msg`, while quoted
  media is handled as a distinct case in the message-processing pipeline, and
- the published plugin code caches `context_token` in process memory rather than
  persisting it, which is a useful warning sign for gateway restart and recovery
  requirements.

This is enough to treat reply-aware routing as a serious requirement input.
However, it is not yet enough to claim that all WeChat clients or all future
platform revisions will preserve identical reply-metadata semantics. That
remains a research caveat.

### Product conclusion for WeChat

For the gateway design, WeChat should be treated as:

- an inbound event source,
- an outbound delivery channel,
- a provider of user identity and message reply metadata,
- a place where session references can be surfaced to the user.

It should **not** define the gateway's core session model.

## Recommended architecture

### Architecture overview

```text
+-------------------+       +----------------------+       +----------------------+
| WeChat Adapter    | ----> | Gateway Core         | ----> | Copilot ACP Adapter  |
| - login           |       | - session registry   |       | - initialize         |
| - receive message |       | - message routing    |       | - new/load session   |
| - send message    | <---- | - turn orchestration | <---- | - prompt/cancel      |
| - reply metadata  |       | - permission state   |       | - stream updates     |
+-------------------+       | - persistence        |       +----------------------+
                            | - audit/logging      |
                            +----------------------+
```

### Architectural principles

### 1. Gateway-owned session control plane

The gateway must own:

- gateway session identifiers,
- mappings between IM messages and gateway sessions,
- mappings between gateway sessions and ACP session identifiers,
- user/channel/workspace association,
- active turn state,
- permission wait state,
- durable history and audit state.

This should remain true even if the underlying agent also persists sessions.

### 2. Adapter isolation

The gateway should isolate:

- IM transport logic per channel,
- agent protocol logic per agent backend,
- core session/routing logic in a channel-agnostic and agent-agnostic layer.

This is necessary to support:

- WeChat first, then other IM channels later,
- Copilot CLI first, then other ACP or non-ACP agents later.

### 3. Capability-driven agent integration

ACP features such as `session/load`, MCP transport support, and richer prompt
content must be used only after capability negotiation.

### 4. Explicit correlation everywhere

Every inbound and outbound event should carry enough correlation data to answer:

- which IM user sent it,
- which IM conversation it belongs to,
- which gateway session it targets,
- which ACP session it targets,
- which turn it belongs to,
- which outbound IM message IDs were produced.

## Session management model

Session management is the core requirement of this product.

### Proposed session layers

#### IM conversation context

Represents where the message came from.

Example dimensions:

- channel: `wechat`
- account or tenant ID
- chat ID or peer ID
- sender user ID
- group/direct chat type

#### Gateway session

Represents the user-facing logical conversation managed by this product.

Suggested fields:

- `gateway_session_id`
- `channel`
- `channel_user_id`
- `channel_chat_id`
- `agent_backend`
- `workspace_id`
- `cwd`
- `status`
- `created_at`
- `updated_at`
- `last_turn_id`
- `active_acp_session_id`
- `title`
- `last_outbound_message_id`

#### ACP session

Represents the session created in the agent backend.

Suggested fields:

- `agent_type`
- `acp_session_id`
- `agent_instance_id`
- `load_supported`
- `agent_capabilities_snapshot`
- `agent_info_snapshot`

### Recommended ownership rule

Use `gateway_session_id` as the product's stable primary key.

Do not expose raw `acp_session_id` as the only user-facing handle. The ACP
session ID is an implementation detail that may change when sessions are
reloaded, migrated, or bridged across agent backends.

### Reply-based routing

Reply-based routing is the most natural user interaction pattern in IM.

The gateway should persist:

- inbound IM message ID,
- outbound IM message ID,
- gateway session ID,
- turn ID,
- optional ACP tool call / permission correlation ID.

Then when a user replies to a previous bot message, the gateway can resolve:

`replied_message_id -> gateway_session_id -> active ACP session`

This avoids asking the agent backend to solve a UI routing problem.

### Explicit session commands

Reply metadata may not always be available or reliable. Therefore the gateway
should also support explicit commands such as:

- `/new`
- `/sessions`
- `/use <session>`
- `/rename <session> <title>`
- `/stop`
- `/approve`
- `/deny`
- `/close`

These commands should be channel-normalized by the gateway, not implemented
inside each agent backend.

## Message and turn lifecycle

The initial WeChat-to-Copilot turn flow should work like this:

1. Receive inbound WeChat text message.
2. Resolve target gateway session:
    - from reply metadata when present,
    - otherwise from explicit command,
    - otherwise from the user's current active session,
    - otherwise create a new session.
3. Resolve or create ACP session:
    - initialize the ACP connection if needed,
    - create a new ACP session or load an existing one when supported.
4. Send `session/prompt`.
5. Stream `session/update` events back into gateway events.
6. Convert agent updates into WeChat-friendly outputs:
    - partial text,
    - final answer,
    - tool/progress notices,
    - permission prompts.
7. Persist all mappings and turn state.
8. Mark the turn complete when ACP returns a final stop reason.

### Cancellation flow

If the user sends `/stop` or equivalent:

1. resolve the active turn,
2. send `session/cancel`,
3. mark pending permission requests as cancelled,
4. deliver a cancellation confirmation to WeChat,
5. persist the cancelled state.

### Permission flow

When the ACP agent requests permission:

1. create a durable permission request record,
2. send a clear approval message to WeChat,
3. bind the user's reply or command to the permission request,
4. respond to ACP with the selected outcome,
5. record the decision for audit purposes.

This flow must be durable because IM interactions are asynchronous and may span
network retries or process restarts.

## Major functional capabilities

The gateway should eventually support the following major functions.

### Session creation and continuation

- create new sessions,
- continue a session by reply or command,
- list recent sessions,
- reopen or resume a session,
- rename sessions for user readability.

### Turn execution

- send prompts to the target agent,
- receive streaming updates,
- expose progress in IM-safe form,
- support cancellation.

### Permission mediation

- surface tool approval requests,
- allow approve-once and deny,
- optionally add approve-for-session later,
- preserve an auditable record.

### Delivery formatting

- render agent updates into IM-friendly text,
- avoid flooding the channel with too many chunks,
- chunk long final responses safely,
- preserve enough metadata for follow-up routing.

### Session-aware reply correlation

- map bot messages to sessions and turns,
- support reply-to-message routing,
- support fallback to explicit session selection.

### Persistence and recovery

- recover active sessions after restart,
- recover pending permission requests,
- rebuild active session pointers per user,
- optionally resume ACP sessions when supported by the agent.

### Observability and operations

- structured logs,
- session and turn audit trail,
- dead-letter or retry queue for outbound IM delivery failures,
- admin diagnostics for stuck sessions and pending approvals.

## Key technologies

### 1. ACP client implementation

Use the official ACP model and strongly consider the TypeScript SDK:

- package: `@agentclientprotocol/sdk`
- likely implementation path: `ClientSideConnection`
- transport: NDJSON over stdio for the first version

This reduces protocol risk and keeps the code aligned with the published
protocol examples.

### 2. Process management for Copilot CLI

The gateway will need controlled management of one or more Copilot CLI ACP
server processes.

Questions to decide during implementation:

- one shared ACP process per host,
- one ACP process per user,
- one ACP process per workspace,
- or a pooled model.

For the first version, a conservative design is:

- one managed Copilot ACP server process per gateway worker or per configured
  execution environment,
- multiple ACP sessions multiplexed over that connection when practical.

### 3. Durable state store

A relational store is a strong fit because the gateway is correlation-heavy.

Suggested initial storage:

- SQLite for local single-node development or MVP,
- PostgreSQL for multi-worker or production deployment.

Suggested tables:

- `channel_accounts`
- `gateway_sessions`
- `agent_sessions`
- `turns`
- `messages`
- `permission_requests`
- `outbound_deliveries`
- `agent_connections`

### 4. Eventing and background workers

Even if the first version is simple, the design should separate:

- inbound IM polling/webhook intake,
- turn orchestration,
- outbound delivery,
- retry handling,
- cleanup and health monitoring.

An internal job queue becomes increasingly useful once retries, chunked output,
media upload, or multiple IM channels are introduced.

### 5. WeChat channel transport

For WeChat specifically, the adapter likely needs:

- QR login bootstrap,
- token persistence,
- long-polling consumer,
- outbound send wrapper,
- mapping between WeChat message metadata and gateway message records.

The transport should be wrapped behind a stable interface so the gateway core
does not depend on Tencent-specific request shapes.

### 6. Content normalization

The gateway should define an internal normalized message model so that future IM
channels do not leak channel-specific shapes into the rest of the system.

Suggested normalized inbound model:

- sender
- conversation
- reply target
- text blocks
- attachments
- timestamps
- raw channel metadata

Suggested normalized outbound model:

- target conversation
- display text
- reply-to target
- severity or type
- chunk index
- raw channel options

## Why TypeScript is a strong implementation choice

For the initial target, TypeScript is a strong fit because:

1. Copilot CLI ACP documentation already provides a TypeScript integration
   example.
2. The official ACP SDK is available for TypeScript.
3. WeChat transport material around OpenClaw is also TypeScript-oriented.
4. The gateway is I/O-heavy, protocol-heavy, and persistence-heavy rather than
   compute-heavy.
5. A monorepo already contains JavaScript/TypeScript tooling.

This does not make TypeScript mandatory forever, but it is a pragmatic first
implementation language for WeChat plus Copilot CLI.

## Key design decisions

### Decision 1: Keep session state in the gateway, not only in Copilot

Reason:

- ACP `session/load` is optional.
- Copilot CLI ACP support is preview.
- IM reply routing is a client/UI concern.
- the gateway needs stable product-level session semantics.

### Decision 2: Treat OpenClaw Weixin artifacts as transport references, not as

the gateway core

Reason:

- they are valuable for WeChat access and protocol understanding,
- but using OpenClaw itself as the middle control plane would create overlapping
  session semantics and more moving pieces.

### Decision 3: Build explicit reply mapping

Reason:

- IM users think in terms of replying to prior messages,
- this is more natural than typing session IDs,
- the mapping is easier and safer to do on the client/gateway side.

### Decision 4: Start with text-first support

Reason:

- text is enough to validate session routing, permission handling, and streaming,
- media support on WeChat introduces extra protocol and cryptographic
  complexity,
- Copilot CLI text workflows are already sufficient for the first product value.

## Risks and constraints

### 1. Copilot CLI ACP is preview

Risk:

- capability shape or behavior may evolve.

Mitigation:

- wrap ACP integration behind an adapter,
- implement strict capability negotiation,
- avoid assuming optional methods exist.

### 2. WeChat platform and policy dependence

Risk:

- channel availability, quotas, login rules, or moderation behavior may change.

Mitigation:

- isolate WeChat transport,
- log transport errors clearly,
- keep the gateway channel-agnostic.

### 3. Permission UX in IM is harder than in a local terminal

Risk:

- users may miss or misunderstand approval prompts.

Mitigation:

- keep permission prompts explicit and compact,
- include session title and short action summary,
- support clear commands such as `/approve` and `/deny`.

### 4. Shared identity and audit concerns

Risk:

- multiple IM users routed through one Copilot identity may blur accountability.

Mitigation:

- define a clear identity model early,
- persist user-to-session and user-to-agent ownership,
- consider separate execution identities later if the product evolves beyond
  personal use.

### 5. Streaming-output noise

Risk:

- raw chunk streaming may spam WeChat and degrade usability.

Mitigation:

- coalesce chunks,
- rate-limit progress updates,
- send final answer separately from transient status.

### 6. Recovery complexity

Risk:

- a crash during a turn or permission request can leave the user confused.

Mitigation:

- persist turn state before sending externally visible prompts,
- reconcile unfinished records on startup,
- expose admin and user-facing recovery hints.

## MVP recommendation

The first deliverable should be intentionally small and prove the hardest core
behaviors first.

### MVP scope

- WeChat text input
- Copilot CLI ACP integration over stdio
- gateway-managed session registry
- one active session per user plus explicit `/new` and `/sessions`
- reply-based continuation when reply metadata is available
- streaming-to-summary delivery policy
- basic permission requests: approve once / deny
- `/stop` cancellation
- SQLite persistence
- structured logs

### Explicitly out of MVP

- media upload and download
- multiple agent backends
- multi-node deployment
- advanced session sharing
- approve-for-session or approve-forever policies
- MCP server injection for external tools

## Suggested roadmap

### Phase 1: single-user, single-worker proof of concept

- one WeChat account
- one Copilot CLI ACP process
- SQLite
- text-only prompts
- manual operational recovery

### Phase 2: usable personal gateway

- multiple sessions per user
- reply-to-message routing
- permission prompts
- session list and rename
- better progress formatting
- restart recovery

### Phase 3: extensible product core

- channel adapter abstraction stabilized
- agent adapter abstraction stabilized
- PostgreSQL
- queue-based workers
- richer observability
- more IM channels
- more agent backends

## Recommended internal interfaces

The code should be organized around explicit contracts such as:

### `ImChannelAdapter`

Responsibilities:

- authenticate or connect to the IM channel,
- receive normalized inbound events,
- send outbound messages,
- expose reply metadata and delivery IDs.

### `AgentBackendAdapter`

Responsibilities:

- initialize backend connections,
- create/load sessions,
- send prompts,
- cancel turns,
- handle permission loops,
- translate backend events into gateway events.

### `SessionRegistry`

Responsibilities:

- create and look up gateway sessions,
- map channel messages to sessions and turns,
- manage active session pointers,
- persist session state transitions.

### `TurnOrchestrator`

Responsibilities:

- resolve target sessions,
- start prompts,
- stream updates,
- handle completion and failure,
- enforce cancellation and permission logic.

## Main implementation guidance

If this repository proceeds to implementation, the safest first cut is:

1. build a TypeScript gateway service,
2. integrate Copilot CLI through the official ACP TypeScript SDK,
3. encapsulate WeChat behind a dedicated adapter,
4. persist all message/session/turn mappings locally,
5. start text-only,
6. treat reply routing and permission handling as first-class features from day
   one.

The biggest design mistake to avoid is building a simple "message in, text out"
bridge without a durable session model. That would fail the core requirement as
soon as multiple concurrent conversations, approvals, retries, or restarts are
introduced.

## Final recommendation

The recommended direction is to implement a **session-aware gateway core** that
uses:

- **WeChat** as the first IM adapter,
- **Copilot CLI ACP** as the first agent backend,
- **TypeScript** as the initial implementation language,
- **gateway-owned session mapping** as the system's central design principle.

In short:

> Build a real gateway, not a chat relay.

That approach best supports the current WeChat plus Copilot CLI goal and keeps
the system structurally ready for future IM channels and future agents.
