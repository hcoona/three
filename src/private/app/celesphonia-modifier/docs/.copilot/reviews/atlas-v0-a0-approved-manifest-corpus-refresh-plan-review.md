# Atlas V0 A0 Approved-Manifest Corpus Refresh Plan Review

**Lifecycle:** Proposed plan-review evidence before verified shared `R0R3`

**Increment:** A0R3 - Approved-Manifest Corpus Refresh

**Outcome:** Utility preparation permitted only after release; private reads and census remain blocked

**Final independent result:** `No findings`

**Base G0R2:** `1f9fbcd369d893e8de88cfe195512936e4815f01`

**Final P0R3:** `1c6a568aa4595784f0da6f06ed8b61a390c6a9dc`

**Final P0R3 tree:** `0308f45f9b74b29af2fe076b96c2715bdff4c01c`

**Governing plan:** `../plans/atlas-v0-a0-approved-manifest-corpus-refresh.md`

**Planned staged-record reviewer:** `a0r3-plan-record-reviewer`

## 1. Exact plan candidate

`P0R3` is the direct child of exact `G0R2`. Its exact no-renames path set is:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-approved-manifest-corpus-refresh.md
    atlas-v0-a0-current-corpus-recovery.md
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a2-intake-safety-plan.md
```

Its exact blobs are:

```text
README.md
  fe126329cb020863b4c01cb0f908c2d0a2232ffd
atlas-v0-a0-approved-manifest-corpus-refresh.md
  4a188215804315ebee85096a9fef0cdb77c229dc
atlas-v0-a0-current-corpus-recovery.md
  698e54035dfce56f5af0341324963d24d77738b8
atlas-v0-a0-current-corpus-refresh.md
  3f7ff8c45690db5d2e64835362d828fb00271de5
atlas-v0-a2-intake-safety-plan.md
  bc307ea13d605420a13da3e2cc50467852968bca
```

`P0R3` was committed unchanged from the final reviewed staged tree, pushed, and verified as the clean
shared branch tip before this record was authored.

## 2. Review iterations

Every reviewer was independent of plan authorship and used GPT-5.6 Sol. Review input was limited to
repository-safe plans, records, source, contracts, Git facts, and the exact staged candidate. No
reviewer received the game, current roots, historical request or manifest, inventory, runtime state,
candidate, private path, private hash, filename, count, difference, or corpus content.

| Candidate            | Tree                                       | Reviewer                   | Result        |
| -------------------- | ------------------------------------------ | -------------------------- | ------------- |
| Initial staged plan  | `cd39550841423e2c63ca38a7206ab6cd93020e3c` | `a0r3-plan-reviewer`       | 6 TP          |
| First corrected plan | `e10c9754ca4f556f63ec5b58e2c10964b365eca0` | `a0r3-plan-rereviewer`     | 6 TP          |
| Final corrected plan | `0308f45f9b74b29af2fe076b96c2715bdff4c01c` | `a0r3-plan-final-reviewer` | `No findings` |

The final reviewer re-examined the complete candidate, all prior findings and dispositions, the
authority pivot, lifecycle edits, and executable gates rather than only the final correction diff.

## 3. Planning-drift gate

Before each remediation round, the candidate was checked against the project-leader-approved ideal
minimum:

- the approved manifest is the sole corpus-specific authority;
- the old request contributes only the smallest baseline-byte and public-game anchor;
- obsolete request execution fields cannot block or influence current execution;
- one protected operator-selected root document is runtime input, not corpus or approval authority;
- one source-qualified metadata-only census publishes one pending candidate or closes no-candidate;
- one consuming marker precedes every private read;
- no diagnostic, candidate decision, finalization, A2 operation, or original-data write occurs; and
- every private branch has result-safe closure without disclosing its payload or cause.

The corrections did not add a threat model, corpus scenario, recovery protocol, or approval process.
They removed duplicate authority and completed only the parser, marker, source-binding, publication,
preflight, and handoff contracts needed to make the approved minimum executable.

## 4. TP/FP adjudication

All 12 findings were made atomic and adjudicated TP. No finding was FP.

| #   | Finding                                                                                | Correction                                                                                                   |
| --- | -------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| 1   | Atomic candidate publication omitted a staging path.                                   | Add one fixed staging artifact, validated before same-directory non-overwriting atomic move.                 |
| 2   | Runtime locator provenance was overstated as project-leader approval.                  | Define it only as protected operator-selected runtime input, never corpus or approval evidence.              |
| 3   | The census attempt marker had no machine contract.                                     | Define its exact schema, tool revision, run ID, source-binding digest, and `S0R3` fields.                    |
| 4   | Request survey alias and baseline revision duplicated digest-bound manifest authority. | Make both inert; retain only schema, baseline digest, Steam app, and build as consequential anchor fields.   |
| 5   | The manifest was incorrectly said to contain public app/build labels.                  | Assign survey/revision to the manifest and public app/build identity only to the minimum request anchor.     |
| 6   | `source-bindings.json` lacked an exact machine contract.                               | Define its versioned ordered fields, fixed relative names, canonical form, and hash requirements.            |
| 7   | Obsolete request fields could still refuse through full-contract deserialization.      | Use a bounded four-field extractor; ignore all other members regardless of presence, type, or value.         |
| 8   | A post-move interruption could precede the claimed final reload success boundary.      | Make the validated atomic move the durable publication boundary; require no post-move operation for success. |
| 9   | Preflight refusal had no explicit fresh-ID reinvocation rule.                          | Permit correction and fresh-ID reinvocation only before any marker or private read.                          |
| 10  | The `S0R3` authority block lacked literal parse delimiters.                            | Define exact `atlas-a0r3-source-authority` start and end marker bytes.                                       |
| 11  | Imported A0R1 text still assigned public labels to manifest and candidate bytes.       | Remove those schema-inaccurate public-label claims from the still-imported authority and codec clauses.      |
| 12  | Acceptance still referred to six anchor fields after the reduction to four.            | Require exactly the four consequential fields in section 2.2.                                                |

Final review verified every correction in the complete staged candidate. None changes approved corpus
policy, privacy, or `trusted-local-filesystem/v1`.

## 5. Validation evidence

Repository hooks validated exact `P0R3` Markdown with EditorConfig, typos, markdownlint-cli2, and
Prettier. `git diff --check` passed. Git verification proved:

- exact `G0R2` is `P0R3`'s direct parent;
- `G0R2..P0R3` changes exactly the five paths and blobs in section 1;
- committed `P0R3` tree equals the final reviewed staged tree;
- `P0R3` equals the remote development-branch tip; and
- the worktree was clean before record authorship.

No private request, manifest, locator, current-root metadata, source content, runtime state, candidate,
or prior unauthorized result was read during A0R3 planning and review. No private operation or A2
operation occurred.

## 6. Accepted boundary

After verified shared `R0R3`, work may:

1. create a fresh protected A0R3 workspace containing only the exact qualified A0R2 project/source
   bytes and an empty state directory;
2. have the operator materialize one fixed protected `root-locators.json` runtime input;
3. modify only the copied project and source to implement the reviewed plan;
4. format, rebuild, run synthetic tests, and prove qualified output byte stability;
5. independently review the complete exact source until `No findings`; and
6. author and independently review the result-safe `S0R3` source-qualification record.

Neither `P0R3` nor `R0R3` authorizes reading the historical anchor, manifest, root-locator document,
current roots, or current-tree metadata. It authorizes no census marker, candidate publication,
candidate decision, finalization, A2 operation, production change, or original-data write.

Only verified shared `S0R3` may authorize one consuming metadata-only census attempt. A pure preflight
refusal before any marker or private read may be corrected and reinvoked with a fresh run ID. Any
complete or partial final marker consumes the attempt.

## 7. R0R3 release gate

This proposed record grants utility-preparation authority only. Work may continue only after:

1. this exact staged record receives independent `No findings`;
2. it is committed unchanged as `R0R3`, the direct child of exact `P0R3`;
3. `P0R3..R0R3` adds only this record path;
4. the committed record blob equals the reviewed staged blob;
5. `R0R3` is pushed and verified as the clean shared branch tip; and
6. no private read or census occurs before exact reviewed, committed, pushed, and verified `S0R3`.
