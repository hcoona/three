# Factorio Cycle Calculator — UI Phase 1 Execution Plan

- Date: 2026-02-17
- Owner role: Tech Lead / Architect
- Audience: Junior developers
- Status: Ready for implementation

## 1) Phase 1 mission

Evolve the current demo UI into a compact, icon-forward planner layout (inspired by Factorio Planner / Helmod / factoriolab), while keeping demo behavior unchanged.

### Success statement

If a user opens the app, they should still be able to do exactly these core interactions:

1. Select machine per recipe
2. Select module + module count
3. Select beacon module + counts
4. Change petroleum gas target and unit
5. Toggle integer machine counts
6. See solved machine counts and net flows

But the page should feel denser, more structured, and easier to scan visually.

## 2) Hard scope boundaries (must not change)

### 2.1 Keep unchanged

- Fixed three-recipe chain (still oil demo):
    - oil processing recipe
    - heavy-oil cracking recipe
    - light-oil cracking recipe
- Existing solving logic in services/viewmodel
- Existing data loading path behavior
- Existing warning/error semantics
- Existing numeric meaning and units

### 2.2 Not included in Phase 1

- No graph editor / arbitrary recipe nodes
- No scenario save/load
- No global preset apply-to-all interaction
- No additional optimization objective options
- No new backend model or solver constraints

## 3) Target UI layout for Phase 1

### 3.1 Zone A: Objective bar (top of main page)

Show compact controls in one row:

- PG target input (with fluid icon if available)
- Unit switch (per minute / per second)
- Integer mode toggle
- One compact status chip text area (last solve status)

Notes:

- Keep existing sidebar path inputs for now if needed for low risk, but objective controls move to main area.
- Keep default values consistent with current app.

### 3.2 Zone B: Recipe block list (middle)

Render exactly 3 recipe blocks in recipe order.

Each block has:

1. Header row: recipe icon + recipe name + category
2. Build row: machine selector (icon + label), modules section, beacon section
3. Output row: machine count + product/byproduct/ingredient mini-columns (icon-first)

Compactness rules:

- Prefer icon + short label instead of long text
- Avoid repeated section text where icon can communicate meaning
- Use same numeric formatter (`format_amount`)

### 3.3 Zone C: Results panel (bottom)

Keep 3 cards/columns:

- Products
- Byproducts
- Ingredients

Plus a clear summary line:

- Solver status
- Crude input rate

No new KPIs in Phase 1.

## 4) Implementation architecture (code-level constraints)

Do not change service/viewmodel responsibilities.

- `services/*`: no behavior changes
- `viewmodels/app_viewmodel.py`: no behavior changes
- `views/app_view.py`: primary refactor target (layout + render composition)
- `views/types.py`: only extend UI-facing types if truly required

Preferred approach:

- Refactor into smaller render functions first
- Rewire layout second
- Keep widget keys stable unless unavoidable

## 5) Task breakdown (junior-friendly)

Each task is intentionally small. Complete sequentially.

### Task 1 — Create phase branch and baseline screenshots

Goal: lock visual baseline before changes.

Steps:

1. Create branch for Phase 1 UI
2. Run app with current demo
3. Capture screenshots:
    - full page
    - one recipe row area
    - bottom summary area
4. Save under `.AGENT/docs/assets/ui-phase1-baseline/`

Definition of done:

- Screenshots exist and are referenced in PR description

---

### Task 2 — Introduce layout constants and section skeletons in `app_view.py`

Goal: make layout refactor safe and readable.

Steps:

1. Add constants for reusable column ratios and icon widths
2. Add placeholder render functions:
    - objective bar
    - recipe blocks container
    - results footer
3. Keep output identical at this point

Definition of done:

- App renders successfully
- No functional change visible (structure only)

---

### Task 3 — Move objective controls from sidebar to top objective bar

Goal: align with planner-style top-first workflow.

Steps:

1. Keep data path controls in sidebar
2. Move these controls to top bar:
    - rate unit
    - PG target
    - force integer
3. Keep `SidebarSettings` semantics unchanged (same fields, same values)

Definition of done:

- Controls appear in top bar
- Solve result matches previous behavior for same inputs

---

### Task 4 — Convert production table rows into compact recipe blocks

Goal: improve scannability and icon density.

Steps:

1. Replace current wide header + flat rows with per-recipe block containers
2. In each block:
    - recipe icon/name at top
    - machine/modules/beacons in one compact row
    - machine count and flows in output row
3. Keep existing selectors and keys

Definition of done:

- Still exactly 3 recipe entries
- All previous selectors are present and usable
- No missing data in products/byproducts/ingredients

---

### Task 5 — Tighten icon-first rendering

Goal: reduce text noise.

Steps:

1. Ensure icon shows wherever currently available:
    - recipe
    - machine
    - flow items/fluids
2. Shorten redundant labels where safe
3. Keep accessibility fallback (text still visible when icon missing)

Definition of done:

- UI remains understandable if icon load fails
- Layout is visibly denser than baseline screenshots

---

### Task 6 — Keep summary/result panel compact and stable

Goal: preserve demo meaning while improving layout consistency.

Steps:

1. Keep existing summary categories (products/byproducts/ingredients)
2. Keep existing status line meaning
3. Improve spacing and alignment only

Definition of done:

- No metric definition changes
- Result values are equivalent for same input and selections

---

### Task 7 — Regression self-check (manual)

Goal: ensure behavior is unchanged.

Test matrix:

1. Default settings -> solve -> capture counts and crude input
2. Toggle integer mode -> verify status/result is sensible
3. Change PG target 900 -> 1800 (per min) -> verify monotonic increase in machine counts
4. Switch to per second -> verify unit conversion remains correct
5. Change module/beacon selections -> verify output updates and no crashes

Definition of done:

- All checks pass
- No new warnings/errors introduced by UI refactor

---

### Task 8 — Documentation update

Goal: make maintenance easier for next contributor.

Steps:

1. Update `UI_DESIGN_v1.md` with “Phase 1 implemented scope” notes
2. Add a short section describing final layout zones
3. Attach before/after screenshots in docs folder

Definition of done:

- Docs explain what changed and what intentionally did not change

## 6) PR slicing strategy (recommended)

Create 3 PRs instead of one large PR:

1. PR-A: Structure only (Task 2)
2. PR-B: Objective bar + recipe block layout (Tasks 3-4)
3. PR-C: Icon compact pass + regression + docs (Tasks 5-8)

Why: easier review, easier rollback, less merge risk.

## 7) Acceptance checklist for reviewer

Reviewer should confirm:

- [ ] Demo logic remains fixed-chain and functionally identical
- [ ] Top objective controls are in main area
- [ ] Recipe rendering is block-based and denser than baseline
- [ ] Icon-first presentation is improved without losing text fallback
- [ ] Result panel meaning and values are unchanged
- [ ] Manual regression matrix completed

## 8) Risk register and safeguards

1. **Risk:** Widget key collisions during refactor
   **Safeguard:** Preserve existing keys unless absolutely necessary.

2. **Risk:** Hidden behavior drift due to control relocation
   **Safeguard:** Compare same-input outputs before/after each task.

3. **Risk:** Over-compaction hurts readability
   **Safeguard:** Keep concise captions and minimum spacing around critical values.

4. **Risk:** Icon loading failure degrades UX
   **Safeguard:** Always keep text fallback visible.

## 9) Handoff notes for junior developers

When unsure, choose the lower-risk option:

- Layout changes > logic changes
- Smaller PR > larger PR
- Keep old semantics > introduce “clever” behavior

If a change seems to require solver/viewmodel edits, stop and ask for tech lead review first.

---

This plan is intentionally constrained to deliver a strong visual/interaction upgrade in Phase 1 while preserving the current demo logic end-to-end.
