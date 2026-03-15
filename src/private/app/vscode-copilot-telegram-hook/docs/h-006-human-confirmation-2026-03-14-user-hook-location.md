# H-006 Human Confirmation — 2026-03-14 User-Level Hook Location Behavior

This file preserves a later human confirmation recorded in chat on 2026-03-14
after manual VS Code verification of user-level hook loading behavior.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Verified VS Code behavior

1. In the currently observed VS Code environment, putting user-level hook
   configuration in `~/.claude/settings.json` does not make the hook run for VS
   Code GitHub Copilot when `chat.useClaudeHooks` is not enabled.
2. This remains true when `chat.hookFilesLocations` relies on its documented
   default value where `~/.claude/settings.json` is enabled.
3. This also remains true when `chat.hookFilesLocations` explicitly sets
   `"~/.claude/settings.json": true`.
4. In the same observed environment, when `"chat.useClaudeHooks": true` is
   configured, the hooks defined in `~/.claude/settings.json` do take effect.
5. This conclusion is based on manual verification in VS Code rather than on
   inference from the current repository implementation or external
   documentation alone.

## Interpretation of the observed condition

1. For the currently observed VS Code environment, the effectiveness of
   `~/.claude/settings.json` is gated by `chat.useClaudeHooks` rather than by
   `chat.hookFilesLocations` alone.
2. Enabling `chat.hookFilesLocations` for `~/.claude/settings.json` is not, by
   itself, sufficient to make that file act as an effective user-level hook
   source for the supported VS Code Copilot scenario.
3. Later derived documents should distinguish the documented default path from
   the manually verified condition under which that path actually works in the
   observed environment.

## Installation guidance interpretation

1. The managed user-level installation should avoid relying on
   `~/.claude/settings.json` plus `chat.useClaudeHooks` as its default
   supported installation target for VS Code.
2. The preferred direction is to use a different dedicated user-level hook JSON
   file and to register that file explicitly through VS Code hook-location
   settings when required.
3. Even though `~/.claude/settings.json` can work when `chat.useClaudeHooks` is
   enabled, managed installation should prefer the explicit dedicated hook JSON
   path rather than depending on Claude-compatibility behavior.
4. Until that installation change is implemented and verified, repository
   documentation should treat the current `.claude`-based install target as a
   known implementation gap rather than as the preferred steady-state design.

## Provenance notes

1. This file records a later human confirmation after H-005.
2. Later derived specifications and repository overviews may use this file to
   refine or supersede earlier statements that treated the documented
   `~/.claude/settings.json` location as an unconditional effective VS Code
   default.
3. This file does not change what the official VS Code documentation currently
   says; it records the human-confirmed project decision to prioritize measured
   VS Code behavior for installation guidance.
