# H-007 Human Confirmation — 2026-03-14 Same-Host VS Code Settings Targets

This file preserves a later human clarification recorded in chat on 2026-03-14
about how the managed user-level installer should interpret VS Code settings
targets on a host that may be used both for local desktop development and as a
VS Code Server backend.

Because the original clarification happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Confirmed interpretation

1. In this project context, the host-local VS Code user settings target refers
   to the settings file used when VS Code runs locally on that same machine.
2. On a Linux host, that host-local desktop target is represented by
   `~/.config/Code/User/settings.json`.
3. On the same host, the VS Code Server remote-development target is
   represented by `~/.vscode-server/data/Machine/settings.json`.
4. These two settings targets should not be modeled as an attempt to install on
   both a remote machine and a separate client machine that initiated the
   connection.
5. A single machine may legitimately be used in both ways, so both settings
   targets can matter for one managed installation on that host.
6. Whether both settings files participate in one VS Code process or one active
   session is a separate platform-behavior question; for managed installation,
   they should still be treated as distinct same-host runtime entry points.

## Installation guidance interpretation

1. Managed user-level installation may register the dedicated managed hook JSON
   file in both same-host settings targets by default.
2. The installer is not expected to solve installation on some other terminal
   machine that merely initiated a remote connection.
3. A single managed hook file may remain the shared hook definition while
   multiple same-host VS Code settings files register that file for their
   respective runtime modes.
4. Any such registration must still respect the documented
   `chat.hookFilesLocations` path-format constraints.

## Relationship to earlier documents

1. This file refines installation guidance after H-006.
2. H-006 remains the source for the measured conclusion that
   `~/.claude/settings.json` is not a reliable steady-state managed install
   target without `"chat.useClaudeHooks": true`.
3. This file adds the later clarification that the managed installer's relevant
   non-`.claude` settings targets are same-host entry points rather than a
   cross-machine client-versus-server installation story.

## Provenance notes

1. This file records a later human clarification after H-006.
2. Later derived specifications and repository overviews may use this file to
   correct descriptions that accidentally treated host-local desktop settings
   and VS Code Server Machine settings as belonging to different machines.
3. This file does not claim that the two settings files are both consumed by one
   specific VS Code process; it records the confirmed product interpretation for
   managed installation targeting.
