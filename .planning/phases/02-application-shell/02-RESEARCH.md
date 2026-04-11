# Phase 2: Application Shell - Research

**Researched:** 2026-04-11
**Domain:** WPF-UI Fluent shell layout, custom Grid-based VS Code layout, CommunityToolkit.Mvvm state management
**Confidence:** HIGH

## Summary

Phase 2 builds the complete visual shell for Deskbridge: a VS Code-style Grid layout with icon rail, slide-out panel, tab bar, status bar, and airspace-safe viewport. The existing Phase 1 code provides a working FluentWindow with TitleBar, dark theme, DI composition root, and a MainWindowViewModel with CommunityToolkit.Mvvm. This phase replaces the placeholder content below the TitleBar with the full layout grid.

The core challenge is building a custom Grid layout (decision D-01 explicitly rejects NavigationView) that cleanly manages panel show/hide via ViewModel state, uses WPF-UI DynamicResource tokens for all theming, and reserves an airspace-safe viewport region for future RDP ActiveX hosting. All icon names from the UI-SPEC have been verified against the actual WPF-UI 4.2.0 DLL and confirmed present (PlugConnected24, Search24, Settings24, Dismiss16).

**Primary recommendation:** Build the layout as nested WPF Grids with column/row widths matching the spec exactly (36px rail, Auto panel, * editor area), manage all toggle state through a single PanelMode enum on MainWindowViewModel with [ObservableProperty] and [NotifyPropertyChangedFor], and use Visibility="Collapsed" on the panel Border to auto-collapse the Auto column.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Custom Grid layout (VS Code-style from DESIGN.md), NOT NavigationView. Manual Grid columns/rows give full control over icon rail, slide-out panel, tab bar, and viewport without fighting NavigationView's opinionated frame management.
- **D-02:** Panel width is fixed at 240px (not user-resizable). No GridSplitter.
- **D-03:** Panel show/hide is instant snap (Visibility toggle), no slide animation.
- **D-04:** VS Code toggle pattern: click icon opens panel with that content. Click same icon again closes panel. Click different icon switches content. Three icons only: Connections (top), Search (top), Settings (bottom).
- **D-05:** Exactly 3 icons for v1: Connections, Search, Settings. Settings pinned to bottom of rail (VS Code convention).
- **D-06:** Custom tab bar control (hand-built ItemsControl with custom tab item template). Full control for middle-click close, horizontal scroll on overflow, active tab accent border, close button per tab.
- **D-07:** Tab bar is always visible (30px height), even when no connections are open. Consistent layout -- no collapsing.
- **D-08:** Subtle branding in empty viewport: centered Deskbridge name/logo with muted text, plus a keyboard shortcut hint ("Ctrl+N to create a connection"). Minimal, professional. Disappears when first tab opens.

### Claude's Discretion
- Icon choices from WPF-UI SymbolIcon library (specified as PlugConnected24, Search24, Settings24 in UI-SPEC)
- Exact panel content layout for each panel mode (placeholder text)
- Status bar content layout and placeholder text
- Whether active icon in rail gets a visual indicator (UI-SPEC specifies 2px left accent border)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SHEL-01 | FluentWindow with WPF-UI dark theme, Mica backdrop, and snap layout support | Already implemented in Phase 1. Verify TitleBar and backdrop remain intact after layout changes. |
| SHEL-02 | 32px custom title bar with min/max/close buttons | Already implemented in Phase 1. No changes needed -- TitleBar is in Row 0 of root Grid. |
| SHEL-03 | 36px left icon rail with Connections, Search, and Settings icons | Custom Border+DockPanel in Column 0 of main content Grid. Icons verified: PlugConnected24, Search24, Settings24 all exist in WPF-UI 4.2.0 SymbolRegular enum. |
| SHEL-04 | 240px slide-out panel that pushes viewport (no overlay) | Border in Column 1 (Width="Auto") with fixed internal Width="240". Visibility bound to IsPanelVisible. Auto column collapses when child is Collapsed. |
| SHEL-05 | 30px tab bar with connection name, close button, active tab accent, scroll overflow | Hand-built ItemsControl in nested Grid Row 0, wrapped in ScrollViewer with HorizontalScrollBarVisibility="Auto". Middle-click via MouseBinding MouseAction="MiddleClick". |
| SHEL-06 | 22px status bar showing hostname, resolution, and connection quality | Border in root Grid Row 2, accent background, DockPanel with left/right TextBlocks. Phase 2 uses placeholder text "Ready" / "No active connection". |
| SHEL-07 | Viewport area fills remaining space with no WPF elements overlapping (airspace-safe) | Grid cell with column * and row * in editor area. Must remain empty except for the empty-state branding overlay (which will be removed/hidden when RDP hosts are added in Phase 4). |
| SHEL-08 | Custom accent colours applied (#007ACC accent, #F44747 error, #89D185 success) | ApplicationAccentColorManager.Apply() in App.xaml.cs after theme application. Custom brush resources in App.xaml for error/success. |
</phase_requirements>

## Standard Stack

### Core (Already in Project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF-UI | 4.2.0 | Fluent Design theming, FluentWindow, SymbolIcon, Button | Already installed. Provides all controls and DynamicResource tokens needed for shell. [VERIFIED: NuGet cache on disk] |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators | Already installed. [ObservableProperty] on partial properties, [RelayCommand], [NotifyPropertyChangedFor]. [VERIFIED: project csproj] |
| Microsoft.Extensions.DependencyInjection | 10.0.x | DI container | Already installed. Register new ViewModels here. [VERIFIED: project csproj] |

### No New Packages Required
This phase uses exclusively WPF-UI controls already in the project and standard WPF controls auto-restyled by ControlsDictionary. No additional NuGet packages needed.

## Architecture Patterns

### Recommended Project Structure (Phase 2 Additions)
```
src/Deskbridge/
|-- ViewModels/
|   |-- MainWindowViewModel.cs      # EXTEND with panel/tab/status state
|   |-- TabItemViewModel.cs         # NEW - per-tab data
|-- Models/
|   |-- PanelMode.cs                # NEW - enum: None, Connections, Search, Settings
|-- MainWindow.xaml                  # EXTEND - replace placeholder with full layout grid
|-- MainWindow.xaml.cs               # Minimal code-behind changes (SnackbarPresenter wiring)
|-- App.xaml                         # ADD custom brushes after MergedDictionaries
|-- App.xaml.cs                      # ADD ApplicationAccentColorManager.Apply() call
```

### Pattern 1: VS Code-Style Grid Layout
**What:** Nested Grid structure with fixed-width columns for icon rail, Auto-width for collapsible panel, and star-sized columns/rows for editor area.
**When to use:** When building a multi-region IDE/tool layout that must be pixel-precise.
**Why not NavigationView:** Decision D-01 locks this. NavigationView fights custom tab bars, has opinionated frame management, and doesn't give control over panel push behavior. [VERIFIED: CONTEXT.md locked decision]

**XAML structure verified against DESIGN.md section 4 and UI-SPEC:**
```xml
<!-- Root Grid (already exists, extend) -->
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />   <!-- Row 0: TitleBar (32px) -->
        <RowDefinition Height="*" />      <!-- Row 1: Main content -->
        <RowDefinition Height="22" />     <!-- Row 2: Status bar -->
    </Grid.RowDefinitions>

    <!-- TitleBar in Row 0 (existing) -->
    <ui:TitleBar Grid.Row="0" Title="Deskbridge" Height="32" />

    <!-- Main content Grid in Row 1 -->
    <Grid Grid.Row="1">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="36" />    <!-- Icon rail -->
            <ColumnDefinition Width="Auto" />  <!-- Slide-out panel -->
            <ColumnDefinition Width="*" />     <!-- Editor area -->
        </Grid.ColumnDefinitions>

        <!-- Icon Rail in Column 0 -->
        <!-- Slide-out Panel in Column 1 (Width=Auto, collapses when child Visibility=Collapsed) -->
        <!-- Editor Area in Column 2 (nested Grid: Row 0 tab bar 30px, Row 1 viewport *) -->
    </Grid>

    <!-- Status bar in Row 2 -->
</Grid>
```
[CITED: DESIGN.md section 4, UI-SPEC Grid Structure section]

### Pattern 2: Panel Visibility via Auto Column
**What:** The slide-out panel is a Border with fixed Width="240" inside a column with Width="Auto". When the panel's Visibility is set to Collapsed, the Auto column collapses to 0px width automatically. The viewport column (*) grows to fill the space.
**When to use:** For instant show/hide without animation (decision D-03).
**Key detail:** Use `Visibility="{Binding IsPanelVisible, Converter={StaticResource BoolToVisibility}}"` on the panel Border. Standard WPF `BooleanToVisibilityConverter` maps `false` to `Visibility.Collapsed` (not Hidden), which reclaims layout space. [VERIFIED: WPF docs -- Collapsed does not reserve space]

### Pattern 3: ViewModel State Machine for Panel Toggle
**What:** A single `ActivePanelMode` enum property drives all panel and icon rail state.
**When to use:** When multiple UI regions depend on a single source of truth.

```csharp
// Source: CommunityToolkit.Mvvm docs + UI-SPEC ViewModel Contracts
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsConnectionsActive))]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private partial PanelMode ActivePanelMode { get; set; } = PanelMode.None;

    public bool IsPanelVisible => ActivePanelMode != PanelMode.None;
    public bool IsConnectionsActive => ActivePanelMode == PanelMode.Connections;
    public bool IsSearchActive => ActivePanelMode == PanelMode.Search;
    public bool IsSettingsActive => ActivePanelMode == PanelMode.Settings;

    [RelayCommand]
    private void TogglePanel(PanelMode mode)
    {
        ActivePanelMode = ActivePanelMode == mode ? PanelMode.None : mode;
    }
}
```
[VERIFIED: CommunityToolkit.Mvvm docs -- NotifyPropertyChangedFor raises PropertyChanged for dependent read-only properties]

### Pattern 4: Custom Tab Bar with ItemsControl
**What:** A horizontal ItemsControl with ScrollViewer for overflow, custom ItemTemplate with active accent border and close button.
**When to use:** When standard TabControl doesn't give enough control over tab appearance and behavior (decision D-06).

```xml
<!-- Source: UI-SPEC Tab Bar section + standard WPF ItemsControl pattern -->
<ScrollViewer HorizontalScrollBarVisibility="Auto"
              VerticalScrollBarVisibility="Disabled">
    <ItemsControl ItemsSource="{Binding Tabs}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <StackPanel Orientation="Horizontal" />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <!-- Tab item: Border with top accent, TextBlock, close button -->
                <Border Height="30" Padding="8,0"
                        Background="Transparent"
                        BorderThickness="0,2,0,0"
                        BorderBrush="{Binding IsActive,
                            Converter={StaticResource BoolToAccentBrush}}">
                    <Border.InputBindings>
                        <MouseBinding MouseAction="MiddleClick"
                            Command="{Binding DataContext.CloseTabCommand,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            CommandParameter="{Binding}" />
                    </Border.InputBindings>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="{Binding Title}" FontSize="14"
                            Foreground="{Binding IsActive,
                                Converter={StaticResource BoolToTextBrush}}" />
                        <ui:Button Appearance="Transparent"
                            Icon="{ui:SymbolIcon Dismiss16}"
                            Width="16" Height="16" Margin="4,0,0,0"
                            Command="{Binding DataContext.CloseTabCommand,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            CommandParameter="{Binding}" />
                    </StackPanel>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</ScrollViewer>
```
[VERIFIED: WPF MouseBinding MouseAction enum includes MiddleClick]
[VERIFIED: WPF-UI Dismiss16 exists in SymbolRegular enum (value 62312)]

### Pattern 5: Custom Accent Color Application
**What:** Override system accent with #007ACC via ApplicationAccentColorManager.Apply() in App.xaml.cs.
**When to use:** After ApplicationThemeManager.Apply() in OnStartup.

```csharp
// Source: WPF-UI API docs -- ApplicationAccentColorManager
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    ApplicationThemeManager.Apply(
        ApplicationTheme.Dark,
        WindowBackdropType.Mica,
        updateAccent: true
    );

    // Override system accent with Deskbridge brand colour
    ApplicationAccentColorManager.Apply(
        Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC),  // #007ACC
        ApplicationTheme.Dark
    );

    // ... DI setup continues
}
```
[VERIFIED: ApplicationAccentColorManager.Apply(Color, ApplicationTheme) signature confirmed from WPF-UI API docs]

### Anti-Patterns to Avoid
- **Using NavigationView for this layout:** Decision D-01 explicitly rejects it. NavigationView manages its own content frame, fights custom tab bars, and doesn't support the push-viewport panel behavior needed here.
- **Hardcoding color values in XAML:** All colors must use `{DynamicResource TokenName}`. The only exception is the custom brushes defined in App.xaml resources (DeskbridgeErrorBrush, DeskbridgeSuccessBrush) which are themselves DynamicResource-compatible.
- **Using GridSplitter:** Decision D-02 locks panel width at 240px with no user resizing.
- **Slide animation on panel:** Decision D-03 requires instant snap. No Storyboard or DoubleAnimation.
- **Overlapping WPF elements over viewport area:** The airspace constraint (SHEL-07) means no popups, tooltips, or floating elements may occupy the viewport Grid cell. Snackbar and ContentDialog presenters must be placed at the root Grid level (siblings of the main content area, not inside it).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Icon rendering | Custom path data or image icons | `{ui:SymbolIcon Name24}` markup extension | WPF-UI bundles Fluent System Icons v1.1.271 with all needed glyphs. SymbolIcon auto-scales, theme-aware, and font-based. |
| Dark theme token colors | Hardcoded hex brushes | `{DynamicResource TextFillColorPrimaryBrush}` etc. | WPF-UI tokens respond to theme changes and high contrast mode automatically. |
| Bool-to-Visibility conversion | Custom IValueConverter | `BooleanToVisibilityConverter` (built-in WPF) | Standard WPF converter maps true=Visible, false=Collapsed. Declare once in App.xaml resources. |
| Transparent icon buttons | Custom button template | `<ui:Button Appearance="Transparent" />` | WPF-UI handles hover states (SubtleFillColorSecondaryBrush background) automatically. |
| Toast notifications | Custom popup system | WPF-UI SnackbarPresenter + ISnackbarService | Built-in positioning, animation, timeout. Place SnackbarPresenter in root Grid now for Phase 6. |
| Modal dialogs | Custom overlay Window | WPF-UI ContentDialog + IContentDialogService | Built-in dimming, focus trap, result handling. Place ContentPresenter in root Grid now for Phase 3. |

## Common Pitfalls

### Pitfall 1: Auto Column Not Collapsing
**What goes wrong:** Panel set to Visibility="Collapsed" but the Auto column retains its width, leaving an empty gap.
**Why it happens:** If the column uses a fixed Width (e.g., Width="240") instead of Width="Auto", collapsing the child does not affect the column. Or if Width="Auto" but MinWidth is set on the column.
**How to avoid:** Use `ColumnDefinition Width="Auto"` with no MinWidth. Set the fixed 240px width on the Border element inside the column, not on the ColumnDefinition. The Auto column measures its child -- when the child is Collapsed (zero desired size), the column collapses.
**Warning signs:** Panel disappears but viewport doesn't grow to fill the space.

### Pitfall 2: NotifyPropertyChangedFor Not Firing for Computed Properties
**What goes wrong:** IsPanelVisible, IsConnectionsActive etc. don't update in the UI when ActivePanelMode changes.
**Why it happens:** CommunityToolkit.Mvvm's `[NotifyPropertyChangedFor]` attribute must be on the source property (ActivePanelMode), referencing the dependent property by `nameof()`. If the dependent properties are regular C# properties (not [ObservableProperty]-decorated), they still need PropertyChanged raised -- which is what NotifyPropertyChangedFor does.
**How to avoid:** Decorate ActivePanelMode with `[NotifyPropertyChangedFor(nameof(IsPanelVisible))]` etc. The computed properties (IsPanelVisible, etc.) are plain `public bool` getters -- they don't need [ObservableProperty] themselves.
**Warning signs:** Icon rail active indicators and panel visibility don't react to toggle clicks.

### Pitfall 3: MiddleClick MouseBinding Not Firing
**What goes wrong:** Middle-clicking a tab doesn't trigger CloseTabCommand.
**Why it happens:** MouseBinding requires the element to be focusable and hit-testable. If the Border has Background="Transparent" (literally no brush), it's not hit-testable. Also, the CommandParameter binding path may be wrong if RelativeSource is not correctly targeting the ItemsControl's DataContext.
**How to avoid:** Set `Background="Transparent"` as a brush (not null) on the tab item container. For CommandParameter, use `{Binding}` (current DataContext = TabItemViewModel). For Command, use RelativeSource to reach the parent ItemsControl DataContext (MainWindowViewModel).
**Warning signs:** Left-click works but middle-click does nothing.

### Pitfall 4: SnackbarPresenter/ContentPresenter Placement
**What goes wrong:** Snackbar appears behind content or dialog doesn't dim properly.
**Why it happens:** SnackbarPresenter and ContentPresenter for dialogs must be the LAST children in the root Grid so they render on top (WPF Z-order is declaration order for same Grid cell).
**How to avoid:** Place `<ui:SnackbarPresenter x:Name="SnackbarPresenter" />` and `<ContentPresenter x:Name="RootContentDialog" />` as the final two children of the root Grid, after all other content. Both span all rows/columns implicitly.
**Warning signs:** Snackbar appears clipped or behind panels.

### Pitfall 5: ApplicationAccentColorManager Call Order
**What goes wrong:** Custom accent #007ACC is not applied -- controls still show system accent color.
**Why it happens:** ApplicationAccentColorManager.Apply() must be called AFTER ApplicationThemeManager.Apply(). If called before, the theme application overwrites the accent. If updateAccent:true is used in ThemeManager, it restores system accent.
**How to avoid:** Call sequence: (1) ApplicationThemeManager.Apply() with updateAccent:false or accept overwrite, then (2) ApplicationAccentColorManager.Apply(). The second call always wins.
**Warning signs:** Status bar and active indicators show the Windows system accent instead of #007ACC.

### Pitfall 6: Airspace Violation in Viewport
**What goes wrong:** WPF elements overlap the viewport area, which will cause rendering issues when RDP ActiveX controls are added in Phase 4.
**Why it happens:** WPF popups, tooltips, or floating panels render in a separate HWND but can visually overlap the viewport. WindowsFormsHost/ActiveX always renders on top of WPF in the same HWND.
**How to avoid:** Ensure no WPF elements are placed in the viewport Grid cell except the empty-state branding (which will be hidden when a tab is active). Snackbar and ContentDialog presenters are at root Grid level, not inside the editor area. No tooltips should appear over the viewport region.
**Warning signs:** Hard to detect in Phase 2 (no ActiveX yet). Establish the clean structure now.

## Code Examples

### Custom Accent and Error/Success Brushes in App.xaml
```xml
<!-- Source: UI-SPEC Resource Dictionaries section -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ui:ThemesDictionary Theme="Dark" />
            <ui:ControlsDictionary />
        </ResourceDictionary.MergedDictionaries>

        <!-- Custom semantic brushes -->
        <SolidColorBrush x:Key="DeskbridgeErrorBrush" Color="#F44747" />
        <SolidColorBrush x:Key="DeskbridgeSuccessBrush" Color="#89D185" />

        <!-- Standard converters -->
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
    </ResourceDictionary>
</Application.Resources>
```
[CITED: UI-SPEC App.xaml Additions section]

### Icon Rail Active Indicator Style
```xml
<!-- Active icon: 2px left accent border. Inactive: transparent left border. -->
<!-- Apply via DataTrigger or custom converter on IsConnectionsActive etc. -->
<ui:Button Appearance="Transparent"
    Icon="{ui:SymbolIcon PlugConnected24}"
    Width="36" Height="36"
    Command="{Binding TogglePanelCommand}"
    CommandParameter="{x:Static models:PanelMode.Connections}"
    AutomationProperties.Name="Connections">
    <ui:Button.Style>
        <Style TargetType="ui:Button" BasedOn="{StaticResource DefaultUiButtonStyle}">
            <Setter Property="BorderThickness" Value="2,0,0,0" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsConnectionsActive}" Value="True">
                    <Setter Property="BorderBrush"
                        Value="{DynamicResource SystemAccentColorPrimaryBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ui:Button.Style>
</ui:Button>
```
[VERIFIED: PlugConnected24 confirmed in WPF-UI 4.2.0 SymbolRegular enum (value 59802)]
[VERIFIED: Settings24 confirmed (value 63146)]
[VERIFIED: Search24 confirmed (value 63120)]

### Status Bar with Accent Background
```xml
<!-- Source: DESIGN.md section 4, UI-SPEC Status Bar section -->
<Border Grid.Row="2"
    Background="{DynamicResource SystemAccentColorPrimaryBrush}">
    <DockPanel Margin="8,0">
        <TextBlock Text="{Binding StatusText}"
            Foreground="{DynamicResource TextOnAccentFillColorPrimary}"
            VerticalAlignment="Center" FontSize="12" />
        <TextBlock DockPanel.Dock="Right"
            Text="{Binding StatusSecondary}"
            Foreground="{DynamicResource TextOnAccentFillColorPrimary}"
            VerticalAlignment="Center" FontSize="12"
            HorizontalAlignment="Right" />
    </DockPanel>
</Border>
```

### Panel Content Switching with ContentControl
```xml
<!-- ContentControl switches displayed content based on ActivePanelMode -->
<ContentControl>
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Content">
                <Setter.Value>
                    <TextBlock Text="Connection tree will appear here"
                        Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                        HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Setter.Value>
            </Setter>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ActivePanelMode}"
                    Value="{x:Static models:PanelMode.Search}">
                    <Setter Property="Content">
                        <Setter.Value>
                            <TextBlock Text="Search will appear here"
                                Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                                HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Setter.Value>
                    </Setter>
                </DataTrigger>
                <DataTrigger Binding="{Binding ActivePanelMode}"
                    Value="{x:Static models:PanelMode.Settings}">
                    <Setter Property="Content">
                        <Setter.Value>
                            <TextBlock Text="Settings will appear here"
                                Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                                HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Setter.Value>
                    </Setter>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Empty Viewport Branding
```xml
<!-- Source: UI-SPEC Viewport Area section -->
<StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
    Visibility="{Binding HasNoTabs, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="Deskbridge" FontSize="20" FontWeight="SemiBold"
        Foreground="{DynamicResource TextFillColorSecondaryBrush}"
        HorizontalAlignment="Center" />
    <TextBlock Text="Ctrl+N to create a connection" FontSize="14"
        Foreground="{DynamicResource TextFillColorTertiaryBrush}"
        HorizontalAlignment="Center" Margin="0,8,0,0" />
</StackPanel>
```

### SnackbarPresenter and ContentDialog Host Placement
```xml
<!-- These go at the END of the root Grid, as siblings of other content -->
<!-- They overlay everything when active. Place now for use in Phases 3+ and 6. -->
<ui:SnackbarPresenter x:Name="SnackbarPresenter" Grid.Row="0" Grid.RowSpan="3" />
<ContentPresenter x:Name="RootContentDialog" Grid.Row="0" Grid.RowSpan="3" />
```
[VERIFIED: WPF-UI docs confirm SnackbarPresenter and ContentPresenter must be last children in Grid for correct Z-order]

### DI Registration for WPF-UI Services (App.xaml.cs)
```csharp
// Register WPF-UI services for future phases (Snackbar, ContentDialog)
services.AddSingleton<ISnackbarService, SnackbarService>();
services.AddSingleton<IContentDialogService, ContentDialogService>();
```
[VERIFIED: Wpf.Ui namespace contains ISnackbarService, IContentDialogService, SnackbarService, ContentDialogService]

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| [ObservableProperty] on private fields | [ObservableProperty] on partial properties | CommunityToolkit.Mvvm 8.4.0 (Sep 2024) | Project already uses `private partial string Title { get; set; }` pattern. Continue this. |
| WPF-UI 3.x namespaces | WPF-UI 4.x unified namespace | WPF-UI 4.0.0 (2024) | Only use `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`. Do NOT reference 3.x examples. |
| `<LangVersion>preview</LangVersion>` needed | Not needed with .NET 10 + C# 14 | .NET 10 GA (Nov 2025) | partial properties are GA in C# 14. Do not add LangVersion to csproj. |

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Shell"` |
| Full suite command | `dotnet test tests/Deskbridge.Tests` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SHEL-01 | FluentWindow with dark theme (Phase 1, already tested) | N/A | N/A | Phase 1 covered |
| SHEL-02 | 32px title bar (Phase 1, already tested) | N/A | N/A | Phase 1 covered |
| SHEL-03 | Icon rail with 3 icons, toggle commands | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~PanelToggle" -x` | Wave 0 |
| SHEL-04 | Panel show/hide via ViewModel state | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~PanelVisibility" -x` | Wave 0 |
| SHEL-05 | Tab bar with close command | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TabBar" -x` | Wave 0 |
| SHEL-06 | Status bar placeholder text | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~StatusBar" -x` | Wave 0 |
| SHEL-07 | Viewport airspace safety | manual-only | Visual inspection -- no overlapping elements | N/A |
| SHEL-08 | Custom accent color application | manual-only | Visual inspection -- #007ACC applied | N/A |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Shell" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/MainWindowViewModelTests.cs` -- covers SHEL-03 (panel toggle state machine), SHEL-04 (IsPanelVisible computed property), SHEL-06 (status bar defaults)
- [ ] `tests/Deskbridge.Tests/TabItemViewModelTests.cs` -- covers SHEL-05 (tab close, tab switch, collection management)

## Security Domain

This phase is purely UI layout with no data handling, authentication, network, or cryptographic operations.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | N/A (Phase 6) |
| V3 Session Management | no | N/A |
| V4 Access Control | no | N/A |
| V5 Input Validation | no | No user inputs in Phase 2 |
| V6 Cryptography | no | N/A |

No security-relevant implementation in this phase.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | BooleanToVisibilityConverter with Auto column width reliably collapses column to 0 when child is Collapsed | Architecture Patterns, Pattern 2 | Panel would leave empty gap. Fix: use a value converter that sets ColumnDefinition.Width directly. LOW risk -- this is standard WPF behavior. [ASSUMED] |
| A2 | `BasedOn="{StaticResource DefaultUiButtonStyle}"` is the correct style key for WPF-UI 4.2.0 ui:Button | Code Examples, Icon Rail section | Style override would strip Fluent template. Fix: check Gallery app for exact key. MEDIUM risk. [ASSUMED] |
| A3 | SnackbarPresenter and ContentPresenter can be pre-placed in Phase 2 without services wired, with no runtime errors | Architecture Patterns, Anti-Patterns | Could throw on startup if services expect presenters. LOW risk -- presenters are passive controls. [ASSUMED] |

## Open Questions

1. **Bool-to-Accent-Brush Converter for Tab Active State**
   - What we know: Active tab needs 2px top border in accent color; inactive needs transparent. Standard BooleanToVisibilityConverter doesn't handle Brush values.
   - What's unclear: Best WPF pattern -- custom IValueConverter, DataTrigger in Style, or multi-binding?
   - Recommendation: Use DataTrigger in the tab item template Style (simplest, no custom converter code). Pattern shown in icon rail code example works for tabs too.

2. **Panel Content Switching Implementation**
   - What we know: Three panel views (Connections, Search, Settings) with placeholder content in Phase 2.
   - What's unclear: Whether to use ContentControl with Style triggers, or three overlapping elements with Visibility bindings, or a DataTemplateSelector.
   - Recommendation: ContentControl with DataTriggers (shown in Code Examples) for Phase 2. When real panel content arrives (Phase 3+), migrate to UserControls with DataTemplateSelector.

3. **WPF-UI ui:Button Style Key for BasedOn**
   - What we know: DESIGN.md section 6 says `BasedOn="{StaticResource DefaultUiButtonStyle}"`.
   - What's unclear: Whether WPF-UI 4.2.0 uses this exact key or a different naming convention.
   - Recommendation: If `DefaultUiButtonStyle` doesn't resolve, try `{StaticResource {x:Type ui:Button}}` or omit BasedOn and use an implicit style. Test at build time.

## Sources

### Primary (HIGH confidence)
- WPF-UI 4.2.0 DLL (NuGet cache) -- SymbolRegular enum verified via reflection: PlugConnected24=59802, Search24=63120, Settings24=63146, Dismiss16=62312 [VERIFIED: local DLL on disk]
- [WPF-UI API: ApplicationAccentColorManager](https://wpfui.lepo.co/api/Wpf.Ui.Appearance.ApplicationAccentColorManager.html) -- Apply(Color, ApplicationTheme) signature confirmed [VERIFIED]
- [WPF-UI API: Wpf.Ui namespace](https://wpfui.lepo.co/api/Wpf.Ui.html) -- ISnackbarService, IContentDialogService, SnackbarService, ContentDialogService confirmed [VERIFIED]
- [CommunityToolkit.Mvvm ObservableProperty docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty) -- NotifyPropertyChangedFor, partial methods, computed properties [VERIFIED]
- [WPF MouseAction enum](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.mouseaction) -- MiddleClick value confirmed [VERIFIED]
- DESIGN.md section 4 -- VS Code-style Grid layout XAML reference [VERIFIED: local file]
- UI-SPEC (02-UI-SPEC.md) -- All dimensions, colors, typography, component specs [VERIFIED: local file]
- CONTEXT.md (02-CONTEXT.md) -- Locked decisions D-01 through D-08 [VERIFIED: local file]

### Secondary (MEDIUM confidence)
- [WPF-UI Discussion #585](https://github.com/lepoco/wpfui/discussions/585) -- SnackbarPresenter + ContentPresenter placement pattern
- [CommunityToolkit.Mvvm 8.4 announcement](https://devblogs.microsoft.com/dotnet/announcing-the-dotnet-community-toolkit-840/) -- Partial property support details
- [WPF-UI Accent docs](https://wpfui.lepo.co/documentation/accent.html) -- Custom accent color application

### Tertiary (LOW confidence)
- DefaultUiButtonStyle key name -- from DESIGN.md section 6 guidance, not verified against WPF-UI 4.2.0 source [ASSUMED -- A2]

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages, all verified in project
- Architecture: HIGH -- Grid layout is standard WPF, patterns verified against DESIGN.md and WPF-UI docs
- Icon names: HIGH -- verified via reflection against actual WPF-UI 4.2.0 DLL on disk
- Pitfalls: HIGH -- common WPF gotchas well-documented
- CommunityToolkit.Mvvm patterns: HIGH -- verified against official docs

**Research date:** 2026-04-11
**Valid until:** 2026-05-11 (stable -- WPF-UI 4.2.0 and CommunityToolkit.Mvvm 8.4.2 are not moving targets)
