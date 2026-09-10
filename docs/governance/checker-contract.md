# Repository Record Checker Contract

## Purpose and Boundary

The repository owner maintains this validation contract; authors, control
maintainers and independent reviewers consume it when checking changed records
and their consumers. The [record policy](record-system.md) owns the invariants;
this contract owns their bounded validation inputs and interfaces. Execution
results and work grants belong in their existing carriers.

Existing HK formatting checks and independent review are the current controls.
No general repository-record checker or additional blocking platform gate is
installed. Reviewers perform the applicable schema, coverage, reference and
routing checks and attach actual commands, snapshots and results to the PR.
Do not describe a manual check as an automated CI gate.

## Inputs and Results

A validation run identifies the repository, explicit accepted base commit,
candidate commit or worktree/index snapshot, scope, tool versions, and commands
in its PR or local review evidence. It must not silently substitute the current
branch for an unavailable accepted base. Refresh evidence when a relied-on
authority or candidate changes. Do not store run status in the catalogs.

Keep schema validity, record coverage, and contextual acceptance separate.
A bounded check reports uncovered paths and unresolved ownership, unavailable
checks, and domain-review dependencies. Passing selected checks cannot produce
a repository-wide completeness claim. Compare the discovered scope with the catalog and record any gap in the PR;
a catalog entry alone does not establish coverage.

## Independent Discovery and Classification

Discover candidates from the selected Git snapshot before consulting the family
catalog. Include tracked hidden paths; for worktree preparation additionally
report intended new files and tracked deletions. Do not let ignore rules, a
catalog deletion, or a missing catalog root erase a retained candidate.

The bounded discovery contract includes:

- Root reader/agent interfaces, documentation, governance schemas, contracts,
  and designs, including removed historical records when checking a change.
- Project README, changelog, legal and package documentation, project `docs/`,
  `designs/`, `specs/`, and hidden `.AGENT/`, `.AGENTS/`, `.copilot/`,
  `.specify/`, and project `.github/` record and prompt trees under `src/`.
- Repository `.github/skills/`, `.github/agents/`, `.agents/skills/`, workflow
  Markdown, and canonical project skill sources. Also inspect other tracked
  Markdown, reStructuredText, AsciiDoc, YAML, JSON, JSONL, and CSV candidates
  across `src/`, `tests/`, `eng/`, and repository hidden roots for classification.

Every candidate needs an explicit classification: canonical record family,
generated interface with its canonical source, package/legal interface,
human-source input, domain-owned contract/evidence, fixture/product asset,
configuration/source code, or unprepared ownership. A path that resembles a
record is not automatically a new policy authority. Unprepared is a coverage
gap, never a successful exclusion. Neither all of `src/` nor all hidden paths
may be excluded. Inspect symlink entries and ancestors; a canonical authority
must be a direct file contained in the repository.

## Catalog and Reference Checks

The two catalogs validate against the schemas in `schemas/governance/` using
JSON Schema draft 2020-12. Reject duplicate mapping keys when loading YAML or
JSON and validate schema documents against their metaschema before instances.
Fixed catalog/schema bindings must not depend on catalog entries describing
themselves. IDs are unique within each catalog.

Family paths are repository-relative POSIX paths; `*` matches within a path
component and `**` spans zero or more components. Singleton paths contain no
glob. Reject absolute paths, parent traversal, backslashes, and symlink routing.
Each retained canonical record matches exactly one current family.
A namespace is `repository` or the owning project path, not a global ID prefix.
`current` describes accepted carriers still in use. A proposed change to that
state does not take effect before merge; state alone does not prove coverage.

Root navigation reaches repository records and project entry points; project
portals reach their retained records. A finite chain of explicit portal links
is sufficient; the root portal need not flatten every project document into a
global list. Directory links route to the local README. Cycles alone do not
establish reachability. Validate internal Markdown links, reference-style links,
and anchors with a Markdown parser that ignores code and escaped examples.
Preserve existing explicit anchors and rendered heading semantics, including
duplicate headings. Validate structured path/anchor references as well.

Compare established identifiers with the explicit accepted base. Preserve
namespace, exact identifier, active/retired representation, and the target of
each retirement reference. A cross-project reference must name the owning path
and anchor. Identifier existence cannot prove semantic preservation.

## Controls and HK

`controls.yaml` inventories the existing record-relevant checks and
reviews; it is not a complete inventory of language or release controls.
`current` controls describe reachable accepted behavior within their stated
selection; `scheduled` controls have a trigger, evaluator, action, and fallback.
Scheduled execution may have a candidate procedure file without being enabled.

Three's `hk.pkl` uses `pre-commit` with `stash = "git"` and a `check` hook.
Local defaults from `mise.toml` enable `small,medium`; the CI validation job uses
`small,medium,large` and explicit base/head refs. Evaluate both plans with the
actual profiles and selected files. Confirm every mapped step is included when
its applicable inputs change, and that the CI set contains local checks with
the same semantics. Plan reachability does not establish an executed result or
branch protection. Preserve exclusions and report their coverage limits.

The source's staged-worktree-consistency hook is not installed in Three. Before
any future index-based record gate, demonstrate partial staging, untracked files,
deletion, rename, hidden paths, and CI base/head behavior against Three's HK
stashing semantics. A mutable worktree read must not claim index equivalence.
The protected v3 static-reference checker keeps its existing domain contract.

## Review Sources and Deployment

Canonical procedures are
`.github/skills/record-system-review/SKILL.md` and
`.github/skills/research-evidence-review/SKILL.md`, with their evaluation cases.
Root agent and contributor instructions explicitly invoke them by direct path
for their declared review triggers. Reading a procedure cannot satisfy an
independent review when the reader authored or implemented the change.

Existing `apm.yml`/`apm.lock.yaml` own deployment routing and source attribution
for `.agents/skills/` and generated `.github/agents/`. Do not edit outputs by
hand. The two governance procedures use direct-path routing; they have no APM
deployment or implicit discovery claim. A future request for automated
integration must validate the installed APM behavior, declare provenance,
regenerate affected outputs and prove parity without unrelated changes. It is
not a prerequisite for the current explicit review interface.

## Source

Derived from the catalog, schema, identifier, Markdown, path, and HK-plan
contracts in [the pinned source checker][source]. The source's authentication
identities, fixed public-build/Lasso evidence validators, product requirement
prefix, root-only portal, and staged-index assumptions are not Three contracts.
The [source copyright and license notice](reference-license.txt) is retained.

[source]: https://github.com/hcoona/microsoft-authentication-cli/blob/1a02498589769c38bc16eefc2efc5f9eca6e6994/tools/check_repository_records.py
