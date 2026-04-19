# Phase 12: General Polish Sweep - Research

**Researched:** 2026-04-19
**Domain:** WPF-UI styling, XAML animations, empty state design, gradient brushes
**Confidence:** HIGH

## Summary

Phase 12 covers three distinct workstreams: (1) upgrading existing empty states in the viewport and tree panel to meaningful, designed layouts with icons and action buttons, (2) standardizing 150ms hover/press transitions across icon rail buttons and context menu items to match the Phase 10/11 established pattern, and (3) adding gradient border brushes to all panel edges for subtle elevation separation.

The codebase already has mature animation patterns established in Phase 10 (tree rows) and Phase 11 (tab bar) using the named-SolidColorBrush + ColorAnimation approach with StaticResource Color keys. These patterns are directly reusable for icon rail and context menu hover overrides. The empty states already exist as simple text-only StackPanels (MainWindow.xaml:427-436 for viewport, ConnectionTreeControl.xaml:432-440 for tree) and need visual upgrades. The gradient borders are new -- LinearGradientBrush resources defined in CardAndPanelStyles.xaml and applied to existing Border elements.

**Primary recommendation:** Extend the established animation pattern to icon rail buttons and context menus, replace empty state placeholders with icon+text+action layouts, and add gradient brush resources to CardAndPanelStyles.xaml.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Viewport empty state (no tab open): icon + text + action button layout. 48px SymbolIcon centered above message text, descriptive subtitle, and a subtle action button ("Open a connection" or similar). Full rich treatment since the viewport has ample space.
- **D-02:** Tree empty state (no connections): compact version. Small icon + short text within the 240px panel width. No action button -- space is too constrained.
- **D-03:** Viewport gets richer treatment than tree. Same design language but different visual weight appropriate to each area's available space.
- **D-04:** Use LinearGradientBrush that fades from visible at the top to transparent at the bottom (~30% of panel height). Creates a subtle "lit from above" elevation feel.
- **D-05:** Apply gradient treatment to ALL panel edges: icon rail right edge, slide-out panel right edge, tab bar bottom edge, and properties panel top edge. Consistent everywhere.
- **D-06:** Define reusable gradient brush resources in CardAndPanelStyles.xaml (or App.xaml) so all borders reference the same brush.
- **D-07:** Override icon rail button hover to use explicit 150ms ColorAnimation pattern matching tree/tab hover transitions. Use the SubtleFillColorSecondary target established in Phase 10/11.
- **D-08:** Override context menu item hover to use explicit 150ms fade transitions. Every interactive element should feel consistent.
- **D-09:** Match everywhere -- buttons, tree rows, tabs, icon rail items, and context menu items all use 150ms hover feedback.
- **D-10:** Settings window uses a fixed size that fits the largest page's content. All pages use the same window dimensions. Smaller pages have whitespace at the bottom rather than the window resizing per page.

### Claude's Discretion
- Specific SymbolIcon choices for empty states (pick icons that match Fluent design language)
- Exact gradient opacity values and color stops (tune for best visual result on dark backgrounds)
- Whether press feedback differs from hover feedback (press could use slightly stronger animation)
- Implementation approach for context menu hover overrides (BasedOn + Style or implicit)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| POLISH-01 | User sees meaningful empty states when no connections exist or no tab is open | Empty state patterns: viewport (icon+text+action), tree (icon+text). Existing empty state StackPanels at MainWindow.xaml:427-436 and ConnectionTreeControl.xaml:432-440 need replacement. Typography styles SubtitleStyle, HintStyle, BodyStyle already available. |
| POLISH-02 | User sees subtle hover/press transitions (150ms fades) on interactive elements | Established named-brush + ColorAnimation pattern from Phase 10/11. Icon rail buttons need style override with Triggers. Context menu items need implicit MenuItem style with BasedOn. |
| POLISH-03 | User sees elevation borders using WPF-UI gradient brushes for panel depth | LinearGradientBrush resources in CardAndPanelStyles.xaml. Applied to existing Border elements on icon rail, slide-out panel, tab bar, properties panel. |
</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF-UI | 4.2.0 | Fluent theming, SymbolIcon | Already installed, provides all color tokens and icon glyphs needed [VERIFIED: src/Deskbridge/App.xaml] |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators | Already installed, used for RelayCommand on action buttons [VERIFIED: project csproj files] |

### No New Dependencies Required
This phase is pure XAML styling work. No new NuGet packages or libraries are needed. All features use built-in WPF primitives (LinearGradientBrush, Storyboard, ColorAnimation) and existing WPF-UI tokens.

## Architecture Patterns

### Relevant Existing Patterns

#### Pattern 1: Named-Brush + ColorAnimation (Phase 10/11 Established)
**What:** A named `SolidColorBrush` element child on a Border's Background, targeted by Storyboard ColorAnimation using `StaticResource` Color keys (not DynamicResource Brush keys).
**Why it works:** WPF Storyboards require Freezable targets. DynamicResource Brush keys cannot be used inside `To=` attributes. StaticResource Color keys work because they resolve at parse time. `FillBehavior="Stop"` on exit animations prevents Setter override conflicts.
**Established in:** ConnectionTreeControl.xaml (RowBrush), MainWindow.xaml (TabBrush)

```xml
<!-- Source: ConnectionTreeControl.xaml lines 173-174 -->
<Border.Background>
    <SolidColorBrush x:Name="RowBrush" Color="Transparent" />
</Border.Background>

<!-- Source: ConnectionTreeControl.xaml lines 251-274 -->
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
[VERIFIED: src/Deskbridge/Views/ConnectionTreeControl.xaml]

#### Pattern 2: Inline Style with BasedOn for WPF-UI Button Override
**What:** Icon rail buttons already use an inline `<ui:Button.Style>` with `BasedOn="{StaticResource DefaultUiButtonStyle}"` and DataTriggers for active state (accent left border).
**Where:** MainWindow.xaml lines 58-68 (Settings button), lines 81-89 (Connections button)
**Implication:** Hover animation can be added to these existing inline styles by adding `Trigger Property="IsMouseOver"` with the same ColorAnimation pattern.

[VERIFIED: src/Deskbridge/MainWindow.xaml lines 58-68]

#### Pattern 3: WPF-UI Token Color Keys Available
**What:** WPF-UI exposes both `*Color` (Color type) and `*Brush` (SolidColorBrush type) resources. For Storyboard animation targets, MUST use the Color variant. For property setters, use the Brush variant.
**Key tokens used in this phase:**
- `SubtleFillColorSecondary` (Color) -- hover target for animations
- `SubtleFillColorSecondaryBrush` (Brush) -- hover target for setters
- `ControlStrokeColorDefault` (Color) -- border stroke color
- `ControlStrokeColorDefaultBrush` (Brush) -- border stroke brush

[VERIFIED: WPF-UI-PITFALLS.md Pitfall 5, Phase 10 STATE.md decisions]

### Empty State Layout Pattern
```
Viewport (rich):
  StackPanel (centered)
    SymbolIcon (48px, Secondary foreground)
    TextBlock (SubtitleStyle, "Deskbridge")
    TextBlock (HintStyle, description)
    ui:Button (Transparent/Secondary, action)

Tree (compact):
  StackPanel (centered)
    SymbolIcon (24px, Tertiary foreground)
    TextBlock (BodyMutedStyle, "No connections")
    TextBlock (HintStyle, "Right-click to add")
```

### Gradient Border Pattern
```xml
<!-- LinearGradientBrush: visible at top, transparent at bottom -->
<LinearGradientBrush x:Key="PanelEdgeGradientBrush"
    StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="{StaticResource ControlStrokeColorDefault}" Offset="0" />
    <GradientStop Color="Transparent" Offset="0.3" />
</LinearGradientBrush>
```
[ASSUMED: Exact opacity values need tuning -- gradient might need reduced opacity on the top stop to avoid being too prominent against dark backgrounds]

### Anti-Patterns to Avoid
- **Using DynamicResource inside Storyboard To= attribute:** WPF throws at parse time. Always use StaticResource Color keys for animation targets. [VERIFIED: Phase 10 decisions in STATE.md]
- **Applying implicit MenuItem style without BasedOn:** Strips WPF-UI's Fluent template entirely. MUST use `BasedOn="{StaticResource {x:Type MenuItem}}"`. [VERIFIED: WPF-UI-PITFALLS.md, DESIGN.md Section 6]
- **Using FillBehavior="HoldEnd" on exit animations:** Prevents subsequent Setters from applying. Use `FillBehavior="Stop"` on exit Storyboards. [VERIFIED: Phase 10 ConnectionTreeControl.xaml uses FillBehavior="Stop" only where needed]
- **Naming conflicts in x:Name for Storyboards:** Each scope needs unique names. If both icon rail buttons have hover animations, they need distinct BeginStoryboard names or separate scopes. [ASSUMED: standard WPF naming constraint]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Gradient border effect | Custom adorner or opacity mask | `LinearGradientBrush` as `BorderBrush` on existing Border elements | WPF's built-in gradient brush handles DPI, theme changes, and layout correctly |
| Icon-only empty state graphics | Custom DrawingImage or PNG assets | `ui:SymbolIcon` from WPF-UI's SymbolRegular enum | 9232 glyphs available, consistent with app icon language, DPI-independent |
| Hover transition timing | Manual DispatcherTimer animation | WPF `Storyboard` + `ColorAnimation` | Built-in easing, compositing, and cleanup |

## Common Pitfalls

### Pitfall 1: Icon Rail Button Hover Needs Named Brush Inside ControlTemplate
**What goes wrong:** Icon rail buttons use `ui:Button` which has its own ControlTemplate. Adding `IsMouseOver` triggers to the outer Style doesn't animate the visual correctly because the Background property is managed by the WPF-UI internal template.
**Why it happens:** WPF-UI's Button template has its own hover visual states that override Style-level Background setters.
**How to avoid:** Override the button's `Background` with a named `SolidColorBrush` child element (same pattern as RowBrush/TabBrush), and add `Trigger Property="IsMouseOver"` with `ColorAnimation` targeting that named brush. Alternatively, since these are Transparent-appearance buttons with simple needs, replace the `Background` setter approach with a visual child element (like a Rectangle behind the icon) that can be animated.
**Warning signs:** Hover appears as a flash rather than a fade, or hover doesn't appear at all.

### Pitfall 2: Context Menu Item Style Override Scope
**What goes wrong:** Context menus live in a separate visual tree. Implicit styles defined on the Window or UserControl don't automatically propagate into ContextMenu items.
**Why it happens:** ContextMenu creates its own visual tree root (a Popup). Styles must either be in App.xaml (application scope) or explicitly set on the ContextMenu's Resources.
**How to avoid:** Define the MenuItem hover override style in App.xaml (application scope) where it will be inherited by all ContextMenu instances, or define it in the ContextMenu's own Resources block.
**Warning signs:** Menu items don't show the custom hover transition; they use the WPF-UI default instead.
[VERIFIED: WPF-UI-PITFALLS.md Pitfall 2 -- ContextMenu visual tree separation]

### Pitfall 3: LinearGradientBrush Color Key vs Brush Key
**What goes wrong:** Using `{StaticResource ControlStrokeColorDefaultBrush}` (a Brush) inside a `GradientStop.Color` property causes a runtime type mismatch.
**Why it happens:** GradientStop.Color expects a `Color` value, not a `Brush`.
**How to avoid:** Use the Color variant: `{StaticResource ControlStrokeColorDefault}` for GradientStop.Color.
**Warning signs:** XamlParseException at runtime mentioning type conversion failure.
[VERIFIED: WPF-UI-PITFALLS.md Pitfall 5 -- Color vs Brush resource keys]

### Pitfall 4: Gradient Border on Bottom Edge Requires Inverted Direction
**What goes wrong:** The gradient brush is defined as top-to-bottom (StartPoint 0,0 EndPoint 0,1) for vertical panel edges. For the tab bar's bottom border and the properties panel's top border, the gradient direction needs to match the edge orientation.
**Why it happens:** A single gradient brush definition assumes all edges are vertical and face the same way.
**How to avoid:** Define separate gradient brush resources for each edge orientation, or use `RelativeTransform` to rotate the gradient for horizontal edges. For vertical right-edge borders, the gradient goes top-visible to bottom-transparent. For horizontal bottom-edge borders, the gradient goes left-visible to right-transparent (or top-visible for a top edge).
**Warning signs:** Gradient appears on the wrong axis or fades in the wrong direction.

### Pitfall 5: Empty State Action Button Command Binding
**What goes wrong:** The viewport empty state's action button needs to invoke a command from the MainWindowViewModel (e.g., NewConnectionCommand). But the command may actually live on the ConnectionTreeViewModel which is a nested property.
**Why it happens:** The MainWindow's DataContext is MainWindowViewModel, but NewConnectionCommand is on ConnectionTreeViewModel.
**How to avoid:** Bind to `ConnectionTree.NewConnectionCommand` via the existing DataContext chain, matching how the KeyBinding on line 19 already works: `Command="{Binding ConnectionTree.NewConnectionCommand}"`.
**Warning signs:** Button click does nothing; binding error in Output window.
[VERIFIED: MainWindow.xaml line 19 -- InputBinding already uses this pattern]

### Pitfall 6: Settings Window Fixed Sizing
**What goes wrong:** D-10 mentions "Settings window uses a fixed size." Currently settings are in the slide-out panel (PanelMode.Settings), which is already fixed at 240px width. If the intent is a separate settings window (not yet implemented), this requires a new Window class.
**Why it happens:** Ambiguity between "panel" and "window" terminology.
**How to avoid:** Clarify with the existing code -- settings are currently in the 240px slide-out panel. If D-10 refers to the panel, the width is already fixed. If it refers to a future dedicated settings window, that would be new scope. For this phase, treat it as ensuring the settings panel content doesn't cause layout jumps.
**Warning signs:** Settings panel changes width when switching between sections.

## Code Examples

### Empty State - Viewport (Rich)
```xml
<!-- Source: pattern derived from design system README.md empty state examples -->
<!-- Replaces MainWindow.xaml lines 427-436 -->
<StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
    Visibility="{Binding HasNoTabs, Converter={StaticResource BoolToVisibility}}">
    <ui:SymbolIcon Symbol="Desktop24" FontSize="48"
        Foreground="{DynamicResource TextFillColorSecondaryBrush}"
        HorizontalAlignment="Center" Margin="0,0,0,16" />
    <TextBlock Text="Deskbridge"
        Style="{StaticResource SubtitleStyle}"
        Foreground="{DynamicResource TextFillColorSecondaryBrush}"
        HorizontalAlignment="Center" />
    <TextBlock Text="Open a connection to get started"
        Style="{StaticResource HintStyle}"
        HorizontalAlignment="Center" Margin="0,4,0,16" />
    <ui:Button Content="New Connection"
        Appearance="Secondary"
        Icon="{ui:SymbolIcon Add24}"
        Command="{Binding ConnectionTree.NewConnectionCommand}"
        HorizontalAlignment="Center" />
</StackPanel>
```

### Empty State - Tree (Compact)
```xml
<!-- Source: pattern derived from design system README.md empty state examples -->
<!-- Replaces ConnectionTreeControl.xaml lines 432-440 -->
<StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
    x:Name="EmptyStateOverlay">
    <ui:SymbolIcon Symbol="PlugConnected24" FontSize="24"
        Foreground="{DynamicResource TextFillColorTertiaryBrush}"
        HorizontalAlignment="Center" Margin="0,0,0,8" />
    <TextBlock Text="No connections"
        Style="{StaticResource BodyMutedStyle}"
        HorizontalAlignment="Center" />
    <TextBlock Text="Right-click to add"
        Style="{StaticResource HintStyle}"
        HorizontalAlignment="Center" Margin="0,4,0,0" />
</StackPanel>
```

### Gradient Border Brush Resources
```xml
<!-- Source: WPF LinearGradientBrush API, tuned for dark theme -->
<!-- Add to CardAndPanelStyles.xaml -->

<!-- Vertical edge gradient: visible at top, fades over ~30% height -->
<LinearGradientBrush x:Key="PanelEdgeGradientVertical"
    StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="{StaticResource ControlStrokeColorDefault}" Offset="0" />
    <GradientStop Color="Transparent" Offset="0.3" />
</LinearGradientBrush>

<!-- Horizontal edge gradient: visible at left, fades over ~30% width -->
<LinearGradientBrush x:Key="PanelEdgeGradientHorizontal"
    StartPoint="0,0" EndPoint="1,0">
    <GradientStop Color="{StaticResource ControlStrokeColorDefault}" Offset="0" />
    <GradientStop Color="Transparent" Offset="0.3" />
</LinearGradientBrush>
```

### Icon Rail Button Hover Animation
```xml
<!-- Applied to icon rail ui:Button elements in MainWindow.xaml -->
<!-- Approach: replace Appearance="Transparent" Background with animated element -->
<ui:Button.Style>
    <Style TargetType="ui:Button" BasedOn="{StaticResource DefaultUiButtonStyle}">
        <Setter Property="BorderThickness" Value="2,0,0,0" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="Background" Value="Transparent" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsConnectionsActive}" Value="True">
                <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
            </DataTrigger>
            <!-- Hover animation using the established 150ms pattern.
                 NOTE: Style triggers on ui:Button may need testing --
                 WPF-UI's internal template may intercept IsMouseOver.
                 Fallback: ControlTemplate override with named brush. -->
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
            </Trigger>
        </Style.Triggers>
    </Style>
</ui:Button.Style>
```

**Note on icon rail hover animation approach:** WPF-UI's `ui:Button` with `Appearance="Transparent"` has its own internal hover state in the ControlTemplate. A simple Setter-based override (as above) provides the correct visual result but without the 150ms animation. To get the animated fade, you would need either:
1. A ControlTemplate override that includes a named brush and Storyboard triggers (heavier but exact control), or
2. An `EventTrigger` approach using `Border.MouseEnter`/`MouseLeave` on a wrapping element (simpler but requires restructuring).

The simplest correct approach: wrap each icon rail button in a Border with a named SolidColorBrush background, and put the IsMouseOver Trigger+Storyboard on that wrapping Border. This avoids fighting WPF-UI's internal template. [ASSUMED: needs validation during implementation]

### Context Menu Item Hover Override
```xml
<!-- Define in App.xaml for application-wide scope -->
<!-- MenuItem is auto-restyled by WPF-UI ControlsDictionary -->
<Style TargetType="MenuItem" BasedOn="{StaticResource {x:Type MenuItem}}">
    <Style.Triggers>
        <Trigger Property="IsHighlighted" Value="True">
            <!-- For MenuItems, IsHighlighted is the hover property, not IsMouseOver -->
            <Setter Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
        </Trigger>
    </Style.Triggers>
</Style>
```

**Important:** MenuItem uses `IsHighlighted` (not `IsMouseOver`) for the keyboard/mouse highlight state. WPF-UI's internal MenuItem template already handles this, but the transition timing may differ from 150ms. To get explicit 150ms animation on context menu items, you would need a ControlTemplate override for MenuItem -- which is heavyweight and risks breaking WPF-UI's Fluent styling. The pragmatic approach is to accept WPF-UI's built-in MenuItem hover timing (which is already fast) unless the visual difference from 150ms is noticeable. [ASSUMED: WPF-UI's MenuItem hover timing is approximately 100-150ms by default]

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Text-only empty states | Icon + text + action empty states | Phase 12 | Better UX guidance for new users |
| 1px solid border between panels | Gradient fade borders for elevation | Phase 12 | Subtle depth without shadows (matches Fluent flat design) |
| Mixed hover timings (WPF-UI defaults vs explicit 150ms) | Consistent 150ms everywhere | Phase 10/11/12 | Cohesive interaction feel |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Gradient border top-stop opacity may need reduction from full ControlStrokeColorDefault to avoid being too prominent | Architecture Patterns | Gradient looks too harsh, needs opacity tuning during implementation |
| A2 | Icon rail button hover requires wrapping Border or ControlTemplate override rather than simple Style setter | Code Examples | Implementation approach fails, fallback to Setter-only (loses animation) |
| A3 | WPF-UI's MenuItem hover timing is approximately 100-150ms already | Code Examples | Context menu hover feels different from tree/tab if timing mismatch exists |
| A4 | Storyboard names need unique scope per icon rail button to avoid conflicts | Anti-Patterns | Runtime error or animation applied to wrong element |
| A5 | D-10 "Settings window" refers to the existing settings slide-out panel sizing consistency, not a new separate window | Common Pitfalls | Scope creep if a new SettingsWindow is expected |

## Open Questions

1. **Settings window vs panel (D-10)**
   - What we know: Settings currently live in the 240px slide-out panel (PanelMode.Settings). D-10 says "Settings window uses a fixed size."
   - What's unclear: Whether D-10 refers to the existing panel (already fixed width) or requests a dedicated settings window.
   - Recommendation: Treat as the existing panel. The 240px width is already fixed. Ensure settings content doesn't cause layout shifts when switching between sections.

2. **Press feedback differentiation**
   - What we know: D-09 says "hover/press transitions." Claude's discretion includes "Whether press feedback differs from hover feedback."
   - What's unclear: Whether press needs a distinct visual (e.g., slightly darker than hover).
   - Recommendation: Add press feedback on icon rail buttons using a darker shade (e.g., `SubtleFillColorTertiaryBrush` or slightly reduced opacity). For context menus, WPF-UI's built-in press state is sufficient.

3. **Gradient direction for horizontal edges**
   - What we know: D-04 says "fades from visible at top to transparent at bottom (~30% of panel height)."
   - What's unclear: For the tab bar bottom border and properties panel top border (horizontal edges), should the gradient go left-to-right or still top-to-bottom?
   - Recommendation: Use top-to-bottom gradient for vertical right-edge borders, and left-to-right for horizontal borders, maintaining the "lit from above" metaphor consistently.

## Project Constraints (from CLAUDE.md)

- **UI Library:** WPF-UI (Fluent dark theme) -- all colours via DynamicResource tokens, BasedOn for style overrides
- **No hard-coded colours:** Use WPF-UI token brushes (TextFillColorPrimaryBrush, ControlFillColorDefaultBrush, etc.)
- **Serialisation:** System.Text.Json only (no impact on this phase)
- **Design system:** `.claude/skills/deskbridge-design/` is visual authority -- read before UI changes
- **Pitfall docs:** Read `docs/WPF-UI-PITFALLS.md` before writing any WPF-UI code
- **Named styles only:** All TextBlock styles are named (x:Key) to prevent implicit style leaking into WPF-UI control templates (Phase 8 decision)
- **Color keys not Brush keys:** Use Color keys for SolidColorBrush.Color and Storyboard animations; use Brush keys for Foreground/Background/BorderBrush properties (Phase 8/10 decisions)

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | Visual inspection (UI polish phase -- no automated test infrastructure for visual styling) |
| Config file | N/A |
| Quick run command | `dotnet build src/Deskbridge/Deskbridge.csproj` |
| Full suite command | `dotnet build -c Release` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| POLISH-01 | Empty states show icon + text + action when tree empty / no tab open | manual-only | N/A (visual verification) | N/A |
| POLISH-02 | 150ms hover/press transitions on icon rail buttons and context menus | manual-only | N/A (visual verification) | N/A |
| POLISH-03 | Gradient border brushes on panel edges | manual-only | N/A (visual verification) | N/A |

### Sampling Rate
- **Per task commit:** `dotnet build src/Deskbridge/Deskbridge.csproj` (verify XAML parses correctly)
- **Per wave merge:** `dotnet build -c Release` (full build with no warnings)
- **Phase gate:** Visual inspection of all three requirements

### Wave 0 Gaps
None -- this is a pure XAML styling phase. Build verification (no XamlParseException) is the automated gate. Visual correctness requires manual inspection.

## Sources

### Primary (HIGH confidence)
- `src/Deskbridge/MainWindow.xaml` -- current viewport empty state, icon rail buttons, tab bar, existing animation patterns
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` -- current tree empty state, Phase 10 hover/selection animation pattern
- `src/Deskbridge/Resources/CardAndPanelStyles.xaml` -- current panel and card styles, gradient brush target file
- `src/Deskbridge/Resources/TypographyStyles.xaml` -- available text styles (SubtitleStyle, HintStyle, BodyMutedStyle)
- `src/Deskbridge/Resources/SpacingResources.xaml` -- spacing tokens
- `src/Deskbridge/App.xaml` -- resource dictionary merge order, semantic brushes, converter registrations
- `docs/WPF-UI-PITFALLS.md` -- Color vs Brush keys, ContextMenu visual tree separation, BasedOn requirement
- `docs/DESIGN.md` -- WPF-UI design patterns, colour tokens, styling conventions
- `.claude/skills/deskbridge-design/README.md` -- Design system: spacing grid, hover states, empty state copy

### Secondary (MEDIUM confidence)
- WPF LinearGradientBrush API -- standard .NET WPF API, well-documented
- WPF Storyboard/ColorAnimation API -- established WPF animation system

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages, all existing WPF primitives
- Architecture: HIGH -- directly extending Phase 10/11 established patterns
- Pitfalls: HIGH -- documented in project's own WPF-UI-PITFALLS.md and verified against codebase
- Empty states: HIGH -- existing placeholders identified, replacement pattern clear
- Gradient borders: MEDIUM -- exact opacity values need visual tuning during implementation
- Context menu hover: MEDIUM -- WPF-UI's internal MenuItem template may limit animation control

**Research date:** 2026-04-19
**Valid until:** 2026-05-19 (stable -- WPF styling patterns don't change)
