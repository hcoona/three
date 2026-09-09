# Repository Governance Migration Design

## Purpose and Acceptance Boundary

This is the inventory and design proposed under the accepted
[migration advancement](https://github.com/hcoona/three/blob/a33e71eb3112f8b79e427ff156cfc0209b716cbc/docs/delivery-wave.md#establish-the-migration-inventory-and-bounded-design). It owns migration destinations,
adaptations, preparation boundaries, and the evidence needed for replacement.
The repository owner maintains it; migration authors and independent reviewers
consume it when preparing and accepting the replacement. Without it, they would
have to reconstruct ownership across several existing documentation systems.
The bootstrap owns procedure and the Wave owns authorization; neither carries
this migration analysis. This record is retired when the replacement and its
consumers no longer need it.

Acceptance of this design accepts its migration decisions, not implementation
of the replacement. Existing authorities remain effective until the final
atomic switch. The [bootstrap](bootstrap.md) and the target branch's accepted
Wave continue to govern preparation. Candidate policies, controls, and moved
records on a working branch cannot govern other work before acceptance.

## Evidence Baseline

- Three: `a33e71eb3112f8b79e427ff156cfc0209b716cbc`, including the bootstrap
  accepted through [PR #664](https://github.com/hcoona/three/pull/664).
- Reference: `hcoona/microsoft-authentication-cli` at
  `1a02498589769c38bc16eefc2efc5f9eca6e6994`, as pinned by the bootstrap.
- Inventory method: enumerate Git-tracked files, including hidden paths;
  inspect root and documentation instructions, workspace manifests, narrative
  record families, entry points, HK wiring, and representative project records.
  Test fixtures, sample documents, generated agent deployments, and product
  assets are classified separately from manually maintained governance records.
- This is a responsibility and destination inventory. It does not certify every
  old statement against current implementation or every external observation.
  That content-level reconciliation is required before deleting its carrier.

The reference record policy, family and control catalogs, contribution guide,
review procedure, and Wave supply the mechanisms below. Authentication-specific
product requirements, operational identities, experiment bundles, and release
decisions are reference-project content, not Three-wide rules.

The pinned Wave already authorizes a Product User Stories authority and states
the accepted primary journey. The pinned family catalog has no installed
user-story family yet. On 2026-09-09, source PRs
[#32](https://github.com/hcoona/microsoft-authentication-cli/pull/32) and
[#33](https://github.com/hcoona/microsoft-authentication-cli/pull/33) were open.
Their proposed text is not an accepted template. A later source update needs
an explicit comparison and accepted adoption; it does not silently replace
the bootstrap's pin.

## Target Ownership

Use one repository control plane and project-local document roots. A project
is a coherent product or engineering concern; a package, test assembly,
language boundary, or release artifact does not automatically create one.
The sole repository owner performs the source's owner and maintainer roles.
Independent review and finding triage still use independent reviewers.

| Concern                                                                                         | Proposed authority or carrier                                                                                   | Producer, maintainer, and consumer                                                                                                    |
| ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Governance principles and record/development policy                                             | `docs/governance/governance-system.md` and `docs/governance/record-system.md`                                   | Owner maintains policy; authors, reviewers, and control maintainers apply it.                                                         |
| Current positive authorization                                                                  | Existing `docs/delivery-wave.md`, extended to repository work only at final switch                              | Owner selects bounded work; every implementing or reviewing session checks it.                                                        |
| Record and control routing                                                                      | `docs/governance/record-families.yaml`, `controls.yaml`, and their schemas                                      | Control maintainer updates them with changed concerns; HK and reviewers consume the same routing.                                     |
| Repository identity, layout, and shared engineering conventions                                 | Repository-scoped records under `docs/governance/` and `docs/engineering/`, admitted only for distinct concerns | Owner and engineering maintainers preserve shared decisions; contributors use them without consulting project product specifications. |
| Human and agent entry points                                                                    | Root `README.md`, `CONTRIBUTING.md`, `AGENTS.md`, `docs/README.md`, and PR template                             | Maintainers route readers and agents to policy, project roots, and review controls; these are not additional policy copies.           |
| Project purpose, stories, requirements, architecture, decisions, research, security, validation | `<project-root>/docs/`, organized by the reference's stable concerns                                            | Owner and project authors maintain each concern; that project's implementers and reviewers consume it.                                |
| Project user and package interfaces                                                             | Project `README` and existing packaged documentation                                                            | Project maintainers retain the reader/package contract; link to internal authorities only where useful to that audience.              |
| Contracts, schemas, fixtures, build and release definitions                                     | Existing domain-owned locations unless an actual consumer requires a move                                       | Domain maintainers and tools retain their typed and runtime contracts. A file is not relocated merely to resemble the reference.      |
| Work discussion, progress, reviews, acceptance evidence                                         | Direct PR; Issue when several PRs or contributors require coordination                                          | Authors and reviewers maintain evidence; owner and replacement sessions recover the next action.                                      |
| Execution context                                                                               | Codex sessions plus a disposable local association lookup                                                       | Running sessions produce context; a coordinator locates work without becoming its authority.                                          |

The family catalog is the proposed authority for record-root routing. Do not add
a separate project database or mirror workspace manifests. A documentation
portal links to cataloged roots; it does not redefine their ownership. Create
project subdirectories only when an admitted record needs them. A small project
can retain its README without acquiring an empty documentation hierarchy.

For a project that needs them, the reference concern names fit directly:
`docs/product/`, `docs/architecture/`, `docs/decisions/`, `docs/research/`,
`docs/security/`, and `docs/validation/`. Project contract or detailed-design
roots are added only for their actual consumers. Separate project roots can
use the same local filenames and identifiers; cross-project references include
the owning repository path and anchor. Existing identifiers keep their meaning
and retirement references. Do not renumber requirements to create a global list.

## Current Records and Required Dispositions

All destinations in this section are proposals. Deletion requires preservation
of unique current rationale or evidence, migration of consumers and references,
and applicable checks and review. A historical-looking filename is not proof
that a record has no current consumer.

| Current surface                                                                            | Observed responsibility or conflict                                                                                                                                                     | Proposed disposition and acceptance evidence                                                                                                                                                                                                                                           |
| ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Root `README.md` and `AGENTS.md`                                                           | Layout/tooling, imported-project provenance, C# exception inventories, project-specific Telegram instructions, and agent routing share broad entry points.                              | Keep entry points concise. Move shared engineering detail to its repository concern and Telegram detail to its project; preserve source commits, licenses, user-facing instructions, and all incoming links.                                                                           |
| `docs/AGENTS.md`, `docs/README.md`, `docs/wiki/index.md`, `overview.md`, and family guides | Wiki ingest/query rules require persistent synthesis and an append-only log, while product requirements and architecture also live in the wiki.                                         | Replace the wiki maintenance control atomically with concern-based routing. Reconcile unique synthesis and route each retained conclusion to its actual owner before removing duplicate entry points.                                                                                  |
| `docs/wiki/sources/*.md`                                                                   | Shared toolchain facts and individual project facts are intermixed as source digests.                                                                                                   | Shared facts go to repository engineering/research concerns; project facts go to project research only if they still support a current conclusion. Preserve source identity and evidence level; do not maintain copies of manifests as specifications.                                 |
| `docs/sources/`, `docs/raw/`                                                               | Human-source and asset namespaces currently contain guides; existing instructions treat their contents as immutable input.                                                              | Do not turn these into a universal archive. At cutover retain any real source/asset with a current consumer in that consumer's scope; remove obsolete scaffold only under the accepted replacement.                                                                                    |
| `docs/wiki/analyses/workflow-delivery/v3/`                                                 | Active requirements, architecture, migration policy, first-slice contract, and a handoff combining current constraints with execution chronology.                                       | Move retained v3 domain records under `src/public/lib/three-workflow-delivery-v3/docs/`; separate concern ownership while preserving v3-only authority, exact contract meaning, evidence links, and current operational limits.                                                        |
| `docs/wiki/log.md`                                                                         | An append-only chronology also retains evidence for current v3 conclusions.                                                                                                             | Identify every current evidence consumer. Move needed evidence into v3 research/validation records with recoverable Git/PR/run references; leave pure chronology in Git and work carriers. Remove the log only with its append-only instruction and all consumers in the final switch. |
| `.github/workflows/*.md`, `.github/workflows/docs/`                                        | Workflow reviews, plans, and explicitly archived pre-v3 design/memory remain in the current tree.                                                                                       | Keep only evidence needed by current consumers, with its original limitations. Route retained v1 compatibility or v2 mechanism evidence into the v3 project; do not reactivate old designs. Git retains the remainder.                                                                 |
| `HK_WINDOWS_STEP_STATUS.md`                                                                | Historical tool-validation snapshot with limitations and unfinished items.                                                                                                              | Revalidate any limitation consumed by current engineering guidance; preserve that conclusion in the shared tooling concern. Remove process history after consumer migration.                                                                                                           |
| Project `docs/`, `.AGENT/`, `.AGENTS/`, `.copilot/`, `.specify/`, and local prompts        | Requirements, current designs, human inputs, plans, review iterations, and local process rules use several independent layouts.                                                         | Reconcile per project into stable concerns, retaining source provenance and product gates. Replace local generic process rules with repository routing; no bulk deletion or global flattening.                                                                                         |
| Project README, changelog, release notes, privacy notices, examples and fixtures           | Reader contracts, published history, legal notices, and executable test inputs can resemble migration records.                                                                          | Preserve their actual package/test/legal consumers. Classify them explicitly rather than rewriting them under generic record rules.                                                                                                                                                    |
| `apm.yml`, lockfile, `.agents/skills/`, `.github/agents/`, local plugin source roots       | Agent deployment has canonical source and generated copies. For example, scan-restoration explicitly identifies project `skills/` as source and `.agents/skills/` as deployment output. | Edit canonical sources and regenerate through existing APM tasks when needed. Catalog installed interfaces as generated consumers, not independently maintained policy authorities. Preserve upstream provenance and licenses.                                                         |
| `hk.pkl`, `mise.toml`, language configs, `eng/`, `.github/workflows/`, domain JSON         | Existing deterministic controls and runtime contracts, including v3 protected Governance.                                                                                               | Map each retained control to its real invariant and execution point. Preserve effective behavior; port only required record controls. Release definitions, access policy, and protected attestation content/path are outside this record migration.                                    |

### Project Boundary Inventory

The following groups cover the tracked source roots found in the baseline.
Grouping a row for readability does not merge its projects. The owner maintains
each proposed project boundary; project authors validate it against actual
consumers during preparation. Test trees remain consumers of their product,
not additional documentation owners.

| Source roots                                                                                                                                                                                                   | Proposed boundary and records                                                                                                                                                                                                                                       |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/private/app/azureauth-credprovider`                                                                                                                                                                       | One product spanning C# and Python components. Keep a single project document root; reconcile its phase decisions, evidence, requirements, and breakdown without weakening ecosystem or release gates.                                                              |
| `src/public/lib/three-workflow-delivery-v3`, `src/private/app/workflow-delivery-v3-nuget-authority`, associated `eng/` and workflow implementation                                                             | One workflow-delivery engineering concern with a helper component. Its project docs own behavior; repository policies own generic work authorization. Native build/release identities retain their own contracts.                                                   |
| `src/public/lib/hcoona-release-smoke-npm`                                                                                                                                                                      | Separate small test product consumed by workflow delivery. Its own README/docs own package purpose; v3 owns the proving protocol.                                                                                                                                   |
| `src/public/lib/nbgv-python` and `src/sample/nbgv-hatch-demo`                                                                                                                                                  | Library and separate sample with explicit dependency references. Move library `.copilot` design material into library docs; sample guidance stays with the sample.                                                                                                  |
| `src/private/app/cf-ddns-updater`, `document-translator-cli`, `im-acp-gateway`, `qidian-novel-downloader`, `vscode-copilot-telegram-hook`                                                                      | Separate products already carrying local documentation. In document-translator, numbered capability folders need semantic reconciliation, not a mechanical rename. In the hook project, H-series inputs still support current decisions and must retain provenance. |
| `src/private/app/factorio-cycle-calculator`, `src/public/app/markdown-hybrid-search-mcp`                                                                                                                       | Separate products with hidden agent design/review trees. Resolve current content and evidence before retiring iterations.                                                                                                                                           |
| `src/private/app/supermemo-mcp`                                                                                                                                                                                | Project knowledge base and script notes need admission review alongside README; retain operational knowledge with its project consumer.                                                                                                                             |
| `src/private/lib/enterprise-translation-team`, `scan-restoration`, `scholarly-publication`                                                                                                                     | Three independent tooling products. Preserve plugin source, contracts, skill routing, and generated deployment relationships.                                                                                                                                       |
| `src/public/lib/asciidoctor-latexmath`                                                                                                                                                                         | Imported Ruby product with local `.specify` constitution/templates and `.github` prompts. Replace generic process authority at cutover; preserve product specifications, package docs, source attribution, and required license notices.                            |
| `src/public/lib/hexo-renderer-asciidoc`, `steam-account-history-to-csv`, `src/public/app/ImageOcclusionEditor`                                                                                                 | Separate imported products. Preserve package-facing docs, privacy/provenance notices, examples, and fixtures; move only admitted internal records.                                                                                                                  |
| `src/public/lib/Memoization`, `Memoization.Generators`; `src/public/lib/PhiFailureDetector`, `src/public/app/PhiFailureDetector.Console`                                                                       | Related library/generator and library/console roots. Preserve existing build boundaries; confirm whether each companion has an independent product consumer before choosing shared versus separate document ownership.                                              |
| `src/public/lib/CircularList`, `Hjg.Pngcs`, `MicrosoftExtensions.Logging.MSTest`, `MicrosoftExtensions.Logging.Xunit`, `MicrosoftExtensions.Options.DedupChangeExtensions`, `WebHdfs.Extensions.FileProviders` | Separate library candidates with README/release-note consumers. Retain small interfaces; do not manufacture vision or story files from manifests.                                                                                                                   |
| `src/private/app/OxfordDictExtractor`, `git-commit-heatmap`, `html-sm-processor`, `llm-text-splitter`, `music-flash-card-generator`, `rag-postgresql-openai`, `transcribe`                                     | Separate application candidates with README guidance. New internal records require an actual consumer.                                                                                                                                                              |
| `src/private/app/DotNetLockFileLister`, `OxfordLearnersDictionaryProcessor`, `OxfordWordlistExtractor`; `src/lab/TaskAssigner`, `TplLab`                                                                       | Source/build roots without narrative record sets in the inventory. Do not infer independent product requirements; confirm ownership before admitting documents.                                                                                                     |
| `src/lab/azure-document-intelligence-lab`, `src/lab/qidian-novel-downloader`                                                                                                                                   | Lab concerns with local guidance. Keep experimental evidence distinct from product commitments; the Qidian lab and application are not implicitly one support promise.                                                                                              |
| `src/private/lib/hk`                                                                                                                                                                                           | Shared engineering configuration component, not a product inferred solely from its directory. Route its policy consumer to repository engineering.                                                                                                                  |

Abbreviated names within a row use the preceding explicit parent path. Unsettled
companion boundaries above do not block the two selected examples, but must be
resolved before the final replacement claims complete ownership coverage.

## Reference Mechanisms and Necessary Adaptations

No GOV-001 through GOV-011 amendment is proposed. Preserve the bootstrap's
accepted wording, including the already accepted `main-v2` to `main`
substitution. The following are scope, namespace, or implementation adaptations;
none waives a principle.

| Reference mechanism                                                 | Three adaptation and limit                                                                                                                                                                                                                                                                                       |
| ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Scoped authority, admission, granularity, formats, lifecycle, gates | Retain their meaning. Instantiate product concerns inside project roots and shared concerns at repository scope. Avoid universal metadata, empty families, and general archives.                                                                                                                                 |
| One Wave and dependency-based Slice concurrency                     | Keep one root Wave. Entries identify affected project authorities and bounded outcomes; no per-project Wave, global stage, or status ledger. At cutover, surviving work needs explicit accepted grants rather than automatic carry-over from old plans.                                                          |
| Stable requirement IDs and decision numbers                         | Preserve uniqueness within the owning project namespace and unambiguous path-qualified cross-project references. Retain existing identifiers and retirement markers; do not impose the source product's `V2-REQ` prefix on unrelated projects.                                                                   |
| Product User Stories advancement                                    | Preserve the distinction between user context, required behavior, design, and evidence. When a project has an accepted journey and a real consumer, propose a project-local stories authority that references requirements. Do not import the source Git journey or its unmerged PR text as Three product scope. |
| Family/control catalogs and schemas                                 | Reuse source structures and only current or properly triggered mechanisms. Bind project path instances and controls to actual consumers; avoid a second project registry.                                                                                                                                        |
| Independent candidate discovery in the record checker               | Extend bounded discovery beyond root `docs/` to project record roots and retained hidden legacy record candidates. Catalog edits cannot make discovered records disappear. Generated agent output, fixtures, and product assets require explicit classification, not blanket exclusion of `src/`.                |
| Direct canonical record paths                                       | Keep manually maintained authorities as direct files. Installed/generated skill interfaces route to their canonical source and do not create editable duplicate authorities.                                                                                                                                     |
| Local-fast checks contained in CI                                   | Preserve Three's HK profile model and actual pre-commit/check execution points. Do not copy source hook commands or staged-tree assumptions without validating them against Three's stash/index behavior.                                                                                                        |
| Record, research, and independent finding review                    | Port the relevant source Skills with their authority references and independence requirements. Do not replace existing domain review with the generic record gate.                                                                                                                                               |
| Source-specific identities and empirical validators                 | Do not install the authentication fork's identity registry, Lasso/public-build validators, experiment results, or product schemas as generic monorepo controls. Retain equivalent Three domain controls under their own authorities.                                                                             |

The source checker is `tools/check_repository_records.py`; adaptation should
preserve its policy checks rather than copy all product-specific branches.
Three's existing `eng/scripts/` is the proposed implementation location.
General record controls cover links, schema validity, identifier ownership,
family coverage, portal routing, and declared HK execution. Existing language,
workflow, secret, and domain controls retain their own responsibilities.
New or materially generalized checks begin advisory unless deterministic
behavior and remediation are demonstrated; the owner reviews any proposed
blocking activation using the bootstrap's rule classification.

## Representative Validation Cases

### Small Project: nbgv-python

Use `src/public/lib/nbgv-python`: it already has README, changelog, two hidden
`.copilot` design records, source, tests, and a sample consumer. This exercises
real ownership and retirement without starting product development.

The candidate must identify which design statements are current, preserve
distinct rationale in project-local architecture/design records, and route the
README appropriately. Remove obsolete blog-style metadata only after checking
for a publishing consumer. Keep sample usage in
`src/sample/nbgv-hatch-demo`; do not copy it into repository policy. Unverified
design statements remain explicitly unresolved rather than becoming promises.

### Cross-Project Case: Workflow Delivery and the Smoke Package

Use the v3 documentation and `hcoona-release-smoke-npm` purpose together.
Place v3 system requirements, architecture, and proving evidence in the v3
project root; retain the smoke package's product purpose in its own root.
Cross-project links reference those authorities. The NuGet helper remains a
component of the engineering concern, not a competing documentation authority.

The candidate must preserve the v3-only priority, exact current contracts,
evidence lineage, and exhausted dispatch boundary. The protected JSON at
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` keeps its
path and bytes. This case does not authorize workflow, runtime, package,
Environment, attestation, publication, or access changes. If a necessary move
would require such a change, report it for a separate bounded decision.

## Recoverable Coordination Requirements

The smallest proposed executor is a bounded local Codex invocation per work
item, with the PR/Issue as its durable work carrier. A controller session can
end and be replaced. A worker can also be replaced without its conversation
being the only copy of a requirement, decision, diff, or review result.

Use ordinary persisted `codex exec --json` jobs as the initial candidate for a
later execution experiment. An app-server client is appropriate when actual
work requires interactive steering, approval handling, or enumerating threads.
The Codex SDK can provide bindings if needed; an additional Agents SDK project
is not required by this contract. No executor or coordinator is implemented or
run by this design change.

| Decision to recover                  | Required source and behavior                                                                                                                                                                                                                                                 |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Is work authorized now?              | Read accepted bootstrap/policy and Wave, then revalidate relied-on authorities against current target state. A session prompt cannot enlarge the grant.                                                                                                                      |
| Which work is this session doing?    | Associate repository, Issue/PR, branch/worktree, session/thread ID, and author/reviewer role in the work carrier or a disposable local lookup. Keep machine-specific process handles local. Do not add these fields to the Wave.                                             |
| Is a worker still active?            | Consult its owning runtime/process and observed turn state. Missing local state or an app-server `notLoaded` thread is not proof that another process or machine is idle. Resolve ownership before starting another writer.                                                  |
| Can a stopped worker continue?       | Read current work-carrier evidence, unresolved findings, Git diff and untracked files, checks, and current prerequisites. Resume the exact known thread when useful, or start a replacement from these same carriers. Never select a global `--last` session by convenience. |
| Has work succeeded?                  | Validate actual output against its bounded outcome and required reviews. Process exit, `turn.completed`, or an agent's final message alone is insufficient. Interrupted work may already have changed files or external state.                                               |
| Can work run concurrently?           | Separate worktrees for simultaneous writers; compare dependencies and shared canonical authorities. Independent sessions do not remove review-role separation or authorize concurrent writes to one authority.                                                               |
| What should the coordinator do next? | Reconcile accepted grant, current PR/Issue, runtime activity, and Git/check/review state. Advance only the next already authorized action; report an unresolved owner decision in that carrier.                                                                              |

The official pages retrieved on 2026-09-09 establish the available interface
shapes, not tested integration behavior in this environment:

- [Non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode):
  JSONL events, persisted sessions unless ephemeral mode is selected, and
  `codex exec resume <SESSION_ID>`.
- [Codex app server](https://learn.chatgpt.com/docs/app-server):
  `thread/list`, `thread/read`, `thread/resume`, `turn/start`, and
  `turn/interrupt`. Listing defaults to interactive sources when source filters
  are omitted; a future client must account for worker source kinds, pagination,
  and the scope of runtime status.
- [Codex SDK](https://learn.chatgpt.com/docs/codex-sdk): thread start/continue/
  resume interfaces; library choice does not define repository authorization.

Before an implementation or execution grant relies on those mutable interfaces,
its author rechecks the official pages and installed version and its reviewer
evaluates the result. Every Wave change is the fallback review event under the
bootstrap. No current Three-wide mutable-source recheck registry was found;
this narrow trigger stays here while this design is its only consumer. No
universal session database, retry scheduler, dashboard, or event archive is
proposed.

## Bounded Preparation and One Final Switch

Preparation is split by reviewable outcome, not by a global delivery phase.
The accepted tree keeps its existing authority throughout preparation. Do not
merge partly migrated authority families or keep old and new policies active.

1. **Prepare the two-case candidate.** On an unmerged candidate branch, adapt
   the repository policies, catalog/checker contracts, routing, and review
   procedures from the pin; prepare exact record transformations for the two
   cases above. Run relevant checks and independent reviews. Preserve the
   reviewed candidate in Git and its work carrier, not as active policy on
   `main`. This is the proposed next Wave advancement.
2. **Prepare remaining ownership groups.** A later accepted grant selects
   bounded groups from the inventory, resolves their content and consumers,
   and incorporates reviewed transformations into the same final candidate.
   Use an Issue if coordinating multiple PRs. Each group is reviewed against
   current accepted authority; material prerequisite changes refresh affected
   evidence. No automatic rollout from success of the two examples.
3. **Accept the atomic replacement.** A separately accepted grant covers the
   final combined change after all retained concerns and consumers are ready.
   Merge the policies, project records, catalogs/schemas, routing, applicable
   controls, and removals together. Retire the bootstrap and this preparation
   design once their consumers have moved. Preserve source license attribution.

Accepting preparation can be recorded by an owner-approved Wave transition
whose PR links the exact reviewed candidate and check/review evidence. That
accepts the bounded preparation outcome; it does not merge the candidate or
make its policies effective. Wave entries remain grants, without checkpoints,
session IDs, results, or historical status. Do not create repository commits
merely to report session activity.

A later coordinator experiment, if needed, gets its own accepted protocol,
scope, effects, and stop conditions. Record migration need not depend on
building that tool.

## Final Acceptance Evidence

The final replacement PR must make the following reviewable:

- Every retained concern has one authority and an actual owner, producer,
  maintainer, consumer, and use point. Previously hidden record roots and
  generated interfaces are included in coverage.
- Project records remain separate. Root policy and navigation contain no
  copied project requirements; cross-project dependencies use explicit links.
- Source principles match the approved baseline. All adaptations and any
  proposed principle amendment have explicit owner disposition.
- Current rationale, human inputs, empirical evidence, identifiers, privacy
  obligations, license notices, and package/test consumers survive relocation
  or have an explicit accepted retirement disposition.
- The old wiki and project process authorities, append-only requirements,
  duplicate instructions, and obsolete records retire in the same change as
  their replacements and migrated consumers. Git retains history.
- Active work receives explicit bounded grants in the replacement Wave.
  Product/domain preconditions and runtime release authorization remain
  independently satisfied; migration acceptance cannot revive spent grants.
- Applicable HK/CI results, record and domain reviews, independent finding
  triage, and owner acceptance are attached to the exact candidate. Refresh
  affected evidence when target authorities change; verify the accepted tree
  against the reviewed change.

## Owner Decisions for This Proposal

Acceptance is requested for the default project-local `docs/` layout, the
proposed two representative cases and v3 engineering boundary, and the next
bounded candidate-preparation grant. The unresolved companion ownership choices
in the inventory remain explicit prerequisites for their later preparation.
No principle amendment, product behavior change, source-baseline update,
runtime experiment, or final switch is proposed for acceptance here.
