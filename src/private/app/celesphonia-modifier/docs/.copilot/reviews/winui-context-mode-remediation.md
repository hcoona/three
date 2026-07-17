# Final WinUI remediation: measured Context mode switcher

The following text is insertion-ready for the current plan. It removes the remaining assumption that `SelectorBar` suitability follows the overall window breakpoint.

## A. Replace the Context portion of the Section 8.2 visual tree

Use:

```text
WorkspaceGrid
├─ ExplorerPane
│  └─ TreeView
├─ DetailPane
│  ├─ BreadcrumbBar
│  └─ DetailPresenter
└─ ContextPane
   ├─ page header with Back/Close
   ├─ ContextModeSwitcherHost
   │  ├─ SelectorBar
   │  └─ visible-label ComboBox
   └─ ContextPresenter
```

`ContextModeSwitcherHost` contains standard controls only. It chooses one active presentation after measuring the actual Context presenter and the localized, text-scaled `SelectorBar`. The workspace window breakpoint does not choose the mode control.

## B. Replace the Section 8.2 layout table and the sentence following it

| Window width | State | Layout | Context mode control |
|---|---|---|---|
| `>=1180` epx | `ExpandedThreePane` | Columns `296, 1, *, 1, 320`. Context may close by setting its separator and column to zero. | Measured capability rule. A fixed 320 epx Context pane is not assumed to fit `SelectorBar`. |
| `1008–1179` epx | `WideTwoPane` | Columns `280, 1, *`. Explorer remains. Context replaces Detail and provides **Back to editor**. | Measured against the actual replacement-region width. |
| `720–1007` epx | `MediumTwoPane` | Columns `248, 1, *`. Explorer remains. Context replaces Detail. Forms are one column. | Measured against the actual replacement-region width. |
| `640–719` epx | `CompactSinglePane` | One `*` column. Exactly one of Explorer, Detail, or Context is visible. Detail and Context have Back. | Measured against the actual full-width Context route. |

Replace the breakpoint-based control sentence with:

> Context mode selection is capability-based, not breakpoint-based. Use `SelectorBar` only when its measured desired width, current localized labels, current text scale, theme resources, Context horizontal padding, and measurement safety spacing fit the actual `ContextPresenter` width without clipping or horizontal scrolling. Otherwise use a visible-label **View** `ComboBox` bound to the same selected mode. At 225% text scaling in the fixed 320 epx Context column, the default expectation is `ComboBox`; `SelectorBar` is permitted only when the recorded post-layout measurement proves that it fits.

`VisualStateManager` continues to position the Context pane. It does not select `SelectorBar` versus `ComboBox`.

## C. Insert after Section 8.2: exact measured capability rule

### Context mode-switcher capability

The three logical modes remain:

- Changes
- Validation
- Backups

The application owns one `SelectedContextMode` state with values `Changes`, `Validation`, and `Backups`. The active content in `ContextPresenter` is driven only by this state.

Both controls expose the same options:

- `SelectorBar` uses three localized `SelectorBarItem` labels.
- `ComboBox` has a visible localized header **View**, three localized items, and binds its selected item to the same `SelectedContextMode`.

Do not maintain separate selected indices as application state.

### Fit calculation

Measure after localized resources and text scaling have been applied:

```text
selectorDesiredWidth = ceil(ContextModeSelector.DesiredSize.Width)
requiredWidth =
    selectorDesiredWidth
    + 12 epx left Context padding
    + 12 epx right Context padding
    + 8 epx measurement/focus/rounding safety spacing
availableWidth = floor(ContextPresenter.ActualWidth)

useSelectorBar =
    availableWidth > 0
    and requiredWidth <= availableWidth
```

`SelectorBar.DesiredSize.Width` is obtained from an unconstrained horizontal measurement of the real localized `SelectorBar`, not from character counts, a hard-coded language table, the window width, or the nominal 320 epx column width.

The 8 epx safety spacing absorbs layout rounding, focus visuals, and small theme-resource differences. The Context surface has no horizontal scroller. If a post-arrange verification detects clipping despite a positive fit result, fail closed to `ComboBox` and invalidate the measurement.

### Measurement invalidation

Queue one measurement at low dispatcher priority after layout when any of these occurs:

1. `ContextPresenter.Loaded`;
2. `ContextPresenter.SizeChanged`;
3. system text scale changes;
4. localized mode labels/resources change or are reloaded;
5. `ActualThemeChanged`, including entering or leaving High Contrast;
6. the Context pane moves between third-column, two-pane replacement, and compact route states.

Coalesce repeated invalidations into one pending measurement. Measure only when `XamlRoot` exists and `ContextPresenter.ActualWidth` is positive.

MVP normally applies a Windows display-language change after restart; initial `Loaded` measurement covers that case. The localization test hook and any future runtime resource refresh must call the same invalidation path.

### Standard-control measurement mechanics

`WorkspacePage` implements the switcher with an ordinary `Grid`, one standard `SelectorBar`, and one standard `ComboBox`. No custom `Control`, replacement template, wrapping panel, or horizontal `ScrollViewer` is introduced.

When `ComboBox` is active and `SelectorBar` must be remeasured:

1. Preserve the current logical selection and whether keyboard focus is inside the mode switcher.
2. Put `SelectorBar` into a temporary measurement-only state before making it visible:
   - `Opacity=0`;
   - `IsEnabled=false`;
   - `IsHitTestVisible=false`;
   - `IsTabStop=false`;
   - every item is non-tab-stop;
   - `AutomationProperties.AccessibilityView=Raw`;
   - no live-region property.
3. Set `Visibility=Visible`, call `Measure` with unconstrained horizontal width on the UI thread, and read `DesiredSize.Width`.
4. Apply the fit calculation.
5. Set the inactive control to `Visibility=Collapsed`.
6. Restore normal opacity, enablement, hit testing, tab behavior, and UIA view only on the chosen active control.

The measurement-only state must never receive focus, pointer input, an access key, or a UIA announcement. It exists only for the measurement pass and is collapsed immediately afterward.

When `SelectorBar` is already active, measure it without changing its visible interaction state, then switch only if it no longer fits.

### Shared selection synchronization

`SelectedContextMode` is the single source of truth:

1. A user selection in either control updates `SelectedContextMode`.
2. A change to `SelectedContextMode` updates both controls under an internal synchronization guard.
3. `ContextPresenter` changes content once in response to `SelectedContextMode`; control synchronization does not independently navigate or announce.
4. Responsive, text-scale, resource, language, or theme changes never change `SelectedContextMode`.
5. Switching control presentation never appends navigation history.

The implementation may map `SelectorBarItem` to the mode enum through an ID/tag and bind the `ComboBox` to `ContextModeOption` items. The enum/state, not either control instance or selected index, remains authoritative.

## D. Insert into Section 8.6: focus, keyboard, touch, and UIA behavior

### Mode-switcher focus transfer

Before changing active presentation, determine whether focus is currently within the active mode control.

- If focus is outside the mode switcher, switch controls without moving focus.
- If focus is in `SelectorBar` and the result changes to `ComboBox`, focus the `ComboBox` after it is arranged.
- If focus is in `ComboBox` and the result changes to `SelectorBar`, close the drop-down if open, select the matching item, then focus that selected `SelectorBarItem` after arrange.
- If the active presentation does not change, retain the existing focused element.
- If the Context surface is closed during the same update, the existing Context close/focus-restoration rule takes precedence.

Queue focus transfer after visibility and layout have completed. Never focus the measurement-only `SelectorBar`.

### Keyboard and touch

- Active `SelectorBar`: arrow keys move among modes; `Enter`/`Space` activates according to standard control behavior.
- Active `ComboBox`: `Alt+Down`, `F4`, `Enter`, or `Space` opens it; arrows move selection; `Enter` commits; `Esc` closes without leaving the Context route.
- `Tab` encounters exactly one mode control.
- `F6` treats the active mode control and Context list as the existing single Context region.
- Both presentations retain standard touch targets. The `ComboBox` stretches to the Context content width at narrow measured widths.

### UI Automation

- Active `SelectorBar`: `AutomationProperties.Name` is the localized equivalent of **Review view**. Each item exposes its localized name and selected state.
- Active `ComboBox`: visible `Header` is **View** and `AutomationProperties.Name` is the localized equivalent of **Review view**. Its selected value exposes the same mode name.
- The inactive control is `Collapsed` outside the short measurement pass.
- During measurement, the hidden selector and its items are Raw-view, disabled, nonfocusable, and produce no property-changed or live-region events.
- Changing only the presentation does not raise a mode-change announcement.
- A user mode change updates the Context content heading and raises one polite announcement from that heading. It is not announced once by each control.

Required automation IDs remain:

- `ContextModeSelector`
- `ContextModeCombo`

Exactly one of these IDs is present in the UIA Control view and tab order after each completed layout pass.

## E. Correct the Section 8.7 wireframe interpretation

The Expanded workspace wireframe's mode row is conditional:

```text
Measured fit succeeds:  | [Changes] [Validation] [Backups] |
Measured fit fails:     | View [ Validation             v ] |
```

The wireframe does not promise `SelectorBar` merely because the window is expanded. At 225% text scaling in the fixed 320 epx column, render the `ComboBox` unless the measured rule proves the selector fits.

The Compact Review wireframe may also render either standard control. Its larger full-route width can legitimately fit `SelectorBar`; it is not forced to `ComboBox` by the `<720` window breakpoint.

## F. Replace the Section 8.11 responsive matrix with this corrected table

| Window epx | Text scale | Language/theme | Required workspace state | Context mode-control expectation |
|---|---:|---|---|---|
| 1280×800 | 100% | English / Light | Expanded three-pane baseline; Context width 320 epx | Evaluate measured fit; use Selector only when `requiredWidth <= 320`. |
| 1280×800 | 225% | Simplified Chinese / Dark | Expanded; fixed Context width 320 epx | `ComboBox` is the default expected result; Selector requires recorded fit proof. |
| 1180×720 | 225% | English / Light | Exact three-pane boundary; Context width 320 epx | `ComboBox` is the default expected result; Selector requires recorded fit proof. |
| 1179×720 | 225% | English / Dark | Context replaces Detail | Measure the actual replacement-region width; do not inherit the prior 320 epx result. |
| 1008×720 | 225% | Simplified Chinese / Light | Slot-table boundary | If Review is open, measure its actual presenter width independently. |
| 1007×720 | 225% | Simplified Chinese / Dark | Slot-card boundary | If Review is open, measure its actual presenter width independently. |
| 720×480 | 225% | English / Contrast | Exact two-pane boundary | Measure after Contrast resources and two-pane layout apply. |
| 719×480 | 225% | Simplified Chinese / Light | Compact Context route | Measure the full route width; do not force ComboBox solely because width is below 720. |
| 640×480 | 100% | English / Light | Minimum baseline | Measure actual compact Context width. |
| 640×480 | 225% | English / Contrast | Minimum, maximum scale, Contrast | Measure after text/theme layout; no clipping or horizontal scroll. |
| 640×480 | 225% | Simplified Chinese / Dark | Minimum, maximum scale, localization | Measure after localized resources; selection must be preserved. |

Add this sentence after the table:

> Every responsive row records `selectorDesiredWidth`, `requiredWidth`, `availableWidth`, active presentation, selected logical mode, and focused automation ID before and after the layout change. A row fails if the selected presentation does not match the measured inequality or if selection/focus is lost.

## G. Add this dedicated Context mode-switcher capability matrix

Run all rows with the shipped localized labels:

| Context presenter width | Text scale | Language | Theme | Assertion |
|---:|---:|---|---|---|
| 320 epx | 100% | English | Light | Presentation equals measured fit result. |
| 320 epx | 100% | Simplified Chinese | Dark | Presentation equals measured fit result. |
| 320 epx | 200% | English | Contrast | Presentation equals measured fit result; no clipping. |
| 320 epx | 200% | Simplified Chinese | Light | Presentation equals measured fit result; no clipping. |
| 320 epx | 225% | English | Dark | Expect `ComboBox` unless recorded measurement proves fit. |
| 320 epx | 225% | Simplified Chinese | Contrast | Expect `ComboBox` unless recorded measurement proves fit. |
| 520 epx | 100% | English | Dark | Presentation equals measured fit result. |
| 520 epx | 100% | Simplified Chinese | Contrast | Presentation equals measured fit result. |
| 520 epx | 200% | English | Light | Presentation equals measured fit result. |
| 520 epx | 200% | Simplified Chinese | Dark | Presentation equals measured fit result. |
| 520 epx | 225% | English | Contrast | Presentation equals measured fit result. |
| 520 epx | 225% | Simplified Chinese | Light | Presentation equals measured fit result. |
| 720 epx | 225% | English | Light | Wider-route fit is measured independently from the prior state. |
| 720 epx | 225% | Simplified Chinese | Dark | Wider-route fit is measured independently from the prior state. |

`SelectorBar` passing at one language, scale, theme, or width never creates a cached global assumption for another row.

## H. Add these acceptance tests to Sections 8.11 and 12.4

### Fit and presentation

1. For every matrix row, capture the real post-resource `SelectorBar.DesiredSize.Width`, apply the 32 epx required spacing, and assert the chosen presentation matches `requiredWidth <= availableWidth`.
2. Assert `ContextPresenter.ActualWidth`, not overall window width or nominal breakpoint, supplies `availableWidth`.
3. At 320 epx and 225% text scaling in both languages, assert `ComboBox` by default. A Selector result is accepted only when the captured values prove fit and screenshot/layout inspection proves zero clipping.
4. Assert no mode switcher has a horizontal scrollbar and no item text/focus visual is clipped.
5. Resize Context through widths immediately below, exactly at, and immediately above the measured threshold. The presentation changes only when the inequality changes.
6. Repeat threshold crossing 20 times and assert no oscillation after the final layout pass.

### Selection and content

7. Select **Backups**, resize from 320 to 520 to 720 and back, and assert `SelectedContextMode=Backups` throughout.
8. Repeat with **Changes** and **Validation**.
9. Change text scale `100% -> 200% -> 225% -> 100%`; assert zero lost selection and one active content mode.
10. Refresh resources `en-US -> zh-Hans -> en-US`; assert zero lost selection and labels update before measurement.
11. Change Light, Dark, and High Contrast; assert zero lost selection and post-theme remeasurement.
12. Switch expanded third-column Context to two-pane replacement to compact route and back; assert selection and Context list scroll anchor survive.
13. Assert a control-presentation switch does not navigate, recreate Context content, append route history, or raise a second content announcement.

### Focus and input

14. With keyboard focus on the selected `SelectorBarItem`, force a switch to `ComboBox`; assert focus lands on `ContextModeCombo` and selected mode is unchanged.
15. With keyboard focus on an open `ComboBox`, force a switch to `SelectorBar`; assert the drop-down closes, focus lands on the matching selected item, and mode is unchanged.
16. When focus is in the Context results list, resize or change text scale so the mode control switches; assert focus remains in the results list.
17. `Tab` encounters exactly one active mode control after every layout pass.
18. `F6` enters the Context region once and does not stop on the measurement-only/inactive control.
19. Verify standard keyboard operation for both controls and standard touch selection for all three modes at 320 and 520 epx.
20. Verify every active control and item maintains at least the standard touch target and visible focus behavior supplied by the platform control.

### Narrator and UIA

21. In Selector presentation, Narrator announces **Review view**, the selected mode, and the three selectable items through standard patterns.
22. In Combo presentation, Narrator announces the visible **View** label, **Review view**, and the selected mode.
23. During and after remeasurement, UIA Control view contains only the active control. The measurement-only selector is absent from Control/Content views and never receives focus.
24. Switching presentation with no logical mode change raises no live-region mode announcement.
25. A user mode change raises exactly one polite Context content-heading announcement.
26. Automated event capture finds no duplicate selection-changed UIA event attributable to synchronizing the inactive control.

### Resource, theme, and lifecycle

27. Initial Context load measures only after localized labels and `XamlRoot` are available.
28. Coalesced resize/text/resource/theme invalidations produce one final measurement and one final presentation.
29. Closing Context while a measurement is queued cancels or harmlessly ignores the stale result; reopening performs a fresh measurement.
30. Navigating away disposes text-scale/resource/theme event subscriptions; returning registers once and does not multiply callbacks.
31. Reduced-motion mode changes presentation without animation and preserves the same selection/focus rules.
32. Light, Dark, and High Contrast use semantic platform resources in both presentations; no custom template or color-only state is introduced.

## I. Add this fixed decision to Section 15.1

| Decision | MVP direction | Evidence required to change |
|---|---|---|
| Context mode control | Measured `SelectorBar` capability with visible-label `ComboBox` fallback; independent of window breakpoint | A future standard control that natively reflows without clipping, scrolling, selection loss, or accessibility regression |
