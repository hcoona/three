# Factorio data-raw-dump.json analysis

Date: 2026-02-11

## Scope and sources

- Analyzed file: `/mnt/c/Users/zhang/AppData/Roaming/Factorio/script-output/data-raw-dump.json`
- File size: ~27.8 MB
- Reference docs:
    - Factorio data.raw schema project (README): https://github.com/jacquev6/factorio-data-raw-json-schema
    - Factorio Lua API (Data::raw / AnyPrototype / Data lifecycle): https://lua-api.factorio.com/latest/types/Data.html#raw
    - Factorio modding overview and data.raw listing: https://wiki.factorio.com/Modding
    - data.raw listing of built-in prototypes: https://wiki.factorio.com/Data.raw

Note: The raw full JSON schema file is available at the URL below, but the fetch attempt could not extract content in this environment (likely due to size). Use it if you want schema validation.

- https://raw.githubusercontent.com/jacquev6/factorio-data-raw-json-schema/refs/heads/main/factorio-data-raw-json-schema.full.json

## High-level structure (matches Lua API documentation)

The dump is the JSON serialization of `data.raw`, which is documented as:

```lua
raw :: dictionary[string -> dictionary[string -> AnyPrototype]]
```

In practice, the file is a dictionary whose keys are prototype type names (e.g., `item`, `recipe`, `technology`). Each value is a dictionary from prototype name to prototype object.

### Structural checks

- Top-level categories: **251**
- Total prototype objects: **5137**
- All top-level values are JSON objects.
- Every prototype has both `name` and `type` fields.
- The `name` field matches its dictionary key.
- The `type` field matches its category key.

This consistency indicates a clean dump and makes it safe to treat `(category, name)` as a unique identifier.

## Distribution of prototypes

Top 20 categories by number of prototypes:

| Rank | Prototype type         | Count |
| ---: | ---------------------- | ----: |
|    1 | optimized-particle     |   845 |
|    2 | recipe                 |   659 |
|    3 | noise-expression       |   504 |
|    4 | technology             |   275 |
|    5 | item                   |   241 |
|    6 | explosion              |   225 |
|    7 | corpse                 |   177 |
|    8 | optimized-decorative   |   160 |
|    9 | virtual-signal         |   155 |
|   10 | tile                   |   150 |
|   11 | item-subgroup          |   136 |
|   12 | smoke-with-trigger     |   101 |
|   13 | delayed-active-trigger |   100 |
|   14 | ambient-sound          |    95 |
|   15 | tips-and-tricks-item   |    81 |
|   16 | trivial-smoke          |    67 |
|   17 | segment                |    60 |
|   18 | noise-function         |    48 |
|   19 | sprite                 |    44 |
|   20 | simple-entity          |    41 |

### Singleton categories

There are **119** categories with exactly one prototype. Examples include:
`accumulator`, `achievement`, `beacon`, `character`, `character-corpse`, `map-settings`, `map-gen-presets`, `rocket-silo`, `space-platform-hub`, `surface`, `utility-constants`.

This is normal for “global” or “singleton” systems (map settings, GUI style, utility constants, etc.).

## Example prototype names (samples)

A few representative names by category:

- `item`: accumulator, active-provider-chest, advanced-circuit, agricultural-tower, assembling-machine-1, assembling-machine-2
- `recipe`: accumulator, accumulator-recycling, acid-neutralisation, advanced-circuit, advanced-oil-processing
- `technology`: advanced-asteroid-processing, advanced-circuit, advanced-material-processing, agriculture, artillery
- `fluid`: ammonia, ammoniacal-solution, crude-oil, fluoroketone-cold, heavy-oil
- `tile`: acid-refined-concrete, ammoniacal-ocean, artificial-jellynut-soil, brash-ice, concrete

## Observations and domain notes

1. **Space Age content is present.** Categories and names such as `space-platform-hub`, `space-connection`, `planet`, and `quality` indicate the Space Age mod is active in this dump (consistent with the wiki’s data.raw listing for 2.0.65 + Space Age).

2. **Very large “content” categories.** Particles, recipes, noise expressions, and technologies dominate the size. Tools should expect these to be the biggest memory/time drivers.

3. **Type system is stable but dynamic.** The schema project notes that `data-raw-dump.json` is large, uses dynamic typing, and includes quirks such as empty arrays serialized as `{}`. It also recommends lenient number handling (integers can be floats in practice) and allowing additional properties for forward compatibility.

4. **data.raw is data-stage only.** The `data` table is populated during the prototype stage (data.lua, data-updates.lua, data-final-fixes.lua) and then frozen. This dump is a snapshot after the data stage has completed.

## Practical implications for tooling

- **Treat `(type, name)` as a stable key.** It is consistent in this dump.
- **Plan for scale.** Thousands of entries and deep nested objects are normal.
- **Be lenient with numeric types.** Many fields documented as integers appear as floats in practice.
- **Allow unknown properties.** The schema project explicitly allows additional properties for compatibility.
- **Handle array/object quirks.** Some arrays may appear as `{}` in JSON output; tooling should normalize these to empty arrays when needed.

## Follow-up ideas

- Validate against the full JSON schema or generate a partial schema for specific domains (e.g., items/recipes only) to simplify downstream typing.
- Build a per-category “field histogram” (top properties and type variability) to identify dynamic fields.
- Normalize known quirks (empty arrays as `{}`) before processing.
