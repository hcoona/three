# Atlas V0 A1 Plan Review

**Increment:** A1 plan

**Outcome:** Execution ready

**Final independent result:** `No findings`

**Plan commit:** `4040f3538aa956fb223751d7865d1e0c8645b177`

**Plan tree:** `6d385b1ab63a5aaa2f44013fa506a50c49474c60`

**Governing plan:** `../plans/atlas-v0-a1-foundation-plan.md`

**Persisted plan commit:** `4040f3538aa956fb223751d7865d1e0c8645b177`

**A0 release commit:** `5681fccc8af78a8253e5d995f90825ecd387350d`

## 1. Exact-plan binding

The independent final review examined the plan commit and tree above. The commit containing this
record must:

1. use the plan commit as its first parent;
2. change only this plan-review record; and
3. be pushed to the shared branch before A1 implementation begins.

Any other repository change creates a new plan candidate and invalidates this result. Handoff
verification compares the recorded identifiers with Git, checks the first-parent relationship,
confirms the changed-path restriction, and requires the record commit to equal upstream.

## 2. Reviewer independence

Each review used a dedicated `rubber-duck` subagent that did not author the plan:

| Iteration | Independent subagent         | Result         |
| --------: | ---------------------------- | -------------- |
|         1 | `atlas-a1-plan-review`       | 19 findings    |
|         2 | `atlas-a1-plan-rereview`     | Eight findings |
|         3 | `atlas-a1-plan-review-three` | Two findings   |
|         4 | `atlas-a1-plan-review-four`  | `No findings`  |

The final reviewer inspected the complete committed plan and governing repository conventions,
not only the last remediation diff.

## 3. Finding disposition

All 29 findings were resolved before the final review.

### Iteration 1

- **Contradictory smoke bytes:** Fixed one compact JSON line and its exact 60-byte hex sequence.
- **System.CommandLine implicit surface:** Removed the dependency and used an exact BCL matcher.
- **Framework invocation defaults:** Eliminated framework invocation, exception, and timeout paths.
- **Missing injected-failure seam:** Added one internal operation-delegate overload.
- **Incomplete result precedence:** Defined argument, help, cancellation, I/O, and diagnostic order.
- **Unsafe parse diagnostics:** Fixed one input-independent `Invalid arguments.` diagnostic.
- **JSON Schema order claim:** Removed the formal schema and kept order in the byte contract.
- **Unavailable schema validator:** Replaced schema tests with BCL shape and exact-byte tests.
- **Visual-only smoke gate:** Added raw-byte subprocess tests for exit code, stdout, and stderr.
- **Unlocked restore:** Required committed lock files and `--locked-mode` validation.
- **Premature handoff commands:** Required A1.1 before commands that need generated projects.
- **Unpersisted status transition:** Made the pushed plan-review record execution authority.
- **Open-ended candidate mechanics:** Bound exact bases, fields, paths, parents, and outcomes.
- **Underspecified public argument behavior:** Defined null, writable, disposed, and cancel order.
- **Unmeasured root traversal:** Added an evaluated `ProjectReference` assertion.
- **Ambiguous test packages:** Listed the exact direct test package set.
- **In-memory-only contradiction:** Permitted repository-safe project metadata.
- **Stale README risk:** Required the implementation candidate to update project status.
- **Unstable help:** Defined exact root and command help bytes.

### Iteration 2

- **Ambient host assumption:** Replaced `DOTNET_HOST_PATH` with the generated Windows apphost.
- **Incomplete release-record fields:** Required every operating-model field.
- **Incomplete release ordering:** Added record-only commit, verification, push, done, and A2 order.
- **Missing risk statement:** Recorded disagreements and accepted residual limitations.
- **Global package ambiguity:** Distinguished direct declarations from inherited global packages.
- **Directory-wide path allowance:** Fixed an exact tracked file manifest and rejected extras.
- **Missing test branches:** Added null, getter-failure, and ordinal case-variant tests.
- **Missing formatting gate:** Added `dotnet format --verify-no-changes` for every project.

### Iteration 3

- **DLL/apphost wording mismatch:** Named the `celesphonia-atlas.exe` apphost consistently.
- **Unspecified package asset metadata:** Fixed exact `PrivateAssets` and `IncludeAssets` values.

### Iteration 4

The reviewer returned exactly `No findings`.

## 4. Reviewed paths

The final review covered:

- `../plans/atlas-v0-a1-foundation-plan.md`;
- `../README.md`;
- `../plans/project-operating-model.md`;
- `../plans/atlas-v0-execution-plan.md`;
- `../plans/atlas-v0-a0-research-contract.md`;
- `atlas-v0-a0-release-gate.md`;
- the repository `AGENTS.md`;
- root `global.json`;
- root `dirs.proj`;
- root `Directory.Build.props`;
- root `Directory.Packages.props`; and
- representative repository C# CLI and xUnit v3 project conventions.

## 5. Acceptance evidence

The exact plan:

- fixes three and only three project boundaries;
- fixes every new tracked implementation file;
- distinguishes direct project references from inherited global build packages;
- defines exact output, help, diagnostics, exit codes, and failure precedence;
- propagates the caller token through every A1 asynchronous operation boundary;
- preserves process-free core tests while adding executable process smoke tests;
- measures traversal, locked restore, build, format, tests, and clean candidate state;
- prohibits save, game-installation, private-workspace, network, WinUI, Agent, and write access;
- defines exact plan and implementation review mechanics; and
- provides executable handoff and stop conditions.

Plan validation outcomes:

- Markdown lint and Prettier checks passed;
- EditorConfig, typo, and commit-message hooks passed;
- all changed lines satisfy the repository line-length limit;
- the documented JSON is 60 bytes and matches the recorded hex sequence;
- the root traversal evaluation command syntax was exercised successfully;
- the candidate commit matched upstream; and
- the tracked worktree was clean.

## 6. Private evidence

A1 planning created and accessed no private evidence. No private path, private hash, save value,
installed source text, or personal Steam identifier appears in this record.

## 7. Execution decision

A1 implementation may begin only after this record's commit passes section 1 and is pushed. That
record commit is the implementation diff base. A1 remains subject to its own exact-candidate
independent release gate before it can move to `done`.
