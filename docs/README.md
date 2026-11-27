# Documentation

This portal routes readers to repository and project concerns. It is navigation;
the linked records own their stated concerns. Project records stay with their
project, and a README is sufficient when it serves the actual reader.

## Repository Governance

| Reader need                                                       | Record                                                                                                                                                   |
| ----------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Creation, amendment and retirement of rules                       | [Governance system](governance/governance-system.md)                                                                                                     |
| Authority, admission, Wave rules, controls and project namespaces | [Repository record system](governance/record-system.md)                                                                                                  |
| Current bounded work authorization                                | [Delivery Wave](delivery-wave.md)                                                                                                                        |
| Retired projects, rationale and historical Git trees              | [Retired projects](retired-projects.md)                                                                                                                  |
| Canonical record bindings and schema                              | [Record bindings](governance/record-families.yaml), [binding schema](../schemas/governance/record-families.schema.json)                                  |
| Human contribution and review                                     | [Contributing](../CONTRIBUTING.md), [PR template](../.github/pull_request_template.md)                                                                   |
| Vulnerability reporting and supported scope                       | [Security policy](../SECURITY.md)                                                                                                                        |
| Agent routing                                                     | [Repository instructions](../AGENTS.md)                                                                                                                  |
| Independent review procedures                                     | [Record-system review](../.github/skills/record-system-review/SKILL.md), [research-evidence review](../.github/skills/research-evidence-review/SKILL.md) |
| Source attribution                                                | [Retained copyright and license](governance/reference-license.txt), [repository provenance](../README.md)                                                |

Git retains former policies and deleted records; Issues and PRs retain work
discussion and results. Neither this portal nor an old plan adds work to the
accepted Wave.

## Shared Engineering

- [Engineering principles](engineering/engineering-principles.md): shared design,
  security and test/evidence guidance applied with each project's authorities.
  Agents use the [thin guidance interface](../.github/skills/engineering-guidance/SKILL.md).
- [Workspace and toolchain setup](engineering/workspaces.md).
- [.NET compatibility rationale](engineering/dotnet-compatibility.md), including
  generator/analyzer and retained legacy-format exceptions.
- [HK execution](engineering/hk-execution.md), including the limits of retained
  Windows evidence.
- [Bounded agent execution](engineering/agent-execution.md), covering worktree
  isolation and recovery.
- [Public release triage and inventory gate](engineering/public-release-triage.md),
  covering tiered release-diff inspection and `src/public/{app,lib}/*`
  exhaustiveness.

## Libraries and Companion Interfaces

| Project concern                           | Reader entry                                                                                                                                                                                                                                                                                                                                                                                     |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| NBGV Python wrapper and Hatch integration | [Library](../src/public/lib/nbgv-python/README.md), [architecture](../src/public/lib/nbgv-python/docs/architecture/overview.md), [separate sample](../src/sample/nbgv-hatch-demo/README.md)                                                                                                                                                                                                      |
| Workflow Delivery v3                      | [Application interface](../src/private/app/workflow-delivery/README.md), [domain records](../src/private/app/workflow-delivery/docs/README.md), [historical evidence readers](../src/private/app/workflow-delivery/docs/validation/historical-readers.md)                                                                                                                                        |
| Asciidoctor LaTeX rendering               | [Package](../src/public/lib/asciidoctor-latexmath/README.md), [contracts, research and evidence](../src/public/lib/asciidoctor-latexmath/docs/README.md)                                                                                                                                                                                                                                         |
| Hexo AsciiDoc rendering                   | [Contributor interface](../src/public/lib/hexo-renderer-asciidoc/README.md), [package interface](../src/public/lib/hexo-renderer-asciidoc/README.npm.md), [change history](../src/public/lib/hexo-renderer-asciidoc/CHANGELOG.md), [example](../src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md)                                                                              |
| Steam account history export              | [Developer interface](../src/public/lib/steam-account-history-to-csv/README.md), [user interface](../src/public/lib/steam-account-history-to-csv/README.user.md), [privacy](../src/public/lib/steam-account-history-to-csv/PRIVACY.md), [change history](../src/public/lib/steam-account-history-to-csv/CHANGELOG.md)                                                                            |
| Circular list                             | [Package](../src/public/lib/CircularList/README.md), [validation](../src/public/lib/CircularList/docs/validation.md), [release notes](../src/public/lib/CircularList/ReleaseNotes.md)                                                                                                                                                                                                            |
| PNGCS                                     | [Package](../src/public/lib/Hjg.Pngcs/README.md), [change history](../src/public/lib/Hjg.Pngcs/ChangeLog.md)                                                                                                                                                                                                                                                                                     |
| Memoization                               | [Package](../src/public/lib/Memoization/README.md), [release notes](../src/public/lib/Memoization/ReleaseNotes.md); the generator remains a build component, without separate product records                                                                                                                                                                                                    |
| Nanopass compiler framework               | [Package](../src/public/lib/Nanopass/README.md), [architecture](../src/public/lib/Nanopass/docs/architecture.md), [validation](../src/public/lib/Nanopass/docs/validation.md), [release notes](../src/public/lib/Nanopass/ReleaseNotes.md), [third-party notices](../src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt); the generator remains a build component, without separate product records |
| Phi failure detector                      | [Library](../src/public/lib/PhiFailureDetector/README.md), [release notes](../src/public/lib/PhiFailureDetector/ReleaseNotes.md)                                                                                                                                                                                                                                                                 |
| MSTest logging                            | [Package](../src/public/lib/MicrosoftExtensions.Logging.MSTest/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Logging.MSTest/ReleaseNotes.md)                                                                                                                                                                                                                                 |
| xUnit logging                             | [Package](../src/public/lib/MicrosoftExtensions.Logging.Xunit/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Logging.Xunit/ReleaseNotes.md)                                                                                                                                                                                                                                   |
| Options change deduplication              | [Package](../src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/README.md), [release notes](../src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/ReleaseNotes.md)                                                                                                                                                                                                   |
| WebHDFS file provider                     | [Package](../src/public/lib/WebHdfs.Extensions.FileProviders/README.md), [release notes](../src/public/lib/WebHdfs.Extensions.FileProviders/ReleaseNotes.md)                                                                                                                                                                                                                                     |

The v1 Official release workflow stack is retired; its former boundary record is
listed with the Workflow Delivery
[retired records](../src/private/app/workflow-delivery/docs/validation/historical-readers.md#retired-records)
as history, not a current route.

## Applications

| Project concern                | Reader entry                                                                                                                                                                                                                                |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AzureAuth credential provider  | [Product records and acceptance](../src/private/app/azureauth-credprovider/docs/project-breakdown.md); C# and Python share this product's authorities                                                                                       |
| AzureAuth Unofficial V2        | [Application](../src/public/app/azureauth-unofficial/README.md), [project records](../src/public/app/azureauth-unofficial/docs/README.md), [packaging](../src/public/app/azureauth-unofficial/packaging/windows/README.md)                  |
| Cloudflare dynamic DNS         | [Requirements](../src/private/app/cf-ddns-updater/docs/requirements.md), [high-level design](../src/private/app/cf-ddns-updater/docs/high-level-design.md), [low-level design](../src/private/app/cf-ddns-updater/docs/low-level-design.md) |
| Document translation           | [Capability records](../src/private/app/document-translator-cli/docs/README.md)                                                                                                                                                             |
| IM/ACP gateway                 | [Source, research and verification](../src/private/app/im-acp-gateway/docs/README.md)                                                                                                                                                       |
| LiteAgensh                     | [Project purpose and record routing](../src/private/app/lite-agensh/README.md)                                                                                                                                                              |
| Qidian downloader              | [Application](../src/private/app/qidian-novel-downloader/README.md), [requirements](../src/private/app/qidian-novel-downloader/docs/requirements.md), [technical notes](../src/private/app/qidian-novel-downloader/docs/technical-notes.md) |
| Sheet music recognition        | [Project purpose and record routing](../src/private/app/sheet-music-omr/README.md), [agent handoff](../src/private/app/sheet-music-omr/docs/agent-handoff.md)                                                                               |
| SuperMemo MCP                  | [Automation knowledge and sources](../src/private/app/supermemo-mcp/README.md)                                                                                                                                                              |
| Copilot Telegram notifications | [Installation and operations](../src/private/app/vscode-copilot-telegram-hook/README.md), [requirements and human-source map](../src/private/app/vscode-copilot-telegram-hook/docs/README.md)                                               |
| Image occlusion editor         | [Usage, licenses and packaging](../src/public/app/ImageOcclusionEditor/README.md)                                                                                                                                                           |
| HTML processing for SuperMemo  | [Usage](../src/private/app/html-sm-processor/README.md)                                                                                                                                                                                     |

## Plugins and Experiments

- [Enterprise translation team](../src/private/lib/enterprise-translation-team/README.md),
  [scan restoration](../src/private/lib/scan-restoration/README.md), and
  [scholarly publication](../src/private/lib/scholarly-publication/README.md)
  own their plugin usage and source contracts. Root [APM configuration](../apm.yml)
  and [lock](../apm.lock.yaml) own deployed-interface provenance; generated copies
  are not editable canonical sources.

Source/build-only roots do not need empty document hierarchies or inferred
product requirements. Workspace membership remains in the manifests linked
from [shared setup](engineering/workspaces.md); this portal is not a second
project registry. Fixtures, examples, licenses, captures and runtime skill assets
retain their actual consumers and provenance even when they are not policy.

## Test Migration Guidance

The [xUnit-to-MSTest skill](../.apm/skills/migrate-xunit-to-mstest/SKILL.md)
preserves fixture and scheduling semantics during requested framework migrations.
