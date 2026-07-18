# Atlas V0 A0 Release Gate

**Increment:** A0 - Scope Decision and Research Contract

**Outcome:** Passed

**Final independent result:** `No findings`

**Candidate commit:** `f291c43c370beedbb6e1d5b19b8d270c4fe9baab`

**Candidate tree:** `6f2662c9dfc4649e6d8c7b869487c5c21b20d37b`

**Governing plan:** `../plans/atlas-v0-execution-plan.md`

**Persisted plan commit:** `1a715130af30e1aafa9af41b9add4c555a399a3a`

**Human scope decision:** `3610d5e2a69073672bda665eed25a545a141c06b`

## 1. Exact-candidate binding

The independent final review examined the candidate commit and tree above. The commit containing
this record must:

1. use the candidate commit as its first parent;
2. change only this release-gate record; and
3. be pushed to the shared branch before A0 moves to `done`.

Any other repository change creates a new candidate and invalidates this result. Handoff
verification compares the recorded identifiers with Git, checks the first-parent relationship,
and confirms the changed-path restriction.

## 2. Reviewer independence

Each review used a dedicated `rubber-duck` subagent that did not author the candidate:

| Iteration | Independent subagent    | Result        |
| --------: | ----------------------- | ------------- |
|         1 | `atlas-a0-release-gate` | Six findings  |
|         2 | `atlas-a0-rereview`     | Four findings |
|         3 | `atlas-a0-exact-review` | Two findings  |
|         4 | `atlas-a0-review-four`  | One finding   |
|         5 | `atlas-a0-review-five`  | `No findings` |

The final reviewer inspected the full committed A0 state rather than only the last remediation
diff.

## 3. Finding disposition

All findings were resolved before the final review:

### Iteration 1

- **Undefined redaction allowlist:** Defined `atlas-schema-key-allowlist/v1` as empty.
- **Arbitrary Agent survey aliases:** Restricted aliases to six digits.
- **Optional private artifact lineage:** Required `lineageAliases`.
- **Open questions in the approved review:** Replaced them with approved outcomes.
- **Incomplete-A0 resume procedure:** Added release handoff and reopening rules.
- **Incorrect preservation schema link:** Corrected the relative link.

### Iteration 2

- **Human approval conflated with completion:** Separated confirmation from release.
- **Release record not bound to a candidate:** Required commit, tree, parent, and path checks.
- **Synthetic Agent vector policy conflict:** Added a synthetic-only test exception.
- **Duplicate artifact and cyclic lineage:** Defined identity and acyclic lineage direction.

### Iteration 3

- **Circular A8 cleanup dependency:** Moved cleanup before the A8 release candidate.
- **Abbreviated approval commit:** Required and recorded the full commit.

### Iteration 4

- **Premature completion claim in the `.copilot` index:** Made completion depend on this gate.

## 4. Reviewed repository paths

The final review covered:

- `../README.md`;
- `../plans/project-operating-model.md`;
- `../plans/save-semantic-atlas-plan.md`;
- `../plans/atlas-v0-execution-plan.md`;
- `../plans/atlas-v0-a0-research-contract.md`;
- `atlas-v0-a0-scope-review.md`;
- `../schemas/atlas-v0/*.schema.json`;
- `../schemas/atlas-v0/test-data/agent-egress-envelope.*.json`; and
- the tracked `.private/.gitignore`.

## 5. Acceptance evidence

The exact candidate satisfies the A0 acceptance criteria:

- the save and installed-definition denominators are finite and reconciled;
- all discovered candidates have terminal classifications;
- the default in-scope structural gap threshold is zero;
- literal source keys are denied because the v1 allowlist is empty;
- private artifact custody, lineage, last use, expiry, and disposition are explicit;
- operational and private-derived Agent envelopes remain private;
- synthetic Agent conformance vectors are hand-authored and contain no private-derived data;
- human confirmation covers corpus, fingerprints, exclusions, privacy, and narrowing authority; and
- handoff and reopening rules bind completion to an exact reviewed candidate.

Validation procedures and outcomes:

- all four schemas passed Draft 2020-12 meta-schema validation;
- the positive synthetic Agent vector validated;
- four negative Agent vectors were rejected for their intended violations;
- the current corpus, private inventory, and preservation manifests validated;
- private artifact aliases were unique and predecessor lineage was acyclic;
- the private provenance and approval records used the expected full commit identifiers;
- private artifacts remained ignored by Git;
- Markdown lint, Prettier, JSON Biome, EditorConfig, and typo checks passed; and
- candidate `HEAD` matched its upstream commit with a clean tracked worktree.

## 6. Repository-safe private evidence

The review used only these aggregate private facts:

- manifest `atlas-intake/v2`, survey `survey-000001`, revision 3;
- two candidate save roots and 23 discovered save entries;
- 21 included saves;
- 580 definition candidates, with 496 included and 84 excluded;
- 10 unique private artifact inventory entries;
- retained historical revisions 1 and 2;
- no incomplete or temporary capture artifact; and
- preservation qualification remains `preservation-unqualified` pending A2.

No private path, private hash, save value, installed source text, or personal Steam identifier is
recorded here.

## 7. Release decision

A0 is complete when this record's commit passes the exact-candidate checks in section 1 and is
pushed to the shared branch. A1 may begin only from its own committed and pushed execution plan.
