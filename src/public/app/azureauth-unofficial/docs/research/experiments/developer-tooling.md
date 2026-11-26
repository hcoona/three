# Developer Tooling Installation and Discovery Evidence

This completed experiment record is retained as evidence for the locked developer
tooling that [CONTRIBUTING.md](../../../CONTRIBUTING.md) now owns. It authorizes no
execution. The tooling Wave grant it ran under was completed and removed.
[Issue #68](https://github.com/hcoona/microsoft-authentication-cli/issues/68) coordinated
the work, and the [experiment policy](../experiment-safety.md) governs any new experiment.
The full protocol, preflight procedure, attempt limits, sequential journal, and execution
history remain at the
[pinned revision](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/developer-tooling.md).
Contributor setup, update, and removal instructions live only in CONTRIBUTING.md.

## Question and Basis

The experiment asked whether the selected .NET 10 SDK, APM, Node.js, and the official
Microsoft Learn CLI could be installed reproducibly from public sources; preserve
repository instructions and existing inputs; expose the same five selected Skills to
GitHub Copilot CLI and Codex CLI; and retrieve public Microsoft documentation through the
official MCP endpoint and CLI.

Observations ran on 2026-09-12 UTC on the owner-designated WSL 2 Ubuntu x64 development
host with mise 2026.9.3, using public package sources, process-only credential
suppression, and existing public download caches. They selected .NET SDK 10.0.401; the
repository later moved its pin to 10.0.402 through a reviewed lock change, which these
observations do not cover.

## Observations

**Runtime observation - reproducible locks.** The generic `dotnet` mise alias resolved to
a backend whose lock named a mutable install script without an SDK-archive checksum. The
exact `http:dotnet-development` HTTP tool instead locked Microsoft's versioned Linux x64
and Windows x64 archives with SHA-512 values; a later lock generation reproduced the six
reviewed APM, Node.js, and SDK entries byte for byte. The resolved Learn npm graph of 98
entries declared no install script and installed with lifecycle scripts disabled.

**Runtime observation - APM deployment and replay.** APM deployed exactly the five
selected Skills and their six MSBuild support documents into the shared `.agents/skills/`
location; all eleven files matched the pinned upstream bytes. Frozen replay, including a
fresh copy without `apm_modules/`, preserved all three locks, the projected files, and
seeded unrelated MCP entries. A disposable removal check deleted only the owned
projections and named Learn entries. APM's absent organization-policy warnings were
independently triaged as a public Contents API lookup rather than private access.

**Runtime observation - client discovery.** In an isolated, network-less local discovery
envelope, Codex CLI 0.153.4 and Copilot CLI 1.0.84-4 each reported exactly one repository
entry for every selected Skill. No model prompt, authentication, or Skill workflow ran.

**Runtime observation - public Learn retrieval.** Direct MCP initialization listed the
three expected tools. Search, page fetch, and C# code-sample search each succeeded once
through MCP and once through the official CLI, with nonempty public results and no
application error. Returned samples were not executed.

## Deviations and Limitations

Two author-discovered deviations were independently confirmed and preserved: one
diagnostic sequence continued after a host-version mismatch before the mise executable
identity was bound, and one APM version diagnostic bypassed the repeated manager
preflight. A Requests netrc fallback was closed with an empty process-only `NETRC` before
later operations; earlier operations cannot claim that control retroactively. The CLI
retrievals used the ambient mise Node selection, so their runtime version is an
inference rather than a per-process receipt.

These observations do not establish an empty-cache installation, Windows runtime
discovery, Agent model selection behavior, .NET product compatibility, authentication
support, or implementation readiness.
