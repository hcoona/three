# Factorio recipe data analysis for an IP-based calculator

Date: 2026-02-11

## Goal

Build a Factorio recipe calculator using integer programming (IP) to find machine counts that meet production targets while minimizing:

1. total raw materials consumed and
2. total machine count.

The app should allow users to choose which machine(s) can produce each recipe and must include UI icon data for items, fluids, recipes, and machine choices.

## Primary data sources

- `data-raw-dump.json` (from `factorio --dump-data-raw`)
- Factorio Lua API docs for `data.raw` structure
- Factorio Wiki for cross-checking recipes and machine roles

Relevant pages used for verification (oil processing chain example):

- https://wiki.factorio.com/Oil_processing#Overview
- https://wiki.factorio.com/Oil_refinery
- https://wiki.factorio.com/Chemical_plant
- https://wiki.factorio.com/Biochamber
- https://wiki.factorio.com/Crude_oil
- https://wiki.factorio.com/Heavy_oil
- https://wiki.factorio.com/Light_oil
- https://wiki.factorio.com/Petroleum_gas
- https://wiki.factorio.com/Oil_processing_(research)
- https://wiki.factorio.com/Advanced_oil_processing_(research)

## Data model needed by the calculator

### 1) Recipes

Required fields (from `recipe` prototypes):

- `name`, `category`, `energy_required`
- `ingredients[]` (see key set below)
- `results[]` (see key set below)
- `enabled` (early access before tech)
- `allow_productivity`, `allow_quality`
- `subgroup`, `order` (UI grouping)
- `surface_conditions` (Space Age constraints)

Observed ingredient keys across all recipes:

- `amount`, `name`, `type`, `fluidbox_index`, `fluidbox_multiplier`, `ignored_by_stats`

Observed result keys across all recipes:

- `amount`, `name`, `type`, `probability`, `extra_count_fraction`, `temperature`, `percent_spoiled`,
  `fluidbox_index`, `ignored_by_productivity`, `ignored_by_stats`, `show_details_in_recipe_tooltip`

Notes:

- 63 recipes in this dump have `energy_required == null`. Use the Factorio default of 0.5 seconds when missing. This is recipe time (the wiki shows it as the "Time" column), not power usage; energy consumption comes from the machine `energy_usage` field.
- Results with `probability` should be handled via expected value (or explicit stochastic modeling). `ignored_by_productivity` disables productivity scaling for that result.
- `surface_conditions` exist for 36 recipes and should be used to constrain availability by planet/surface.

### 2) Machines (crafting entities)

Main production buildings live under `assembling-machine` prototypes (also includes oil refinery, chemical plant, biochamber, etc.).

Required fields:

- `name`, `crafting_categories[]`, `crafting_speed`
- `energy_usage`, `energy_source.type` (electric/burner/heat/void)
- `module_slots`, `allowed_effects`
- `effect_receiver.base_effect` (for built-in productivity bonuses)
- `ingredient_count` (hard limit if present)
- `fixed_recipe` (if present, restricts to one recipe)

Notes:

- `allowed_effects` must include an effect before a module can apply it.
- `effect_receiver.base_effect.productivity` exists on `biochamber` (0.5 in this dump).
- Some machines are burner-powered (e.g., biochamber uses fuel category `nutrients`). If modeling fuel usage, read `energy_source.fuel_categories`.

Throughput impact of machine speed:

- Effective craft time per cycle: `cycle_seconds = (energy_required or 0.5) / effective_crafting_speed`
- Effective speed: `effective_crafting_speed = crafting_speed * (1 + speed_bonus)`
- Per-second rates: `rate = amount / cycle_seconds`
- Productivity applies only when both the recipe and the machine allow it (and to results not marked `ignored_by_productivity`).

### 3) Technologies (unlock gating)

From `technology` prototypes:

- `effects[]` with `{type: "unlock-recipe", recipe: "..."}`
- `prerequisites` and `unit` or `research_trigger`

Notes:

- `oil-processing` in this dump is triggered by mining crude oil (`research_trigger`), not a science pack unit.
- Use tech effects to filter which recipes are available in a given tech state.

### 4) Items and fluids

Items (`item`) and fluids (`fluid`) are used to:

- resolve ingredient/result identities
- display UI icons
- apply stack size / energy / fuel logic if needed

Useful fields:

- `item`: `stack_size`, `fuel_value`, `fuel_category`, `place_result`, `subgroup`, `order`, `icon`/`icons`
- `fluid`: `default_temperature`, `max_temperature`, `heat_capacity`, `base_color`, `flow_color`, `icon`/`icons`

### 5) Modules

From `module` prototypes:

- `category`, `tier`, `effect` (speed/productivity/consumption/pollution/quality)
- optional `limitation` / `limitation_blacklist` (none in this dump)

## UI data requirements

### Icons

Sources:

- `icon` or `icons[]` (IconData) fields on `item`, `recipe`, `fluid`, `item-group`, `item-subgroup`, and some entities.

Findings from this dump:

- Many `item` and `fluid` entries have `icon` but **no `icon_size`**.
- A large portion of recipes use `icons[]` (layered icons). IconData entries also often lack `icon_size`.

Recommendation:

- When `icon_size` is missing, read the PNG image dimensions directly.
- Support layered icons: apply `tint`, `scale`, `shift` if present in IconData.

Path resolution:

- Icon paths use mod tokens, e.g., `__base__/graphics/icons/...` or `__space-age__/...`.
- Resolve to the Factorio install data directory: `data/<mod-name>/...`.

### Localization

`localised_name` is `null` for many prototypes in this dump.

Recommendation:

- Load locale files from mods: `data/<mod>/locale/<lang>/...` to map internal names to display strings.
- Use internal names as fallback if locale resolution is missing.

### Grouping and ordering

Use `item-group` and `item-subgroup` prototypes:

- `item-subgroup.group` → `item-group.name`
- `order` fields for sorted display

Example:

- Recipe subgroup `fluid-recipes` belongs to item group `intermediate-products`.

## How to extract the data (examples)

All extraction should be done against `data-raw-dump.json` without loading the full file into memory at once.

Examples using jq (safe for large files):

- Recipe fields: `jq '.recipe["advanced-oil-processing"]' data-raw-dump.json`
- Machine summary: `jq '."assembling-machine"["chemical-plant"] | {crafting_categories, crafting_speed, module_slots, allowed_effects}'`
- Category-to-machine mapping: filter `assembling-machine` by `crafting_categories`.
- Ingredient/result schema: aggregate unique keys across all recipes.

If using Python:

- Stream parse with `ijson` or incremental JSON readers.
- Extract only the sections needed (recipes, machines, items, fluids, technologies, modules).

## Test case: advanced oil processing → cracking → petroleum gas

This chain is sufficient to test production math and UI rendering.

### Recipes (from this dump)

1. `advanced-oil-processing`
    - Category: `oil-processing`
    - Time: 5 s
    - Ingredients: water 50, crude-oil 100
    - Results: heavy-oil 25, light-oil 45, petroleum-gas 55
    - `allow_productivity`: true, `allow_quality`: false

2. `heavy-oil-cracking`
    - Category: `organic-or-chemistry`
    - Time: 2 s
    - Ingredients: water 30, heavy-oil 40
    - Results: light-oil 30
    - `allow_productivity`: true, `allow_quality`: false

3. `light-oil-cracking`
    - Category: `organic-or-chemistry`
    - Time: 2 s
    - Ingredients: water 30, light-oil 30
    - Results: petroleum-gas 20
    - `allow_productivity`: true, `allow_quality`: false

### Machines for those categories

- `oil-processing` → `oil-refinery` only
- `organic-or-chemistry` → `chemical-plant`, `biochamber`

Machine summaries (from this dump):

- `oil-refinery`: crafting_speed 1, module_slots 3, allowed_effects [consumption, speed, productivity, pollution]
- `chemical-plant`: crafting_speed 1, module_slots 3, allowed_effects [consumption, speed, productivity, pollution, quality]
- `biochamber`: crafting_speed 2, module_slots 4, allowed_effects [consumption, speed, productivity, pollution, quality],
  base productivity via `effect_receiver.base_effect.productivity = 0.5`, fuel category `nutrients`

### Tech gating

From technology prototypes:

- `oil-processing` unlocks: `oil-refinery`, `chemical-plant`, `basic-oil-processing`, `solid-fuel-from-petroleum-gas`
- `advanced-oil-processing` unlocks: `advanced-oil-processing`, `heavy-oil-cracking`, `light-oil-cracking`,
  `solid-fuel-from-heavy-oil`, `solid-fuel-from-light-oil`

### Feasibility for IP model

All required data is present in the dump:

- exact IO amounts
- craft times
- machine speeds
- category-to-machine mapping
- module effects and base productivity
- tech gating if needed
- UI icon paths for recipes/items/fluids

This is sufficient to model the chain and verify that the calculator can:

1. match a target petroleum-gas output, and
2. minimize raw inputs (crude oil + water) and machine counts.

## Recommended data pipeline

1. Extract and cache:
    - `recipes`, `machines`, `items`, `fluids`, `technologies`, `modules`, `item-group`, `item-subgroup`
2. Build lookup indexes:
    - recipe → ingredients/results
    - recipe → category
    - category → machines
    - item/fluid → icon path + colors
    - tech → unlocked recipes
3. For UI:
    - icon path resolution to mod data directory
    - locale string resolution from mod locale files
4. For the solver:
    - convert each recipe/machine option into a linear production rate
    - apply module/base effects where allowed
    - treat probabilistic results as expected value unless a more complex model is desired

## Known gaps and handling

- Missing `icon_size` on most items/fluids/recipes: read PNG dimensions at runtime.
- Localization mostly absent in the dump: use locale files.
- Some recipes have `surface_conditions`: enforce planet constraints.
- Recipes with `energy_required == null`: use default 0.5 s.
