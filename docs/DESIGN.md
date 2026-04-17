# WPF-UI design guide for .NET desktop applications

**WPF-UI (NuGet: `WPF-UI`, v4.2.0) delivers Windows 11 Fluent Design controls to WPF applications with a single XML namespace and two resource dictionary entries.** This guide provides every XAML pattern, control example, and styling convention needed to produce polished WPF-UI code on the first attempt. All examples use the namespace `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`, target .NET 8+ (API-compatible with .NET 10), and assume the `WPF-UI` NuGet package is installed. The library reskins standard WPF controls automatically while providing new Fluent-native controls like `FluentWindow`, `NavigationView`, `InfoBar`, and `Snackbar`.

---

## 1. Project bootstrap and dark theme configuration

Every WPF-UI application requires two resource dictionaries in App.xaml and a code-behind call to ensure the theme applies reliably at startup.

**App.xaml — complete dark theme setup:**

```xml
<Application x:Class="MyApp.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>

            <!-- Global style overrides go here, AFTER the merged dictionaries -->
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**App.xaml.cs — belt-and-suspenders theme application:**

```csharp
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true
        );
    }
}
```

`ThemesDictionary` is a custom `ResourceDictionary` subclass that internally loads `pack://application:,,,/Wpf.Ui;component/Resources/Theme/Dark.xaml`. Setting `Theme="Dark"` in XAML alone can be overridden by `ApplicationThemeManager`, so the code-behind call is essential. `ControlsDictionary` merges all control templates and automatically reskins standard WPF controls (Button, TextBox, TreeView, TabControl, ListBox, etc.) with Fluent styling — no prefix needed for those.

**Custom accent colour override:**

```csharp
using Wpf.Ui.Appearance;

// Single color — light/dark variants auto-generated
ApplicationAccentColorManager.Apply(
    Color.FromArgb(0xFF, 0x00, 0x99, 0xFF),
    ApplicationTheme.Dark
);

// Or specify all four variants explicitly
ApplicationAccentColorManager.Apply(
    systemAccent:    Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
    primaryAccent:   Color.FromArgb(0xFF, 0x00, 0x67, 0xC0),
    secondaryAccent: Color.FromArgb(0xFF, 0x00, 0x3E, 0x92),
    tertiaryAccent:  Color.FromArgb(0xFF, 0x00, 0x1A, 0x68)
);
```

**Automatic system theme tracking** — call `SystemThemeWatcher.Watch(this)` in the MainWindow constructor to follow Windows light/dark changes. Unwatch with `SystemThemeWatcher.UnWatch(this)`. The `ApplicationThemeManager.Changed` event fires on every theme switch, useful for non-XAML rendering code.

---

## 2. FluentWindow, TitleBar, and the complete application shell

FluentWindow replaces the standard `Window` base class to provide Mica/Acrylic backdrops, rounded corners, and native snap layout support. It removes the default OS title bar, so you must add a `<ui:TitleBar>` control explicitly.

**MainWindow.xaml — full application shell:**

```xml
<ui:FluentWindow x:Class="MyApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:pages="clr-namespace:MyApp.Views.Pages"
    Title="My Application"
    Width="1100" Height="700"
    ExtendsContentIntoTitleBar="True"
    WindowBackdropType="Mica"
    WindowCornerPreference="Round"
    WindowStartupLocation="CenterScreen">

    <Grid>
        <ui:NavigationView x:Name="NavigationView"
            PaneDisplayMode="Left"
            IsBackButtonVisible="Auto"
            IsPaneToggleVisible="True"
            OpenPaneLength="280">

            <ui:NavigationView.MenuItems>
                <ui:NavigationViewItem Content="Home"
                    Icon="{ui:SymbolIcon Home24}"
                    TargetPageType="{x:Type pages:DashboardPage}" />
                <ui:NavigationViewItem Content="Data"
                    Icon="{ui:SymbolIcon DataHistogram24}"
                    TargetPageType="{x:Type pages:DataPage}" />
            </ui:NavigationView.MenuItems>

            <ui:NavigationView.FooterMenuItems>
                <ui:NavigationViewItem Content="Settings"
                    Icon="{ui:SymbolIcon Settings24}"
                    TargetPageType="{x:Type pages:SettingsPage}" />
            </ui:NavigationView.FooterMenuItems>
        </ui:NavigationView>

        <ui:SnackbarPresenter x:Name="SnackbarPresenter" />
        <ContentPresenter x:Name="RootContentDialog" />
    </Grid>
</ui:FluentWindow>
```

**MainWindow.xaml.cs:**

```csharp
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        SystemThemeWatcher.Watch(this);
        InitializeComponent();

        navigationService.SetNavigationControl(NavigationView);
        NavigationView.SetServiceProvider(serviceProvider);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);
    }
}
```

The code-behind class **must** inherit from `FluentWindow`, not `Window`. The `SnackbarPresenter` and `ContentPresenter` for dialogs should be the last children in the Grid so they overlay content correctly.

**TitleBar with custom embedded content:**

```xml
<ui:TitleBar Grid.Row="0" Title="My App" Height="32">
    <ui:TitleBar.Header>
        <ui:TextBox Width="240" Margin="8,0"
            PlaceholderText="Search everywhere (Ctrl+K)"
            ClearButtonEnabled="True"
            Icon="{ui:SymbolIcon Search24}" />
    </ui:TitleBar.Header>
    <ui:TitleBar.TrailingContent>
        <StackPanel Orientation="Horizontal">
            <ui:Button Appearance="Transparent"
                Icon="{ui:SymbolIcon Alert24}" />
        </StackPanel>
    </ui:TitleBar.TrailingContent>
</ui:TitleBar>
```

The `Header` property holds left-side custom content; `TrailingContent` holds right-side content before the window buttons. Both areas are hit-test visible (fixed in PR #1366), so buttons and text boxes work correctly. Elements placed **outside** the TitleBar control but overlapping the title bar area will not receive mouse events — always use the designated content slots.

---

## 3. Every control you need, with exact XAML

### Buttons — all appearance variants

```xml
<!-- Primary / accent button -->
<ui:Button Content="Save" Appearance="Primary" Icon="{ui:SymbolIcon Save24}" />

<!-- Danger button -->
<ui:Button Content="Delete" Appearance="Danger" />

<!-- Success, Caution, Info variants -->
<ui:Button Content="Confirm" Appearance="Success" />
<ui:Button Content="Warning" Appearance="Caution" />
<ui:Button Content="Details" Appearance="Info" />

<!-- Transparent / subtle button (icon-only) -->
<ui:Button Appearance="Transparent" Icon="{ui:SymbolIcon MoreHorizontal24}" />

<!-- Standard WPF Button — automatically restyled by ControlsDictionary -->
<Button Content="Standard Button" />
```

The `Appearance` property accepts: **Primary**, **Secondary**, **Info**, **Success**, **Caution**, **Danger**, **Transparent**, **Dark**, **Light**. Standard `Command` and `Click` bindings work normally.

### TextBox with placeholder and clear button

```xml
<ui:TextBox
    PlaceholderText="Enter your name..."
    ClearButtonEnabled="True"
    Icon="{ui:SymbolIcon Person24}"
    IconPlacement="Left" />
```

### CardControl and Card

```xml
<!-- Interactive card (inherits from ButtonBase, clickable) -->
<ui:CardControl
    Header="Storage Settings"
    Icon="{ui:SymbolIcon Storage24}"
    Command="{Binding OpenStorageCommand}">
    <TextBlock Text="Manage your storage preferences" />
</ui:CardControl>

<!-- Static card container -->
<ui:Card Margin="8" Padding="16">
    <StackPanel>
        <TextBlock Text="Dashboard" FontWeight="SemiBold" FontSize="16" />
        <TextBlock Text="Overview of system metrics"
            Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
    </StackPanel>
</ui:Card>
```

### InfoBar — inline notifications

```xml
<ui:InfoBar
    Title="Update Available"
    Message="Restart to apply the latest update."
    Severity="Informational"
    IsOpen="True"
    IsClosable="True" />

<ui:InfoBar Title="Error" Message="Failed to save."
    Severity="Error" IsOpen="{Binding HasError}" IsClosable="False" />
```

`Severity` options: **Informational**, **Success**, **Warning**, **Error**. The `IsOpen` property controls visibility and supports two-way binding.

### Snackbar — transient toast notifications

Place a `<ui:SnackbarPresenter x:Name="SnackbarPresenter" />` in your main window Grid. Then show notifications from code:

```csharp
snackbarService.Show(
    "Saved",
    "Your changes have been saved.",
    ControlAppearance.Success,
    new SymbolIcon(SymbolRegular.Checkmark24),
    TimeSpan.FromSeconds(3)
);
```

### ContentDialog — modal dialogs

Place a `<ContentPresenter x:Name="RootContentDialog" />` as the last child in the window Grid. Show a simple dialog:

```csharp
var result = await contentDialogService.ShowSimpleDialogAsync(
    new SimpleContentDialogCreateOptions()
    {
        Title = "Save changes?",
        Content = "You have unsaved changes.",
        PrimaryButtonText = "Save",
        SecondaryButtonText = "Don't Save",
        CloseButtonText = "Cancel"
    }
);
if (result == ContentDialogResult.Primary) { /* save */ }
```

For custom dialog content, create a XAML-defined dialog:

```xml
<ui:ContentDialog x:Class="MyApp.Dialogs.RenameDialog"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="Rename Item" DialogMaxWidth="400">
    <ui:ContentDialog.Resources>
        <Style BasedOn="{StaticResource {x:Type ui:ContentDialog}}"
               TargetType="{x:Type local:RenameDialog}" />
    </ui:ContentDialog.Resources>
    <ui:TextBox PlaceholderText="New name..." Margin="0,8" />
</ui:ContentDialog>
```

### TreeView and TabControl — auto-styled

Standard WPF `TreeView` and `TabControl` are automatically restyled by `ControlsDictionary`. No `ui:` prefix needed:

```xml
<TreeView>
    <TreeViewItem Header="Documents" IsExpanded="True">
        <TreeViewItem Header="Work" />
        <TreeViewItem Header="Personal">
            <TreeViewItem Header="Photos" />
        </TreeViewItem>
    </TreeViewItem>
</TreeView>

<TabControl>
    <TabItem Header="General">
        <StackPanel Margin="12">
            <TextBlock Text="General settings" />
        </StackPanel>
    </TabItem>
    <TabItem Header="Advanced">
        <TextBlock Text="Advanced settings" Margin="12" />
    </TabItem>
</TabControl>
```

---

## 4. NavigationView modes and a VS Code-style layout

NavigationView's `PaneDisplayMode` property controls the sidebar style. The enum `NavigationViewPaneDisplayMode` has five values:

| Mode | Behavior |
|------|----------|
| `Left` | Always-visible sidebar with icons + labels; collapsible via hamburger to **48px** icon-only strip |
| `LeftMinimal` | Hidden by default; opens as overlay when triggered |
| `LeftFluent` | Large icons with labels underneath; no hamburger button, no back button, no nested items |
| `Top` | Horizontal navigation bar across the top |
| `Bottom` | Horizontal navigation bar at the bottom |

Key sizing properties: **`CompactPaneLength`** (icon-only width, default **48px**) and **`OpenPaneLength`** (expanded width, default **320px**).

**For a VS Code-style activity bar**, use `Left` mode with `IsPaneOpen="False"` for a permanently compact icon strip that can expand, or `LeftFluent` for a fixed icon bar. For maximum layout control, build a custom sidebar alongside NavigationView's content frame.

**Complete VS Code-style layout:**

```xml
<ui:FluentWindow ExtendsContentIntoTitleBar="True"
    WindowBackdropType="Mica"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />   <!-- TitleBar -->
            <RowDefinition Height="*" />      <!-- Main area -->
            <RowDefinition Height="22" />     <!-- Status bar -->
        </Grid.RowDefinitions>

        <!-- Title Bar (32px compact) -->
        <ui:TitleBar Grid.Row="0" Title="MyApp" Height="32" />

        <!-- Main layout -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="48" />     <!-- Activity bar -->
                <ColumnDefinition Width="Auto" />   <!-- Side panel -->
                <ColumnDefinition Width="*" />      <!-- Editor area -->
            </Grid.ColumnDefinitions>

            <!-- Activity bar — slim icon sidebar -->
            <Border Grid.Column="0"
                Background="{DynamicResource ControlFillColorDefaultBrush}">
                <DockPanel>
                    <StackPanel DockPanel.Dock="Bottom">
                        <ui:Button Appearance="Transparent"
                            Icon="{ui:SymbolIcon Settings24}"
                            Width="48" Height="48" />
                    </StackPanel>
                    <StackPanel>
                        <ui:Button Appearance="Transparent"
                            Icon="{ui:SymbolIcon Files24}"
                            Width="48" Height="48" />
                        <ui:Button Appearance="Transparent"
                            Icon="{ui:SymbolIcon Search24}"
                            Width="48" Height="48" />
                        <ui:Button Appearance="Transparent"
                            Icon="{ui:SymbolIcon BranchFork24}"
                            Width="48" Height="48" />
                    </StackPanel>
                </DockPanel>
            </Border>

            <!-- Collapsible side panel -->
            <Border Grid.Column="1" Width="260"
                Visibility="{Binding IsSidePanelOpen,
                    Converter={StaticResource BoolToVisibility}}"
                Background="{DynamicResource ControlFillColorDefaultBrush}"
                BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                BorderThickness="0,0,1,0">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="EXPLORER"
                        FontSize="11" Margin="12,8"
                        Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                    <TreeView />
                </DockPanel>
            </Border>

            <!-- Editor area with tab bar -->
            <Grid Grid.Column="2">
                <Grid.RowDefinitions>
                    <RowDefinition Height="35" />  <!-- Tab bar -->
                    <RowDefinition Height="*" />   <!-- Content -->
                </Grid.RowDefinitions>

                <TabControl Grid.Row="0">
                    <TabItem Header="Program.cs" />
                    <TabItem Header="App.xaml" />
                </TabControl>

                <ContentPresenter Grid.Row="1"
                    Content="{Binding CurrentEditorContent}" />
            </Grid>
        </Grid>

        <!-- Status bar -->
        <Border Grid.Row="2"
            Background="{DynamicResource SystemAccentColorPrimaryBrush}">
            <DockPanel Margin="8,0">
                <TextBlock Text="Ready"
                    Foreground="{DynamicResource TextOnAccentFillColorPrimary}"
                    VerticalAlignment="Center" FontSize="12" />
                <TextBlock DockPanel.Dock="Right" Text="Ln 1, Col 1"
                    Foreground="{DynamicResource TextOnAccentFillColorPrimary}"
                    VerticalAlignment="Center" FontSize="12"
                    HorizontalAlignment="Right" />
            </DockPanel>
        </Border>
    </Grid>
</ui:FluentWindow>
```

Alternatively, NavigationView itself can serve as the shell with its `Header` property holding a tab bar and its internal Frame managing page content:

```xml
<ui:NavigationView PaneDisplayMode="LeftFluent"
    CompactPaneLength="48"
    IsBackButtonVisible="Collapsed"
    IsPaneToggleVisible="False">
    <ui:NavigationView.Header>
        <TabControl />  <!-- Tab bar above content frame -->
    </ui:NavigationView.Header>
    <ui:NavigationView.MenuItems>
        <ui:NavigationViewItem Icon="{ui:SymbolIcon Home24}"
            TargetPageType="{x:Type pages:HomePage}" />
    </ui:NavigationView.MenuItems>
</ui:NavigationView>
```

---

## 5. Spacing, sizing, and design token conventions

WPF-UI follows the **Windows 11 Fluent Design spacing ramp** — all spacing in **4px increments**. No official design spec document exists, but these values are consistent across the source code and Gallery app.

| Element | Recommended height | Notes |
|---------|-------------------|-------|
| TitleBar (compact) | **32px** | Set `Height="32"` explicitly; default renders ~36-40px |
| TitleBar (standard) | **40px** | Default auto-sized height |
| NavigationViewItem | **36–40px** | Fixed internally by the control template |
| Compact pane width | **48px** | `CompactPaneLength` default; matches WinUI |
| Open pane width | **280–320px** | `OpenPaneLength`; 320px is WinUI default |
| Tab bar | **32–35px** | Standard Fluent tab header |
| Status bar | **22–28px** | VS Code uses 22px; 24px is a good Fluent default |
| Icon sizes | **16, 20, 24px** | Match icon suffix: `Home24` = 24px |

**Spacing values used throughout the library:**

| Context | Value |
|---------|-------|
| Between stacked controls | `Margin="0,0,0,8"` or `"0,0,0,12"` |
| Card internal padding | `Padding="12"` or `Padding="16"` |
| Card external margin | `Margin="8"` |
| Page content margin | `Margin="42,0"` or `Margin="56,0"` (when NavigationView pane is present) |
| Button padding | `Padding="12,6"` or `Padding="16,8"` |
| Section spacing | `Margin="0,0,0,16"` or `Margin="0,0,0,24"` |

**Font sizes follow the WinUI type ramp:** 12px (caption), **14px** (body, default), 16px (body strong), 20px (subtitle), 28px (title), 40px (display).

---

## 6. Colour tokens and styling patterns

### Using WPF-UI colour tokens

All colour references **must** use `DynamicResource` (not `StaticResource`) to support runtime theme switching. The token naming follows WinUI 3 conventions. Each token exists as both a `Color` and a `Brush`:

```xml
<!-- Text colours -->
<TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" />      <!-- #FFFFFF in dark -->
<TextBlock Foreground="{DynamicResource TextFillColorSecondaryBrush}" />    <!-- Muted text -->
<TextBlock Foreground="{DynamicResource TextFillColorTertiaryBrush}" />     <!-- Even more muted -->
<TextBlock Foreground="{DynamicResource TextFillColorDisabledBrush}" />

<!-- Surface colours -->
<Border Background="{DynamicResource ControlFillColorDefaultBrush}" />      <!-- Semi-transparent fill -->
<Border Background="{DynamicResource ControlFillColorSecondaryBrush}" />
<Border Background="{DynamicResource ControlFillColorInputActiveBrush}" />
<Border Background="{DynamicResource SubtleFillColorTransparentBrush}" />
<Border Background="{DynamicResource SubtleFillColorSecondaryBrush}" />

<!-- Borders and strokes -->
<Border BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}" />
<Border BorderBrush="{DynamicResource ControlStrokeColorSecondaryBrush}" />

<!-- Accent colours -->
<Border Background="{DynamicResource SystemAccentColorBrush}" />
<Border Background="{DynamicResource SystemAccentColorPrimaryBrush}" />
<Border Background="{DynamicResource SystemAccentColorSecondaryBrush}" />
<Border Background="{DynamicResource AccentFillColorDefaultBrush}" />

<!-- Text on accent surfaces -->
<TextBlock Foreground="{DynamicResource TextOnAccentFillColorPrimary}" />

<!-- Application background -->
<Grid Background="{DynamicResource ApplicationBackgroundColor}" />          <!-- #202020 dark, #FAFAFA light -->
```

### Overriding styles with BasedOn

WPF-UI style keys follow the pattern `DefaultUi<ControlName>Style` for WPF-UI controls and `Default<ControlName>Style` for restyled WPF controls. **Always use `BasedOn`** when overriding — omitting it strips the Fluent template entirely.

```xml
<!-- Named style override for ui:Button -->
<Style x:Key="CompactDangerButton" TargetType="ui:Button"
       BasedOn="{StaticResource DefaultUiButtonStyle}">
    <Setter Property="Appearance" Value="Danger" />
    <Setter Property="Padding" Value="8,4" />
    <Setter Property="FontSize" Value="12" />
</Style>

<!-- Implicit style override — applies to ALL ui:Button instances -->
<Style TargetType="ui:Button" BasedOn="{StaticResource DefaultUiButtonStyle}">
    <Setter Property="FontSize" Value="14" />
    <Setter Property="Padding" Value="12,8" />
</Style>

<!-- Override standard WPF controls (auto-restyled by ControlsDictionary) -->
<Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Margin" Value="4" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
```

**Building a theme-aware custom panel with WPF-UI tokens:**

```xml
<Style x:Key="SidePanel" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource ControlFillColorDefaultBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource ControlStrokeColorDefaultBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16" />
</Style>

<Style x:Key="CaptionText" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="TextTransform" Value="Uppercase" />
</Style>
```

Place all global style overrides in App.xaml **after** the `MergedDictionaries` block but still inside the root `ResourceDictionary`, so they cascade correctly over the WPF-UI defaults.

---

## 7. FluentWindow versus custom WindowChrome

For most applications, **FluentWindow is the correct choice**. It handles Mica/Acrylic backdrops, Windows 11 snap layouts, DPI-aware hit-testing, and theme integration automatically. The trade-offs matter only for advanced scenarios.

**Snap layouts work natively** through the TitleBar control — its `HwndSourceHook` returns `HTMAXBUTTON` for the maximize button area, which triggers the Windows 11 snap flyout. This requires `WindowStyle` to remain at `SingleBorderWindow` (the default); setting `WindowStyle.None` breaks snap layouts.

**Title bar height is customisable** by setting `Height="32"` directly on the `<ui:TitleBar>` element. FluentWindow itself does not expose a `CaptionHeight` property. The TitleBar control auto-sizes by default (~36-40px), but an explicit height works reliably.

**When to use custom WindowChrome instead:**

- You need arbitrary controls embedded anywhere in the caption area, not just in `Header` and `TrailingContent` slots
- You need complete template freedom for the window chrome (tabs in the title bar like Chrome/VS Code)
- You're targeting Windows 10 where FluentWindow's TitleBar can have visual artefacts

**Custom WindowChrome approach (when needed):**

```xml
<Window x:Class="MyApp.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="32"
            UseAeroCaptionButtons="False"
            ResizeBorderThickness="5" />
    </WindowChrome.WindowChrome>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="32" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Custom title bar with full control -->
        <Grid Grid.Row="0"
            Background="{DynamicResource ControlFillColorDefaultBrush}">
            <TabControl WindowChrome.IsHitTestVisibleInChrome="True"
                HorizontalAlignment="Left" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button WindowChrome.IsHitTestVisibleInChrome="True"
                    Content="─" Click="OnMinimize" Width="46" />
                <Button WindowChrome.IsHitTestVisibleInChrome="True"
                    Content="□" Click="OnMaximize" Width="46" />
                <Button WindowChrome.IsHitTestVisibleInChrome="True"
                    Content="✕" Click="OnClose" Width="46" />
            </StackPanel>
        </Grid>

        <ContentPresenter Grid.Row="1" />
    </Grid>
</Window>
```

With custom WindowChrome, you must apply the backdrop effect manually via `WindowBackdrop.ApplyBackdrop(hwnd, backdropType)` and wire up theme management yourself. Every interactive control in the caption area needs `WindowChrome.IsHitTestVisibleInChrome="True"`.

| Aspect | FluentWindow | Custom WindowChrome |
|--------|-------------|-------------------|
| Setup effort | Minimal | Substantial |
| Snap layouts | Automatic | Manual NCHITTEST handling |
| Mica/Acrylic | `WindowBackdropType` property | Manual `WindowBackdrop.ApplyBackdrop()` |
| Title bar content | `Header` + `TrailingContent` slots | Full template freedom |
| DPI handling | Built-in | Manual |
| Windows 10 | Some visual artefacts | More predictable |

---

## 8. Real-world reference applications

The most valuable codebases to study for WPF-UI patterns, ordered by relevance:

**Wpf.Ui.Gallery** (official, in-repo at `src/Wpf.Ui.Gallery/`) — The definitive reference. Demonstrates every control, uses .NET Generic Host with DI, `AddTransientFromNamespace()` for page registration, FluentWindow with NavigationView in `Left` mode, theme switching, and Mica backdrop. Installable from the Microsoft Store (`winget install 'WPF UI'`).

**Bloxstrap** (~2,900 stars, `github.com/bloxstraplabs/bloxstrap`) — Alternative Roblox bootstrapper. Maintains a custom WPF-UI fork for modifications. Demonstrates FluentWindow, full navigation, custom settings pages, and real production-grade usage with extensive theming.

**RevitLookup** (`github.com/lookup-foundation/RevitLookup`) — Professional Autodesk Revit database exploration tool. Shows WPF-UI in a complex professional application with TitleBar, navigation, ContentDialog, data tables supporting 100K+ rows, and theme synchronisation with the host application.

**WPF UI Violeta** (`github.com/emako/wpfui.violeta`) — Extension library built on top of WPF-UI adding Toast notifications, FlyoutService, TreeListView, splash screens, and tray icons. Demonstrates how to extend the library with additional themed resource dictionaries using the companion namespace `http://schemas.lepo.co/wpfui/2022/xaml/violeta`.

**Radiograph** (by the WPF-UI creator) — Hardware monitoring tool and the original reason WPF-UI was created. Uses `LeftFluent` navigation, Cards for data display, and the full theme system.

Additional community projects worth studying: **youtube-dl-wpf** (media downloader GUI), **Symbolic11** (symbolic link manager with clean Windows 11 UI), and various Revit add-in samples showing WPF-UI integration with host applications.

---

## Conclusion

Three patterns recur across every polished WPF-UI application. First, the **shell pattern**: `FluentWindow` → `NavigationView` → pages, with `SnackbarPresenter` and `ContentPresenter` (for dialogs) as sibling overlays in the root Grid. Second, the **theme contract**: `ThemesDictionary` + `ControlsDictionary` in App.xaml, `ApplicationThemeManager.Apply()` in code-behind, and `DynamicResource` exclusively for all colour references. Third, the **styling discipline**: always `BasedOn` the `DefaultUi*Style` key when overriding WPF-UI controls, and use the library's token brushes (`TextFillColorPrimaryBrush`, `ControlFillColorDefaultBrush`, `SystemAccentColorSecondaryBrush`) rather than hardcoded colours.

The most important decision point is **NavigationView mode selection**. `Left` mode (the default) covers most applications. `LeftFluent` gives a compact activity bar without hamburger or back buttons. For a VS Code-style layout requiring a slide-out panel, custom sidebar, and tab bar, build the layout manually with Grid columns and use WPF-UI's styled controls within that structure rather than fighting NavigationView's opinionated frame management. The TitleBar control's `Header` and `TrailingContent` slots handle most title bar customisation needs; resort to custom `WindowChrome` only when you need tabs or complex controls spanning the entire caption area.