# Game and save format summary

This summary restates verified, non-sensitive facts from
`../plans/celesphonia-modifier-plan.md`. Use the main plan for full evidence,
compatibility, and acceptance details.

## Verified baseline

- The plan baseline is Magical Girl Celesphonia v1.05 on Steam App ID `1786790`,
  Steam build `13624401`, and database `versionId` `2444532`.
- The verified installation family uses RPG Maker MV 1.6.1 and likely NW.js 0.29.0 x86.
- The observed installation language can differ from `System.locale`.
  `System.locale` is not a reliable installation-language or app-language detector.

## Save discovery and path resolution

- Installation recognition and writable compatibility are separate decisions.
- Discovery checks the Kagura Games registry key, Steam library manifests, and a user-selected
  game folder.
- For the verified baseline, the active save directory is `<install>\save`.
- `<install>\www\save` is not the active path on the verified baseline because a save-path
  relocation plugin changes the location.
- Save-path resolution is a separate step: parse the recognized plugin when possible, otherwise
  keep behavior read-only unless a fallback boundary is separately qualified.

## File families and encoding

- The save set can include `file1.rpgsave` through `file20.rpgsave`, `global.rpgsave`,
  `config.rpgsave`, game-generated `.bak` files, and `steam_autocloud.vdf`.
- Slot numbering can be sparse. Cataloging must enumerate actual files instead of assuming a
  contiguous range.
- Files use RPG Maker MV LZ-String `compressToBase64`.
- `config.rpgsave` and `global.rpgsave` decode to ordinary JSON.
- Slot saves decode to RPG Maker JsonEx object graphs that use `@`, `@c`, `@a`, and `@r`.
- The verified JavaScript decoder is the reference for byte-identical codec behavior.

## Document roles

- `fileN.rpgsave` holds the slot object graph for one save slot.
- `global.rpgsave` stores selected-entry metadata that remains coupled to the slot set.
- `config.rpgsave` is read-only in MVP and requires its own later evidence gate and
  N-participant transaction design before any write support.

## Known top-level slot sections

- Required slot roots are `system`, `screen`, `timer`, `switches`, `variables`,
  `selfSwitches`, `actors`, `party`, `map`, `player`, and `saveParams`.
- Variables are heterogeneous. Property presence, order, wrappers, identities, references,
  unknown fields, and optional values are compatibility data.

## Key known couplings

- Save, RestoreSlot, and ReconcilePair are defined over one slot plus the current
  `global.rpgsave`, not over an isolated slot file.
- The current `global.rpgsave` is never rebuilt from a DTO, and updates are limited to proven
  selected-entry paths.
- Gold evidence currently depends on both `party._gold` and variable `215`.
- Future config editing remains blocked because coupled slot leaves still require dedicated,
  proven synchronization, including switches `40` and `66` where applicable.

## Safe-write principles

- A semantic no-op must return the original compressed bytes.
- Allowed edits must preserve unknown data, ordering, wrappers, identities, references, and
  untouched lexemes.
- Writable authority requires a matching compatibility fingerprint and E3-qualified operation
  evidence. Recognition, backups, or user confirmation do not replace that gate.
- The immutable session baseline is established by Open, explicit Reload, or the start of a
  confirmed Restore or Reconcile transaction.
- No live file is deleted before replacement, and writes are designed as recoverable
  transactions over the slot/global pair.
- MVP writes are limited to a release-qualified local fixed-NTFS profile.

## Supported product domains

- The plan defines UI domains for Overview, Character, Progression, Exploration Status,
  Combat & Skills, Memory Engrams, Equipment & Outfits, Inventory & Currency,
  Missions & Titles, Collections, Difficulty, Mature Status, and Diagnostics.
- MVP write intent is intentionally narrow: Gold is required after E3 qualification,
  ordinary inventory is optional after separate E3 qualification, and other domains remain
  read-only unless they earn their own approved packets.

## Unknowns and evidence gates

- Observed leaves for EXP, inventory, equipment, map state, switches, variables, quests,
  collections, and plugin state do not grant write authority by themselves.
- Only E3, release-qualified operations are writable on a matching compatibility fingerprint.
- Open research includes save-path relocation variants, localized-compatible probes,
  per-domain evidence packets, and additional filesystem or volume-profile qualification.

For authoritative wording and the full gate set, see
`../plans/celesphonia-modifier-plan.md`.
