# Documentation

This portal routes readers to repository and project concerns. It is navigation;
the linked records own their stated concerns. Project records stay with their
project, and a README is sufficient when it serves the actual reader.

## Repository Governance

| Reader need                                            | Record                                                                                                                                                   |
| ------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Creation, amendment and retirement of rules            | [Governance system](governance/governance-system.md)                                                                                                     |
| Authority, admission, lifecycle and project namespaces | [Repository record system](governance/record-system.md)                                                                                                  |
| Current bounded work authorization                     | [Delivery Wave](delivery-wave.md)                                                                                                                        |
| Canonical record routing                               | [Family catalog](governance/record-families.yaml)                                                                                                        |
| Policy-backed checks and reviews                       | [Control catalog](governance/controls.yaml)                                                                                                              |
| Validation inputs, reference checks and coverage       | [Checker contract](governance/checker-contract.md)                                                                                                       |
| Catalog machine contracts                              | [Family schema](../schemas/governance/record-families.schema.json), [control schema](../schemas/governance/controls.schema.json)                         |
| Human contribution and review                          | [Contributing](../CONTRIBUTING.md), [PR template](../.github/pull_request_template.md)                                                                   |
| Agent routing                                          | [Repository instructions](../AGENTS.md), [documentation instructions](AGENTS.md)                                                                         |
| Independent review procedures                          | [Record-system review](../.github/skills/record-system-review/SKILL.md), [research-evidence review](../.github/skills/research-evidence-review/SKILL.md) |
| Source attribution                                     | [Retained copyright and license](governance/reference-license.txt), [repository provenance](../README.md)                                                |

The review procedures link to their evaluation cases. Git retains former
policies and deleted records; Issues and PRs retain work discussion and results.
Neither this portal nor an old plan adds work to the accepted Wave.

## Shared Engineering

- [Workspace and toolchain setup](engineering/workspaces.md).
- [.NET compatibility rationale](engineering/dotnet-compatibility.md), including
  generator/analyzer and retained legacy-format exceptions.
- [HK execution](engineering/hk-execution.md), including the limits of retained
  Windows evidence.
- [Bounded agent execution](engineering/agent-execution.md), including recovery
  and mutable-interface rechecks.

## Libraries and Companion Interfaces

| Project concern                           | Reader entry                                                                                                                                                                                                                                                                                                          |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NBGV Python wrapper and Hatch integration | [Library](../src/public/lib/nbgv-python/README.md), [architecture](../src/public/lib/nbgv-python/docs/architecture/overview.md), [separate sample](../src/sample/nbgv-hatch-demo/README.md)                                                                                                                           |
| Workflow Delivery v3                      | [Domain records](../src/public/lib/three-workflow-delivery-v3/docs/README.md), [library interface](../src/public/lib/three-workflow-delivery-v3/README.md), [NuGet helper routing](../src/private/app/workflow-delivery-v3-nuget-authority/README.md)                                                                 |
| Separate npm smoke product                | [Frozen package interface](../src/public/lib/hcoona-release-smoke-npm/README.md)                                                                                                                                                                                                                                      |
| Asciidoctor LaTeX rendering               | [Package](../src/public/lib/asciidoctor-latexmath/README.md), [contracts, research and evidence](../src/public/lib/asciidoctor-latexmath/docs/README.md)                                                                                                                                                              |
| Hexo AsciiDoc rendering                   | [Contributor interface](../src/public/lib/hexo-renderer-asciidoc/README.md), [package interface](../src/public/lib/hexo-renderer-asciidoc/README.npm.md), [change history](../src/public/lib/hexo-renderer-asciidoc/CHANGELOG.md), [example](../src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md)   |
| Steam account history export              | [Developer interface](../src/public/lib/steam-account-history-to-csv/README.md), [user interface](../src/public/lib/steam-account-history-to-csv/README.user.md), [privacy](../src/public/lib/steam-account-history-to-csv/PRIVACY.md), [change history](../src/public/lib/steam-account-history-to-csv/CHANGELOG.md) |
| Circular list                             | [Package](../src/public/lib/CircularList/README.md), [release notes](../src/public/lib/CircularList/ReleaseNotes.md)                                                                                                                                                                                                  |
| PNGCS                                     | [Package](../src/public/lib/Hjg.Pngcs/README.md), [change history](../src/public/lib/Hjg.Pngcs/ChangeLog.md)                                                                                                                                                                                                          |
| Memoization                               | [Package](../src/public/lib/Memoization/README.md), [release notes](../src/public/lib/Memoization/ReleaseNotes.md); the generator remains a build component, without separate product records                                                                                                                         |
| Phi failure detector                      | [Library](../src/public/lib/PhiFailureDetector/README.md), [release notes](../src/public/lib/PhiFailureDetector/ReleaseNotes.md), [console-source reader interface](../src/public/app/PhiFailureDetector.Console/README.md)                                                                                           |
| MSTest logging                            | [Package](../src/public/lib/MicrosoftExtensions.Logging.MSTest/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Logging.MSTest/ReleaseNotes.md)                                                                                                                                                      |
| xUnit logging                             | [Package](../src/public/lib/MicrosoftExtensions.Logging.Xunit/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Logging.Xunit/ReleaseNotes.md)                                                                                                                                                        |
| Options change deduplication              | [Package](../src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/ReleaseNotes.md)                                                                                                                        |
| WebHDFS file provider                     | [Package](../src/public/lib/WebHdfs.Extensions.FileProviders/README.md), [release notes](../src/public/lib/WebHdfs.Extensions.FileProviders/ReleaseNotes.md)                                                                                                                                                          |

The v3 [legacy boundary](../src/public/lib/three-workflow-delivery-v3/docs/research/legacy-workflow-boundary.md)
routes retained diagnostic interfaces at their existing workflow paths. Those
interfaces do not restore earlier workflow authority.

## Applications

| Project concern                     | Reader entry                                                                                                                                                                                                                                |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AzureAuth credential provider       | [Product records and acceptance](../src/private/app/azureauth-credprovider/docs/project-breakdown.md); C# and Python share this product's authorities                                                                                       |
| Cloudflare dynamic DNS              | [Requirements](../src/private/app/cf-ddns-updater/docs/requirements.md), [high-level design](../src/private/app/cf-ddns-updater/docs/high-level-design.md), [low-level design](../src/private/app/cf-ddns-updater/docs/low-level-design.md) |
| Document translation                | [Capability records](../src/private/app/document-translator-cli/docs/README.md)                                                                                                                                                             |
| Factorio planner                    | [Usage and design/research routing](../src/private/app/factorio-cycle-calculator/README.md)                                                                                                                                                 |
| IM/ACP gateway                      | [Source, research and verification](../src/private/app/im-acp-gateway/docs/README.md)                                                                                                                                                       |
| Qidian downloader                   | [Application](../src/private/app/qidian-novel-downloader/README.md), [requirements](../src/private/app/qidian-novel-downloader/docs/requirements.md), [technical notes](../src/private/app/qidian-novel-downloader/docs/technical-notes.md) |
| SuperMemo MCP                       | [Automation knowledge and sources](../src/private/app/supermemo-mcp/README.md)                                                                                                                                                              |
| Copilot Telegram notifications      | [Installation and operations](../src/private/app/vscode-copilot-telegram-hook/README.md), [requirements and human-source map](../src/private/app/vscode-copilot-telegram-hook/docs/README.md)                                               |
| Markdown hybrid search MCP          | [Scaffold](../src/public/app/markdown-hybrid-search-mcp/README.md), [design and source map](../src/public/app/markdown-hybrid-search-mcp/docs/README.md)                                                                                    |
| Image occlusion editor              | [Usage, licenses and packaging](../src/public/app/ImageOcclusionEditor/README.md)                                                                                                                                                           |
| Oxford dictionary extraction        | [Usage](../src/private/app/OxfordDictExtractor/README.md)                                                                                                                                                                                   |
| Git commit heatmap                  | [Usage](../src/private/app/git-commit-heatmap/README.md)                                                                                                                                                                                    |
| HTML processing for SuperMemo       | [Usage](../src/private/app/html-sm-processor/README.md)                                                                                                                                                                                     |
| Text splitting                      | [Usage](../src/private/app/llm-text-splitter/README.md)                                                                                                                                                                                     |
| Music flashcards                    | [Usage](../src/private/app/music-flash-card-generator/README.md)                                                                                                                                                                            |
| PostgreSQL/OpenAI retrieval example | [Usage](../src/private/app/rag-postgresql-openai/README.md)                                                                                                                                                                                 |
| Transcription                       | [Usage](../src/private/app/transcribe/README.md)                                                                                                                                                                                            |

## Plugins and Experiments

- [Enterprise translation team](../src/private/lib/enterprise-translation-team/README.md),
  [scan restoration](../src/private/lib/scan-restoration/README.md), and
  [scholarly publication](../src/private/lib/scholarly-publication/README.md)
  own their plugin usage and source contracts. Root [APM configuration](../apm.yml)
  and [lock](../apm.lock.yaml) own deployed-interface provenance; generated copies
  are not editable canonical sources.
- [Document Intelligence lab](../src/lab/azure-document-intelligence-lab/README.md)
  and the [Qidian lab](../src/lab/qidian-novel-downloader/README.md) retain their
  experimental scope separately from application support.

Source/build-only roots do not need empty document hierarchies or inferred
product requirements. Workspace membership remains in the manifests linked
from [shared setup](engineering/workspaces.md); this portal is not a second
project registry. Fixtures, examples, licenses, captures and runtime skill assets
retain their actual consumers and provenance even when they are not policy.
