# Factorio Cycle Calculator UI Design Plan v1

- Date: 2026-02-17
- Scope: Planning only (no code changes)
- Target inspirations: Factorio Planner (mod), Helmod (mod), factoriolab

## 1. Why redesign now

The current Streamlit UI (`views/app_view.py`) is functional but still demo-oriented:

- Fixed to exactly 3 recipes in one chain (advanced oil processing + two cracking recipes)
- Controls are distributed between sidebar and table rows without clear workflow stages
- “Solve once after render” interaction model is not obvious to users
- Limited summary hierarchy (status + products/byproducts/ingredients), no explicit objective panel
- Weak visual grouping for recipe blocks, modules, beacons, and machine count outcomes

This is good for validating solver correctness, but not yet aligned with planner-style experience.

## 2. Product direction

Design a **planner-first interface** with these UX goals:

1. **Task-oriented flow**: define demand -> configure production blocks -> solve -> inspect bottlenecks
2. **Card/block mental model** (Planner/Helmod): each recipe node feels like a production block
3. **Fast global edits** (factoriolab style): simple toggles and presets, low click cost
4. **Strong feedback loops**: users can instantly see impact of machine/module/beacon choices
5. **Future-ready architecture**: can expand from oil chain to general graph/cycle planning

## 3. UX principles adapted from inspirations

### 3.1 From Factorio Planner / Helmod

- Hierarchical production blocks with explicit per-block configuration
- Dense but readable tabular data with icons and compact numbers
- Local block settings + global defaults
- “Production line sheet” feeling instead of loose widgets

### 3.2 From factoriolab

- Top-level objective panel with clear target output and rate units
- Fast swapping of machines/modules through compact controls
- Consistent icon-driven reading order: item icon -> name -> rate
- Immediate visibility of net inputs/outputs and critical constraints

### 3.3 For this Streamlit app

- Keep layout stable between solves (avoid UI jump)
- Avoid over-nesting; use expandable sections for advanced controls
- Keep novice-safe defaults while allowing expert controls in-place

## 4. Information architecture (v1)

Proposed 4-zone layout:

1. **Top Objective Bar** (main area)
    - Target product and rate
    - Unit switch (per second / per minute)
    - Solve mode (continuous / integer)
    - Primary CTA: Solve / Recalculate

2. **Global Presets Panel** (left sidebar or top expander)
    - Data source paths (assets/data-raw)
    - Global machine/module/beacon defaults
    - Optional advanced assumptions (e.g., water treated as free input)

3. **Production Blocks Area** (main body)
    - One block per recipe in current chain
    - Block sections:
        - Header: recipe icon/name/category
        - Build settings: machine, module set, beacon setup
        - Computed row: machine count, power (placeholder if not modeled), local I/O rates
    - Optional collapsed details for ingredients/results composition

4. **Results & Diagnostics Panel** (bottom)
    - Objective summary (status, crude consumption, objective value)
    - Net flow cards: products/byproducts/ingredients
    - Constraint diagnostics (e.g., infeasible reasons, rate inconsistencies)

## 5. Interaction model

### 5.1 Primary user journey

1. Load data sources
2. Select chain recipes
3. Set target output and unit
4. Tune machine/module/beacon settings per block
5. Click solve (or auto-solve mode in future)
6. Read machine counts and net flows
7. Iterate with minimal clicks

### 5.2 Interaction details

- **Explicit solve trigger** in v1
    - Keep deterministic behavior and avoid heavy re-solve on every widget change
- **Preview before solve**
    - Show “dirty state” badge when controls changed but not solved
- **Advanced settings collapsed by default**
    - Beacon/module fine-tuning visible but non-intrusive
- **Sticky objective/result header** (if Streamlit supports comfortably)
    - User always sees target and current status while scrolling

## 6. Proposed UI components (mapped to current code)

Current single-file view (`app_view.py`) should evolve into composable sections (planning only):

- `render_objective_bar(...)`
    - target, unit, integer/continuous, solve button
- `render_global_presets(...)`
    - data paths + global defaults + warnings
- `render_production_block(recipe_key, ...)`
    - one planner-like row/card per recipe
- `render_result_overview(...)`
    - status, objective, primary KPIs
- `render_diagnostics(...)`
    - validation and infeasible hints

Suggested grouping (still within existing architecture):

- Keep business math in services (`solver_service.py`, `catalog_service.py`)
- Keep orchestration in viewmodel (`app_viewmodel.py`)
- Keep rendering state in `views/types.py` + new UI dataclasses if needed

## 7. Visual language v1

- **Density**: medium-compact (closer to planner tools, not dashboard-only)
- **Icon-first** rows for recipes/items/fluids
- **Rate formatting consistency** using existing `format_amount`
- **Status emphasis**:
    - Optimal/Feasible: success tone
    - Infeasible/Abnormal: prominent warning panel with actionable hints
- **Terminology**:
    - Prefer “Block”, “Target”, “Net Inputs”, “Net Outputs”, “Machine Count”

## 8. Feature roadmap (no code yet)

### Phase A — Structural UI pass (high impact, low risk)

- Re-layout into objective bar + production blocks + results panel
- Add explicit solve button and dirty-state indicator
- Move path controls into dedicated “Data & Assets” section
- Improve summary card readability and order

Expected gain: interaction clarity and reduced cognitive load.

### Phase B — Planner ergonomics pass

- Add per-block collapsible advanced settings
- Introduce global defaults (apply to all blocks)
- Add quick actions (copy previous block settings, reset block)
- Improve icon alignment and compact labels

Expected gain: fewer repetitive clicks; closer to Helmod/factoriolab workflow.

### Phase C — Diagnostics and explainability

- Show objective components (e.g., crude term contributions)
- Surface key constraints and slack (where meaningful)
- Better error messages for invalid chain selections

Expected gain: better trust in solver results.

### Phase D — Scalability preparation

- Generalize from fixed 3-recipe oil chain to extensible block list
- Prepare UI state model for graph/cycle scenarios
- Introduce optional scenario save/load in app state

Expected gain: future extension path without full UI rewrite.

## 9. Acceptance criteria for v1 redesign

1. Users can understand the end-to-end flow in under 1 minute without docs
2. Objective, solve state, and key outputs are always visible in one screenful (desktop)
3. Per-recipe controls are visually grouped and do not feel fragmented
4. Results remain stable and interpretable across repeated solve iterations
5. Existing solver behavior and data-loading behavior stay functionally unchanged

## 10. Risks and mitigations

- Risk: Streamlit layout constraints limit planner-like density
    - Mitigation: favor simple cards/columns + expanders over custom CSS-heavy hacks

- Risk: UI refactor accidentally mixes rendering and solver logic
    - Mitigation: enforce service/viewmodel boundaries before adding features

- Risk: over-design before general-chain backend exists
    - Mitigation: use phased roadmap and keep v1 focused on oil-chain UX quality

## 11. Suggested implementation order (when coding starts)

1. Introduce objective bar + explicit solve action
2. Restructure production rows into block-like sections
3. Rework results area into KPI + net flow + diagnostics
4. Add global defaults and block quick actions
5. Add tests/smoke checks for rendering state and unchanged solver outputs

## 12. Out-of-scope for this v1 plan

- Full graph editor for arbitrary recipe networks
- Full parity with Helmod/Factory Planner feature depth
- Optimization objective redesign beyond current crude-minimization baseline

---

This document defines a practical v1 UX direction that keeps the current architecture intact while moving the app toward a planner-grade interaction style.
