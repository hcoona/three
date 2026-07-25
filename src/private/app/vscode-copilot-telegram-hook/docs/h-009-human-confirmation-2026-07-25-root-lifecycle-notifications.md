# H-009 Human Confirmation — 2026-07-25 Root Lifecycle Notifications

This file preserves the notification-lifecycle correction provided in chat after
the installed hook produced Telegram messages for several reviewer subagents.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Notification boundary

1. A completion notification should be sent only when the root agent has
   finished all current work and is waiting for further user instructions.
2. Completion of a subagent, reviewer, background agent, or other tool-call
   session must not produce a Telegram completion notification.
3. A root agent request for user input through `ask_user` should produce an
   attention notification when the request is made.
4. Hook and lifecycle information should be used to enforce this boundary and
   reduce notification spam.

## Reported evidence

The user supplied three Telegram completion messages whose session identifiers
were tool-call identifiers beginning with `call_`. Local event correlation
showed that these sessions were reviewer subagents rather than root user
sessions.

## Provenance notes

1. This clarification supersedes earlier derived statements that treated every
   observed `Stop` event as a notification opportunity without distinguishing
   root and subagent sessions.
2. It does not remove root completion notifications or the existing summary
   handoff. It narrows those behaviors to root sessions and adds root
   `ask_user` attention notifications.
