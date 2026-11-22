# Triage Fixture

## Case 1: Unresolved Authority Overlap Escalated to Owner

### Originating Finding

A reviewer reports that a proposed record may overlap an existing authority, but the
available files do not establish whether the existing record's owner still consumes that
section.

### Expected Triage

Classify the finding as `unresolved`, record the inspected evidence and remaining factual
question in the governing pull request (or governing Issue when no pull request governs),
and identify the exact repository-owner decision needed. The triage reviewer must not be
the originating reviewer, change author, or implementation agent. Do not force a
true-positive or false-positive result and do not close the finding without recorded owner
disposition and rationale.

## Case 2: Deterministic Pointer or Display-Text Typo With Unchanged Provenance

### Originating Defect

A pull-request summary links to `#issuecomment-7067355696` under parent Issue `#1260`
instead of sub-issue `#1302` (and writes `[run 102](.../runs/1002)` while the target URL
and underlying candidate commit/run evidence are `1002`), with the exact comment fragment
identifier, quoted content, authority ownership, and commit/run provenance unchanged and
unambiguous, alongside an unclosed Markdown code fence.

### Expected Handling

Treat these defects as purely deterministic carrier defects rather than material `GOV-011`
findings: the author repairs the container Issue number, display label, and Markdown
delimiter directly and verifies link/Markdown formatting mechanically, without invoking a
third-party `GOV-011` classifier or appending permanent triage ledger rows.

## Case 3: Misattributed Commit, Workflow-Run, or Artifact Provenance Identifier

### Originating Finding

A reviewer finds that a pull request's validation ledger cites passing CI workflow run
`41743250099` (or candidate commit `a3b80be2`) from an earlier branch or unrelated
project rather than the actual run and commit that produced the claimed check result.

### Expected Triage

Treat the wrong run/commit/artifact identifier as a **material provenance finding** (not
a mechanical typo). Route it to an independent `GOV-011` classifier separate from the
originating reviewer, change author, and implementation agent; classify it as
`true positive` with concrete run/commit evidence in the governing review carrier before
remediating the ledger; and verify closure on the remediation delta without re-litigating
unrelated closed triage rows.
