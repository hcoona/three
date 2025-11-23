# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → If not found: ERROR "No implementation plan found"
   → Extract: tech stack, libraries, structure
2. Load optional design documents:
   → data-model.md: Extract entities → model tasks
   → contracts/: Each file → contract test task
   → research.md: Extract decisions → setup tasks
3. Generate tasks by category:
   → Setup: project init, dependencies, linting
   → Tests: contract tests, integration tests
   → Core: models, services, CLI commands
   → Integration: DB, middleware, logging
   → Polish: unit tests, performance, docs
4. Apply task rules:
   → Different files = mark [P] for parallel
   → Same file = sequential (no [P])
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All contracts have tests?
   → All entities have models?
   → All endpoints implemented?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Single project**: `src/`, `tests/` at repository root
- **Web app**: `backend/src/`, `frontend/src/`
- **Mobile**: `api/src/`, `ios/src/` or `android/src/`
- Paths shown below assume single project - adjust based on plan.md structure

## Phase 3.1: Setup
- [ ] T001 Create project structure per implementation plan
- [ ] T002 Initialize [language] project with [framework] dependencies
- [ ] T003 [P] Configure linting and formatting tools

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3 (Principle P2)
**CRITICAL: These specs MUST be written and MUST FAIL before ANY implementation**
- [ ] T004 [P] BlockProcessor contract spec in spec/processors/block_processor_spec.rb
- [ ] T005 [P] InlineMacroProcessor contract spec in spec/processors/inline_macro_processor_spec.rb
- [ ] T006 [P] Renderer pipeline & cache key spec in spec/renderer/pipeline_spec.rb

## Phase 3.3: Core Implementation (ONLY after tests are failing)
- [ ] T007 [P] Implement BlockProcessor (lib/asciidoctor/latexmath/block_processor.rb)
- [ ] T008 [P] Implement InlineMacroProcessor (lib/asciidoctor/latexmath/inline_macro_processor.rb)
- [ ] T009 [P] Implement renderer pipeline (lib/asciidoctor/latexmath/renderer/pipeline.rb)
- [ ] T010 Implement cache key & storage (lib/asciidoctor/latexmath/cache.rb)
- [ ] T011 Attribute parsing & validation module (lib/asciidoctor/latexmath/attributes.rb)
- [ ] T012 External toolchain detection & timeout handling (lib/asciidoctor/latexmath/toolchain.rb)

## Phase 3.4: Integration
- [ ] T013 [P] Attribute precedence integration spec (spec/integration/attribute_precedence_spec.rb)
- [ ] T014 [P] Cache reuse integration spec (spec/integration/cache_reuse_spec.rb)
- [ ] T015 [P] Security & timeout integration spec (spec/integration/security_spec.rb)

## Phase 3.5: Polish
- [ ] T016 [P] Standard Ruby lint & autofix (CI) (bin/ or Rake task)
- [ ] T017 [P] Performance smoke spec (< target ms) (spec/performance/pipeline_perf_spec.rb)
- [ ] T018 Update README attribute & usage tables
- [ ] T019 Update DESIGN.md & class diagram
- [ ] T020 Prepare CHANGELOG & version bump rationale
- [ ] T021 Manual smoke test on sample.adoc (document steps)

## Dependencies
- Tests (T004-T007) before implementation (T008-T014) (P2 enforcement)
- T011 depends on cache & attributes (T012, T013) partial; may stub initially
- Integration specs (T015-T017) after core implementations (T008-T014)
- Polish tasks (T018-T023) after all integration specs pass

## Parallel Example
```
# Launch T004-T006 together (independent spec files):
Task: "BlockProcessor contract spec"
Task: "InlineMacroProcessor contract spec"
Task: "Renderer pipeline & cache key spec"
```

## Notes
- [P] tasks = different files, no dependencies
- Verify tests fail before implementing
- Commit after each task
- Avoid: vague tasks, same file conflicts

## Task Generation Rules
*Applied during main() execution*

1. **From Contracts**:
   - Each contract file → contract test task [P]
   - Each endpoint → implementation task

2. **From Data Model**:
   - Each entity → model creation task [P]
   - Relationships → service layer tasks

3. **From User Stories**:
   - Each story → integration test [P]
   - Quickstart scenarios → validation tasks

4. **Ordering**:
   - Setup → Tests → Models → Services → Endpoints → Polish
   - Dependencies block parallel execution

## Validation Checklist
*GATE: Checked by main() before returning*

- [ ] Both processors have contract specs (P1/P2)
- [ ] Renderer pipeline & cache key spec present (P5)
- [ ] All tests come before implementation (P2)
- [ ] Parallel tasks truly independent
- [ ] Each task specifies exact file path
- [ ] No [P] task conflicts on same file
- [ ] External toolchain handling task present (P5)
- [ ] Lint & style task present (P4)

---
*Based on Constitution v2.0.0 - See `.specify/memory/constitution.md`*
