# Factorio Cycle Calculator

_Factorio Cycle Calculator_ is a steady-state calculator for production chains that don’t form a simple tree—especially **recycling loops** and **multi-output bottlenecks**.

In _Factorio: Space Age_, the _Recycler_ can convert most items back into the ingredients of their crafting recipe, but it is lossy (you lose 75% of the ingredients on average). That means “craft → recycle → craft” creates real feedback loops that a normal “expand the recipe tree” calculator can’t balance cleanly.

Cycles become even more important on _Fulgora_, where scrap is a primary resource and recycling it yields a probabilistic mix of many different items. The scrap recycling outputs add up to 60% on average, so a full belt of scrap becomes ~60% belt of products, and many of those products are then recycled again to reach basic ingredients.

The base game also has classic “cycle-like” balancing problems, such as advanced oil processing producing multiple fluids (heavy/light/petroleum) where production can stall if any output backs up, and cracking is used to keep the system flowing.

This project models your factory as a flow network and solves the balance equations for a stable throughput, so you can reason about:

1. recycling loops (lossy reverse crafting via _Recycler_)
2. scrap recycling chains and downstream recycling decisions on _Fulgora_
3. multi-output systems like oil processing that can deadlock when outputs fill

## Example app (Streamlit)

An initial Streamlit prototype is available in `app.py`. It focuses on the
advanced oil processing chain and uses Google OR-Tools to compute machine
counts given a petroleum gas demand and the chosen machine/effect settings.

Run the app after installation with the script entry point:

- `uv run --project src/private/app/factorio-cycle-calculator factorio-cycle-calculator`

## Schema and generated models

The generated models live in
`src/factorio_cycle_calculator/generated/data_raw_models.py`.

Model generation is driven entirely by a mise task that pulls the schema from
the upstream URL and does not store a local JSON schema file.

Generate models:

- `mise //src/private/app/factorio-cycle-calculator:schema:generate`
