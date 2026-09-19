# Repository Record Checker Contract

## Purpose and Boundary

The repository owner maintains this validation contract; authors, control
maintainers and independent reviewers consume it when checking changed records
and their consumers. The [record policy](record-system.md) owns the invariants;
this contract owns their bounded validation inputs and interfaces. Execution
results and work grants belong in their existing carriers.

The versioned [record checker](../../eng/scripts/check_repository_records.py)
provides repeatable mechanical checks with pinned script dependencies. Existing
HK formatting and independent semantic review remain separate controls. The
checker is an explicitly invoked advisory tool, not an automatic CI or blocking
hook gate. Its nonzero exit reports invalid inputs or deterministic findings;
advisory status does not turn a failed check into successful validation.

Run from the repository root with an explicit accepted commit, replacing the
example revision with the actual reviewed base:

```powershell
mise run records:check -- --base <accepted-commit> --worktree --output ../three-record-review.json
mise run records:check -- --base <accepted-commit> --candidate <candidate-commit> --output ../three-record-review.json
mise run records:test
```

Write generated reports outside the worktree, as in the examples above. A report
inside the worktree becomes an untracked candidate on the next run and requires
classification like any other input. Attach the report or a retrievable artifact to the
PR, together with the command and tool revision. A local filename and a digest
alone do not provide the bytes needed for another reviewer to repeat the check.

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

The report carries the discovered paths and their classifications, snapshot
identities and file digests, diagnostics, dependency versions and explicit check
limits. A worktree report describes the bytes read, including intended untracked
inputs; it is not proof of index equivalence. Commit input reads the selected Git
tree. An unavailable base is an error, never permission to substitute `HEAD`.
Run again after changing the candidate or any relied-on prerequisite.

The tool is a prospective implementation of this contract. The original #670
classification inventory and the external validator referenced by #677/#683
were not recoverable from the inspected carriers and known local locations.
Those carriers retain execution reports and snapshot identities, but their
original frozen classification cannot be reproduced from the published summary
alone. A new report must identify its own tool and inputs; it must not claim to
replay those historical runs. Git and those PRs retain the original reports and
their limitations. The versioned checker and its regression cases now supply
the maintained executable interface for future reviews.

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
themselves. Family IDs and binding IDs are unique within their respective lists;
control IDs are unique within the control catalog. Every binding refers to an
existing family, and every retained family has a current or scheduled binding.
Changing or deleting a family cannot hide a discovered record.

Binding paths are repository-relative POSIX paths; `*` matches within a path
component and `**` spans zero or more components. Singleton paths contain no
glob. Reject absolute paths, parent traversal, backslashes, and symlink routing.
Each retained canonical record matches exactly one current binding. Its family
supplies the shared responsibilities; its binding supplies the owning namespace,
representation, path and lifecycle. Version-2 catalog input is supported only
for accepted-base comparison during migration, not as a second current format.
A namespace is `repository` or the owning project path, not a global ID prefix.
`current` describes accepted carriers still in use. A proposed change to that
state does not take effect before merge; state alone does not prove coverage.

Root navigation reaches repository records and project entry points; project
portals reach their retained records. A finite chain of explicit portal links
is sufficient; the root portal need not flatten every project document into a
global list. A directory used as a canonical-record portal routes through its
existing local README; cycles alone do not establish reachability. An ordinary
link to an existing source directory is valid without a README and does not
establish reachability of records beneath it. Do not invent a README requirement
for source browsing. Validate internal Markdown links, reference-style links,
and anchors with a Markdown parser that ignores code and escaped examples.
Preserve existing explicit anchors and rendered heading semantics, including
duplicate headings. Validate structured path/anchor references as well.

Compare established identifiers with the explicit accepted base. Preserve
namespace, exact identifier, active/retired representation, and the target of
each retirement reference. A cross-project reference must name the owning path
and anchor. Identifier existence cannot prove semantic preservation.

The current executable recognizes numeric `REQ`, `FR`, `NFR`, and `AC` heading
definitions, including project prefixes. It checks duplicate and preserved IDs
and supported retirement targets. Other identifier syntax, active/retired
representation and preservation of meaning require separate review evidence;
the report names these limits rather than certifying all project conventions.

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

The record checker does not execute HK or certify its plans, staged/index
behavior, platform configuration or semantic evidence. Retain applicable HK
plans and domain reviews separately. Its regression suite exercises independent
discovery after a binding deletion, README-only admission, moves and cross-root
companions, snapshot selection, schema failures, and source-directory versus
portal links. These bounded cases verify the implemented mechanics; they do
not replace the review of project ownership or support claims.

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
