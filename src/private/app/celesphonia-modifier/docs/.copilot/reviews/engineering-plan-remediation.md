# Engineering-plan integration remediation v2

This text is insertion-ready for the current plan. It supersedes conflicting language in Sections 3, 4, 5.4, 7.4–7.5, 8.8, 10.2, 11.6, 12, 13, 14, and 16 without changing the Gold-first scope.

## 1. Separate installation recognition from writable compatibility

Replace Sections 3.1 and 3.2 with the following.

### 3.1 Discovery and installation recognition

Discovery answers only whether a folder is a recognized Magical Girl Celesphonia installation. It does not decide whether any save is writable.

Discovery order remains:

1. Read `HKLM\SOFTWARE\WOW6432Node\Kagura Games\Magical Girl Celesphonia`.
2. Discover Steam libraries and parse `appmanifest_1786790.acf`.
3. Ask the user to choose the game folder.

A candidate is recognized when accessible installation evidence identifies the title:

- `Game.exe`, `package.json`, and `www\data\System.json` exist and are regular files under the canonical installation root;
- Steam App ID `1786790`, when Steam metadata is present, or the verified game/package/database identity proves the same title;
- package main-entry and database structure are consistent with an RPG Maker MV installation for this game.

Recognition does **not** require:

- a supported build or database `versionId`;
- a known executable, database, or plugin hash;
- a resolvable writable adapter;
- an E3 operation capability;
- an unmodified plugin set.

Discovery returns a `RecognizedInstallation` containing canonical paths, recognition evidence, and recognition warnings. A decoy or ambiguous folder is rejected as unrecognized; an unknown, newer, localized, or modded but positively identified game remains recognized.

Save-path resolution is evaluated separately:

- A recognized installation may use a safely parsed save-path plugin or an adapter-declared read-only fallback.
- If the active path cannot be established safely, retain recognition, show why the catalog is unavailable, and allow the user to select a candidate save directory for bounded read-only inspection.
- Manual selection never creates a writable binding. It is limited to regular `global.rpgsave`, `config.rpgsave`, `file1.rpgsave` through `file20.rpgsave`, and game `.bak` candidates in the selected directory.

### 3.2 Read-only installation session and writable catalog binding

Use distinct types and state:

| State | Meaning | Allowed behavior |
|---|---|---|
| Unrecognized | Game identity is absent or ambiguous. | Refuse catalog binding; allow folder validation help only. |
| Recognized, catalog unavailable | Game identity is known, but no safe save directory or bounded parse path is available. | Installation diagnostics and manual read-only save-directory selection. |
| Recognized read-only | Save directory is safe to enumerate and files parse within the read-only profile, but writable compatibility is unknown, newer, modded, incomplete, or failed. | Bounded slot catalog, safe previews, read-only domain summaries, original-byte export, and redacted diagnostics. |
| Writable compatible | Recognition succeeded and every writable fingerprint, adapter, capability, global-derivation, baseline, process, and transaction precondition passed. | Create a `WritableCatalogBinding` and expose only qualified operations. |

A `RecognizedInstallation` can never be used directly by Save, Restore, or an edit operation. `IWritableBindingFactory` accepts only a recognized installation plus:

- resolved active save directory;
- existing catalog slot and current global;
- exact or adapter-declared compatible writable fingerprint;
- qualified adapter and operation capability;
- exact global preview derivation;
- local backup/recovery store;
- fresh slot/global baselines and all write preconditions.

The type boundary must make it impossible for user confirmation, a manual path, or recognition status alone to create a writable binding.

Unknown/newer/modded fingerprints may expose the read-only catalog only when:

1. enumeration stays within the resolved or user-selected directory and the fixed slot-role allowlist;
2. compressed input, decompression, parsing, graph resolution, and preview extraction stay within the reviewed read-only resource profile;
3. no unsafe type activation or adapter-dependent interpretation is required;
4. failures are isolated per file and no partial document becomes editable.

If those conditions fail, show recognized-installation diagnostics without opening the affected save. No unsupported case exposes Save, Restore, edited-copy export, or semantic edit controls.

### Workflow wording updates

In Section 7.4, replace “Show one supported installation” with:

> Show each recognized installation with separate **Recognized game** and **Write support** status. Offer **Open saves read-only** when bounded cataloging is safe. Offer writable actions only after a `WritableCatalogBinding` exists.

In Section 7.5, replace the first four steps with:

1. Recognize the installation without requiring writable compatibility.
2. Resolve or safely select the save directory and build a bounded read-only catalog when possible.
3. Open the selected slot/global read-only and evaluate the writable compatibility fingerprint and capabilities.
4. Create a `WritableCatalogBinding` only after all write gates pass; otherwise retain the read-only session and explain the failed gate.

### Recognition and compatibility acceptance

- Exact baseline recognition plus writable binding succeeds.
- Recognized unknown build, newer database, changed relevant plugin, extra mod plugin, and changed plugin parameter can open a bounded read-only catalog when their files are structurally safe.
- The same cases never create `WritableCatalogBinding`, even after confirmation or manual directory selection.
- A recognized installation with an unresolvable save path retains diagnostics and can accept a manually selected read-only directory.
- Malformed, over-limit, inaccessible, aliasing, or unsafe files fail individually without enabling editing.
- A folder with copied RPG Maker-shaped files but insufficient game identity is unrecognized.
- Command/view-model/UIA tests prove that read-only catalog sessions have no Save, Restore, edited-copy, or edit-control path.

Add to Section 4.2:

> Writable compatibility is evaluated only after installation recognition. Recognition is neither a fingerprint component nor evidence of write support.

## 2. WinUI globalization and resource packaging

Augment Section 10.2 with the following.

The repository root sets `InvariantGlobalization=true`. The WinUI project must override it because the application selects Windows language-qualified resources and formats localized UI:

```xml
<PropertyGroup Label="Globalization">
  <InvariantGlobalization>false</InvariantGlobalization>
  <DefaultLanguage>en-US</DefaultLanguage>
  <NeutralLanguage>en-US</NeutralLanguage>
  <SatelliteResourceLanguages>en-US;zh-Hans</SatelliteResourceLanguages>
</PropertyGroup>
```

`en-US` is the complete default and neutral fallback. Simplified Chinese uses the canonical `zh-Hans` resource qualifier and serves `zh-CN`, `zh-SG`, and other `zh-Hans-*` preferences through Windows resource matching. App-owned WinUI strings live in:

- `Strings\en-US\Resources.resw`;
- `Strings\zh-Hans\Resources.resw`.

Every app-owned resource exists in `en-US`. All MVP commands, validation, recovery, privacy, install/update guidance, and safety-critical errors must also exist in `zh-Hans`; release cannot rely on English fallback for a missing safety string. Unsupported languages and `zh-Hant` fall back to `en-US` under the product policy. Game-provided labels remain independent and are never treated as app resources.

`SatelliteResourceLanguages` limits publish output to the declared app languages. It must not strip the MRT Core language-qualified resources or `resources.pri`. Non-UI projects may retain the repository default unless a separately reviewed requirement needs culture data.

### Globalization and resource acceptance

- An MSBuild evaluation test fails if `CelesphoniaModifier.WinUI` resolves `InvariantGlobalization` to anything other than `false`, or if the default, neutral, and satellite language properties differ from the values above.
- A release-build smoke test constructs and formats with `en-US` and `zh-CN` cultures, proving the runtime is not invariant.
- A resource-contract test compares resource keys: `en-US` is complete; `zh-Hans` contains every release-required and safety-critical key; no value is empty or still a localization placeholder.
- The publish-output test verifies both language qualifiers and the compiled resource index are present before installer creation.
- The signed installer test verifies installation preserves the same resource index/language payload byte-for-byte from the approved publish staging area.
- UIA runs on clean Windows user profiles for `en-US`, `zh-CN`, `zh-SG`, an unsupported language, and `zh-Hant`. Expected results are English, Simplified Chinese, Simplified Chinese, English fallback, and English fallback respectively.
- Mixed preferred-language ordering, restart behavior, 225% text scaling, accessible names, dialogs, recovery pages, and installer-facing guidance are verified in both shipped languages.
- Packaging fails if a satellite resource is omitted, an unexpected language is included, or installed resource lookup returns a missing-key placeholder.

Update Section 8.8 to name `zh-Hans` as the stored Simplified Chinese resource qualifier while retaining its existing `zh-CN`/`zh-SG`/`zh-Hans-*` matching behavior.

## 3. Minimum parser-calibration hardware and load profile

Replace the calibration procedure in Section 5.4 with the following.

Concrete parser limits remain non-production until measured on the minimum supported benchmark baseline and the representative worst-case corpus.

### Minimum benchmark baseline

| Dimension | Required baseline |
|---|---|
| CPU | x64 Intel Core i5-8250U-class or AMD Ryzen 5 3500U-class processor, no faster than the selected reference machine, with at least 4 physical cores and 8 logical processors. Record model, microcode, core count, and power plan. |
| RAM | 8 GiB installed. The loaded run must begin with at least 2 GiB available and must not use a page file hosted on faster storage than the measured application volume. |
| Storage | Local NTFS SATA SSD; no NVMe-only baseline, RAM disk, network share, compressed/encrypted test folder, or warmed synthetic memory file system. Record model, firmware, free space, and volume allocation unit. |
| OS/runtime | Oldest Windows build claimed by the release, x64, fully patched for that support baseline; shipped self-contained .NET/Windows App SDK versions; Release `win-x64` publish output; no debugger. If Windows build `17763` remains supported, it is part of calibration or the support floor must be raised explicitly. |
| Security/background services | Microsoft Defender real-time protection remains enabled. Exclude active OS update, indexing rebuild, backup, or antivirus full-scan bursts; such interference invalidates and repeats the run rather than silently enlarging limits. |
| Concurrent load | Run both idle and standardized loaded profiles. The loaded profile continuously occupies one logical processor and reserves memory until only 2–3 GiB remains available. The generator, version, affinity, and achieved CPU/memory range are recorded. |

The minimum supported hardware statement for users must be no lower than this benchmark baseline. A faster developer machine cannot substitute.

### Representative worst-case corpus

The calibration corpus contains, for every recognized read-only and writable baseline:

- largest compressed file;
- largest decompressed document;
- deepest valid graph;
- highest node, identity, reference, array, and total-array-element counts;
- widest valid object and largest valid scalar;
- slowest valid decode, parse, graph-resolution, and validation samples;
- generated combined near-limit cases that exercise multiple dimensions together;
- hostile limit-plus-one and cancellation cases.

Real corpus identities and hashes remain private under the fixture policy. The calibration report uses internal opaque case IDs.

### Measurement method

1. Build the exact signed-candidate Release `win-x64` publish output with production instrumentation disabled except approved stage counters.
2. Reboot the reference machine, allow startup activity to settle, verify the required free-memory/load state, and record OS, runtime, application commit/version, parser profile candidate, hardware, power plan, Defender state, and corpus revision.
3. Run each case in both idle and loaded profiles. Use separate cold-process and warm-process series: at least 5 cold-process measurements and 30 warm measurements per case after one discarded warm-up.
4. Measure decode, parse, graph resolution, validation, and cleanup separately using a monotonic high-resolution clock.
5. Record total allocated bytes and GC counts with runtime counters, and peak private bytes/working set with ETW or equivalent process counters. Timing runs must not attach a sampling profiler that materially changes results; diagnostic allocation traces are collected in a separate corroborating run.
6. Record median, p95, maximum, variance, cancellation latency, retained-memory delta after cleanup, and any paging or GC anomaly. Repeat a series whose environmental noise or outlier investigation shows invalid interference.
7. Select each compressed/decompressed/count/depth/time/memory cap above the largest supported case with explicit reviewed headroom demonstrated by generated boundary tests. Document why the selected headroom is sufficient; do not derive it from an unexplained multiplier.
8. Verify exactly-at-limit success and limit-plus-one refusal on the same baseline under standardized load.
9. Archive the report, raw benchmark output, profile ID, corpus revision, and approval. Recalibrate after parser/codec/runtime changes, accepted fingerprint changes, minimum-hardware changes, or statistically significant regression.

No `ParserLimitProfile` is marked production and no writable release proceeds until this benchmark artifact is approved. Read-only profiles are also measured and bounded; they may differ from writable profiles but cannot be unbounded.

### Parser benchmark release tests

- CI validates profile schema and boundary tests; scheduled/manual release hardware runs validate wall-clock and memory values.
- The release candidate's profile ID must match the approved benchmark artifact.
- A slower-than-baseline result, unexplained regression, paging failure, cleanup retention, or absent loaded-profile run blocks release.
- Resource-limit errors remain stage-specific, preserve no partial editable document, and are tested in recognized read-only sessions as well as writable candidates.

## 4. Recovery-aware installer, update, and uninstall

Add the following policy to Section 11.6 and replace Section 13.2 with the lifecycle rules below.

### Backward-compatible recovery reader policy

`journalVersion` is a durable on-disk recovery contract:

- Every release contains readers and recovery transition writers for every journal version emitted by any earlier released build.
- Minor-version additions are optional fields only and cannot change recovery meaning. A semantic change creates a new major journal version and retains the old implementation.
- Recovery never rewrites or “upgrades” an unresolved journal in place. It validates the original hash chain and appends transitions using that journal version's canonical encoder and state rules. New transactions use the current version.
- Unknown major versions, unknown required fields, invalid hash chains, unsupported state transitions, and ambiguous artifacts are preserved and treated as unresolved conflicts. The application blocks normal writes.
- Backward-reader support is not removed merely because a release is old; local unresolved journals have no reliable expiration date.
- Recovery inspection runs before normal document opening and before any new write. Actual live/artifact hashes remain authoritative.
- The installer preflight checker and application recovery engine share the same versioned reader library and golden vectors. Both components are signed.

Maintain a compatibility matrix for every released journal version, state, torn-final-record case, and target application version.

### Signed installer lifecycle preflight

The manual update installer, repair installer, and uninstaller perform a read-only preflight before changing binaries or registration:

1. Verify their own Authenticode signature and expected publisher.
2. Require the application process to be closed.
3. Scan only the app-owned journal roots, validate journal chains, and classify terminal versus unresolved without mutating save, journal, backup, stage, rollback, or evidence files.
4. Record only redacted counts, journal versions, and state categories in installer logs.

Behavior:

| Condition | Upgrade/update | Repair same version | Uninstall |
|---|---|---|---|
| No unresolved journal | Proceed normally. | Proceed. | Proceed; preserve backups/settings unless separately selected. |
| Readable unresolved journal | Block before changing binaries. Offer **Open recovery** and **Cancel**. | Allowed only to restore the same signed version's binaries; preserve all recovery data and force Recovery on launch. | Block. Offer **Open recovery** and **Cancel**. |
| Torn, corrupt, unknown-version, or inaccessible journal | Block with no override. Offer same-version repair when applicable and redacted diagnostic export. | Preserve all data; repair binaries only. | Block with no cleanup option. |
| All journals terminal | Proceed. Retention may later clean eligible app-owned artifacts. | Proceed. | Proceed; unresolved-data warning is not shown. |

There is no **Ignore**, **Force upgrade**, **Force uninstall**, or installer option that deletes unresolved recovery data.

After recovery reaches `Committed`, `RolledBack`, `Aborted`, or user-resolved `Conflict` with verified recorded resolution, the user reruns the installer/uninstaller. A repair must leave the installed version launchable so recovery can complete.

Upgrade, update, repair, and uninstall preserve unresolved journals and every referenced stage, rollback, evidence, backup, and manifest byte-for-byte. Cleanup selection is disabled while any unresolved journal exists. Installer failure rolls back installer-owned binary/registration changes without touching recovery data.

A clean install that discovers preserved journals from an earlier installation launches directly into Recovery. The current application uses its backward-compatible reader; if it cannot interpret the journal safely, it remains blocked and exports redacted diagnostics.

Downgrade is blocked when the target installer cannot prove reader support for every preserved journal version. Manual browser-based updates use this exact installer flow; downloading manually does not bypass recovery preflight.

### Installer/recovery acceptance

- Generate every nonterminal state for every released journal version and prove update and uninstall stop before the first binary, shortcut, registration, journal, or recovery-artifact mutation.
- Resolve each state, rerun update/uninstall, and prove it then succeeds.
- Same-version repair restores missing/corrupt app binaries while preserving unresolved journals and forcing Recovery on launch.
- A new release reads and safely resolves old-version journals copied into a clean installation without rewriting their existing records.
- Unknown-major, torn, corrupt, inaccessible, and ambiguous-artifact cases block update/uninstall and preserve all bytes.
- Installer cancellation, crash, and power-loss injection cannot strand a half-upgraded application or remove recovery data.
- Terminal-journal, no-journal, upgrade, repair, uninstall, reinstall, and blocked-downgrade paths are covered.
- Signature, publisher, timestamp, installed resource languages, and NBGV version remain verified in every allowed lifecycle path.

## 5. Roadmap, tests, and release-gate updates

Add to Section 12:

### Recognition/read-only tests

- Recognition is tested independently from compatibility.
- Recognized unknown/newer/modded installations can catalog only safe bounded files and never expose a writable binding.
- Unrecognized lookalikes and unsafe aliases are rejected.
- Read-only resource-limit and per-file failure states retain catalog/diagnostic usability without partial editing.

### Globalization/package tests

- Evaluate the WinUI MSBuild globalization properties.
- Validate `en-US` and `zh-Hans` resource-key contracts, culture availability, fallback behavior, publish payload, installed payload, and UIA language matrix.
- Treat missing safety translations or stripped resources as release blockers.

### Parser-calibration tests

- Run the approved cold/warm idle/loaded benchmark matrix on the defined minimum hardware.
- Verify profile-to-report identity, worst-case corpus coverage, exactly-at-limit behavior, cleanup, and regression thresholds.

### Lifecycle/recovery tests

- Run the complete journal-version/state by installer action matrix.
- Failure-inject update, repair, uninstall, and backward recovery.
- Prove every blocked lifecycle action leaves application and recovery bytes unchanged.

Update Phase 0 deliverables and exit gates:

- Deliver separate installation recognition, bounded read-only session creation, and writable-binding factory.
- Define and provision the minimum benchmark machine/load generator before parser calibration.
- Produce the approved idle/loaded parser benchmark artifact.
- Freeze journal version 1, its golden vectors, compatibility matrix, and backward-reader policy.
- Exit only when unknown/newer/modded recognized installs are safely read-only, parser profiles are approved on the baseline, and recovery reader tests pass.

Update Phase 1 deliverables and exit gates:

- Deliver complete `en-US`/`zh-Hans` resources with WinUI invariant globalization disabled.
- Deliver the signed installer, same-version repair, recovery preflight, and blocked unsafe update/uninstall behavior.
- Exit only when publish/install resource verification and the full language UIA matrix pass.
- Exit only when every unresolved-journal lifecycle case preserves data, blocks unsafe upgrade/uninstall, can be resolved by a supported reader, and then permits the lifecycle action.

Add to product release acceptance:

1. Discovery recognizes the game independently of write support, and no recognized unsupported installation can create a writable binding.
2. The production parser profile is tied to an approved benchmark on the defined minimum hardware and standardized loaded profile.
3. WinUI resolves `InvariantGlobalization=false`; English and Simplified Chinese resources survive publish and installation and pass fallback/UIA tests.
4. The shipped application and signed installer support every released journal version.
5. Upgrade, update, repair, and uninstall pass the unresolved-journal matrix with no recovery-data loss.

Amend Section 16:

> Implementation may begin only after recognition and writable binding are modeled as separate types, the minimum parser benchmark baseline is accepted, WinUI globalization overrides/resource languages are explicit, and the journal-version/backward-reader lifecycle contract is accepted. Production release additionally requires approved parser measurements, bilingual resource packaging verification, and recovery-safe installer/update/uninstall evidence.
