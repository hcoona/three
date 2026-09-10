# Planner UI Design and Boundaries

This record preserves the UI direction and the narrower oil-demo refactor
constraints from the [UI design][ui-design] and [execution plan][ui-plan] dated
2026-02-17. Project authors maintain it for UI implementers and reviewers. It is
design context, not a claim that every proposed interaction has shipped.
Work authorization and generic review procedure come from the
[repository record system](../../../../../../docs/governance/record-system.md)
and [Delivery Wave](../../../../../../docs/delivery-wave.md).

## Existing Implementation and Scope Boundary

The source at the migration baseline has an objective bar, recipe blocks, and
results rendering in
[`views/app_view.py`](../../src/factorio_cycle_calculator/views/app_view.py).
`render_chain` calls `vm.solve` during rendering. That observation does not
establish the explicit Solve action and dirty-state interaction proposed below.

The narrower refactor design preserves:

- the fixed advanced-oil-processing, heavy-oil-cracking, and light-oil-cracking
  chain;
- machine selection, module and module-count selection, beacon module and count
  selection, petroleum-gas target and rate units, and integer machine counts;
- service/viewmodel solving logic, data loading paths, warning/error semantics,
  numeric meaning, machine counts, and products/byproducts/ingredients;
- the crude-minimization baseline, with water treated as a free input.

Graph editing, arbitrary recipe nodes, scenario save/load, apply-to-all presets,
new optimization objectives, and new backend constraints are outside that
narrower refactor. The broader design and earlier recipe research discuss
possible extensions; they do not establish those extensions as current behavior.

## Design Rationale

The original demo distributed controls between sidebar and table rows, exposed
little objective hierarchy, and made the solve-on-render interaction hard to
read. The proposed planner layout draws on Factorio Planner, Helmod, and
factoriolab: define demand, configure production blocks, solve, then inspect
bottlenecks.

Use a medium-compact production sheet with icons and consistent numeric
formatting. Group local machine/module/beacon settings by recipe. Keep layout
stable between solves, avoid deep nesting, and put advanced controls in
expanders. Keep text fallbacks when icons are unavailable. Prefer the terms
Block, Target, Net Inputs, Net Outputs, and Machine Count. Feasible/optimal
results use a success treatment; infeasible/abnormal results need a visible,
actionable warning.

## Layout and Interaction

The broad design has four areas:

1. **Objective bar:** target product and rate, per-second/per-minute units,
   continuous/integer mode, and a proposed Solve/Recalculate action.
2. **Global settings:** data and asset paths, proposed global defaults for
   machines/modules/beacons, and advanced assumptions. The narrower refactor
   permits data-path controls to stay in the sidebar and excludes apply-to-all.
3. **Production blocks:** recipe icon/name/category; machine, modules, and
   beacons; computed machine count and local flows; optional collapsed
   ingredient/result details. The oil demo renders exactly three blocks in
   recipe order. Use `format_amount`, compact columns, stable widget keys, and
   icons with short labels.
4. **Results and diagnostics:** status and crude input rate, plus products,
   byproducts, and ingredients. The broader design discusses objective values,
   bottlenecks, slack, and power placeholders; the narrower refactor adds no
   KPIs or metric definitions.

The broader interaction proposal loads data, selects recipes, sets demand,
tunes blocks, explicitly solves, and inspects results. It proposes a dirty-state
badge for changed-but-unsolved controls, advanced settings collapsed by default,
and a sticky objective/result header if Streamlit supports it comfortably.
Auto-solve is described there as a future option. These are proposals requiring
reconciliation with the existing solve-on-render implementation, not permission
to change its behavior during a layout-only refactor.

## Component Boundaries

Keep math in `services/solver_service.py` and `services/catalog_service.py`,
orchestration in `viewmodels/app_viewmodel.py`, and rendering/UI state in
`views/app_view.py` and `views/types.py`. The original composition proposed
objective, global-settings, production-block, result-overview, and diagnostics
render functions. UI-facing types may be extended only when needed.

The layout refactor's dependency order is to extract render functions and layout
constants without changing output, move objective controls while preserving
`SidebarSettings` semantics/defaults, group recipe controls, then refine icons
and summary spacing. Preserve widget keys unless unavoidable. A need to change
solver or viewmodel behavior requires separate technical review of the scope.
Branch creation, agent assignment, and PR slicing belong to the work carrier.

## Validation Basis

The original design's usability targets remain unverified design criteria:
understand the workflow within one minute without documentation; keep the
objective, solve state, and key outputs within one desktop screenful; group
recipe controls visibly; and keep results stable and interpretable across
iterations without changing solving or data-loading behavior.

For the narrower layout refactor, compare a full-page view, one recipe block,
and the results area before and after. Attach the screenshots and actual results
to the reviewing PR rather than prescribing a permanent screenshot directory.
The retained manual regression matrix is:

1. Record default machine counts and crude input.
2. Toggle integer mode and inspect status/results.
3. Change petroleum gas from 900 to 1800 per minute and check the expected
   monotonic machine-count response.
4. Switch to per-second units and check conversion.
5. Change module/beacon selections and check updates and errors.

Review all prior selectors, exactly three recipe entries, flow completeness,
icon failure text fallbacks, unchanged warnings/errors, and equivalent outputs
for identical inputs. These checks are inherited validation guidance, not
results of a migration-time app run.

## Open Design Scope

The broad UI direction also proposed block quick actions (copy/reset), global
defaults, objective-component/constraint diagnostics, a generalized block list,
and scenario state. Its motivating risks were Streamlit layout limitations,
mixing rendering and solver logic, and over-design before a general-chain
backend exists. Favor simple columns/cards/expanders and preserve component
boundaries. Full graph editing, parity with mature planner mods, and objective
redesign were explicitly excluded from its v1 plan.

An owner decision is still needed before treating the broader interactions as
product scope. In particular, the original explicit-solve proposal and the
narrower behavior-preserving refactor must not be silently combined. The
[recipe research](../research/factorio-recipe-data-analysis.md) also discusses
raw-material and machine-count objectives beyond the current crude-only
implementation; it remains research, not a replacement solver contract.

[ui-design]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/private/app/factorio-cycle-calculator/.AGENT/docs/UI_DESIGN_v1.md
[ui-plan]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/private/app/factorio-cycle-calculator/.AGENT/docs/UI_PHASE1_EXECUTION_PLAN.md
