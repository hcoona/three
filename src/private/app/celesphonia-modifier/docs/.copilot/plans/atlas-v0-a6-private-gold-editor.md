# Atlas V0 A6R5 Private Gold Editor Vertical Slice

**Status:** Conditional governing plan before verified shared `R6R5`

**Base:** Verified shared `G6R4`
`3ef99ae7e23c3e88795308848f080e1203903cbf`

**Product posture:** Personal, private, single-user Windows utility

**Threat model:** Accidental local save loss, stale previews, misunderstood outcomes, and ordinary
filesystem failures; not a hostile local owner, compromised administrator, or adversarial checkout

**Outcome:** One usable WinUI window that loads one canonical slot, displays consistent Gold, accepts
one `Int64` target, confirms the game-closed and backup contract, invokes released G6R4, and presents
the exact classified result

## 1. Authority and lifecycle

Presence of this plan grants no implementation or private-run authority.

Only verified shared `R6R5` activates the bounded synthetic implementation described here. Only
verified shared `G6R5` releases the resulting executable for deliberate owner-operated private use.
Agents, automated tests, reviewers, and release procedures remain prohibited from selecting,
opening, or modifying private or Git-ignored saves.

This increment follows the persisted-plan, independent-review, exact-candidate, and release-gate
rules in `project-operating-model.md`.

## 2. Proportional supersession

After verified shared `R6R5`, this plan supersedes older requirements only for this first private
Gold editor slice.

The following older assumptions are not prerequisites for A6R5:

- completion of historical A7/A8 claim-ledger views before a Gold UI;
- installation discovery, a slot catalog, `global.rpgsave`, preview congruence, or pair writes;
- the multi-page shell, three-region workspace, recovery page, settings page, or full product IA;
- journals, replicas, ledgers, manifests, generalized transactions, automatic rollback, retention,
  recovery services, or NTFS metadata attestation;
- Domain/Application/Infrastructure project decomposition, Generic Host, DI, or a logging stack;
- installer, signing, updates, telemetry, external distribution, support commitments, or
  localization; and
- gameplay-range policy beyond the released `Int64` representation contract.

Released G6R4 is the authoritative write protocol for this slice: one canonical slot, one fixed
archive backup, one candidate stage, one `File.Replace`, conservative outcome classification, and
no automatic cleanup or rollback.

The older product and Atlas plans remain supporting sources for future broader product work. This
plan does not cancel their installation, catalog, global-preview, multi-domain, responsive-workspace,
recovery, packaging, or distribution hypotheses; it only removes them as blockers or inherited
requirements for A6R5.

## 3. User-visible outcome

The released application provides one single-purpose window:

1. **Browse** opens a Windows App SDK file picker filtered to `.rpgsave`.
2. The application accepts only a fully qualified path whose exact ordinal leaf is
   `file1.rpgsave` through `file20.rpgsave`.
3. It reads the selected slot with released Atlas limits and displays Gold only when
   `party._gold` and `variables._data[215]` are both present and equal.
4. The user enters one invariant decimal value representable by `Int64`.
5. **Apply Gold** opens one confirmation dialog showing the exact slot path, current Gold, requested
   Gold, adjacent archive path, and the requirement that the game be closed.
6. The dialog's primary action remains disabled until the user checks **I closed the game**.
7. After confirmation, the application verifies that the live slot still exactly matches the
   displayed preview, then invokes released G6R4.
8. The application presents `Unchanged`, `AppliedWithBackupCreated`, or
   `AppliedWithBackupPreserved` without translating them into stronger claims.
9. After an applied result, it reloads the live slot and displays the new consistent Gold.
10. Classified failures preserve their distinction and never appear as success.

The application does not claim that every `Int64` value is gameplay-valid. It performs no automatic
cleanup, rollback, artifact deletion, restore, or retry without a new confirmation.

## 4. In-scope documents

`P6R5` is the cumulative documentation candidate from exact G6R4 and contains exactly:

1. `src\private\app\celesphonia-modifier\docs\.copilot\README.md`;
2. `src\private\app\celesphonia-modifier\docs\.copilot\plans\project-operating-model.md`;
3. `src\private\app\celesphonia-modifier\docs\.copilot\plans\atlas-v0-execution-plan.md`;
4. `src\private\app\celesphonia-modifier\docs\.copilot\plans\celesphonia-modifier-plan.md`;
5. `src\private\app\celesphonia-modifier\docs\.copilot\plans\atlas-v0-a6-private-gold-editor.md`.

The first four paths receive only lifecycle, sequencing, and narrow supersession corrections needed
to make this plan authoritative. They do not absorb the implementation contract.

## 5. In-scope implementation

The cumulative `R6R5..C6R5` implementation diff contains exactly these paths.

### 5.1 Application

```text
src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.WinUI/
  Hcoona.CelesphoniaModifier.WinUI.csproj
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  GoldEditorOperations.cs
  GoldEditorViewModel.cs
  app.manifest
  packages.lock.json
```

### 5.2 Tests

```text
tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.WinUI.Tests/
  Hcoona.CelesphoniaModifier.WinUI.Tests.csproj
  GoldEditorOperationsTests.cs
  GoldEditorViewModelTests.cs
  GoldEditorProjectBoundaryTests.cs
  SyntheticGoldSave.cs
  packages.lock.json
```

### 5.3 Repository integration

```text
dirs.proj
```

No Atlas production, Atlas CLI, Atlas schema, existing Atlas test, CPM version, root build-property,
or root version file may change.

## 6. Explicit exclusions

A6R5 adds no:

- installation discovery, remembered recent path, slot catalog, or folder picker;
- `global.rpgsave`, `config.rpgsave`, pair preview, pair write, or multi-file guarantee;
- raw JsonEx, variable, switch, event, map, script, plugin, or general integer editor;
- gameplay maximum, minimum, clamping, normalization, or balance claim;
- undo/redo history beyond editing the current text field before application;
- export, restore, backup browser, artifact cleanup, or backup deletion;
- persistent settings, database, serialization contract, journal, ledger, or transaction record;
- command-line operation, protocol activation, URI handler, file association, or background task;
- telemetry, automatic networking, logging framework, crash upload, or diagnostic export;
- installer, signing, packaging workflow, update channel, or external distribution;
- app icon or game-owned art; or
- private data in tests, documentation, logs, screenshots, commits, or Agent context.

## 7. Project and build shape

### 7.1 WinUI application project

The application:

- targets `net10.0-windows10.0.22000.0`;
- sets `TargetPlatformMinVersion` to `10.0.17763.0`;
- targets only `x64` and `win-x64`;
- is an unpackaged, self-contained WinUI 3 executable;
- sets `UseWinUI`, `WindowsAppSDKSelfContained`, `WindowsPackageType=None`, and `SelfContained`;
- overrides repository invariant globalization with `InvariantGlobalization=false`;
- uses `en-US` as default and neutral language for this private slice;
- references `Microsoft.WindowsAppSDK` and `Microsoft.Windows.SDK.BuildTools` through existing CPM;
- references only `Hcoona.CelesphoniaModifier.Atlas`;
- uses root NBGV versioning; and
- exposes its internals only to the exact test assembly through `InternalsVisibleTo`.

Native AOT, single-file publish, MSIX, installer metadata, additional language resources, and custom
branding are deferred.

### 7.2 Test project

The test project:

- targets the same Windows TFM, `x64`, and `win-x64`;
- uses the repository's xUnit v3 and Microsoft.Testing.Platform packages;
- references the WinUI application and Atlas only as required by synthetic test construction;
- disables Microsoft Testing Platform telemetry using the existing repository pattern; and
- contains no UI automation dependency or private fixture.

### 7.3 Traversal

`dirs.proj` adds both new projects to `WindowsOnlyProjectReference`. Non-Windows traversal excludes
both projects from build and test while retaining the existing locked restore path with
`EnableWindowsTargeting=true` and `RuntimeIdentifier=win-x64`.

## 8. Internal application contracts

No new public library API is introduced.

`GoldEditorOperations.cs` contains a small internal operation boundary:

```csharp
internal interface IGoldEditorOperations
{
    ValueTask<GoldEditorDocument> LoadAsync(
        string slotPath,
        CancellationToken cancellationToken);

    ValueTask<GoldEditorApplyOutcome> ApplyAsync(
        GoldEditorDocument document,
        long requestedGold,
        CancellationToken cancellationToken);
}
```

The exact concrete type names may vary only if the same responsibilities and test seam remain.

`GoldEditorDocument` owns:

- the exact fully qualified canonical slot path;
- the consistent current Gold value;
- a private owned copy of the original compressed bytes used for preview convergence; and
- no decoded graph, mutable Atlas node, source span, or writable stream.

It must not expose its owned baseline array for mutation.

`GoldEditorApplyOutcome` distinguishes:

- G6R4 `Unchanged`;
- G6R4 `AppliedWithBackupCreated`;
- G6R4 `AppliedWithBackupPreserved`;
- preview changed before G6R4 invocation;
- applied but post-apply UI reload failed; and
- the exact G6R4 classified exception.

It may retain an exception for local diagnostic presentation but must not reduce an unknown outcome
to success or a proven failure.

## 9. Load and preview protocol

`LoadAsync`:

1. checks cancellation;
2. rejects null, empty, relative, normalized-different, or noncanonical slot paths;
3. opens the source with `FileMode.Open`, `FileAccess.Read`,
   `FileShare.Read | FileShare.Delete`, asynchronous and sequential options;
4. reads through `AtlasSaveReader.ReadAsync` with `AtlasSaveReaderLimits.Default`;
5. runs `AtlasGoldReadModel.Read`;
6. accepts only `Consistent` with both candidates present and equal;
7. creates an immutable `GoldEditorDocument` with an owned baseline byte copy; and
8. closes the file before returning.

Load failures map to fixed local UI categories:

- unsupported slot path;
- missing or inaccessible file;
- unsupported or malformed save;
- inconsistent Gold locations;
- read limit exceeded;
- canceled; or
- unexpected local failure.

The local UI may display the exact user-selected path. Repository records and test names use only
synthetic paths.

## 10. Confirmation and stale-preview protocol

The confirmation dialog is not security authorization. It prevents an accidental write and makes
the backup and game-closed assumptions visible.

Immediately after confirmation, `ApplyAsync`:

1. opens the same path with the load share mode and retains that handle;
2. rereads the bounded source and compares exact compressed bytes with the document baseline;
3. returns **Preview changed — review and confirm again** without invoking G6R4 if bytes differ;
4. retains the converged read handle while invoking
   `AtlasGoldFileApplication.ApplyAsync` with the exact path, requested `Int64`,
   `AtlasSaveReaderLimits.Default`, and the supplied cancellation token;
5. disposes the convergence handle after G6R4 returns or throws; and
6. reloads the live path after an applied result.

Retaining the converged handle denies new write-sharing opens while still allowing G6R4's
same-path replacement through `FileShare.Delete`. This closes the UI preview-to-application gap
without adding a new persistent protocol or modifying G6R4.

If preview reread is malformed, inaccessible, or over limit, G6R4 is not invoked. The current
document becomes blocked until a successful explicit reload or a different file is selected.

## 11. Input contract

The target control is a labelled `TextBox`, not `NumberBox`, because `NumberBox.Value` is `double`
and cannot exactly represent the complete released `Int64` domain.

Input:

- is parsed with invariant `Int64` decimal semantics;
- may contain an ordinary leading sign and surrounding whitespace;
- rejects decimal points, exponents, group separators, non-ASCII digits, and overflow;
- displays the normalized invariant decimal value in confirmation; and
- is never silently clamped or rounded.

The application makes no gameplay-validity claim. A semantic no-op remains permitted and delegates
to G6R4, which returns exact original bytes and touches no artifacts.

## 12. UI and interaction contract

### 12.1 App silhouette

Use the WinUI single-purpose utility silhouette. Do not add `NavigationView`, `TabView`, `TreeView`,
custom segmented controls, a multi-pane workspace, or a custom control template.

The window opens at approximately `720 x 680` DIPs, converted to physical pixels through
`AppWindow.Resize` and the current window DPI. The content uses one page-level vertical
`ScrollViewer`, no horizontal scroller, and remains operable at `640 x 480` and 225% text scaling.

### 12.2 Controls

The main window contains:

- a heading and short explanation;
- a persistent `InfoBar` explaining that the game must be closed and the first changed write creates
  an adjacent archive;
- a visible **Save slot** label, read-only path `TextBox`, and **Browse...** button;
- a visible **Current Gold** label and value;
- a visible **New Gold** label and editable `TextBox`;
- inline validation text;
- an accent **Apply Gold** button;
- a **Cancel operation** button visible only while work is active;
- a `ProgressRing` with visible progress text; and
- one result/error `InfoBar`.

Use `Microsoft.Windows.Storage.Pickers.FileOpenPicker` with the current `WindowId`, not the legacy
`Windows.Storage.Pickers` API. The picker filters `.rpgsave` and returns a path-based result. Picker
cancellation preserves the current document and input unchanged.

### 12.3 Confirmation

The `ContentDialog`:

- sets the window content's `XamlRoot`;
- uses **Apply Gold** as the primary verb and **Cancel** as the close verb;
- shows the exact slot path, current value, normalized requested value, and archive path;
- contains **I closed the game** as a visible `CheckBox`;
- disables the primary action until checked; and
- restores focus to **Apply Gold** after cancellation or a recoverable result.

### 12.4 State and commands

`GoldEditorViewModel` implements `INotifyPropertyChanged` directly and exposes a finite state:

- `Empty`;
- `Ready`;
- `Busy`;
- `BlockedUntilReload`; and
- `AppliedReloadFailed`.

The view model owns data, validation, cancellation, and result state. `MainWindow` owns only picker,
dialog, focus, window-close coordination, and view-model event wiring.

Keyboard:

- `Ctrl+O` opens the picker;
- `Ctrl+S` requests Apply when enabled;
- `Escape` requests cancellation while active; and
- standard dialog keyboard behavior remains intact.

Closing while busy requests cancellation and defers close until the operation reaches a classified
result. It never terminates the process during replacement classification.

### 12.5 Accessibility and theme

Every interactive control has a stable English Automation ID and a visible or programmatic name.
Status is never color-only. Busy state, preview change, success, proven failure, and unknown outcome
have distinct text.

Use standard theme resources and controls. Do not use hard-coded colors. The page must remain usable
with keyboard only, Light, Dark, and a Windows Contrast theme. Success and recoverable messages use
polite live-region announcements; replacement-unknown and post-verification failures use assertive
announcements.

## 13. Result and failure presentation

### 13.1 Successful dispositions

- `Unchanged`: **Gold already has this value. No backup or staging file was touched.**
- `AppliedWithBackupCreated`: **Gold was applied. The original slot was archived at `<backup>`.**
- `AppliedWithBackupPreserved`: **Gold was applied. The existing archive at `<backup>` was
  preserved.**

After an applied result, successful reload replaces the document baseline and current Gold. Further
editing requires a new target and confirmation.

If application succeeded but reload fails, the UI says **Gold was applied, but the slot could not be
reloaded. Reopen the file before editing again.** It must not reclassify the write as failed.

### 13.2 Classified failures

The UI preserves every G6R4 failure:

- `UnsupportedPlatform`;
- `UnsupportedSlotPath`;
- `BackupConflict`;
- `StagingConflict`;
- `SourceChanged`;
- `ReplacementFailed`;
- `ReplacementOutcomeUnknown`; and
- `PostReplaceVerificationFailed`.

`ReplacementFailed` may leave the source unchanged and exact stage retained. The UI may offer
**Try again** only through a new confirmation.

`ReplacementOutcomeUnknown` and `PostReplaceVerificationFailed` disable Apply and show the exact
live, archive, backup-staging, and candidate-stage paths with an instruction to inspect them and
reload. They offer no cleanup, rollback, delete, or success claim.

Top-level UI text does not include raw decoded save content. Inner exception text is not displayed
by default.

## 14. Synthetic tests

All automated tests use generated compressed saves and test-owned temporary directories.

### 14.1 Operation tests

Cover:

- null, empty, relative, normalized-different, noncanonical, and canonical paths;
- consistent positive, zero, negative, minimum, and maximum `Int64` Gold;
- missing, ambiguous, wrong-shape, noninteger, overflow, disagreeing, malformed, and over-limit
  saves;
- exact owned baseline bytes and mutation resistance;
- preview unchanged, preview changed, preview unreadable, and cancellation before G6R4 invocation;
- retained convergence handle behavior;
- semantic no-op with no artifact access;
- first changed apply and archive creation;
- later apply and archive preservation;
- deleted archive followed by new archive creation;
- every G6R4 disposition and failure;
- post-apply reload success and applied-but-reload-failed distinction; and
- no private path or payload in test output.

The real replacement cases run only in synthetic temporary directories on Windows.

### 14.2 View-model tests

Use a fake `IGoldEditorOperations` and cover:

- initial, loading, ready, busy, canceled, blocked, and reload-failed states;
- invariant `Int64` parsing and normalized confirmation value;
- apply enablement;
- picker cancellation preserving the active document;
- cancellation request behavior;
- result and failure mapping;
- stale-preview reconfirmation;
- success baseline replacement;
- retry requiring a new confirmation; and
- no success-shaped fallback for exceptions.

### 14.3 Boundary tests

Mechanically inspect project and XAML sources to prove:

- the WinUI project references only the released Atlas library plus required Windows SDK packages;
- no Atlas CLI, schema, global-save, journal, ledger, restore, cleanup, telemetry, network, settings,
  DI Host, or logging framework is introduced;
- `dirs.proj` treats both projects as Windows-only;
- the target frameworks, RID, unpackaged/self-contained, globalization, and telemetry settings are
  exact;
- the Windows App SDK picker namespace is used and the legacy picker namespace is absent;
- a labelled `TextBox`, not `NumberBox`, owns the Gold input;
- required visible labels, confirmation checkbox, status controls, keyboard accelerators, and
  Automation IDs are present;
- bindings that must update are not left at `x:Bind`'s `OneTime` default; and
- no hard-coded color literal or nested collection `ScrollViewer` is introduced.

## 15. Windows validation

Exact C6R5 must pass:

1. locked restore of `dirs.proj`;
2. Release build of `dirs.proj` with zero warnings and errors;
3. the direct xUnit v3 WinUI test executable;
4. the authoritative full direct Atlas test executable;
5. `dotnet format --verify-no-changes --no-restore` for the app and test projects;
6. changed-file HK checks;
7. `git diff --check`;
8. exact cumulative implementation inventory;
9. a local launch smoke that starts the built executable with no selected file, observes a main
   window, closes that exact process by PID, and leaves no child process;
10. no lock-file change after a second locked restore; and
11. fresh independent review of the complete exact candidate until `No findings`.

The launch smoke must not supply a path, command-line operation, private file, or persisted setting.
Functional file application evidence comes from the synthetic operation tests.

## 16. Gates

### P6R5 - plan candidate

`P6R5` must be a direct descendant of exact G6R4. Its cumulative diff from G6R4 contains exactly the
five documentation paths in Section 4.

Required evidence:

- exact G6R4 ancestry and five-path inventory;
- Markdown formatting, lint, links, lifecycle, privacy, and `git diff --check`;
- holistic independent review against repository instructions, released G6R4, current project
  conventions, and the accepted proportional threat model; and
- correction and rereview until `No findings`.

### R6R5 - plan-review activation

After final exact P6R5 receives `No findings`, create only:

`src\private\app\celesphonia-modifier\docs\.copilot\reviews\atlas-v0-a6-private-gold-editor-plan-review.md`

The record binds G6R4, exact P6R5, the five paths, every finding and disposition, validation,
supersession scope, synthetic-only implementation authority, and the no-private-agent-access rule.

R6R5 must be the direct child of P6R5 and add exactly that review record.

### C6R5 - implementation candidate

Initial C6R5 descends directly from exact R6R5. Its cumulative diff from R6R5 contains exactly the
implementation paths in Section 5. Reviewed corrections may change only those paths.

The candidate must pass Section 15 and receive fresh independent `No findings`. Private or
Git-ignored saves must not be accessed.

### G6R5 - release gate

After exact C6R5 passes validation and review, create only:

`src\private\app\celesphonia-modifier\docs\.copilot\reviews\atlas-v0-a6-private-gold-editor-release-gate.md`

The release record binds R6R5, final C6R5, the exact implementation inventory, validation, review,
residual risks, exclusions, and the personal-use release decision. G6R5 must be the direct child of
final C6R5 and add exactly that record.

Verified shared G6R5 releases the executable for deliberate owner-operated selection of one private
canonical slot. It grants no Agent private-data access, automated private execution, installation
discovery, catalog, broader editor, cleanup, restore, or distribution authority.

## 17. Residual risks

- The fixed archive is one baseline copy, not backup history.
- The user can delete or modify adjacent artifacts.
- Storage or operating-system failures may leave an unknown outcome requiring manual inspection.
- The app cannot prove the game is closed; it relies on the user's confirmation and G6R4 sharing
  behavior.
- Steam or another local process may change the slot outside the application's held-handle window.
- Any `Int64` may be accepted even if the game treats some values poorly.
- The app targets one known format interpretation and does not perform installation fingerprinting.
- A successful write followed by reload failure leaves the app unable to display the new value until
  the file is reopened.

These are visible limitations for a trusted local owner. They do not justify adding a generalized
transaction system, attestation, journal, ledger, rollback engine, or hidden gameplay clamp.

## 18. Stop and resume

Stop and return to planning if implementation requires:

- an Atlas production API or behavior change;
- another file participant or noncanonical slot;
- installation discovery, catalog, persistence, settings, restore, cleanup, or recovery;
- a new package beyond existing Windows App SDK, SDK Build Tools, and test packages;
- an externally distributed artifact;
- a gameplay range or semantic claim not stated here;
- a new persistent protocol or generated private artifact; or
- access to a private or Git-ignored save by an Agent or automated validation step.

On interruption:

- before R6R5, review only the exact five-path documentation candidate;
- after R6R5, implement only the exact Section 5 paths;
- after C6R5, perform only validation, review, bounded corrections, and release gating; and
- after G6R5, stop without selecting or running against a private save.
