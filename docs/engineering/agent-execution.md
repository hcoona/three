# Agent Execution and Recovery

The repository owner maintains this execution guidance. Orchestrators and
replacement sessions use it to recover work without relying on one conversation
or a repository progress ledger. The [record policy](../governance/record-system.md)
and accepted [Delivery Wave](../delivery-wave.md) determine authorization.

An existing Codex session may coordinate bounded persisted `codex exec --json`
workers in separate worktrees when the accepted grant permits delegation.
Independent workers may proceed concurrently; dependent work and conflicting
writes to shared authorities remain ordered. Independent review and finding
triage retain separate roles. No standalone coordination service is required.

Associate each session with its Issue/PR, worktree, exact thread ID, owned scope
and role. Keep machine-specific process handles and any disposable lookup local.
Before resuming or replacing a worker, inspect its actual runtime ownership,
worktree/index/untracked state, target branch, checks and unresolved findings.
A missing local process or unloaded app-server thread does not prove inactivity
on another process or machine. Resolve ambiguous ownership before another writer
starts, and preserve partial work before recovery.

Use the exact known session ID when resuming. Do not select a global latest
session by convenience. Persist actual events, outputs and validated patches;
replacement sessions recover from Git and work carriers. A failed or stopped
worker neither establishes completion nor permits an unverified external effect
to be repeated. Never put session IDs, retries or results into the Wave.

## Mutable Interface Recheck

Before an implementation or execution grant relies on Codex CLI, app-server or
SDK interfaces, its author rechecks the applicable official page and installed
version; an independent reviewer evaluates that evidence. Every merged Wave
change is the fallback review event. If an interface changed, update the affected
invocation and validation before relying on it; uncertainty pauses only dependent
work. Record the retrieval, exact version and conclusion in the governing PR.

- [Non-interactive CLI](https://learn.chatgpt.com/docs/non-interactive-mode):
  JSONL events, persistence and explicit session resume.
- [App server](https://learn.chatgpt.com/docs/app-server): thread enumeration,
  source filters, pagination, read/resume and loaded runtime state.
- [SDK](https://learn.chatgpt.com/docs/codex-sdk): explicit thread continuation.

Documentation establishes an interface contract, not tested local integration
or comprehensive process discovery. Use the existing CLI unless an accepted
need justifies another integration.
