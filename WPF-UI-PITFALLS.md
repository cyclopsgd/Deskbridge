# WPF-UI pitfalls, bugs, and correct patterns for .NET 10

**WPF-UI 4.x (latest stable: 4.2.0) introduces several subtle traps** that silently break dialogs, navigation, theming, and MVVM bindings in FluentWindow applications. The library's migration from `ContentPresenter` to `ContentDialogHost`, its internal `WindowChrome` management, and its resource dictionary swapping mechanism are the root causes of most developer frustration. This guide documents every major pitfall with exact incorrect and correct code patterns, drawn from GitHub issues, source code analysis, and community reports. All patterns target WPF-UI 4.x on .NET 10, which gained support in v4.1.0 via forward-compatibility from the `net9.0-windows` TFM.

---

## 1. ContentDialog requires an explicit host element in your visual tree

The single most reported WPF-UI issue is `ContentDialog` failing silently or throwing `InvalidOperationException: ContentPresenter is not set` or `The DialogHost was never set`. **ContentDialog will not render without a host control** placed in your window's XAML and registered with the `IContentDialogService`.

WPF-UI 4.x introduced `ContentDialogHost` as the replacement for the legacy `ContentPresenter` approach. The old `SetContentPresenter()` method is deprecated, and mixing host types on the same service instance throws an explicit error: *"Cannot set ContentPresenter: a ContentDialogHost host has already been set."*

**Incorrect — no dialog host registered:**
```xml
<ui:FluentWindow x:Class="MyApp.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Grid>
        <!-- No ContentPresenter or ContentDialogHost — dialogs will throw -->
        <ContentControl Content="{Binding CurrentView}" />
    </Grid>
</ui:FluentWindow>
```

**Correct — host element in XAML, registered in code-behind:**
```xml
<ui:FluentWindow x:Class="MyApp.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <ui:TitleBar Title="My App" Grid.Row="0" />
        <ContentControl Grid.Row="1" Content="{Binding CurrentView}" />
        <!-- This host MUST exist in the visual tree -->
        <ContentPresenter x:Name="RootContentDialog" Grid.Row="1" />
    </Grid>
</ui:FluentWindow>
```

```csharp
public partial class MainWindow : FluentWindow
{
    public MainWindow(IContentDialogService contentDialogService)
    {
        InitializeComponent();
        contentDialogService.SetDialogHost(RootContentDialog);
    }
}
```

**Showing a dialog from a ViewModel** (injecting `IContentDialogService`):

```csharp
[RelayCommand]
private async Task OnShowDialog()
{
    var result = await _contentDialogService.ShowSimpleDialogAsync(
        new SimpleContentDialogCreateOptions()
        {
            Title = "Save your work?",
            Content = "Your changes will be lost.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't Save",
            CloseButtonText = "Cancel",
        });
    if (result == ContentDialogResult.Primary) { /* Save */ }
}
```

**Four additional ContentDialog traps to know.** First, custom `ContentDialog` subclasses must include a base style or they render incorrectly:

```xml
<ui:ContentDialog x:Class="MyApp.Dialogs.AddUserDialog"
    xmlns:local="clr-namespace:MyApp.Dialogs"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <ui:ContentDialog.Resources>
        <!-- Without this, custom dialog subclasses won't render -->
        <Style BasedOn="{StaticResource {x:Type ui:ContentDialog}}"
               TargetType="{x:Type local:AddUserDialog}" />
    </ui:ContentDialog.Resources>
    <!-- Custom content -->
</ui:ContentDialog>
```

Second, pressing **Enter inside a TextBox** prematurely triggers the primary button (Discussion #1404). Handle this with a `PreviewKeyDown` filter:

```csharp
private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox)
        e.Handled = true;
}
```

Third, setting `AllowsTransparency = true` with `Background = Brushes.Transparent` on the FluentWindow makes the ContentDialog overlay invisible (Issue #925). Fourth, ContentDialog modal behavior, focus handling, and multiline text rendering all had bugs fixed in **v4.2.0** (PRs #1601, #1611) — ensure you're on this version or later.

---

## 2. TreeView context menus operate on the wrong item

WPF's TreeView does not select an item on right-click. This bug is inherited directly by WPF-UI's TreeView, and it means your context menu commands will operate on the **previously left-clicked item**, not the one under the cursor. This is the single most common TreeView/ContextMenu bug in all of WPF.

**Incorrect — assumes SelectedItem matches right-clicked item:**
```xml
<ui:TreeView ItemsSource="{Binding Items}">
    <ui:TreeView.ContextMenu>
        <ContextMenu>
            <ui:MenuItem Header="Delete"
                Command="{Binding DeleteCommand}"
                CommandParameter="{Binding SelectedItem,
                    RelativeSource={RelativeSource AncestorType=ui:TreeView}}" />
        </ContextMenu>
    </ui:TreeView.ContextMenu>
</ui:TreeView>
```

**Correct — select-on-right-click via PreviewMouseRightButtonDown:**
```csharp
private void TreeView_OnPreviewMouseRightButtonDown(
    object sender, MouseButtonEventArgs e)
{
    var treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
    if (treeViewItem != null)
    {
        treeViewItem.Focus(); // Focus() sets IsSelected = true
        e.Handled = true;
    }
}

static TreeViewItem? VisualUpwardSearch(DependencyObject? source)
{
    while (source != null && source is not TreeViewItem)
        source = VisualTreeHelper.GetParent(source);
    return source as TreeViewItem;
}
```

The second trap is that **ContextMenu lives in a separate visual tree**, so `RelativeSource FindAncestor` to Window or any control outside the menu silently fails. The binding error `Cannot find source for binding with reference 'RelativeSource FindAncestor...'` appears in the Output window but causes no exception — the command parameter is just `null`.

**Correct — BindingProxy pattern to bridge the visual tree gap:**
```csharp
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register("Data", typeof(object),
            typeof(BindingProxy), new UIPropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
```

```xml
<Window.Resources>
    <local:BindingProxy x:Key="Proxy" Data="{Binding}" />
</Window.Resources>

<ui:TreeView.ItemTemplate>
    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
        <TextBlock Text="{Binding Name}">
            <TextBlock.ContextMenu>
                <ContextMenu>
                    <ui:MenuItem Header="Delete"
                        Command="{Binding Data.DeleteCommand,
                            Source={StaticResource Proxy}}"
                        CommandParameter="{Binding}" />
                </ContextMenu>
            </TextBlock.ContextMenu>
        </TextBlock>
    </HierarchicalDataTemplate>
</ui:TreeView.ItemTemplate>
```

**Alternative for shared-resource ContextMenus:** when the menu is defined as a shared resource and attached at runtime via code-behind, set `menu.DataContext = viewModel` explicitly before attaching, and bind commands directly with `{Binding CommandName}` (no RelativeSource). Pull the item VM via `CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}"`. Mark menus `x:Shared="False"` so each TreeViewItem gets a fresh instance.

**WPF-UI specific MenuItem bugs** compound these issues. Issue #1387 (open) reports that **`IsEnabled` bindings on `ui:MenuItem` are silently overridden** by `Command.CanExecute`. Issue #168 documents that WPF-UI's MenuItem template hardcodes the `Icon` property to a `TextBlock` with a Fluent icon font — you cannot use an `Image` or arbitrary element as an icon without completely replacing the control template.

---

## 3. FluentWindow conflicts with manual WindowChrome

FluentWindow internally creates and manages its own `WindowChrome` with `CaptionHeight = 0` and calculated `GlassFrameThickness`. **Setting your own WindowChrome will conflict** and break Mica/Acrylic backdrop effects, snap layouts, or window resize behavior. Likewise, setting `WindowStyle="None"` and `AllowsTransparency="True"` on a FluentWindow throws a crash (Issue #576) because FluentWindow manipulates these properties internally.

**Incorrect — manual WindowChrome on FluentWindow:**
```xml
<ui:FluentWindow ...
    WindowStyle="None"
    AllowsTransparency="True">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="32" />
    </WindowChrome.WindowChrome>
</ui:FluentWindow>
```

**Correct — minimal FluentWindow setup:**
```xml
<!-- App.xaml — both dictionaries required, ThemesDictionary FIRST -->
<Application x:Class="MyApp.App"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

```xml
<!-- MainWindow.xaml -->
<ui:FluentWindow x:Class="MyApp.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="My App" Height="450" Width="800"
    ExtendsContentIntoTitleBar="True"
    WindowBackdropType="Mica">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <!-- TitleBar MUST be explicitly added — FluentWindow removes native titlebar -->
        <ui:TitleBar Title="My App" Grid.Row="0" />
        <ContentControl Grid.Row="1" />
    </Grid>
</ui:FluentWindow>
```

```csharp
// Code-behind MUST inherit FluentWindow, not Window
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        SystemThemeWatcher.Watch(this);
        InitializeComponent();
    }
}
```

**Key traps.** Omitting `<ui:TitleBar>` produces a window with no minimize/maximize/close buttons (Issues #537, #862). Setting `Theme="Dark"` in `ThemesDictionary` alone is insufficient — `SystemThemeWatcher.Watch()` or `ApplicationThemeManager.Apply()` may override it at runtime. Force dark theme explicitly:

```csharp
ApplicationThemeManager.Apply(ApplicationTheme.Dark);
```

Setting `SizeToContent="WidthAndHeight"` causes a visible white border until the window is resized (Issue #1027). `WindowBackdropType="Mica"` only works on Windows 11 — **Windows 10 silently falls back** with no backdrop effect and no error message. For title bar button interactivity on custom content, use `WindowChrome.IsHitTestVisibleInChrome="True"` on interactive elements placed in the title bar region.

---

## 4. NavigationView page wiring fails without DI registration

WPF-UI's `NavigationView` with `PaneDisplayMode="LeftCompact"` or `PaneDisplayMode="LeftFluent"` provides the closest match to a VS Code-style icon sidebar. `LeftFluent` renders as a thin icon strip (like VS Code's Activity Bar); `LeftCompact` expands to show labels on hover.

**The critical mistake is forgetting to register pages in DI.** Every page type referenced in `TargetPageType` must be registered, or you get: `InvalidOperationException: 'The MyApp.Views.Pages.EditorPage has not been registered.'`

**Correct DI registration and NavigationView wiring (v4.x):**

```csharp
// App.xaml.cs
private static readonly IHost _host = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // v4.x: replaces old IPageService
        services.AddNavigationViewPageProvider(); // from Wpf.Ui.DependencyInjection

        // EVERY page and ViewModel must be registered
        services.AddTransient<ExplorerPage>();
        services.AddTransient<ExplorerViewModel>();
        services.AddTransient<SearchPage>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();
    }).Build();

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    _host.Start();
    _host.Services.GetRequiredService<MainWindow>().Show();
}
```

**Navigation-aware pages** must implement `INavigableView<T>` (moved to `Wpf.Ui.Abstractions.Controls` in v4.0):

```csharp
public partial class ExplorerPage : INavigableView<ExplorerViewModel>
{
    public ExplorerViewModel ViewModel { get; }

    public ExplorerPage(ExplorerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
```

A VS Code-style collapsible side panel is **not built into NavigationView** and requires a manual `Grid` with a `GridSplitter` alongside the NavigationView content area. Bind the panel's `Visibility` to a boolean in your ViewModel.

---

## 5. XAML parse errors from missing resources, wrong icons, and base class mismatches

**The most common XamlParseException** is forgetting to include both `ThemesDictionary` and `ControlsDictionary` in `App.xaml`, or placing them in the wrong order. `ThemesDictionary` must come before `ControlsDictionary`. Without them, controls render invisible or black (Issue #704).

**Icon rendering failures** come from three sources. Using a raw string like `Icon="Home24"` does nothing — the `Icon` property requires a `SymbolIcon` markup extension object (Issue #1083). Some `SymbolRegular` values beyond the Basic Multilingual Plane render as hieroglyphs due to font mapping bugs (Issue #1232). Segoe Fluent Icons is only pre-installed on Windows 11 — Windows 10 deployments must bundle the font manually.

**Incorrect — string icon value:**
```xml
<ui:NavigationViewItem Content="Home" Icon="Home24"/>
```

**Correct — markup extension:**
```xml
<ui:NavigationViewItem Content="Home" Icon="{ui:SymbolIcon Home24}"
    TargetPageType="{x:Type pages:DashboardPage}" />
```

**Validate icon names against the enum.** Not every plausible-looking name exists in `SymbolRegular`. Example: `ClipboardText24` does not exist and throws `FormatException` at runtime. Use reflection on `C:\Users\<user>\.nuget\packages\wpf-ui\4.2.0\lib\net10.0-windows7.0\Wpf.Ui.dll` to validate — the enum has ~9232 values.

**Color vs Brush resource keys matter.** WPF-UI exposes both `XxxColor` (a `Color` resource) and `XxxBrush` (a `SolidColorBrush` resource). For `Foreground` / `Background` / `BorderBrush` always use the `*Brush` variant. `Foreground="{DynamicResource TextOnAccentFillColorPrimary}"` throws at runtime with `'#FF000000' is not a valid value for property 'Foreground'` because the resource is a Color, not a Brush. Correct key: `TextOnAccentFillColorPrimaryBrush`. Same for `ApplicationBackgroundColor` vs `ApplicationBackgroundBrush`.

**The XAML/code-behind base class mismatch** is another frequent source of `XamlParseException`. If your XAML declares `<ui:FluentWindow>` but the code-behind inherits `Window`, the parser crashes. **Both must match:**

```xml
<!-- XAML root element -->
<ui:FluentWindow x:Class="MyApp.MainWindow" ...>
```
```csharp
// Code-behind must match
public partial class MainWindow : FluentWindow { }
```

**The `ProgressRing` initialization crash** in v4.0.1 (`TypeInitializationException` in `Wpf.Ui.Controls.Arc` static constructor) was fixed in v4.0.2 (PR #1357). If you're on v4.0.1, upgrade immediately or remove any `ProgressRing` usage.

**Namespace errors** arise from using CLR namespaces instead of the custom URI. Always use `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"` — not `xmlns:ui="clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"`. Using `DynamicResource` (not `StaticResource`) for all WPF-UI theme resources is mandatory for runtime theme switching.

---

## 6. WindowsFormsHost airspace is worse with FluentWindow backdrops

WPF-UI introduces **no new fixes** for the classic WPF airspace problem, but FluentWindow's backdrop effects make it worse. Mica and Acrylic backdrops work by setting `window.Background = Brushes.Transparent` via DWM APIs (source: `WindowBackdrop.cs`). This effectively creates the same condition as `AllowsTransparency = true`, which can cause WindowsFormsHost content to **become invisible or render as black rectangles**.

Additionally, WPF-UI overlays (ContentDialog, NavigationView flyouts, Snackbar) render behind WindowsFormsHost content due to airspace, breaking the expected z-ordering. No GitHub issues exist specifically for WindowsFormsHost in WPF-UI, suggesting most developers avoid the combination entirely.

**Correct — disable backdrop when hosting WinForms:**
```xml
<ui:FluentWindow WindowBackdropType="None"
    Background="{DynamicResource ApplicationBackgroundBrush}">
    <!-- WindowsFormsHost now renders correctly -->
</ui:FluentWindow>
```

**For cases where you need both backdrop effects and WinForms hosting**, the only viable workarounds are the AirspaceFixer NuGet package (renders WinForms to a bitmap at ~30fps — unsuitable for video), hosting the WinForms control in a separate borderless `Window` positioned as an overlay, or replacing ActiveX/COM controls with WebView2, which is a native WPF control without airspace issues.

**Deskbridge-specific:** This affects Phase 4 (RDP Integration). The RDP ActiveX control is hosted in WindowsFormsHost — the viewport region MUST NOT have Mica backdrop. Consider disabling Mica only in the viewport area, or using a solid background in that region.

---

## 7. MVVM command bindings have three WPF-UI-specific traps

**Trap 1: `[NotifyCanExecuteChangedFor]` is silently required.** When using CommunityToolkit.Mvvm's `[RelayCommand(CanExecute = ...)]`, changing the property that `CanExecute` depends on does **not** automatically re-evaluate the command's enabled state. You must explicitly decorate the dependency property:

```csharp
// Incorrect — button stays disabled after selecting an item
[ObservableProperty]
private MyItem? selectedItem;

[RelayCommand(CanExecute = nameof(CanDelete))]
private void Delete() { /* ... */ }
private bool CanDelete() => SelectedItem != null;
```

```csharp
// Correct — command re-evaluates when SelectedItem changes
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
private MyItem? selectedItem;

[RelayCommand(CanExecute = nameof(CanDelete))]
private void Delete() { /* ... */ }
private bool CanDelete() => SelectedItem != null;
```

**Trap 2: `MenuItem` CommandParameter is null inside DataTemplates.** This is a known WPF framework bug (dotnet/wpf#4078) that affects WPF-UI's `ui:MenuItem` equally. When a `MenuItem` is generated inside a `DataTemplate`, `CommandParameter` bindings silently resolve to `null`. The workaround is to use `ItemContainerStyle` instead of nesting `MenuItem` in a `DataTemplate`.

**Trap 3: The generated command name strips prefixes and suffixes.** `[RelayCommand]` on `OnGoToSettings()` generates `GoToSettingsCommand` (strips `On`). On `DeleteAsync()`, it generates `DeleteCommand` (strips `Async`). On `Delete()`, it generates `DeleteCommand`. Binding to the wrong name produces no error — the command simply never fires.

```csharp
[RelayCommand]
private async Task OnSaveAsync() { /* ... */ }
// Generates: SaveCommand
// Correct:   Command="{Binding SaveCommand}"
// Wrong:     Command="{Binding OnSaveAsyncCommand}"
```

**Both the class and any containing types must be `partial`** for source generators to work. Missing `partial` produces a compile error, but it's easily overlooked when the class is nested.

**Trap 4: ComboBoxItem Content inside ContentDialog renders as placeholder glyphs.** When using `<ComboBoxItem Content="Label" Tag="{x:Static ...}" />` inside a `ContentDialog` with `SelectedValuePath="Tag"`, WPF-UI's ContentDialog template reinterprets the items as placeholder glyphs ("...", "-"). Use a ViewModel collection of wrapper records instead:

```csharp
// Correct pattern
public record OptionDisplay(MyEnum Value, string DisplayName);
public ObservableCollection<OptionDisplay> Options { get; } = [
    new(MyEnum.A, "Option A"),
    new(MyEnum.B, "Option B"),
];
```

```xml
<ComboBox ItemsSource="{Binding Options}"
          SelectedValue="{Binding CurrentValue}"
          SelectedValuePath="Value"
          DisplayMemberPath="DisplayName" />
```

---

## 8a. Explicit Height on WPF-UI controls can clip internal templates

WPF-UI's default ComboBox / TextBox / Button templates use internal `ContentPresenter`s, `TextBlock`s, and visual states that have implicit minimum heights (~30-32px for ComboBox SelectionBoxItem, ~28px for TextBox). Setting an explicit `Height="24"` or similar small value silently clips the internal template, causing the rendered content to fall back to placeholder glyphs ("- - -", "-", dots) instead of the actual data.

**Incorrect — clipped ComboBox:**
```xml
<ComboBox ItemsSource="{Binding Items}"
          SelectedValue="{Binding CurrentValue}"
          SelectedValuePath="Value"
          Height="24" FontSize="14">   <!-- CLIPS template -->
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding DisplayName}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

This renders correctly in the dropdown (items show "Inherit", "Own", "Prompt") but the collapsed `SelectionBoxItem` shows "- - -" / "-" placeholder glyphs.

**Correct — let it size naturally, or use MinHeight:**
```xml
<ComboBox ItemsSource="{Binding Items}"
          SelectedValue="{Binding CurrentValue}"
          SelectedValuePath="Value"
          MinHeight="32" FontSize="14">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding DisplayName}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

When laying out compact UI (like an inline property grid), use `Height="Auto"` on the row definition and let the ComboBox use its natural ~32px height. If you need a specific visual height, use `MinHeight` (not `Height`) so WPF-UI's template can expand if needed.

**Same trap applies to:** `ui:TextBox`, `ui:Button` with content, `PasswordBox` (auto-restyled by WPF-UI). If a WPF-UI control shows broken rendering at small sizes, check for `Height="N"` and remove it or change to `MinHeight`.

---

## 8. Theme overrides silently revert without the Changed event handler

WPF-UI's theme system works by **swapping entire resource dictionaries** when `ApplicationThemeManager.Apply()` is called. This means any custom color overrides you placed in `App.xaml` resources are wiped on theme switch. The `SystemThemeWatcher.Watch()` method, called in most FluentWindow constructors, re-applies the system accent color by default, silently overriding any custom accent you set.

**Incorrect — custom accent reverts silently:**
```csharp
public MainWindow()
{
    SystemThemeWatcher.Watch(this); // Overrides custom accent!
    InitializeComponent();
    ApplicationAccentColorManager.Apply(Colors.Red); // Gets overridden
}
```

**Correct — disable accent sync, re-apply on theme change:**
```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    // Watch theme changes but DON'T auto-update accents
    SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);
    InitializeComponent();
}

// App.xaml.cs
private readonly Color _customAccent = Color.FromRgb(0x00, 0x7A, 0xCC);

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);
    ApplicationAccentColorManager.Apply(_customAccent, ApplicationTheme.Dark);
    ApplicationThemeManager.Changed += OnThemeChanged;
}

private void OnThemeChanged(ApplicationTheme newTheme, Color systemAccent)
{
    // Re-apply custom accent after every theme change
    ApplicationAccentColorManager.Apply(_customAccent, newTheme);
}
```

**Overriding colors in XAML has a subtle trap**: WPF-UI's `Accent.xaml` defines brushes using `StaticResource` references to color keys internally. Overriding just the `Color` resource won't propagate to the brushes. You must override **both colors and brushes**, or use the programmatic API which handles everything:

```csharp
// Preferred: API handles all brush updates
ApplicationAccentColorManager.Apply(
    systemAccent: Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC),
    primaryAccent: Color.FromArgb(0xFF, 0x00, 0x67, 0xC0),
    secondaryAccent: Color.FromArgb(0xFF, 0x00, 0x3E, 0x92),
    tertiaryAccent: Color.FromArgb(0xFF, 0x00, 0x1A, 0x68));
```

The most important resource keys for custom theming are **`SystemAccentColorPrimaryBrush`** (used for interactive element fills), **`ApplicationBackgroundBrush`** (window background), **`TextFillColorPrimaryBrush`** (primary text), and **`ControlFillColorDefaultBrush`** (default control fill). For scoped overrides on individual controls, place color resources in that control's `.Resources` block — these survive theme dictionary swaps.

Version **4.0.3 and earlier** had a bug where `ApplicationAccentColorManager.Apply()` mapped accent brushes differently than `Accent.xaml` defaults (Issue #1481, fixed in PR #1492). Version **4.2.0** fixed theme and accent color resetting after Windows session unlock (PR #1608). Both fixes are essential for any .NET 10 project — **use v4.2.0 or later**.

---

## Summary — recurring pattern

**WPF-UI silently fails** rather than throwing helpful errors. Dialogs render invisibly without a host, icons display nothing without the markup extension, theme overrides revert without warning, and context menu commands bind to null without exceptions. The library's `DataContext = this` convention, where the Window is the DataContext and the ViewModel is a property, is an unusual pattern that breaks standard MVVM binding assumptions.

**For .NET 10:** WPF-UI 4.2.0 runs via forward-compatibility (dedicated `net10.0-windows7.0` lib folder exists in the NuGet package). Use v4.2.0 or later to get ContentDialog focus fixes (PR #1601), accent color mapping fixes (PR #1492), and session-unlock theme reset fixes (PR #1608). One significant open bug remains: `IsEnabled` bindings on `ui:MenuItem` are ignored when `Command` is also bound (Issue #1387) — always use `CanExecute` on the `RelayCommand` instead.
