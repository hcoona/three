# Addendum: machine speed, 900 petroleum gas/min example, and test script

Date: 2026-02-11

## Machine speed impact (same recipe, different machines)

Given a recipe with `energy_required` and a machine with `crafting_speed`, the per-machine throughput is:

- `effective_crafting_speed = crafting_speed * (1 + speed_bonus)`
- `cycle_seconds = (energy_required or 0.5) / effective_crafting_speed`
- `output_rate = result_amount / cycle_seconds`
- `input_rate = ingredient_amount / cycle_seconds`

Productivity applies only when:

- the recipe allows it (`allow_productivity == true`),
- the machine allows it (`allowed_effects` includes `productivity`), and
- the result is not marked `ignored_by_productivity`.

This is why the same recipe can yield different rates across different machines: the difference is strictly due to `crafting_speed` and module/base effects.

## Example: 900 petroleum gas per minute

Target: 900 petroleum gas/min = 15 petroleum gas/s.

Using only the advanced oil processing chain (no coal liquefaction), with the dump values:

- `advanced-oil-processing` (oil refinery): 5 s → heavy-oil 25, light-oil 45, petroleum-gas 55
    - per refinery: 5 HO/s, 9 LO/s, 11 PG/s
- `heavy-oil-cracking` (chemical plant/biochamber): 2 s → light-oil 30
    - per plant: consumes 20 HO/s, produces 15 LO/s
- `light-oil-cracking` (chemical plant/biochamber): 2 s → petroleum-gas 20
    - per plant: consumes 15 LO/s, produces 10 PG/s

Let A = refineries, H = heavy cracking, L = light cracking.

Steady-state with no leftover fluids:

- HO balance: `5A - 20H = 0` → `H = 0.25A`
- LO balance: `9A + 15H - 15L = 0` → `L = 0.6A + H = 0.85A`
- PG rate: `PG = 11A + 10L = 19.5A`

To reach 15 PG/s:

- `A = 15 / 19.5 = 0.76923`
- `H = 0.19231`
- `L = 0.65385`

This is the fractional solution. In an integer program, you can:

1. keep the balance constraints as inequalities (allow leftovers), or
2. enforce exact balance and allow overproduction with a penalty, or
3. relax integer constraints for early planning, then round and re-optimize.

Example integer reference:

- `A = 1` (no cracking) gives 11 PG/s = 660 PG/min (short of target).
- `A = 2` (no cracking) gives 22 PG/s = 1320 PG/min (over target).

If you enforce zero leftovers and integer counts, you must scale the ratio 20:5:17 (from the wiki) or accept overproduction with a penalty term.

## Icon + localization test script

Script path:

- `src/private/app/factorio-cycle-calculator/.AGENT/scripts/check_icons_and_locale.py`

It validates:

- icon paths for selected recipes, fluids, items, and machines
- PNG dimensions (if available)
- localization strings from `data/<mod>/locale/<lang>/*.cfg`

Usage (example):

- `python check_icons_and_locale.py --data-dir /mnt/c/Program\ Files/Factorio/data`
- `python check_icons_and_locale.py --data-raw /mnt/c/Users/zhang/AppData/Roaming/Factorio/script-output/data-raw-dump.json --data-dir /mnt/c/Program\ Files/Factorio/data --locale en`

## Notes on missing item/entity localization and subgroup icons

The missing locale entries reported by the script are expected and consistent
with how Factorio data is organized:

- Placeable buildings often only have `entity-name` localization entries. The
  corresponding `item-name` can be missing (for example, `oil-refinery`,
  `chemical-plant`, `biochamber`). For UI labels, prefer `item-name` and fall
  back to `entity-name` when the item key is not present.
- `item-subgroup` entries are internal categorization metadata. They frequently
  have no localization entry and no icon. If you need a label or icon, prefer
  the parent `item-group` or fall back to the raw subgroup name.
