# Phase 1R Git Discovery Re-scope Decision

Status: **Accepted MVP re-scope**

Date: **2026-06-05**

Decision ID: **phase-1r-git-discovery-rescope**

Gate name: **Phase 1R re-scope for Phase 1.5 Git GUI and PATH discovery**

Owner: **PL with ADAPTER-GIT and PLATFORM**

## Decision Summary

| Field                      | Decision                                                                                                                                                                                                                                                                                                       |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Evidence links             | `phase-1.5-git-discovery-evidence.md` accepted only the local Linux shell helper-discovery subset. It blocked Git for Windows and real GUI-launched Git support because neither environment was available for evidence.                                                                                        |
| User decision              | Human-in-the-loop decision: narrow the MVP and temporarily do not support Git for Windows or GUI-launched Git modes.                                                                                                                                                                                           |
| Decision                   | Re-scope the MVP Git installation support to local shell Git helper discovery only. Do not claim MVP support for Git for Windows, Windows GUI clients, GUI-launched Git clients, or PATH-only helper discovery in GUI environments.                                                                            |
| Scope change               | The product keeps Windows-first and GUI-client support as long-term goals, but those modes are deferred beyond MVP until evidence closes. Local and shell-launched helper discovery remains accepted for MVP planning, subject to later Phase 9 adapter protocol validation and Phase 15 end-to-end hardening. |
| Implementation may proceed | Yes for Phase 2 and later work that treats Git Windows and GUI support as deferred non-MVP scope. No implementation or documentation may state that MVP supports Git for Windows or GUI-launched Git until the acceptance conditions in this record are met.                                                   |

## MVP Support Statement

For MVP, Git support is limited to Azure Repos HTTPS credential-helper behavior
where Git can discover the installed `git-credential-<helper-name>` entry point
from a local shell or from an explicitly configured helper path validated by Git
itself. The MVP may rely on the accepted Phase 1.5 local Linux shell evidence for
helper shorthand discovery, missing-helper detection, `useHttpPath`, and absolute
helper behavior under a stripped shell-like environment.

The MVP does not support or advertise the following Git installation modes:

1. Git for Windows helper discovery.
2. Windows `.exe` or `.cmd` helper suffix behavior.
3. Windows paths with spaces for Git helper configuration.
4. Visual Studio, VS Code, Git GUI, or other GUI-launched Git clients.
5. PATH-only helper discovery for GUI-launched Git clients.

This is an MVP support boundary, not a product-goal removal. Windows remains a
first-class long-term platform goal in `requirements.md`,
`high-level-design.md`, `mid-level-design.md`, and `phase-0-decisions.md`.

## Affected Requirements and Designs

| Source                                | Impact                                                                                                                                                                                                                                       |
| ------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `phase-0-decisions.md`                | The Windows-first target-platform baseline remains valid as a long-term and release-readiness goal. For this MVP re-scope only, Git for Windows and GUI-launched Git are deferred evidence items rather than MVP-supported modes.            |
| `phase-1.5-git-discovery-evidence.md` | The local shell subset remains accepted. The blocked Windows and GUI evidence is resolved for MVP by narrowing supported installation modes instead of claiming the full Phase 1.5 gate passed.                                              |
| `project-breakdown.md`                | The Phase 1.5 mandatory failure path enters Phase 1R and uses the allowed outcome that narrows supported installation modes. Phase 9 and Phase 15 must not require MVP support for deferred Windows or GUI modes unless this record changes. |
| `requirements.md`                     | Functional Git helper requirements remain in scope for the accepted shell modes. Non-functional Windows-first requirements are deferred beyond MVP for Git helper discovery until evidence is produced.                                      |
| `high-level-design.md`                | The recommendation to avoid shell snippets and to validate discovery through Git remains. The installer and doctor design must present Git for Windows and GUI-client support as unsupported for MVP.                                        |
| `mid-level-design.md`                 | Git adapter and doctor design may proceed for local shell discovery. Cross-platform design items for Git for Windows helper discovery, `.exe`, `.cmd`, paths with spaces, and GUI PATH differences remain required follow-up prototypes.     |

## Dependency Impacts

- Phase 2 contract freeze may proceed if contracts distinguish supported,
  unsupported, skipped, and deferred doctor results.
- Phase 5B installer and discovery scaffolding may proceed for fake artifacts,
  but it must not lock a Git for Windows or GUI-client installation mode for MVP.
- Phase 8 vertical-slice work may exercise only local shell Git discovery.
- Phase 9 Git adapter implementation may proceed for protocol parsing,
  stdout discipline, `get`, `store`, `erase`, `useHttpPath`, and local shell
  discovery. Its MVP exit criteria must not require Git for Windows or GUI
  acceptance.
- Phase 14.2 configure/unconfigure orchestration may configure only supported
  MVP Git modes by default and must emit clear unsupported-mode messaging for
  deferred modes.
- Phase 15 hardening must not report MVP acceptance for deferred modes. If Phase
  15 reintroduces Git for Windows or GUI support, it must satisfy the acceptance
  conditions below first.

## Non-MVP Follow-ups

| Owner                    | Follow-up                                                                                                                                                                     | Dependency effect                                                                    |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| PLATFORM                 | Run Git for Windows helper discovery with helper shorthand, absolute helper paths, `.exe`, `.cmd`, path-with-spaces installation paths, and system/global scope interactions. | Required before Git for Windows can become supported.                                |
| PLATFORM and ADAPTER-GIT | Run at least one real GUI-launched Git scenario, such as Visual Studio, VS Code, Git GUI, or another approved desktop client, using fake credentials only.                    | Required before GUI-launched Git support can be claimed.                             |
| ADAPTER-GIT              | Add Git doctor checks that classify deferred Windows or GUI scenarios as unsupported for MVP without leaking credential fields.                                               | Required for accurate MVP diagnostics and later support expansion.                   |
| CONFIG                   | Ensure Git configure and unconfigure plans avoid PATH-only GUI assumptions and record ownership metadata for helper and `useHttpPath` writes.                                 | Required before user-facing configure/unconfigure commands enable MVP Git setup.     |
| PL and QA                | Update release acceptance checklists so MVP Git support excludes Git for Windows and GUI-launched modes unless this record is superseded.                                     | Prevents accidental release claims beyond accepted evidence.                         |
| PL                       | Open a later support-expansion decision when the missing platform evidence is available.                                                                                      | Required to supersede this MVP re-scope and reintroduce the deferred support claims. |

## Acceptance Conditions for Reintroducing Deferred Support

A later decision may reintroduce Git for Windows or GUI-launched Git support only
when all applicable conditions are satisfied and linked from a superseding record:

1. Git for Windows evidence covers helper shorthand, absolute helper paths,
   `.exe`, `.cmd`, path-with-spaces installation locations, helper ordering, and
   interaction with existing `manager` or `manager-core` helpers.
2. At least one real GUI-launched Git client proves the selected installation mode
   is visible to the Git process launched by that GUI.
3. The selected installation mode avoids PATH-only assumptions for GUI clients, or
   doctor reliably detects and explains PATH-only failures.
4. `credential.https://dev.azure.com.useHttpPath=true` behavior is validated for
   Azure Repos remotes on the reintroduced platform or client mode.
5. Configure and unconfigure flows record ownership metadata and avoid removing
   unrelated user Git configuration or Git Credential Manager settings.
6. Doctor checks use Git itself for discovery validation, report unsupported or
   misconfigured modes clearly, and do not print credentials to protocol stdout,
   logs, or diagnostics.
7. Phase 9 or equivalent adapter acceptance validates `get`, `store`, `erase`,
   protocol stdout discipline, and safe error handling in the reintroduced modes.
8. Phase 15 or equivalent end-to-end hardening passes on the target Windows and
   GUI matrix before release notes claim support.

## Residual Risks

- Users may expect Windows support because Windows remains the long-term primary
  platform; MVP documentation and doctor output must state the narrower Git scope
  explicitly.
- Local shell discovery may not predict Windows quoting, suffix, or GUI PATH
  behavior.
- Existing Git Credential Manager configuration may affect helper ordering and
  cleanup once Windows support is revisited.
- Deferring GUI validation may delay support for developers who primarily access
  Azure Repos through Visual Studio or other desktop clients.
