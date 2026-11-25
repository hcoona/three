# Retired Projects

Git retains deleted file contents ([GOV-009](governance/governance-system.md#gov-009-git-retains-history)).
This index maps retired monorepo applications, prototypes, internal delivery
components, and pre-monorepo deprecated packages to their historical source,
purpose, retirement rationale, and recommended alternatives. Routine moves of
active projects, such as relocating smoke fixtures from
`src/public/lib/hcoona-release-smoke-*` to
`tests/private/app/workflow-delivery/fixtures/products/*`, are not retirements.
When retiring a project, add its entry in the same pull request with an
immutable full-SHA link to its last intact tree on `main`.

## Quick Index

| Project                                                                                                                                                 | Ecosystem         | Category       | Retired    | Successor / Alternative                                 |
| ------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------- | -------------- | ---------- | ------------------------------------------------------- |
| [DotNetLockFileLister](#dotnetlockfilelister)                                                                                                           | .NET              | Tool           | 2026-11-25 | NuGet SDK features and Renovate                         |
| [Oxford Dictionary Extraction Suite](#oxford-dictionary-extraction-suite-oxforddictextractor-oxfordlearnersdictionaryprocessor-oxfordwordlistextractor) | .NET              | Tools          | 2026-11-25 | Lightweight `IChatClient` script, if needed             |
| [PhiFailureDetector.Console](#phifailuredetectorconsole)                                                                                                | .NET              | Companion app  | 2026-11-25 | `PhiFailureDetector` library and tests                  |
| [factorio-cycle-calculator](#factorio-cycle-calculator)                                                                                                 | Python            | Prototype      | 2026-11-25 | YAFC-CE                                                 |
| [rag-postgresql-openai](#rag-postgresql-openai)                                                                                                         | Python            | Demo           | 2026-11-25 | Managed retrieval APIs or local MCP search              |
| [llm-text-splitter](#llm-text-splitter)                                                                                                                 | Python            | Prototype      | 2026-11-25 | Structured-output prompt or PEP 723 script              |
| [transcribe](#transcribe)                                                                                                                               | Python            | Tool           | 2026-11-25 | `faster-whisper`, `whisper.cpp`, or OpenAI Audio API    |
| [git-commit-heatmap](#git-commit-heatmap)                                                                                                               | Python            | Tool           | 2026-11-25 | `onefetch`, `git-quick-stats`, or an ephemeral script   |
| [music-flash-card-generator](#music-flash-card-generator)                                                                                               | Python / LilyPond | Tool           | 2026-11-25 | Standalone LilyPond for future batches                  |
| [markdown-hybrid-search-mcp](#markdown-hybrid-search-mcp)                                                                                               | Python            | Scaffold       | 2026-11-25 | `qmd`                                                   |
| [chrome-mcp-client](#chrome-mcp-client)                                                                                                                 | Python            | Tool           | 2025-12-10 | Native MCP clients                                      |
| [summarize-edge-tab](#summarize-edge-tab)                                                                                                               | Python            | Tool           | 2025-12-10 | Browser Copilot or Playwright MCP                       |
| [windows-app-mcp](#windows-app-mcp)                                                                                                                     | Python            | Prototype      | 2025-12-10 | Domain-specific automation                              |
| [TplLab](#tpllab)                                                                                                                                       | .NET              | Lab            | 2026-11-25 | `System.Threading.Channels`                             |
| [TaskAssigner](#taskassigner)                                                                                                                           | .NET              | Lab            | 2026-11-25 | Standalone OR-Tools experiment, if needed               |
| [qidian-novel-downloader (Python PoC Lab)](#qidian-novel-downloader-python-poc-lab)                                                                     | Python            | Lab            | 2026-11-25 | Qidian C# CLI                                           |
| [azure-document-intelligence-lab](#azure-document-intelligence-lab)                                                                                     | Python            | Lab            | 2026-11-25 | `scholarly-publication`, MarkItDown, or Docling         |
| [im-acp-gateway Channel Prototypes](#im-acp-gateway-channel-prototypes-telegram-bot-verifier-telegram-topic-session-bridge-wechat-ilink-verifier)       | Node.js           | Prototypes     | 2026-11-25 | Retained IM/ACP research records                        |
| [Early Workflow Delivery v3 Python Control & Split .NET Helpers](#early-workflow-delivery-v3-python-control--split-net-helpers)                         | Python / .NET     | Infrastructure | 2026-10-12 | Unified Workflow Delivery app                           |
| [Workflow Delivery Embedded uv Rust Supplement](#workflow-delivery-embedded-uv-rust-supplement)                                                         | Rust              | Infrastructure | 2026-10-15 | Native `uv` CLI                                         |
| [Legacy V1 Official Release Workflow Stack](#legacy-v1-official-release-workflow-stack)                                                                 | GitHub Actions    | Infrastructure | 2026-10-18 | Workflow Delivery v3 release callers                    |
| [Clocks.*](#clocksabstraction-clockssystem-clockslamportclock-and-clocksintervaltreeclocks)                                                             | .NET              | NuGet packages | Legacy     | `System.TimeProvider` for physical time                 |
| [RateLimiter2](#ratelimiter2)                                                                                                                           | .NET              | NuGet package  | Legacy     | `System.Threading.RateLimiting` or `Polly.RateLimiting` |
| [QuickGraph.All](#quickgraphall)                                                                                                                        | .NET              | NuGet package  | Legacy     | FastGraph or QuikGraph                                  |

## Applications and Personal Tools

### DotNetLockFileLister

- **Last intact tree:** [`src/private/app/DotNetLockFileLister`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/private/app/DotNetLockFileLister)
- **Retired:** 2026-11-25 ([PR #1803](https://github.com/hcoona/three/pull/1803)).
- **Summary:** C# CLI that compared `packages.lock.json` with `Directory.Packages.props` to flag unused or unpinned transitive NuGet packages.
- **Why retired:** SDK features and Renovate replaced manual lockfile comparisons.
- **Recommended alternative:** Use `RestoreEnablePackagePruning`, `CentralPackageTransitivePinningEnabled`, `dotnet restore --locked-mode`, and Renovate as applicable.

<!-- markdownlint-disable MD013 -->

### Oxford Dictionary Extraction Suite (OxfordDictExtractor, OxfordLearnersDictionaryProcessor, OxfordWordlistExtractor)

<!-- markdownlint-enable MD013 -->

- **Last intact trees:**
    - [`src/private/app/OxfordDictExtractor`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/private/app/OxfordDictExtractor) and [`tests/private/app/OxfordDictExtractor.Tests`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/tests/private/app/OxfordDictExtractor.Tests)
    - [`src/private/app/OxfordLearnersDictionaryProcessor`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/private/app/OxfordLearnersDictionaryProcessor)
    - [`src/private/app/OxfordWordlistExtractor`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/private/app/OxfordWordlistExtractor)
- **Retired:** 2026-11-25 ([PR #1803](https://github.com/hcoona/three/pull/1803)).
- **Summary:** Three C# CLIs for extracting Oxford 3000/5000 wordlists and learner's dictionary HTML, with RocksDB caching and Semantic Kernel enrichment.
- **Why retired:** The one-off vocabulary workflow ended. Removing the separate `win-x64` projects also removed RocksDB, Semantic Kernel, and WebMarkupMin dependencies from the `dirs.proj` build.
- **Recommended alternative:** If revisited, use a small script with `Microsoft.Extensions.AI` (`IChatClient`) for enrichment.

### PhiFailureDetector.Console

- **Last intact tree:** [`src/public/app/PhiFailureDetector.Console`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/public/app/PhiFailureDetector.Console)
- **Retired:** 2026-11-25 ([PR #1803](https://github.com/hcoona/three/pull/1803)).
- **Summary:** Companion C# console runner and algorithm notes for `PhiFailureDetector`.
- **Why retired:** Its `TimeProvider` timestamp guidance, architecture notes, and normal-distribution scenario test were consolidated into the library and its tests.
- **Recommended alternative:** See the [PhiFailureDetector README](../src/public/lib/PhiFailureDetector/README.md) and [unit tests](../tests/public/lib/PhiFailureDetector.UnitTest/).

### factorio-cycle-calculator

- **Last intact tree:** [`src/private/app/factorio-cycle-calculator`](https://github.com/hcoona/three/tree/ec5c418fe1da762e97bc5d8a6090a31a5c672926/src/private/app/factorio-cycle-calculator)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Python, Streamlit, and Google OR-Tools prototype for steady-state Factorio oil cracking, with `data.raw` research notes on Space Age recycling loops.
- **Why retired:** The implementation stopped at the initial oil chain but kept a roughly 32,700-line generated model and the `ortools` and `streamlit` workspace dependencies. YAFC-CE addresses the same OR-Tools matrix-balancing problem.
- **Recommended alternative:** [YAFC-CE](https://github.com/shpaass/yafc-ce) for Factorio 2.0 and Space Age planning, including quality, spoilage, Fulgora scrap recycling, and Lua modpacks. Check its release notes for mod compatibility.

### rag-postgresql-openai

- **Last intact tree:** [`src/private/app/rag-postgresql-openai`](https://github.com/hcoona/three/tree/4a02e41cceb2fb94a3ac017b0d50b77a13f7594b/src/private/app/rag-postgresql-openai)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Early Python RAG demo combining PostgreSQL and `pgvector`, BM25 and vector search, query rewriting, Cohere reranking, and a single-book splitter.
- **Why retired:** Managed and lighter local retrieval options superseded the book-specific demo; `streamlit`, `cohere`, `pgvector`, and `psycopg` enlarged the root `uv.lock`.
- **Recommended alternative:** Use a managed retrieval API or a lightweight local MCP search tool such as [`qmd`](https://github.com/tobi/qmd) for Markdown.

### llm-text-splitter

- **Last intact tree:** [`src/private/app/llm-text-splitter`](https://github.com/hcoona/three/tree/8cddfb3da1c6a9dbb7d994ba1d4eccbac232ed31/src/private/app/llm-text-splitter)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Python prototype using `openai-agents` and Streamlit to segment dialogues and documents by topic and have the model rate its own output.
- **Why retired:** The early prompt experiment had no ongoing use and kept Streamlit in the workspace.
- **Recommended alternative:** Use a direct structured-output prompt or a standalone PEP 723 script when topic segmentation is needed.

### transcribe

- **Last intact tree:** [`src/private/app/transcribe`](https://github.com/hcoona/three/tree/5f72a178e7a1ec309f4bd0f59c3db5a1cdcb143d/src/private/app/transcribe)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Approximately 100 lines of Python for chunking and transcribing audio with OpenAI speech-to-text models.
- **Why retired:** The CLI was unused and depended on unmaintained `pydub` and the `audioop-lts` compatibility shim for Python 3.13 and later.
- **Recommended alternative:** Use `faster-whisper` or `whisper.cpp` locally, or segment audio with `ffmpeg` before sending it to the OpenAI Audio API.

### git-commit-heatmap

- **Last intact tree:** [`src/private/app/git-commit-heatmap`](https://github.com/hcoona/three/tree/5a84bebd05407e1859fe76f400dcb4f4cbcd002e/src/private/app/git-commit-heatmap)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** 99-line Python script that plotted Git commit heatmaps with `gitpython`, `pandas`, `matplotlib`, and `seaborn`.
- **Why retired:** Keeping a one-off plot in the permanent workspace pulled the scientific Python stack into root `uv.lock`.
- **Recommended alternative:** Use `onefetch` or `git-quick-stats` for repository summaries, or an ephemeral `uv run --with` or PEP 723 script for a custom plot.

### music-flash-card-generator

- **Last intact tree:** [`src/private/app/music-flash-card-generator`](https://github.com/hcoona/three/tree/d4c3df2a301e617af294cab904013a38cae9a8d5/src/private/app/music-flash-card-generator)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Python generator for LilyPond and Ninja builds that produced sheet-music flashcards and MIDI or audio using the bundled `SalC5Light2.sf2` soundfont.
- **Why retired:** The personal flashcard batch was complete; the tool and LFS asset no longer needed to stay in the working tree.
- **Recommended alternative:** No in-repository successor; use a standalone LilyPond build for future batches.

### markdown-hybrid-search-mcp

- **Last intact tree:** [`src/public/app/markdown-hybrid-search-mcp`](https://github.com/hcoona/three/tree/8cddfb3da1c6a9dbb7d994ba1d4eccbac232ed31/src/public/app/markdown-hybrid-search-mcp)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Five-line Python FastMCP server stub and architecture and clarification drafts for ephemeral local Markdown vector and full-text search.
- **Why retired:** Community tools already addressed the scenario before the scaffold was implemented or published.
- **Recommended alternative:** Use a local Markdown hybrid-search MCP server such as [`qmd`](https://github.com/tobi/qmd).

### chrome-mcp-client

- **Last intact tree:** [`OnePython/packages/chrome-mcp-client`](https://github.com/hcoona/three/tree/16fedfc9e404363b8f50f02e11ba5d509abf0ca3/OnePython/packages/chrome-mcp-client)
- **Retired:** 2025-12-10 ([removal commit](https://github.com/hcoona/three/commit/4a8df85c8dc9765889f7ed7c4e0a96bb8e3f7a6a)).
- **Summary:** Thin Python FastMCP and Typer client wrapper for `chrome-mcp-server`.
- **Why retired:** Native MCP integration made the wrapper unnecessary during the `OnePython` cleanup.
- **Recommended alternative:** Use the native MCP client in Copilot CLI or VS Code with a browser server such as Playwright MCP.

### summarize-edge-tab

- **Last intact tree:** [`OnePython/packages/summarize-edge-tab`](https://github.com/hcoona/three/tree/16fedfc9e404363b8f50f02e11ba5d509abf0ca3/OnePython/packages/summarize-edge-tab)
- **Retired:** 2025-12-10 ([removal commit](https://github.com/hcoona/three/commit/4a8df85c8dc9765889f7ed7c4e0a96bb8e3f7a6a)).
- **Summary:** Python Playwright and OpenAI Agents utility that summarized the page in an already-open Microsoft Edge tab.
- **Why retired:** Browser Copilot and Playwright MCP covered the live-tab use case.
- **Recommended alternative:** Use Copilot in Edge for summarization or Playwright MCP for live-tab workflows.

### windows-app-mcp

- **Last intact tree:** [`OnePython/packages/windows-app-mcp`](https://github.com/hcoona/three/tree/16fedfc9e404363b8f50f02e11ba5d509abf0ca3/OnePython/packages/windows-app-mcp)
- **Retired:** 2025-12-10 ([removal commit](https://github.com/hcoona/three/commit/4a8df85c8dc9765889f7ed7c4e0a96bb8e3f7a6a)).
- **Summary:** Early Python FastMCP and `uiautomation` concept for AutoHotkey-style Windows window and input control by LLMs.
- **Why retired:** Domain-specific automation was preferred over an unbounded OS-wide control surface.
- **Recommended alternative:** Use a domain-specific tool, such as [`supermemo-mcp`](../src/private/app/supermemo-mcp/README.md) for SuperMemo.

## Labs and Feasibility Prototypes

### TplLab

- **Last intact tree:** [`src/lab/TplLab`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/lab/TplLab)
- **Retired:** 2026-11-25 ([PR #1803](https://github.com/hcoona/three/pull/1803)).
- **Summary:** 80-line C# producer-consumer sample using `System.Threading.Tasks.Dataflow` and `BufferBlock<byte[]>`.
- **Why retired:** The .NET base libraries now provide an asynchronous producer-consumer primitive suited to this sample.
- **Recommended alternative:** Use `Channel<T>` from `System.Threading.Channels`.

### TaskAssigner

- **Last intact tree:** [`src/lab/TaskAssigner`](https://github.com/hcoona/three/tree/6792ea3639562f5dc4a56b75980cf3f65250cd9a/src/lab/TaskAssigner)
- **Retired:** 2026-11-25 ([PR #1803](https://github.com/hcoona/three/pull/1803)).
- **Summary:** Hardcoded C# CPU and memory task bin-packing experiment using `Google.OrTools` and its CBC mixed-integer solver.
- **Why retired:** The one-off experiment had no consumers but pulled large native OR-Tools binaries into `dirs.proj` restores and builds.
- **Recommended alternative:** If needed again, run a standalone OR-Tools experiment outside the shared build.

### qidian-novel-downloader (Python PoC Lab)

- **Last intact tree:** [`src/lab/qidian-novel-downloader`](https://github.com/hcoona/three/tree/a1f4f7266f153525212e637bed6f12272623a679/src/lab/qidian-novel-downloader)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Three Python Playwright and MCP prototypes investigating Qidian rendering, Chromium `SingletonLock` profile isolation, and headed-login fallback.
- **Why retired:** The application graduated into a C# CLI; relevant findings were preserved in its technical notes.
- **Recommended alternative:** Use the [Qidian downloader CLI](../src/private/app/qidian-novel-downloader/README.md) and its [technical notes](../src/private/app/qidian-novel-downloader/docs/technical-notes.md).

### azure-document-intelligence-lab

- **Last intact tree:** [`src/lab/azure-document-intelligence-lab`](https://github.com/hcoona/three/tree/fedc50bca789d23b00ad0b34e5dfa901a0e3841a/src/lab/azure-document-intelligence-lab)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** A single Jupyter notebook (`main.ipynb`) that converted PDFs to Markdown and extracted images with Azure AI Document Intelligence.
- **Why retired:** The notebook experiment was superseded; its retirement removes `ipykernel`, `azure-ai-documentintelligence`, and `pypdfium2` from root `uv.lock`.
- **Recommended alternative:** Use `scholarly-pdf-reconstruction` in [scholarly-publication](../src/private/lib/scholarly-publication/README.md) for born-digital scholarly PDFs, or an external CLI such as MarkItDown or Docling.

<!-- markdownlint-disable MD013 -->

### im-acp-gateway Channel Prototypes (telegram-bot-verifier, telegram-topic-session-bridge, wechat-ilink-verifier)

<!-- markdownlint-enable MD013 -->

- **Last intact tree:** [`src/private/app/im-acp-gateway/poc`](https://github.com/hcoona/three/tree/61336a7af93d1eb3b907fb3ce8b700cdb0a14dad/src/private/app/im-acp-gateway/poc)
- **Retired:** 2026-11-25 ([#1832](https://github.com/hcoona/three/issues/1832)).
- **Summary:** Three Node.js and TypeScript feasibility prototypes for WeChat iLink login and messaging and for bridging Telegram forum topics to Agent Client Protocol (ACP) sessions.
- **Why retired:** Verification findings were recorded, so the prototypes no longer needed to stay in the active PNPM workspace.
- **Recommended alternative:** See the [retained IM/ACP gateway research records](../src/private/app/im-acp-gateway/docs/README.md) for findings and limits. They do not imply a supported replacement.

## Superseded Repository Infrastructure

### Early Workflow Delivery v3 Python Control & Split .NET Helpers

- **Last intact trees:**
    - [`src/public/lib/three-workflow-delivery-v3`](https://github.com/hcoona/three/tree/c33517b80d6c7f5c76f7dd3bb727e20dbfb2b81b/src/public/lib/three-workflow-delivery-v3)
    - [`src/private/app/workflow-delivery-v3-dotnet-provider`](https://github.com/hcoona/three/tree/c33517b80d6c7f5c76f7dd3bb727e20dbfb2b81b/src/private/app/workflow-delivery-v3-dotnet-provider)
    - [`src/private/app/workflow-delivery-v3-nuget-authority`](https://github.com/hcoona/three/tree/c33517b80d6c7f5c76f7dd3bb727e20dbfb2b81b/src/private/app/workflow-delivery-v3-nuget-authority)
    - [`src/private/app/workflow-delivery-v3-nuget-consumer`](https://github.com/hcoona/three/tree/c33517b80d6c7f5c76f7dd3bb727e20dbfb2b81b/src/private/app/workflow-delivery-v3-nuget-consumer)
- **Retired:** 2026-10-12 ([PR #1152](https://github.com/hcoona/three/pull/1152)).
- **Summary:** Earlier CI and release control split across a Python package and separate C# executables.
- **Why retired:** Workflow Delivery v3 consolidated control into one C# application.
- **Recommended alternative:** See the unified [Workflow Delivery control app](../src/private/app/workflow-delivery/README.md).

### Workflow Delivery Embedded uv Rust Supplement

- **Last intact tree:** [`src/private/app/workflow-delivery/Native/Python/uv`](https://github.com/hcoona/three/tree/7380dfc4d3ca024fd104d3de9a1066d62bd3696a/src/private/app/workflow-delivery/Native/Python/uv)
- **Retired:** 2026-10-15 ([PR #1187](https://github.com/hcoona/three/pull/1187)).
- **Summary:** Custom Rust crate that parsed `uv` workspace state for Python CI planning.
- **Why retired:** Workflow Delivery moved to official `uv` metadata commands rather than maintaining a parallel parser.
- **Recommended alternative:** Use the native `uv` CLI through the [Workflow Delivery Python integration](../src/private/app/workflow-delivery/docs/integration/python.md).

### Legacy V1 Official Release Workflow Stack

- **Last intact tree:** [`.github/workflows`](https://github.com/hcoona/three/tree/157e8e57044195db45a6ec1b53edb60d9d1c17ec/.github/workflows), the `release-*.yml` family coordinated by [`release-orchestrate.yml`](https://github.com/hcoona/three/blob/157e8e57044195db45a6ec1b53edb60d9d1c17ec/.github/workflows/release-orchestrate.yml).
- **Retired:** 2026-10-18 ([PR #1283](https://github.com/hcoona/three/pull/1283)).
- **Summary:** First-generation GitHub Actions release orchestration.
- **Why retired:** Workflow Delivery v3 superseded the legacy release stack.
- **Recommended alternative:** See the v3 ecosystem release callers (`.github/workflows/workflow-delivery-v3-*-release.yml`) routed from the [Workflow Delivery README](../src/private/app/workflow-delivery/README.md).

## Pre-Monorepo Deprecated Public NuGet Packages

These packages predate this repository. Their upstream repositories, rather than
monorepo Git trees, are the source lookup points.

### Clocks.Abstraction, Clocks.System, Clocks.LamportClock, and Clocks.IntervalTreeClocks

- **Upstream:** [hcoona/Clocks.Net](https://github.com/hcoona/Clocks.Net). NuGet.org marks version `1.4.0` of
  [Clocks.Abstraction](https://www.nuget.org/packages/Clocks.Abstraction/1.4.0),
  [Clocks.System](https://www.nuget.org/packages/Clocks.System/1.4.0),
  [Clocks.LamportClock](https://www.nuget.org/packages/Clocks.LamportClock/1.4.0),
  and [Clocks.IntervalTreeClocks](https://www.nuget.org/packages/Clocks.IntervalTreeClocks/1.4.0) as Legacy.
- **Summary:** Physical and logical or causal clock abstractions for .NET.
- **Why retired:** .NET 8 and later include testable physical time in the base libraries.
- **Recommended alternative:** Use `System.TimeProvider` for physical time, `Microsoft.Bcl.TimeProvider` where a polyfill is needed, and `Microsoft.Extensions.TimeProvider.Testing` for tests. These do not replace logical or causal clocks.

### RateLimiter2

- **Upstream:** [hcoona/RateLimiter](https://github.com/hcoona/RateLimiter); [version `1.2.0`](https://www.nuget.org/packages/RateLimiter2/1.2.0) is marked Legacy on NuGet.org.
- **Summary:** Token-bucket rate limiter for .NET.
- **Why retired:** .NET 7 and later ship rate-limiting primitives, including a token-bucket implementation.
- **Recommended alternative:** Use `System.Threading.RateLimiting`, or `Polly.RateLimiting` when composing resilience policies with `Microsoft.Extensions.Resilience`.

### QuickGraph.All

- **Upstream:** [hcoona/quickgraph](https://github.com/hcoona/quickgraph); [version `3.6.61119.8`](https://www.nuget.org/packages/QuickGraph.All/3.6.61119.8) is marked Legacy on NuGet.org.
- **Summary:** Repackaged build of the QuickGraph generic graph algorithms library.
- **Why retired:** Other forks took over maintenance of the graph data structures and algorithms.
- **Recommended alternative:** Evaluate a fork such as FastGraph or QuikGraph for the target framework and algorithms needed.
