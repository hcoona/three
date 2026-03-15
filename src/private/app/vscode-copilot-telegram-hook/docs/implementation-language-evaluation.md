# Implementation Language Evaluation — VS Code Copilot Telegram Hook

## Provenance

- Kind: derived implementation design research note.
- Derived from:
    - [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
    - [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)
    - [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
    - [`nonfunctional-and-constraints-research.md`](./nonfunctional-and-constraints-research.md)
    - [`vscode-hook-inputs-research.md`](./vscode-hook-inputs-research.md)
    - the official external references inherited from H-001:
        - VS Code hooks documentation,
        - VS Code custom instructions documentation, and
        - Telegram Bot API documentation
    - supplemental official VS Code customization context discovered while
      following links from the official hooks documentation.
- Purpose: record the language-selection research requested after the
  requirement review and tie it back to official platform behavior rather than
  to the current repository implementation.

This document compares **PowerShell**, **Python**, and **C#** (including native
Ahead-of-Time compilation) for implementing the supported product scope.

The product requirements themselves remain implementation-agnostic. This
document records a design recommendation only.

## Scope and evaluation boundary

This evaluation covers the supported product boundary as currently documented:

- user-level installation,
- Windows and WSL Linux support,
- hook-driven Telegram delivery at `SessionStart` and `Stop`-adjacent lifecycle
  points,
- summary handoff support,
- secure credential resolution,
- diagnostics and lifecycle tooling such as install, upgrade, uninstall, and
  health checks.

The comparison intentionally treats the current repository scripts as
non-normative. The question here is not "what language is the current code
already using?" but rather "what language is the best fit for the product we
have described?"

## Official facts confirmed during this research pass

### Hooks are the deterministic automation mechanism

The official VS Code customization documentation distinguishes hooks from
instructions and prompts in an important way:

- instructions guide model behavior,
- prompts package reusable requests, but
- hooks run custom shell commands at defined lifecycle points and provide
  deterministic, code-driven automation.

That distinction matters directly to implementation language choice. The
notification sender is not merely prompt guidance; it is executable hook logic.

### Hook execution is shell-command based

The official hooks documentation confirms that hook entries are command objects
with a `command` property and optional `windows`, `linux`, and `osx`
overrides.

This means any viable implementation language must still be exposed through a
shell-friendly entry point. Even if the core implementation is a compiled
binary, VS Code still launches it as a command.

### OS selection follows the extension host platform

The official hooks documentation states that OS-specific hook commands are
selected based on the **extension host platform**. In remote scenarios such as
WSL, SSH, or containers, that platform can differ from the user's local
desktop operating system.

This makes packaging and invocation strategy more important than language taste
alone. A solution that looks simple on a local Windows machine can still need a
Linux-targeted executable or wrapper when the extension host runs in WSL.

### Hooks already provide structured runtime input

The official hook contract provides structured JSON input through standard
input, including:

- `timestamp`,
- `cwd`,
- `sessionId`,
- `hookEventName`, and
- `transcript_path`.

`Stop` also provides `stop_hook_active`, and `SessionStart` can inject
`additionalContext` into the conversation.

This confirms that the implementation needs solid JSON parsing, state handling,
and event-specific behavior more than it needs rich UI facilities.

### Hooks and instructions solve different parts of the product

The official custom instructions documentation confirms that instructions are
Markdown guidance loaded into chat requests, while the hooks documentation
documents hook input and output only for hook scripts.

The official docs do **not** document a direct way for instructions files to
read hook standard-input fields such as `sessionId` or `timestamp`.

As a result, if the product wants Copilot-authored summaries that are later
delivered by a hook, the design still needs an explicit summary handoff or
shared correlation mechanism.

### Telegram delivery is ordinary HTTPS plus strict text-format rules

The Telegram Bot API confirms the key delivery constraints:

- requests go to `https://api.telegram.org/bot<token>/METHOD_NAME`,
- Bot API responses always contain `ok` and may contain `description`,
- `sendMessage.text` is limited to `1-4096` characters after entities parsing,
  and
- HTML formatting is supported but requires valid escaping for text that is not
  part of a supported tag.

For this project, the important consequence is that overlength notifications
must be shortened or split across multiple `sendMessage` requests while
preserving valid HTML formatting.

## What the implementation actually needs to do

When the requirements and confirmed platform constraints are combined, the
implementation language must support the following practical work well:

1. Parse structured JSON hook input and optionally emit JSON hook output.
2. Persist coordination state across multiple hook invocations.
3. Probe repository metadata on a best-effort basis.
4. Resolve secrets securely, likely through environment overrides and external
   secret-store tooling.
5. Send HTTPS requests to Telegram with limited retry.
6. Escape Telegram HTML correctly and split long notifications safely.
7. Install and update user-level hook and instructions configuration while
   preserving conflicting user configuration safely.
8. Provide diagnostics and unattended lifecycle support.
9. Operate cleanly across Windows and WSL Linux.

This is more than a single scripting task. It is effectively a small,
cross-platform command-line product.

## Evaluation criteria

The following criteria matter most for the current product scope:

1. **Deployment friction** for a user-level hook product.
2. **Cross-platform behavior** on Windows and WSL Linux.
3. **Shell integration** because hooks launch commands, not in-process code.
4. **JSON, file, and HTTP ergonomics** for the hook runtime itself.
5. **Text-processing reliability** for Telegram HTML and message chunking.
6. **Testability and maintainability** for lifecycle and failure handling.
7. **Startup cost** because hooks run on lifecycle events and have timeouts.
8. **Packaging story** for a tool that may need to run without asking the user
   to set up a full development environment.

## Option A — PowerShell

### Strengths

- It is naturally aligned with hook execution as shell commands.
- It is a strong fit for install, upgrade, uninstall, and diagnostic workflows.
- JSON, filesystem, environment variable, and process-invocation support are
  built in.
- Calling external tools such as `git` or `gopass` is straightforward.
- For the supported environments, PowerShell Core can cover both Windows and
  WSL Linux.
- The original human brief already identified PowerShell as a cross-platform
  candidate for the product target.

### Weaknesses

- The language is noticeably less comfortable than Python or C# for building a
  well-tested text-processing and retry-heavy notification engine.
- Robust HTML escaping, message chunking, and state coordination are all
  possible, but the code tends to become harder to structure cleanly.
- Quoting, encoding, and object-shape differences can become annoying at the
  Windows-versus-Linux boundary.
- The long-term maintainability ceiling is lower than for a typed compiled
  language.

### Practical fit

PowerShell is the strongest choice if the primary goal is the **fastest route to
a working, script-first product** with minimal conceptual distance from the hook
execution model.

It is not the strongest choice if the goal is a more durable, product-like
runtime that will keep growing in complexity.

### Estimated effort

Assuming one experienced engineer, official documentation availability, and no
secret-store bootstrap automation:

- MVP: about **4-6 engineer-days**
- Hardened lifecycle-capable implementation: about **7-10 engineer-days**

PowerShell is likely the fastest initial delivery path.

## Option B — Python

### Strengths

- It is very productive for JSON, HTTP, retry logic, text processing, and test
  authoring.
- Message formatting and chunking logic are usually easier to express than in
  PowerShell.
- The resulting runtime code would likely be concise and readable.

### Weaknesses

- The hook model still needs a shell entry point, so Python does not remove the
  wrapper and invocation concerns.
- A user-level product for Windows and WSL Linux needs a runtime-distribution
  story. Requiring Python to already exist on the user's machine is operational
  friction.
- Bundling Python into standalone artifacts is possible, but it is usually more
  cumbersome than shipping a native compiled executable.
- Installer and lifecycle tasks do not become simpler than in PowerShell, and
  deployment does not become cleaner than with C# AOT.

### Practical fit

Python is a technically viable middle ground, but it is the least compelling
strategic fit for this specific product shape.

Compared with PowerShell, it usually loses on deployment simplicity.
Compared with C#, it usually loses on packaging and long-term productization.

Python becomes more attractive only if the surrounding ecosystem is already
standardized on Python distribution and operations.

### Estimated effort

Assuming the same conditions as above:

- MVP: about **5-7 engineer-days**
- Hardened lifecycle-capable implementation: about **8-12 engineer-days**

The extra cost mainly comes from packaging and installation design rather than
from Telegram or hook logic itself.

## Option C — C# with native AOT

### Strengths

- It is a strong fit for a small but serious command-line product.
- JSON, HTTP, logging, retries, and structured state handling are all first-
  class tasks in the .NET ecosystem.
- A native AOT build gives a clean deployment story with low startup overhead
  and no separate runtime dependency on the target machine.
- Strong typing makes the hook input, state model, and Telegram delivery code
  easier to evolve safely.
- Testability and long-term maintainability are better than in a script-heavy
  implementation.

### Weaknesses

- Initial scaffolding and release setup are heavier than a pure script-based
  approach.
- AOT introduces trimming and compatibility considerations that need to be
  handled consciously.
- Cross-platform publishing must target the extension-host environments that the
  product supports.
- Lifecycle tooling might still benefit from thin shell wrappers even if the
  core runtime is a compiled binary.

### Practical fit

C# with native AOT is the strongest choice if the product is treated as a
**long-lived tool** rather than as a small convenience script.

It matches the actual problem shape well:

- structured inputs,
- persistent coordination state,
- careful message formatting,
- reliable retry behavior, and
- user-level lifecycle management.

### Estimated effort

Assuming one experienced engineer and the same scope assumptions:

- MVP: about **6-8 engineer-days**
- Hardened lifecycle-capable implementation: about **8-11 engineer-days**

The initial setup cost is slightly higher than PowerShell, but the hardening
cost grows more slowly.

## Comparative summary

| Option             | Best at                                                      | Main liability                                         | Relative recommendation    |
| ------------------ | ------------------------------------------------------------ | ------------------------------------------------------ | -------------------------- |
| PowerShell         | Fastest initial delivery and lifecycle scripting             | Harder long-term runtime structure and testing         | Strong short-term choice   |
| Python             | Pleasant runtime code for HTTP and text processing           | Runtime distribution story is awkward for this product | Weakest overall fit        |
| C# with native AOT | Best long-term product shape, packaging, and maintainability | Higher initial setup cost                              | Strongest strategic choice |

## Recommendation

If one language must be chosen for the product **without being constrained by
the current repository implementation**, the best overall choice is:

1. **C# with native AOT** as the primary implementation language.
2. **PowerShell** as the best alternative when optimizing for the shortest path
   to an initial supported release.
3. **Python** only if there is an external reason to standardize on Python
   packaging or reuse Python-specific operational tooling.

In short:

- choose **C# AOT** for the best long-term product fit,
- choose **PowerShell** for the fastest initial delivery,
- do **not** choose Python unless a Python-centered operating model already
  exists for reasons outside this feature itself.

## Why this recommendation follows from the official docs

The official documentation does not force a specific implementation language.
However, it does confirm the shape of the problem:

- hooks are deterministic command execution,
- instructions and hooks solve different responsibilities,
- hook runtime data is structured JSON,
- extension-host platform selection matters,
- Telegram delivery is ordinary HTTPS with strict formatting and per-message
  limits.

That combination makes the solution look less like an editor macro and more
like a small cross-platform CLI product. That is exactly the category where C#
with AOT has the strongest overall fit, while PowerShell remains the most
efficient scripting option for a shorter first release.
