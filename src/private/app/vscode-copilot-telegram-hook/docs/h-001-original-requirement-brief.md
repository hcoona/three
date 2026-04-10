# H-001 Original Requirement Brief

This file preserves the original human-authored requirement brief that
previously appeared in `../README.md` before later requirement-alignment edits.

This document is a direct human-authored source input.

## Functional brief

The goal of this project is to add a hook to GitHub Copilot in Visual Studio
Code so that a Telegram notification is sent at the end of each chat turn. The
original user-provided points were:

1. The target platform is VS Code GitHub Copilot, not Copilot CLI and not
   Claude Code.
2. The hook should be installed at the user level, not at the project level.
3. Each notification should include a brief summary of the completed chat turn.
4. The `Stop` event does not mean that the entire session has ended, because a
   session can continue. In this project, the `Stop` event is used to capture
   the end of the current chat turn.

## Original non-functional brief

1. During installation, prefer copy-on-write first; if that is not available,
   fall back to hard links; if that is still not available, use ordinary copy.
2. Use PowerShell for cross-platform scripts because PowerShell is available in
   both Linux and Windows environments.
3. `gopass` may be used to satisfy the secret-storage requirement.

## User-provided references

These references were part of the original human-authored brief and are the
intended upstream sources for later research documents.

- [Agent hooks in Visual Studio Code (Preview)](https://code.visualstudio.com/docs/copilot/customization/hooks)
- [Use custom instructions in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)
- [Telegram Bot API](https://core.telegram.org/bots/api)

## Provenance notes

1. This file preserves the original brief as a source artifact.
2. Research documents should treat the references listed here as their upstream
   external source set.
3. Later human confirmations should be captured in separate H-series documents
   rather than rewriting this file.
