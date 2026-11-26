# Agent Execution and Recovery

Orchestrators and replacement sessions use this guidance to coordinate parallel
agent worktrees and recover cleanly from Git and GitHub carriers under
[`record-system.md`](../governance/record-system.md) and
[`docs/delivery-wave.md`](../delivery-wave.md).

- **Worktree isolation and ordering:** Run concurrent implementation workers in
  separate persistent `git worktree` checkouts (outside `tmpfs`), keeping
  conflicting edits to shared authorities ordered and keeping independent review
  and finding triage in separate roles. Remove finished worktrees with
  `git worktree remove`.
- **Explicit ownership and recovery:** Bind each worker to its Issue/PR,
  worktree, exact session ID, and scope. Before resuming or replacing an
  interrupted worker, inspect live process ownership, `git status` / branch
  state, CI checks, and open review findings rather than picking a global
  "latest" session or assuming a missing local process means an external effect
  did not occur.
- **Idempotent external safety:** A timed-out or failed command neither proves
  completion nor permits blindly repeating an external or paid effect without
  first checking destination/ledger state. Record outcomes in Issues/PRs, never
  in `docs/delivery-wave.md`.
