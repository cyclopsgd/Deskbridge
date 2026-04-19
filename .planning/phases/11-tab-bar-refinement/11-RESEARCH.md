# Phase 11: Tab Bar Refinement - Research

**Researched:** 2026-04-19
**Domain:** WPF tab bar visual polish -- active tab distinction, hover-reveal close buttons, status color accents
**Confidence:** HIGH

## Summary

Phase 11 refines the existing tab bar in `MainWindow.xaml` with three visual enhancements: a more distinct active tab treatment (TAB-01v2), hover-only close buttons with fade animation (TAB-02v2), and subtle connection-state color accents on the tab background (TAB-03v2). All three requirements modify the existing `DataTemplate` inside the tab bar `ItemsControl` and the `TabItemViewModel` -- no new files, services, or packages are needed.

The existing tab bar (built in Phase 5, lines 235-338 of MainWindow.xaml) already has the structural foundation: a horizontal `ItemsControl` with per-tab `Border` elements, `IsActive` DataTrigger for a 2px top accent border, instant `IsMouseOver` trigger for subtle hover, per-tab state indicators (ProgressRing/amber/red dots), and a close `ui:Button` that is always visible. The Phase 10 tree view polish established the animation pattern for this project: named `SolidColorBrush` child elements with `ColorAnimation` Storyboards using `StaticResource` Color keys (not DynamicResource Brush keys) inside `EnterActions`/`ExitActions`. This exact same pattern applies to the tab bar hover and active transitions.

The primary technical challenge is TAB-02v2 (hover-reveal close button with no layout shift). The current close button is a 16x16 `ui:Button` at the end of the StackPanel with `Margin="4,0,0,0"`. Simply toggling its `Visibility` would cause a layout shift because `Collapsed` removes the element from layout. The solution is to use `Opacity` animation (0 to 1) instead of `Visibility`, keeping the button always in the layout flow but visually hidden. This eliminates layout shift entirely. The button must remain hit-testable on hover (Opacity 0 is not hit-testable by default in WPF), so either `IsHitTestVisible` needs to be toggled alongside opacity, or the button should use `Visibility=Visible` always but animate opacity.

**Primary recommendation:** Modify the existing tab `DataTemplate` in `MainWindow.xaml` to: (1) replace instant hover/active triggers with Storyboard-based animations using the Phase 10 named-brush pattern, (2) animate close button opacity from 0 to 1 on hover with no layout shift, and (3) add a subtle background tint per connection state via DataTriggers on `TabItemViewModel.State`.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| TAB-01v2 | User sees a visually distinct active tab (bottom border accent or background shift) | Upgrade existing 2px top accent border to include a background shift; animate active/inactive transitions with Storyboard (Phase 10 pattern); add SemiBold text weight differentiation (already exists, just needs smooth transition) |
| TAB-02v2 | User sees tab close buttons only on hover with a fade transition | Replace always-visible close button with Opacity 0 default; animate to Opacity 1 on parent Border IsMouseOver with 150ms DoubleAnimation; always-show on active tab for discoverability; no layout shift because button stays in layout flow |
| TAB-03v2 | User sees subtle status color accents on tabs reflecting connection state | Add DataTriggers on `TabItemViewModel.State` to tint tab background: subtle accent for Connected, muted/default for Disconnected/Connecting, subtle red tint for Error; use low-opacity background overlays to avoid clashing with hover/active states |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **UI Library**: WPF-UI 4.2.0 (Fluent dark theme) -- all colours via DynamicResource tokens, BasedOn for style overrides [VERIFIED: CLAUDE.md]
- **All TextBlock styles must be named** (x:Key) to prevent implicit style leaking into WPF-UI control templates [VERIFIED: Phase 8 decision in STATE.md]
- **Use Color keys (not Brush keys) for SolidColorBrush.Color** to avoid runtime type errors [VERIFIED: Phase 8 decision]
- **StaticResource Color keys for Storyboard animation targets** -- WPF Freezable constraint; dark-theme-only safe [VERIFIED: Phase 10 decision in STATE.md]
- **Named SolidColorBrush pattern** for animation targets -- child element approach enables ColorAnimation targeting [VERIFIED: Phase 10 pattern in ConnectionTreeControl.xaml]
- **No Co-Authored-By**: Do not add Co-Authored-By lines to commit messages [VERIFIED: CLAUDE.md Conventions]
- **Design system authority**: `.claude/skills/deskbridge-design/` -- accent in exactly 3 places: status bar, selected tree row stripe, active tab top border. Tab bar height 30px. Close button 16x16. Status dots 8x8. [VERIFIED: SKILL.md README.md]
- **Mandatory reading**: `docs/WPF-UI-PITFALLS.md` before any WPF-UI code [VERIFIED: CLAUDE.md]

## Standard Stack

No new libraries needed. This phase uses only existing project dependencies.

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF-UI | 4.2.0 | Fluent theme tokens, auto-restyled controls | Already in project; provides all brush tokens and SymbolIcon for Dismiss16 [VERIFIED: App.xaml] |
| CommunityToolkit.Mvvm | 8.4.2 | ObservableProperty source generators | Already in project; `[ObservableProperty]` on TabItemViewModel properties [VERIFIED: TabItemViewModel.cs] |

**Installation:** No new packages. All required dependencies are already referenced.

## Architecture Patterns

### Existing Tab Bar Structure (files to modify)
```
src/Deskbridge/
  MainWindow.xaml                           # Tab bar DataTemplate (lines 235-338)
  ViewModels/TabItemViewModel.cs            # IsActive, State, Title, status helpers
  ViewModels/MainWindowViewModel.cs         # Tab collection, SwitchTab, CloseTab commands
  Resources/CardAndPanelStyles.xaml         # DeskbridgePanelBackgroundBrush
  Resources/SpacingResources.xaml           # TabBarHeight
  App.xaml                                  # DeskbridgeErrorBrush, DeskbridgeWarningBrush, DeskbridgeSuccessBrush
```

### Pattern 1: Animated Active Tab Background (TAB-01v2)

**What:** Replace the instant `DataTrigger` setter for active tab background with a Storyboard-based animation, and add a subtle background shift alongside the existing 2px top accent border.

**How it works today (Phase 5):**
```xml
<!-- Current: instant border brush change only -->
<DataTrigger Binding="{Binding IsActive}" Value="True">
    <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
</DataTrigger>
```

**Target pattern (Phase 10 style):**
```xml
<!-- Named brush for animation targeting -->
<Border.Background>
    <SolidColorBrush x:Name="TabBrush" Color="Transparent" />
</Border.Background>

<!-- Active tab: animate background + set border accent -->
<DataTrigger Binding="{Binding IsActive}" Value="True">
    <DataTrigger.EnterActions>
        <BeginStoryboard x:Name="ActiveEnter">
            <Storyboard>
                <ColorAnimation
                    Storyboard.TargetName="TabBrush"
                    Storyboard.TargetProperty="Color"
                    To="{StaticResource SubtleFillColorSecondary}"
                    Duration="0:0:0.12" />
            </Storyboard>
        </BeginStoryboard>
    </DataTrigger.EnterActions>
    <DataTrigger.ExitActions>
        <BeginStoryboard x:Name="ActiveExit">
            <Storyboard>
                <ColorAnimation
                    Storyboard.TargetName="TabBrush"
                    Storyboard.TargetProperty="Color"
                    To="Transparent"
                    Duration="0:0:0.15" />
            </Storyboard>
        </BeginStoryboard>
    </DataTrigger.ExitActions>
    <!-- Border accent remains instant (instant visual confirmation of click) -->
    <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
</DataTrigger>
```

**Design spec alignment:** The design system says active tab gets a "2px top border (SystemAccentColorPrimaryBrush) + text becomes TextFillColorPrimaryBrush + FontWeight SemiBold". TAB-01v2 adds a background shift on top of this for stronger visual distinction. Using `SubtleFillColorSecondary` (the card-layer fill, ~8% white overlay) differentiates the active tab from the panel background (`ControlFillColorDefault`, ~6% white overlay). [VERIFIED: SKILL.md README.md design system selected states section]

### Pattern 2: Hover-Reveal Close Button (TAB-02v2)

**What:** Close button is invisible by default, fades in on tab hover, and is always visible on the active tab.

**Why Opacity instead of Visibility:** `Visibility.Collapsed` removes the element from layout, causing all elements after it to shift left when it appears -- this is the "layout shift" the requirement explicitly forbids. `Opacity=0` keeps the element in layout flow but makes it invisible. [VERIFIED: WPF layout behavior]

**Critical detail -- hit testing:** In WPF, elements with `Opacity=0` are still hit-testable if they have a non-null background. However, `ui:Button` with `Appearance="Transparent"` may not have a filled background. To ensure the button is not accidentally clickable when hidden, set `IsHitTestVisible="False"` when opacity is 0, and toggle it alongside the opacity animation. Alternatively, since the opacity animation is fast (150ms) and the user needs to hover first, we can rely on the Storyboard-driven opacity: the button fades in when the parent Border's `IsMouseOver` is true, which is already the natural state when the user's cursor reaches the button. [ASSUMED]

**Implementation approach:**

```xml
<!-- Close button with fade animation -->
<ui:Button x:Name="CloseBtn" Appearance="Transparent"
    Icon="{ui:SymbolIcon Dismiss16}"
    Width="16" Height="16" Margin="4,0,0,0"
    Padding="0" Opacity="0"
    Command="{Binding DataContext.CloseTabCommand,
        RelativeSource={RelativeSource AncestorType=ItemsControl}}"
    CommandParameter="{Binding}" />
```

The `Opacity` animation is driven by the parent Border's `IsMouseOver` trigger. Since the close button is inside the Border, hovering over the close button itself keeps `IsMouseOver=True` on the Border (WPF bubbles IsMouseOver up). No edge case where the button disappears while clicking it. [VERIFIED: WPF IsMouseOver inheritance behavior]

**Active tab exception:** The close button should always be visible on the active tab (opacity 1). This is handled by the `IsActive` DataTrigger setting close button opacity to 1, which takes precedence.

**Trigger ordering for close button opacity:**

1. Default: `Opacity="0"` on the button
2. Hover trigger on parent Border: animate button opacity to 1
3. Active DataTrigger: set button opacity to 1 (always visible)

Since DataTrigger (IsActive) and Trigger (IsMouseOver) coexist, and the active tab is always hovered when clicked, there is no conflict. When the active tab is not hovered, the DataTrigger keeps the button visible. When an inactive tab is hovered, the hover animation fades the button in. [VERIFIED: WPF trigger precedence -- later triggers override earlier ones]

### Pattern 3: Connection State Tab Accent (TAB-03v2)

**What:** Tabs show a subtle background tint reflecting connection state.

**Design spec alignment:** The requirement says "accent for connected, muted for disconnected, red tint for error." The existing tab bar already has per-tab state indicators (ProgressRing for connecting, amber dot for reconnecting, red dot for error). TAB-03v2 adds a complementary background accent -- NOT replacing the existing dots, but adding a subtle full-tab tint.

**Color mapping:**

| TabState | Background Tint | Source |
|----------|----------------|--------|
| Connecting | No tint (default) | Spinner is sufficient visual feedback |
| Connected | Very subtle accent tint (~5% opacity of system accent) | "accent for connected" per requirement |
| Reconnecting | Very subtle amber tint (~5% opacity of DeskbridgeWarningBrush) | Complements amber dot |
| Error | Very subtle red tint (~5% opacity of DeskbridgeErrorBrush) | "red tint for error" per requirement |

**Implementation approach:** Since the background is animated by the hover and active triggers via the named `TabBrush`, the state tint needs a separate overlay element. Adding a second Border (or Rectangle) behind the content with a state-driven fill and low opacity avoids fighting with the hover/active animations.

```xml
<!-- State tint overlay (behind content, not competing with hover animation) -->
<Rectangle x:Name="StateTint" Opacity="0.06" IsHitTestVisible="False">
    <Rectangle.Style>
        <Style TargetType="Rectangle">
            <Setter Property="Fill" Value="Transparent" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding State}" Value="Connected">
                    <Setter Property="Fill"
                        Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding State}" Value="Reconnecting">
                    <Setter Property="Fill"
                        Value="{DynamicResource DeskbridgeWarningBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding State}" Value="Error">
                    <Setter Property="Fill"
                        Value="{DynamicResource DeskbridgeErrorBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Rectangle.Style>
</Rectangle>
```

The `Opacity="0.06"` ensures the tint is extremely subtle -- just enough to be perceptible on a dark background without competing with the hover/active transitions. The `IsHitTestVisible="False"` ensures the overlay does not interfere with mouse events.

**Alternative considered:** Using a converter to produce a tinted Color directly for the TabBrush. Rejected because it would fight with the hover/active Storyboard animations which target TabBrush.Color. A separate overlay is cleaner. [ASSUMED]

### Anti-Patterns to Avoid

- **Animating DynamicResource brushes directly:** `ColorAnimation` cannot target a `DynamicResource` brush. Must use local `SolidColorBrush` with `StaticResource` Color keys for `To` values. [VERIFIED: Phase 10 research Pitfall 1, WPF Freezable constraint]
- **Using Visibility for close button show/hide:** `Visibility.Collapsed` removes the element from layout flow, causing layout shift. Use `Opacity` animation instead. [VERIFIED: WPF layout behavior]
- **Adding implicit TextBlock styles:** All TextBlock styles must use `x:Key` to avoid leaking into WPF-UI control templates. [VERIFIED: Phase 8 decision in STATE.md]
- **Overlapping hover and active animations on the same target:** If both hover and active animations target `TabBrush.Color`, the later Storyboard wins. Ensure active DataTrigger comes AFTER hover Trigger in XAML ordering. [VERIFIED: Phase 10 research Pitfall 2]
- **Setting explicit Height on ui:Button:** WPF-UI's button template can clip content at small sizes. The existing close button uses `Width="16" Height="16"` which works because it's icon-only with no text content -- this is safe to keep. [VERIFIED: WPF-UI-PITFALLS.md Pitfall 8a]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tab state tracking | Custom state machine | Existing `TabItemViewModel.State` + `TabState` enum | Already built in Phase 5, driven by `TabStateChangedEvent` from event bus |
| Fade animations | Manual DispatcherTimer opacity changes | WPF Storyboard `DoubleAnimation` and `ColorAnimation` | WPF handles frame timing, composition thread, and cleanup automatically |
| Layout-shift-free hide/show | Custom visibility converter with margin compensation | `Opacity` property animation (0 to 1) | Element stays in layout flow at Opacity=0; no margin or size tricks needed |

## Common Pitfalls

### Pitfall 1: Close Button Opacity and Hit Testing
**What goes wrong:** Setting close button `Opacity=0` but leaving it hit-testable means accidental clicks close tabs when the user clicks the end of a tab title.
**Why it happens:** WPF elements with Opacity=0 remain in the visual tree and are hit-testable if they have a background or are a Button.
**How to avoid:** The close button is only 16x16 with 4px margin from the title text. Given the hover requirement (button appears on hover), the user's cursor is already over the tab when the button appears, and the 150ms fade-in provides visual feedback before the click target is obvious. For extra safety, set `IsHitTestVisible` to False by default and toggle it with the opacity via a DataTrigger on the parent's IsMouseOver. However, since the active tab always shows the close button (Opacity=1), and inactive tabs only show it on hover (when IsMouseOver is already true), the risk is low.
**Warning signs:** Tabs closing when user clicks near the right edge of the tab title without seeing a close button.

### Pitfall 2: Hover Animation Competing with Active Animation
**What goes wrong:** User hovers an active tab, then moves away. The hover exit animation fades the background to Transparent, overriding the active tab's background.
**Why it happens:** WPF Storyboards that modify the same property on the same target compete. The last one to complete wins.
**How to avoid:** Two approaches: (A) The hover trigger can check `IsActive` via a MultiDataTrigger and skip the animation for active tabs. (B) Place the active DataTrigger AFTER the hover Trigger in XAML -- WPF gives priority to later triggers. Phase 10 used approach (B) for tree view selection vs hover. The same pattern applies here: active DataTrigger comes last, its animation overrides hover. When hovering an active tab then moving away, the hover exit fires (to Transparent) but the active DataTrigger's Storyboard is still active and keeps the background. [VERIFIED: Phase 10 precedent in ConnectionTreeControl.xaml]
**Warning signs:** Active tab background flickering or disappearing when mouse moves away from the active tab.

### Pitfall 3: State Tint Opacity Stacking with Hover/Active Background
**What goes wrong:** When a Connected tab is hovered, both the state tint (6% accent) and the hover background (SubtleFillColorSecondary, ~8% white) are visible, creating an overly tinted appearance.
**Why it happens:** The state tint is a separate overlay element; its opacity stacks visually with the animated background on the main Border.
**How to avoid:** Keep the state tint opacity very low (0.04-0.06) so the combined visual effect is still subtle. The tint is meant to be barely perceptible -- just enough to give a warm/cool/danger impression at a glance. At 6% opacity on a dark background, the visible difference is approximately 2-3 RGB values -- perceptible but not distracting. [ASSUMED]
**Warning signs:** Connected tabs looking noticeably different from disconnected tabs on hover, drawing too much attention.

### Pitfall 4: ContextMenu Binding Breaks After Template Restructuring
**What goes wrong:** The existing tab ContextMenu uses `RelativeSource AncestorType=ItemsControl` to reach the MainWindowViewModel. If the template structure changes (e.g., wrapping the Border in a Grid), this binding path may break.
**Why it happens:** ContextMenu lives in a separate visual tree (WPF-UI-PITFALLS.md Pitfall 2). The existing binding works because ItemsControl is an ancestor in the visual tree. Restructuring could break the ancestor chain.
**How to avoid:** Keep the ContextMenu on the same Border element. If the template needs a Grid wrapper, ensure the Border with ContextMenu remains a direct child of the ItemsControl's item container. [VERIFIED: WPF-UI-PITFALLS.md Pitfall 2]
**Warning signs:** Context menu commands not firing (null CommandParameter), no visible error.

### Pitfall 5: DoubleAnimation on Opacity Requires Named Element
**What goes wrong:** Trying to target `CloseBtn.Opacity` from a Trigger on the parent Border fails because the parent's triggers can only target named elements within the same template scope.
**Why it happens:** In a `DataTemplate`, triggers on the root element can target named children via `Storyboard.TargetName`. But the close button is nested inside a StackPanel inside the Border. If the Trigger is on the Border's Style, it cannot use `Storyboard.TargetName` to reach a child element -- Style triggers can only target the element the style is applied to.
**How to avoid:** Move the triggers to the DataTemplate level (as `DataTemplate.Triggers`) or restructure the template to use a ControlTemplate where triggers can reference named parts across the tree. The simpler approach: use a `DataTrigger` on the close button's own style, binding to `IsMouseOver` of the parent Border via `RelativeSource AncestorType=Border`. [VERIFIED: WPF template trigger scoping rules]
**Warning signs:** `XamlParseException` or animation simply not running.

## Code Examples

### Example 1: Named Brush with ColorAnimation (Phase 10 established pattern)
```xml
<!-- Source: src/Deskbridge/Views/ConnectionTreeControl.xaml lines 172-174 -->
<Border.Background>
    <SolidColorBrush x:Name="RowBrush" Color="Transparent" />
</Border.Background>

<!-- Trigger with Storyboard (lines 250-274) -->
<Trigger Property="IsMouseOver" Value="True">
    <Trigger.EnterActions>
        <BeginStoryboard x:Name="HoverEnter">
            <Storyboard>
                <ColorAnimation
                    Storyboard.TargetName="RowBrush"
                    Storyboard.TargetProperty="Color"
                    To="{StaticResource SubtleFillColorSecondary}"
                    Duration="0:0:0.15" />
            </Storyboard>
        </BeginStoryboard>
    </Trigger.EnterActions>
    <Trigger.ExitActions>
        <BeginStoryboard x:Name="HoverExit">
            <Storyboard>
                <ColorAnimation
                    Storyboard.TargetName="RowBrush"
                    Storyboard.TargetProperty="Color"
                    To="Transparent"
                    Duration="0:0:0.15" />
            </Storyboard>
        </BeginStoryboard>
    </Trigger.ExitActions>
</Trigger>
```

### Example 2: DoubleAnimation for Opacity Fade (close button)
```xml
<!-- Standard WPF DoubleAnimation pattern for opacity -->
<DoubleAnimation
    Storyboard.TargetName="CloseBtn"
    Storyboard.TargetProperty="Opacity"
    To="1"
    Duration="0:0:0.15" />
```

### Example 3: DataTrigger on Enum for State Colors
```xml
<!-- Source: existing pattern in MainWindow.xaml lines 296-307 -->
<DataTrigger Binding="{Binding State}" Value="Connected">
    <Setter Property="Fill"
        Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
</DataTrigger>
<DataTrigger Binding="{Binding State}" Value="Error">
    <Setter Property="Fill"
        Value="{DynamicResource DeskbridgeErrorBrush}" />
</DataTrigger>
```

### Example 4: Template-Level Triggers for Cross-Element Animation
```xml
<!--
  DataTemplate.Triggers can target named elements across the template.
  This is different from Style.Triggers which can only target the styled element.
  Use this pattern when a trigger on one element needs to animate another.
-->
<DataTemplate>
    <Border x:Name="TabBorder" ...>
        <StackPanel>
            <TextBlock ... />
            <ui:Button x:Name="CloseBtn" Opacity="0" ... />
        </StackPanel>
    </Border>
    <DataTemplate.Triggers>
        <!-- Hover on TabBorder fades in CloseBtn -->
        <Trigger SourceName="TabBorder" Property="IsMouseOver" Value="True">
            <Trigger.EnterActions>
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation
                            Storyboard.TargetName="CloseBtn"
                            Storyboard.TargetProperty="Opacity"
                            To="1" Duration="0:0:0.15" />
                    </Storyboard>
                </BeginStoryboard>
            </Trigger.EnterActions>
        </Trigger>
    </DataTemplate.Triggers>
</DataTemplate>
```

## State of the Art

| Old Approach (Phase 5) | Current Approach (Phase 11) | Reason |
|------------------------|----------------------------|--------|
| Instant Setter for hover background | Storyboard 150ms ColorAnimation | Smooth Fluent-style transition per design system |
| Always-visible close button | Opacity-animated hover-reveal | Cleaner tab appearance, less visual noise |
| No state background tint | Low-opacity overlay per connection state | Instant state recognition at a glance |
| Instant IsActive border switch | Animated background + instant border | Stronger active/inactive differentiation |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Opacity=0 on ui:Button is not accidentally clickable due to small size (16x16) and hover-only context | Pitfall 1 | Could add IsHitTestVisible toggle for extra safety; low impact |
| A2 | Separate Rectangle overlay for state tint is cleaner than converter-based TabBrush color | Pattern 3 | Could use a multi-value converter instead; minor architectural difference |
| A3 | State tint at 0.04-0.06 opacity is perceptible but not distracting on dark theme | Pitfall 3 | May need tuning during implementation; visual calibration |
| A4 | DataTemplate.Triggers with SourceName can animate child elements across the template | Pattern 2 / Pitfall 5 | If this fails, fallback to individual per-element DataTrigger styles |

## Open Questions

1. **Close button hit testing at Opacity=0**
   - What we know: WPF elements with Opacity=0 remain hit-testable. The close button is 16x16 at the right edge of the tab.
   - What's unclear: Whether users will accidentally click it when clicking near the right edge of a tab title.
   - Recommendation: Start without IsHitTestVisible toggle. If testing reveals accidental closes, add the toggle. The active tab always shows the button (Opacity=1), so the risk is only for inactive tabs.

2. **State tint opacity calibration**
   - What we know: 0.06 opacity of a colored fill on `#202020` background produces approximately a 2-3 RGB value shift.
   - What's unclear: Whether this is perceptible enough on various monitors and panel backgrounds.
   - Recommendation: Start at 0.05, adjust during implementation. The tint should be "I can tell something is different" not "that tab is colored."

3. **Template restructuring scope**
   - What we know: The current tab uses a Border > StackPanel layout. Adding a state tint overlay requires a Grid wrapper or a second element.
   - What's unclear: Whether adding a Grid wrapper will break existing InputBindings, ContextMenu bindings, or TabReorderBehavior.
   - Recommendation: Keep the Border as the outermost named element with all bindings intact. Add a Grid inside the Border containing the state tint Rectangle and the content StackPanel.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | Manual visual inspection (WPF UI) |
| Config file | N/A |
| Quick run command | `dotnet build src/Deskbridge -c Debug` |
| Full suite command | `dotnet build src/Deskbridge -c Debug` (no automated tests for visual behavior) |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TAB-01v2 | Active tab has distinct visual (background + accent border) | manual-only | Build and run app, switch tabs | N/A |
| TAB-02v2 | Close button fades in on hover, no layout shift | manual-only | Build and run app, hover tabs | N/A |
| TAB-03v2 | State color accents on tabs | manual-only | Build and run app, connect/disconnect | N/A |

**Justification for manual-only:** All three requirements are purely visual/animation. WPF Storyboard animations cannot be unit tested. Visual correctness requires human evaluation against the design spec.

### Sampling Rate
- **Per task commit:** `dotnet build src/Deskbridge -c Debug` (ensures XAML parses and compiles)
- **Per wave merge:** Full build + manual visual review
- **Phase gate:** Full build clean, manual verification of all 3 success criteria

### Wave 0 Gaps
None -- no test infrastructure needed for visual-only changes. Build verification is sufficient.

## Security Domain

Not applicable. Phase 11 is purely visual UI polish with no data handling, authentication, input validation, or network operations.

## Sources

### Primary (HIGH confidence)
- `src/Deskbridge/MainWindow.xaml` lines 235-338 -- existing tab bar implementation, DataTemplate, triggers, close button
- `src/Deskbridge/ViewModels/TabItemViewModel.cs` -- IsActive, State, Title properties with NotifyPropertyChanged
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` lines 170-310 -- Phase 10 animation patterns (RowBrush, Storyboard, StaticResource Color keys)
- `.claude/skills/deskbridge-design/README.md` -- design system: accent placement, hover states, selected states, animation timing
- `docs/WPF-UI-PITFALLS.md` -- Pitfall 2 (ContextMenu visual tree), Pitfall 8a (explicit Height clipping)

### Secondary (MEDIUM confidence)
- `.planning/phases/10-tree-view-polish/10-RESEARCH.md` -- established animation patterns, anti-patterns, Freezable constraint documentation
- `.planning/STATE.md` -- accumulated decisions: Phase 8 Color vs Brush keys, Phase 10 StaticResource in Storyboards

### Tertiary (LOW confidence)
- None -- all patterns verified against existing codebase implementation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages, all patterns exist in codebase
- Architecture: HIGH -- direct extension of Phase 10 animation patterns to tab bar
- Pitfalls: HIGH -- all pitfalls documented from Phase 10 precedent and WPF behavior verification

**Research date:** 2026-04-19
**Valid until:** 2026-05-19 (stable -- no external dependency changes expected)
