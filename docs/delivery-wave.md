# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Reconcile Workflow V1, V2 and V3 documentation

- **Work carrier:** [Issue #1406](https://github.com/hcoona/three/issues/1406)
  and its linked authorization and implementation pull requests. The repository
  owner requested this cleanup and the explicit closure condition below.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md), [documentation portal](README.md),
  and Workflow Delivery's
  [project portal](../src/private/app/workflow-delivery/docs/README.md),
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [design and integration contracts](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md) and
  [historical readers](../src/private/app/workflow-delivery/docs/validation/historical-readers.md).
- **Advancement and outcome:** audit and reconcile Workflow V1, V2 and V3
  documentation against the current implementation and accepted authorities.
  Delete superseded records after migrating current consumers, links and any
  unique current rationale or evidence. Use immutable Git references for
  historical bodies rather than an archive or duplicate authority. Begin with
  closed-campaign validation records and the mixed smoke research records;
  preserve current native-integration findings, recheck obligations, provenance,
  evidence limits, stable requirement identifiers, legal notices and immutable
  human-source inputs.
- **Ordering and gates:** merge this authorization before substantive cleanup.
  Migrate consumers and navigation atomically with retirement. Retain independent
  record-system, research-evidence and applicable engineering/OCR reviews,
  independent classification of material findings, record checks and affected
  HK validation. Product, evidence-level or authority changes outside faithful
  documentation reconciliation return to the owner.
- **Acceptance and closure:** the current tree has one authority per retained
  concern, accurate implementation/status routing and no superseded body
  without a current consumer. Only an explicit further repository-owner request
  may close or delete this entry; completing or merging an individual cleanup
  PR does not authorize its closure. Retaining the entry does not enlarge this
  bounded documentation scope. Keep progress and results in the Issue and PRs.
- **Effects:** permit repository and read-only GitHub inspection, documentation
  and directly affected record-routing/catalog changes, local record and
  conformance checks, independent delegated review, and ordinary Issue/PR/check
  operations and their normal CI.
- **Exclusions:** no runtime, dependency, workflow-execution or product-behavior
  changes; no new empirical experiments or support claims; no release/proving
  dispatch, publication, authentication, Environment, account, publisher or
  access change. All closed-campaign failures, spent ceilings and operation
  limits remain unchanged.

### Derive tool and runtime versions from committed configuration

- **Work carrier:** the pull request proposing this entry and its linked
  implementation pull requests; [Issue #1247](https://github.com/hcoona/three/issues/1247)
  retains the triggering Python 3.15 investigation. The repository owner
  accepted an independent multi-reviewer audit after Renovate PR
  [#1166](https://github.com/hcoona/three/pull/1166) failed because Workflow
  Delivery hard-coded the Python 3.14 line, and requested a complete fix.
- **Accepted inputs:** the [engineering principles](engineering/engineering-principles.md),
  the Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md) and
  [integration contracts](../src/private/app/workflow-delivery/docs/middle-level-design.md#integration-mapping),
  root `mise.toml`, `mise.lock`, `global.json`, `global.pkl`,
  `renovate.json` and the GitHub workflows.
- **Owner decisions:** root `mise.toml` and `mise.lock` are the only tool and
  runtime version authority; a project-local mise configuration does not
  select CI tools. The .NET SDK is the exception: `global.json` is its
  authority, with `rollForward` kept `disable`, and mise reads it as an
  idiomatic version file. Its upgrades edit `global.json`. The required check
  `Test (Python 3.14)` becomes `Test (Python)` because no Python matrix is
  planned; the owner authorizes the matching ruleset update.
- **Advancement and outcome:**
    - Make mise read the .NET SDK from `global.json`, stop generating the SDK
      selection from `mise.lock`, and update the Renovate .NET rules and their
      post-upgrade tasks accordingly.
    - Remove version literals that duplicate committed configuration from
      Workflow Delivery code, tests, fixtures and workflows: application tool
      checks accept the locked committed selection; tests and fixtures read the
      root `mise.lock` or `global.json`; workflows read versions once from the
      checked-out root `mise.lock` or `global.json` or install through
      `mise --locked`. A retained version bound must be a documented native
      adapter compatibility limit with its own named check and message.
    - Rename the required Python check and update its consumers and the main
      ruleset together.
    - Repair the other audited principle findings: preparation and native
      query failures report their stage, termination, exit and bounded stderr
      in caller-visible output with scenario tests; tool-check messages name
      the tool and failed condition; unit tests exercise tool selection against
      fixture configuration rather than copied literals; test selection covers
      configuration guards; a mirror-only test is removed; record the PNPM
      12.13.0 depth recheck under research-evidence review.
- **Ordering and gates:** land the .NET authority and shared helpers before
  their consumers. Each implementation PR follows the existing Workflow
  Delivery engineering, OCR, record-system and research-evidence review gates;
  independently classify material findings before repair. New feasibility or
  product tradeoffs return to the owner.
- **Acceptance:** a routine Renovate patch update of each affected tool
  changes only committed configuration and passes ordinary CI without code,
  test or workflow edits; no remaining literal duplicates a committed tool
  selection outside a documented adapter limit; the renamed required check
  protects `main`. Dispatch-only Release and smoke workflows are accepted on
  static validation (actionlint, workflow tests and their local readers)
  because their campaigns are closed. Delete this entry when accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation,
  workflow, Renovate and implementation changes, locked tool preparation,
  isolated local builds and credential-free tests, independent delegated
  review, ordinary PR/check operations and their normal CI, and the owner-
  authorized `main` ruleset required-check rename.
- **Exclusions:** no Python 3.15 migration (tracked by #1247) and no change to
  the Renovate Python hold; no release, proving or campaign dispatch,
  publication, credential, Environment, account, publisher or other access
  change; no restoration or modification of the retired legacy
  `release-orchestrate.yml` stack beyond an owner decision recorded separately;
  no new runtime matrix.

### Design and plan sheet-music recognition

- **Work carrier:** [Issue #686](https://github.com/hcoona/three/issues/686),
  its sub-issues and their linked pull requests. After the desk study recorded
  there, the repository owner requested this design and planning grant.
- **Accepted inputs:** the [governance policy](governance/governance-system.md),
  [record policy](governance/record-system.md),
  [engineering principles](engineering/engineering-principles.md) and
  [documentation portal](README.md). Findings in #686 and its sub-issues are
  unreviewed research inputs, not accepted evidence; a repository record that
  relies on one rechecks it under research-evidence review.
- **Owner decisions:** the project root is `src/private/app/sheet-music-omr/`;
  its product implementation language and packaging remain undecided. Public
  records, Issues and pull requests exclude private paths, piece identities
  and private score content, using anonymized batch numbers and aggregate
  counts instead. Content that the owner's development agents read is
  processed by their model services; the owner accepts this.
- **Advancement and outcome:** refine the overall design and plan for
  recognizing photographed or scanned printed piano scores into editable
  notation, including model fine-tuning options. Maintain the workstream
  decomposition, dependencies, open owner decisions and desk-research
  findings in the Issues. Admit project records only when the record policy's
  admission questions have concrete answers. The outcome is a current plan
  from which the owner can authorize each further workstream as its own
  bounded entry.
- **Ordering and gates:** apply record-system review to new or changed records,
  research-evidence review to research records and evidence-backed claims, and
  the shared engineering guidance to architecture, dependency-boundary and
  test/evidence strategy decisions, with independent classification of
  material findings. Product value, scope and risk decisions, including those
  listed on #686, return to the owner.
- **Acceptance and closure:** only an explicit further repository-owner request
  closes or deletes this entry. Retaining it does not enlarge this design and
  planning scope.
- **Effects:** permit repository and read-only GitHub inspection, read-only
  retrieval of public web sources, read-only local inspection of the owner's
  private transcription repository and local desk-study archive, documentation
  and directly affected record-routing/catalog changes, Issue and sub-issue
  creation and editing, independent delegated review, and ordinary
  pull-request/check operations and their normal CI.
- **Exclusions:** no implementation, experiment, runtime observation, tool or
  package installation, dataset or model download, training, inference or paid
  compute; work on a workstream beyond its design and planning requires its
  own entry. No other upload of score material to external recognition,
  model-hosting or storage services; no change to the private transcription
  repository; no publication, credential or access change.

### Define the sheet-music text representation (W1)

- **Work carrier:** [Issue #1488](https://github.com/hcoona/three/issues/1488),
  a sub-issue of #686, and its linked pull requests. The repository owner
  requested authorization of all W1 work.
- **Accepted inputs:** those of the sheet-music design and planning entry and
  the project records accepted under either entry. Findings in #1488 are
  unreviewed research inputs.
- **Owner decisions:**
    - W1 code consists of experiments under `src/private/app/sheet-music-omr/`
      in any language the repository currently supports. It does not select
      the product's language, packaging or release.
    - Converters are written anew in this repository; the private v1 converter
      source is not imported.
    - The owner's development agents may read the private transcription
      repository, including its drafts, review records and accepted gold, and
      the owner's local desk-study archive, locally and read-only. Their model
      services process what they read; the owner accepts this. That content,
      anything derived from it and piece identities are never committed,
      otherwise uploaded or quoted; public carriers report only aggregate
      results and anonymized batch numbers.
    - Acceptance uses two measures: a hard criterion of zero in-scope content
      differences on the accepted private gold pieces, and a reported
      layout-fidelity score, where higher is better, that does not gate
      acceptance.
    - Tokenizer comparisons may use the tokenizer files of any candidate base
      model; no base model is selected.
    - The project integrates into the existing monorepo workspaces, checks and
      CI rather than a separate toolchain.
    - The owner approved the local installations and downloads listed under
      Effects. Numeric limits there are accident fuses, not bounds on normal
      development.
- **Advancement and outcome:**
    - Accept W1 requirements covering the required notation categories, the
      as-printed and canonical invariants, system-level carry-in and
      carry-out state, both acceptance measures, explicit unsupported cases
      and the validation basis. The validation basis freezes, from the
      recorded review findings and under research-evidence review, the set of
      toolchain-class findings that the text and converters must eliminate.
    - Accept the text-format specification as the project's contract, with a
      normative grammar and, subject to record admission, the recognition
      sidecar interface. Record the ABC comparison evidence available inside
      W1: an xml2abc/abc2xml round trip and tokenizer counts.
    - Implement experimental forward (text to MusicXML) and reverse (MusicXML
      to text, for MuseScore, PDMX and OpenScore exports) converters, a grammar
      validator, a round-trip harness using MuseScore import/re-export and
      musicdiff scoring, the system-assembly test and the replay of the frozen
      toolchain-class findings.
- **Ordering and gates:** merge this entry before any W1 work. Accept the
  requirements and specification before converter implementation relies on
  them. Accept each experiment or runtime-observation protocol, including the
  ABC round trip, tokenizer counts and MuseScore import checks, under
  research-evidence review before it executes. If the ABC round trip loses
  under about 1% of in-scope elements, the format choice remains open pending
  model evidence outside W1; return to the owner before implementing
  converters. Integration changes to shared root or CI configuration are
  ordered with other entries changing the same files. Each pull request
  retains the applicable record-system, research-evidence and engineering
  reviews, with independent classification of material findings. New
  feasibility, product or scope tradeoffs return to the owner.
- **Acceptance:** the accepted W1 requirements and validation basis are met,
  including the hard criterion and a zero-difference system-assembly test on
  every accepted private gold piece, at least 99% of in-scope elements
  identical on the public corpus files and their MuseScore re-exports, and
  every finding in the frozen toolchain-class set reproduced correctly from
  the text without score-file edits. The desk study counted 34 such findings,
  an unreviewed estimate; if the frozen set materially differs, return to the
  owner. The layout-fidelity score and ABC comparison result are recorded.
  Delete this entry when accepted or when the owner stops W1.
- **Effects:** permit the design and planning entry's inspection and
  documentation effects; local read-only use of the private inputs above;
  local installation of musicdiff 6.0.0, xml2abc/abc2xml and, when missing,
  the MuseScore 4.7.6 command line; download of public scores from OpenScore
  Lieder (CC0) and PDMX, including PDMX's whole-dataset archives, using only
  license-compatible files, and of candidate tokenizer files without model
  weights, all kept outside the repository, stopping to return to the owner
  before cumulative downloads exceed 20 GB; experimental source and tests;
  root workspace membership, lockfile, central package version, check
  configuration and CI test selection and preparation changes needed to
  integrate the project into the existing monorepo tooling; isolated local
  runs and credential-free tests; and ordinary pull-request/check operations
  and their normal CI.
- **Exclusions:** no model weights, including rokot-omr-2b, and no GPU
  inference, training or paid compute; no other workstream's implementation,
  synthetic training data or format A/B training; no committed fixture other
  than self-authored, public-domain or CC0 material with its provenance; no
  other upload of score material to external recognition, model-hosting or
  storage services; no change to the private transcription repository or its
  workflow; no change to existing tool or runtime versions, other projects'
  behavior or required checks, and no new CI workflow; no publication,
  release, credential or access change.
